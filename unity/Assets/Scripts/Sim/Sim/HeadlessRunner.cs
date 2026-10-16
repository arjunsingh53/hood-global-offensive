using System;
using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.World;

namespace HoodGlobalOffensive.Sim
{
    /// <summary>
    /// Runs the whole simulation with a scripted robot player. Used by
    /// <c>--headless</c> to prove the movement, collision, grapple, combat, AI,
    /// upgrade economy, scoring and respawn paths all work without opening a window.
    /// </summary>
    public static class HeadlessRunner
    {
        public static int Run(float seconds, int botCount, BotSkill skill, ulong seed, bool verbose,
            string mapName = MapRegistry.Default)
        {
            var sim = new Simulation(botCount, skill, seed, mapName: mapName);
            var rng = new Rng(seed ^ 0xA5A5_1234_ABCD_0001UL);

            const float dt = 1f / 60f;
            int totalTicks = (int)(seconds / dt);

            int insideGeometry = 0;
            int nonFinite = 0;
            int ropeViolations = 0;
            int deaths = 0;
            float distanceWalked = 0f;
            float peakSpeed = 0f;
            int headshots = 0;
            int grapplesFired = 0;
            int upgradesBought = 0;
            bool prevLeftAttached = false;
            bool prevRightAttached = false;
            int nextUpgradeTick = 300;
            int upgradeCursor = 0;

            Vector3 previousPosition = sim.Player.Position;

            var sw = System.Diagnostics.Stopwatch.StartNew();

            int tick = 0;
            for (; tick < totalTicks; tick++)
            {
                InputFrame input = ScriptedPlay.Build(tick, sim, ref rng);
                sim.Update(input, dt);

                // ---- invariant checks
                if (sim.Map.BoxBlocked(sim.Player.Bounds))
                {
                    insideGeometry++;
                    if (verbose)
                    {
                        Aabb bounds = sim.Player.Bounds;
                        for (int b = 0; b < sim.Map.Brushes.Length; b++)
                        {
                            if (!sim.Map.Brushes[b].Box.Intersects(bounds)) continue;
                            Brush hit = sim.Map.Brushes[b];
                            Console.WriteLine(
                                $"  [embed] tick {tick} pos {sim.Player.Position} vel {sim.Player.Velocity} " +
                                $"onGround {sim.Player.OnGround} crouch {sim.Player.CrouchAmount:F2} " +
                                $"brush {b} kind {hit.Kind} box min {hit.Box.Min} max {hit.Box.Max}");
                        }
                    }
                }

                if (!IsFinite(sim.Player.Position) || !IsFinite(sim.Player.Velocity))
                {
                    nonFinite++;
                    sim.Player.Position = sim.Map.Spawns[0].Position;
                    sim.Player.Velocity = Vector3.Zero;
                }

                grapplesFired += CountAttaches(sim, ref prevLeftAttached, ref prevRightAttached);
                ropeViolations += RopeViolations(sim);

                // Exercise the kill-point economy through the same API the cabinet
                // menu calls, so the purchase/penalty path stays covered headlessly.
                if (tick >= nextUpgradeTick && sim.Player.Alive && sim.Player.KillPoints > 0)
                {
                    UpgradeStat stat = (UpgradeStat)(upgradeCursor++ % 4);
                    if (sim.TryBuyUpgrade(sim.Player, stat)) upgradesBought++;
                    nextUpgradeTick = tick + 900;
                }

                float moved = U.FlatLength(sim.Player.Position - previousPosition);
                if (moved < 40f) distanceWalked += moved;
                previousPosition = sim.Player.Position;

                peakSpeed = MathF.Max(peakSpeed, sim.Player.HorizontalSpeed);

                if (sim.MatchOver) { tick++; break; }
            }

            sw.Stop();

            foreach (Actor actor in sim.Actors) deaths += actor.Deaths;
            int respawns = sim.RespawnCount;

            sim.ShotsFired.TryGetValue(sim.Player.Id, out int fired);
            sim.ShotsHit.TryGetValue(sim.Player.Id, out int hits);
            sim.Headshots.TryGetValue(sim.Player.Id, out headshots);

            Console.WriteLine();
            Console.WriteLine("=========================================================");
            Console.WriteLine(" HEADLESS SIMULATION REPORT");
            Console.WriteLine("=========================================================");
            Console.WriteLine($" simulated ticks      : {tick}  ({tick * dt:0.0}s of {seconds:0.0}s budget)");
            Console.WriteLine($" wall clock           : {sw.Elapsed.TotalMilliseconds:0} ms  ({tick / MathF.Max(0.001f, (float)sw.Elapsed.TotalSeconds):0} ticks/s)");
            Console.WriteLine($" bots in match        : {botCount}  (skill {skill}, map {sim.Map.Name})");
            Console.WriteLine($" nav nodes            : {sim.Nav.Nodes.Length}");
            Console.WriteLine($" map brushes          : {sim.Map.Brushes.Length}");
            Console.WriteLine($" upgrade stations     : {sim.Stations.Count}");
            Console.WriteLine($" match over           : {sim.MatchOver}  {sim.MatchResult}");
            Console.WriteLine($" time remaining       : {sim.TimeLeft:0.0}s");
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine($" player kills/deaths  : {sim.Player.Kills} / {sim.Player.Deaths}");
            Console.WriteLine($" player shots fired   : {fired}   hits {hits}   headshots {headshots}");
            Console.WriteLine($" player accuracy      : {(fired > 0 ? hits * 100f / fired : 0f):0.0}%");
            Console.WriteLine($" player health        : {sim.Player.Health:0}   alive={sim.Player.Alive}");
            Console.WriteLine($" player kill points   : {sim.Player.KillPoints}   upgrades bought {upgradesBought}");
            Console.WriteLine($" grapple fires       : {grapplesFired}");
            Console.WriteLine($" distance walked      : {distanceWalked:0} units");
            Console.WriteLine($" peak horizontal speed: {peakSpeed:0} units/s  (run speed {Cfg.RunSpeed})");
            Console.WriteLine($" respawns handled     : {respawns}");
            Console.WriteLine($" kill feed entries    : {sim.KillFeed.Count}");
            Console.WriteLine($" tracers alive        : {sim.Tracers.Count}   decals {sim.Decals.Count}");
            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine(" LEADERBOARD");
            foreach (Actor actor in sim.Leaderboard())
            {
                sim.ShotsFired.TryGetValue(actor.Id, out int af);
                sim.ShotsHit.TryGetValue(actor.Id, out int ah);
                float acc = af > 0 ? ah * 100f / af : 0f;
                Console.WriteLine($"   {(actor.IsPlayer ? "YOU " : "BOT "),-4} {actor.Name,-10} kills {actor.Kills,3}  deaths {actor.Deaths,3}  acc {acc,5:0.0}%  mk={GetMarksman(sim, actor)}");
            }

            Console.WriteLine("---------------------------------------------------------");
            Console.WriteLine($" INVARIANTS");
            Console.WriteLine($"   ticks with player embedded in geometry : {insideGeometry}  (expect 0)");
            Console.WriteLine($"   frames with non-finite transforms      : {nonFinite}  (expect 0)");
            Console.WriteLine($"   frames with over-extended grapple rope : {ropeViolations}  (expect 0)");
            Console.WriteLine($"   deaths across all actors               : {deaths}");
            Console.WriteLine($"   kills recorded == deaths recorded      : {(TotalKills(sim) == deaths ? "yes" : $"no ({TotalKills(sim)} vs {deaths})")}");
            Console.WriteLine($"   spawns are clear of geometry           : {(SpawnsClear(sim) ? "yes" : "NO")}");
            Console.WriteLine($"   upgrade stations exist                 : {(sim.Stations.Count > 0 ? "yes" : "NO")}");

            bool healthy = insideGeometry == 0 && nonFinite == 0 && ropeViolations == 0
                && sim.Nav.Nodes.Length > 0 && SpawnsClear(sim) && sim.Stations.Count > 0;

            // The whole point of the harness: prove the game actually progresses.
            if (fired == 0 || TotalKills(sim) == 0)
            {
                Console.WriteLine("   >>> FAILED: no shots or no kills happened during the run");
                healthy = false;
            }

            Console.WriteLine($" RESULT: {(healthy ? "PASS" : "FAIL")}");
            Console.WriteLine("=========================================================");

            if (verbose)
            {
                Console.WriteLine();
                Console.WriteLine(" KILL FEED (last events)");
                foreach (KillFeedEntry entry in sim.KillFeed)
                {
                    Console.WriteLine($"   {entry.Killer} [{entry.Weapon}{(entry.Headshot ? " HS" : "")}] {entry.Victim}");
                }
            }

            return healthy ? 0 : 1;
        }

