using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Entities;

public enum BotSkill
{
    Easy,
    Normal,
    Hard,
    Expert,
}

public enum BotState
{
    Patrol,
    Hunt,
    Engage,
    Dead,
}

/// <summary>Per-difficulty personality knobs for a bot.</summary>
public readonly struct BotProfile
{
    public readonly float ReactionMin, ReactionMax;
    public readonly float TurnRate;
    public readonly float AimError;
    public readonly float ViewRange;
    public readonly float PreferredRange;
    public readonly int BurstMin, BurstMax;
    public readonly float BurstPauseMin, BurstPauseMax;

    private BotProfile(float reactionMin, float reactionMax, float turnRate, float aimError,
        float viewRange, float preferredRange, int burstMin, int burstMax, float pauseMin, float pauseMax)
    {
        ReactionMin = reactionMin;
        ReactionMax = reactionMax;
        TurnRate = turnRate;
        AimError = aimError;
        ViewRange = viewRange;
        PreferredRange = preferredRange;
        BurstMin = burstMin;
        BurstMax = burstMax;
        BurstPauseMin = pauseMin;
        BurstPauseMax = pauseMax;
    }

    public static BotProfile For(BotSkill skill) => skill switch
    {
        BotSkill.Easy => new BotProfile(0.42f, 0.62f, 190f, 3.4f, 2200f, 800f, 2, 5, 0.55f, 1.40f),
        BotSkill.Normal => new BotProfile(0.28f, 0.44f, 260f, 2.0f, 2600f, 750f, 3, 7, 0.40f, 1.05f),
        BotSkill.Hard => new BotProfile(0.18f, 0.30f, 340f, 1.15f, 3000f, 700f, 4, 10, 0.28f, 0.80f),
        _ => new BotProfile(0.10f, 0.18f, 460f, 0.60f, 3600f, 650f, 5, 14, 0.18f, 0.60f),
    };
}

/// <summary>
/// Deathmatch bot: perceives through a view cone plus line of sight, paths with A*
/// over the nav graph when hunting, and duels with burst fire, strafing and
/// recoil compensation when it has a target.
/// </summary>
public sealed class Bot : Actor
{
    public BotProfile Profile;
    public BotState State = BotState.Patrol;
    public bool WantsFire;
    public float Difficulty;

    private int _targetId = -1;
    private Vector3 _lastKnownTarget;
    private float _lastSeenTime = -100f;
    private float _acquiredAt;
    private float _reactionDelay;
    private float _burstPause = 0.6f;

    private int _burstShotsLeft;
    private float _burstResume;

    private readonly List<int> _path = new();
    private int _pathIndex;
    private float _repathTimer;
    private Vector3 _pathGoal;
    private bool _hadIntent;

    private float _strafeDir = 1f;
    private float _strafeTimer;
    private float _jumpTimer;
    private float _stuckTimer;
    private float _repathCooldown;
    private Vector3 _lastPosition;

    public Bot(int id, string name, BotSkill skill)
    {
        Id = id;
        Name = name;
        Profile = BotProfile.For(skill);
        Difficulty = (int)skill / 3f;
        GiveWeapon(WeaponRegistry.Knife);
        GiveWeapon(WeaponRegistry.Glock);
    }

    public void Update(Simulation sim, float dt)
    {
        float now = sim.Time;
        WantsFire = false;
        _hadIntent = false;

        if (!Alive)
        {
            Velocity = Vector3.Zero;
            State = BotState.Dead;
            _path.Clear();
            return;
        }

        Current.Tick(now, dt);
        RecoverRecoil(now, dt);

        Actor? target = AcquireTarget(sim);
        if (target is not null)
        {
            if (_targetId != target.Id)
            {
                _reactionDelay = sim.Rand(Profile.ReactionMin, Profile.ReactionMax);
                _acquiredAt = now;
                _burstShotsLeft = 0;
                _burstResume = 0f;
            }
            _targetId = target.Id;
            _lastSeenTime = now;
            _lastKnownTarget = target.EyePosition;
            State = BotState.Engage;
        }
        else if (now - _lastSeenTime < 7f)
        {
            State = BotState.Hunt;
        }
        else
        {
            State = BotState.Patrol;
            _targetId = -1;
            _burstShotsLeft = 0;
        }

        if (State == BotState.Engage && target is not null) UpdateCombat(sim, target, now, dt);
        else if (State == BotState.Hunt) FollowPath(sim, _lastKnownTarget, dt);
        else UpdatePatrol(sim, dt);

        TrackStuck(sim, dt);
        _lastPosition = Position;
    }