        private static int CountAttaches(Simulation sim, ref bool prevLeft, ref bool prevRight)
        {
            int count = 0;
            if (sim.Player.HookLeft.Attached && !prevLeft) count++;
            if (sim.Player.HookRight.Attached && !prevRight) count++;
            prevLeft = sim.Player.HookLeft.Attached;
            prevRight = sim.Player.HookRight.Attached;
            return count;
        }

        private static int RopeViolations(Simulation sim)
        {
            int violations = 0;
            float limit = Cfg.GrappleRange * 1.05f;
            if (sim.Player.HookLeft.Attached
                && (sim.Player.HookLeft.Anchor - sim.Player.EyePosition).Length() > limit) violations++;
            if (sim.Player.HookRight.Attached
                && (sim.Player.HookRight.Anchor - sim.Player.EyePosition).Length() > limit) violations++;
            return violations;
        }

        private static string GetMarksman(Simulation sim, Actor actor)
        {
            sim.Headshots.TryGetValue(actor.Id, out int hs);
            return hs.ToString();
        }

        private static int TotalKills(Simulation sim)
        {
            int total = 0;
            foreach (Actor actor in sim.Actors) total += actor.Kills;
            return total;
        }

        private static bool SpawnsClear(Simulation sim)
        {
            foreach (SpawnPoint spawn in sim.Map.Spawns)
            {
                var box = Aabb.Footprint(spawn.Position, Cfg.HalfWidth, Cfg.StandHeight);
                if (sim.Map.BoxBlocked(box)) return false;
            }
            return true;
        }