    // ---------------------------------------------------------------- perception

    private Actor? AcquireTarget(Simulation sim)
    {
        Actor? best = null;
        float bestScore = float.MaxValue;
        Vector3 eye = EyePosition;
        Vector3 forward = U.DirFromAngles(Yaw, 0f);

        foreach (Actor other in sim.Actors)
        {
            if (ReferenceEquals(other, this) || !other.Alive) continue;

            Vector3 toTarget = other.EyePosition - eye;
            float distance = toTarget.Length();
            if (distance > Profile.ViewRange) continue;

            Vector3 dir = toTarget / MathF.Max(distance, 1e-4f);
            float facing = Vector3.Dot(forward, dir);
            bool inCone = facing > 0.42f;              // roughly 65 degrees either side
            bool audible = distance < 300f;
            if (!inCone && !audible) continue;

            if (!sim.Map.LineOfSight(eye, other.EyePosition)) continue;

            float score = distance - facing * 400f;
            if (score < bestScore)
            {
                bestScore = score;
                best = other;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------- duelling

    private void UpdateCombat(Simulation sim, Actor target, float now, float dt)
    {
        float distance = (target.Position - Position).Length();

        // Aim slightly above centre so shots land around chest/head height.
        Vector3 aimPoint = target.EyePosition + new Vector3(0f, -2f, 0f);

        // Aim jitter shaped by difficulty and range.
        float aimError = Profile.AimError * U.Lerp(1.4f, 0.7f, U.Clamp01(distance / 1200f));
        aimPoint += new Vector3(
            MathF.Sin(now * 2.7f + Id * 1.3f) * aimError,
            MathF.Sin(now * 1.9f + Id * 2.1f) * aimError * 0.5f,
            MathF.Cos(now * 3.1f + Id * 0.7f) * aimError);

        AimAt(aimPoint, dt, Profile.TurnRate);

        bool aimed = IsAimedAt(aimPoint, 4.5f);
        bool reacted = now - _acquiredAt >= _reactionDelay;
        Weapon weapon = Current;

        if (weapon.Mag == 0 && !weapon.Reloading && weapon.Reserve > 0
            && weapon.BeginReload(now))
        {
            sim.Emit("reload", EyePosition, 0.5f);
        }

        if (aimed && reacted && weapon.CanFire(now))
        {
            if (_burstShotsLeft <= 0 && now >= _burstResume)
            {
                _burstShotsLeft = sim.RandInt(Profile.BurstMin, Profile.BurstMax + 1);
                _burstPause = sim.Rand(Profile.BurstPauseMin, Profile.BurstPauseMax);
            }

            if (_burstShotsLeft > 0) WantsFire = true;
        }

        // ---- movement: hold a range band, strafe, take cover when hurt
        Vector3 toTarget = U.FlatNormalized(target.Position - Position);
        if (toTarget.LengthSquared() < 1e-4f) toTarget = U.RightFromYaw(Yaw);

        _strafeTimer -= dt;
        if (_strafeTimer <= 0f)
        {
            _strafeDir = sim.Chance(0.5f) ? 1f : -1f;
            _strafeTimer = sim.Rand(0.6f, 1.7f);
        }

        Vector3 right = new(-toTarget.Z, 0f, toTarget.X);
        Vector3 wish = right * _strafeDir * 0.9f;

        bool lowHealth = Health < 35f;
        float preferred = lowHealth ? Profile.PreferredRange * 1.5f : Profile.PreferredRange;

        if (distance > preferred + 220f) wish += toTarget;
        else if (distance < preferred - 320f) wish -= toTarget;
        else if (weapon.Def.Class == "Sniper") wish *= 0.15f;   // snipers hold still to shoot

        if (!sim.Map.LineOfSight(EyePosition, target.EyePosition)) wish = toTarget;

        wish = U.FlatNormalized(wish);
        if (wish.LengthSquared() > 1e-4f)
        {
            _hadIntent = true;
            wish = AvoidWalls(sim, wish);
            wish += Separation(sim) * 0.35f;
            wish = U.FlatNormalized(wish);
        }

        bool crouch = weapon.Def.Class == "Sniper" && distance > 700f;
        bool jump = MaybeJump(sim, wish, dt);
        CharacterMover.Move(sim.Map, this, wish, Cfg.RunSpeed * weapon.Def.MoveScale, jump, crouch, walk: false, dt);
    }

    private void AimAt(Vector3 point, float dt, float turnRate)
    {
        Vector3 dir = point - EyePosition;
        if (dir.LengthSquared() < 1e-5f) return;

        float desiredYaw = U.YawFromDirection(dir);
        float desiredPitch = U.PitchFromDirection(dir);

        // Cancel our own accumulated recoil so the barrel lands on the target.
        desiredYaw -= Punch.X;
        desiredPitch -= Punch.Y;

        float maxTurn = turnRate * dt;
        Yaw += U.Clamp(U.AngleDelta(Yaw, desiredYaw), -maxTurn, maxTurn);
        Pitch = U.Clamp(Pitch + U.Clamp(desiredPitch - Pitch, -maxTurn, maxTurn), -Cfg.MaxPitch, Cfg.MaxPitch);
    }

    private bool IsAimedAt(Vector3 point, float toleranceDeg)
    {
        Vector3 dir = U.NormalizeOrZero(point - EyePosition);
        if (dir.LengthSquared() < 1e-5f) return false;
        float dot = U.Clamp(Vector3.Dot(AimDirection, dir), -1f, 1f);
        return MathF.Acos(dot) * U.Rad2Deg <= toleranceDeg;
    }

    // ---------------------------------------------------------------- navigation

    private void UpdatePatrol(Simulation sim, float dt)
    {
        if (sim.Nav.Nodes.Length == 0) return;

        bool needGoal = _path.Count == 0
            || _pathIndex >= _path.Count
            || U.FlatLength(Position - _pathGoal) < 70f;

        if (needGoal)
        {
            int pick = sim.RandInt(0, sim.Nav.Nodes.Length);
            _pathGoal = sim.Nav.Nodes[pick].Position;
            RecomputePath(sim, _pathGoal);
        }

        FollowPath(sim, _pathGoal, dt);
    }

    private void FollowPath(Simulation sim, Vector3 goal, float dt)
    {
        _repathTimer -= dt;
        _repathCooldown -= dt;

        bool goalMoved = (goal - _pathGoal).LengthSquared() > 250f * 250f;
        if (_repathTimer <= 0f || _path.Count == 0 || goalMoved)
        {
            _pathGoal = goal;
            RecomputePath(sim, goal);
            _repathTimer = 0.55f;
        }

        while (_pathIndex < _path.Count)
        {
            Vector3 node = sim.Nav.Nodes[_path[_pathIndex]].Position;
            if (U.FlatLength(node - Position) < 50f) _pathIndex++;
            else break;
        }

        Vector3 steer = _pathIndex < _path.Count ? sim.Nav.Nodes[_path[_pathIndex]].Position : goal;
        Vector3 wish = U.FlatNormalized(steer - Position);

        if (wish.LengthSquared() > 1e-4f)
        {
            _hadIntent = true;
            float desiredYaw = U.YawFromDirection(wish);
            float maxTurn = Profile.TurnRate * 0.7f * dt;
            Yaw += U.Clamp(U.AngleDelta(Yaw, desiredYaw), -maxTurn, maxTurn);
            Pitch = U.Approach(Pitch, 0f, 4f, dt);

            wish = AvoidWalls(sim, wish);
            wish += Separation(sim) * 0.6f;
            wish = U.FlatNormalized(wish);
        }

        bool jump = MaybeJump(sim, wish, dt);
        CharacterMover.Move(sim.Map, this, wish, Cfg.RunSpeed * Current.Def.MoveScale, jump, crouchHeld: false, walk: false, dt);
    }

    private void RecomputePath(Simulation sim, Vector3 goal)
    {
        _path.Clear();
        _path.AddRange(sim.Nav.FindPath(Position, goal));
        _pathIndex = 0;
    }

    /// <summary>Probes rotated wish directions so bots slide along cover instead of grinding into it.</summary>
    private Vector3 AvoidWalls(Simulation sim, Vector3 wish)
    {
        if (wish.LengthSquared() < 1e-5f || !ProbeBlocked(sim, wish)) return wish;

        float[] offsets = { 30f, -30f, 60f, -60f, 95f, -95f, 140f, -140f, 180f };
        foreach (float offset in offsets)
        {
            Vector3 candidate = RotateY(wish, offset);
            if (!ProbeBlocked(sim, candidate)) return candidate;
        }

        return wish;
    }

    private bool ProbeBlocked(Simulation sim, Vector3 wish)
    {
        Vector3 origin = Position + new Vector3(0f, 20f, 0f);
        return sim.Map.SegmentBlocker(origin, origin + wish * 60f) >= 0;
    }

    private Vector3 Separation(Simulation sim)
    {
        Vector3 push = Vector3.Zero;
        foreach (Actor other in sim.Actors)
        {
            if (ReferenceEquals(other, this) || !other.Alive) continue;
            Vector3 d = Position - other.Position;
            float dist = U.FlatLength(d);
            if (dist > 1e-3f && dist < 90f) push += d / dist * (1f - dist / 90f);
        }
        return push;
    }

    private bool MaybeJump(Simulation sim, Vector3 wish, float dt)
    {
        _jumpTimer -= dt;
        if (wish.LengthSquared() < 1e-5f || !OnGround || _jumpTimer > 0f) return false;

        Vector3 origin = Position + new Vector3(0f, 8f, 0f);
        if (sim.Map.SegmentBlocker(origin, origin + wish * 40f) < 0) return false;

        Vector3 highOrigin = Position + new Vector3(0f, Cfg.StepHeight + 26f, 0f);
        if (sim.Map.SegmentBlocker(highOrigin, highOrigin + wish * 40f) >= 0) return false;

        _jumpTimer = 0.35f;
        return true;
    }

    /// <summary>Detects a bot making no progress and forces a re-path to break stalemates.</summary>
    private void TrackStuck(Simulation sim, float dt)
    {
        if (!_hadIntent)
        {
            _stuckTimer = 0f;
            return;
        }

        float moved = U.FlatLength(Position - _lastPosition);
        if (moved < 0.05f) _stuckTimer += dt;
        else _stuckTimer = 0f;

        if (_stuckTimer > 0.9f && _repathCooldown <= 0f)
        {
            _repathCooldown = 1.5f;
            _stuckTimer = 0f;
            _path.Clear();
            _repathTimer = 0f;
            _strafeDir = -_strafeDir;
            _strafeTimer = 0.4f;
        }
    }

    public override void OnShotFired(float now)
    {
        LastShotTime = now;
        if (_burstShotsLeft > 0) _burstShotsLeft--;
        if (_burstShotsLeft <= 0) _burstResume = now + _burstPause;
    }

    private static Vector3 RotateY(Vector3 v, float degrees)
    {
        float r = degrees * U.Deg2Rad;
        float c = MathF.Cos(r), s = MathF.Sin(r);
        return new Vector3(v.X * c + v.Z * s, 0f, -v.X * s + v.Z * c);
    }
}