        private static bool IsFinite(Vector3 v)
            => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    }

    /// <summary>
    /// A deterministic robot player: it bunny-hops toward the nearest visible
    /// enemy, swings from hooks, fires when roughly on target and reloads when dry.
    /// Also used by the screenshot mode so the captured frame is a real frame.
    /// </summary>
    public static class ScriptedPlay
    {
        public static InputFrame Build(int tick, Simulation sim, ref Rng rng)
        {
            var input = InputFrame.Neutral;
            Player player = sim.Player;

            if (!player.Alive) return input;

            Actor? target = null;
            float bestDistance = float.MaxValue;
            foreach (Actor actor in sim.Actors)
            {
                if (actor.IsPlayer || !actor.Alive) continue;
                float d = (actor.Position - player.Position).Length();
                if (d < bestDistance)
                {
                    bestDistance = d;
                    target = actor;
                }
            }

            if (target is not null)
            {
                Vector3 to = target.EyePosition - player.EyePosition;
                float desiredYaw = U.YawFromDirection(to) - player.Punch.X;
                float desiredPitch = U.PitchFromDirection(to) - player.Punch.Y;

                float dYaw = U.AngleDelta(player.Yaw, desiredYaw);
                float dPitch = desiredPitch - player.Pitch;

                // Invert the controller mapping so the scripted view converges.
                const float scale = Cfg.DegPerCountPerSens * Cfg.Sensitivity;
                input.Look = new Vector2(
                    -U.Clamp(dYaw, -18f, 18f) / scale,
                    -U.Clamp(dPitch, -18f, 18f) / scale);

                bool onTarget = MathF.Abs(dYaw) < 3.5f && MathF.Abs(dPitch) < 3.5f;
                bool visible = sim.Map.LineOfSight(player.EyePosition, target.EyePosition);

                input.FireDown = onTarget && visible && bestDistance < 2200f;
                input.FirePressed = input.FireDown;

                if (bestDistance > 700f) input.Move = new Vector2(rng.Chance(0.5f) ? 0.55f : -0.55f, 1f);
                else if (bestDistance < 320f) input.Move = new Vector2(0f, -1f);
                else input.Move = new Vector2(rng.Chance(0.5f) ? 0.8f : -0.8f, 0.15f);
            }
            else
            {
                input.Move = new Vector2(MathF.Sin(tick * 0.011f), 1f);
                input.Look = new Vector2(MathF.Sin(tick * 0.004f) * 6f, 0f);
            }

            Weapon weapon = player.Current;
            if (weapon.Mag == 0 && !weapon.Reloading && weapon.Reserve > 0) input.Reload = true;

            // The reference's core locomotion: hold jump and never stop. Holding
            // the button chains bunny-hops; the chained air-strafe is what builds
            // speed, and the robot's random strafe direction supplies the input.
            input.Jump = true;

            // Periodic hook swings: fire both hooks out of phase, release inside
            // the same cycle, so every run exercises attach, swing and release.
            if (tick % 210 == 0) input.GrappleLeftPressed = true;
            if (tick % 210 == 70) input.GrappleLeftPressed = true;
            if (tick % 210 == 35) input.GrappleRightPressed = true;
            if (tick % 210 == 95) input.GrappleRightPressed = true;

            // Quick knife, flashlight, secondary blast and slot cycling all get
            // sparse coverage so their input-to-simulation paths stay live.
            input.MeleePressed = tick % 1500 == 700;
            input.FlashlightTogglePressed = tick % 2600 == 1300;
            if (weapon.Def.HasSecondary) input.ZoomPressed = tick % 600 == 300;
            if (tick % 3000 == 200) input.SwitchCycle = 1;

            return input;
        }

        public static void Reset()
        {
        }
    }
}
