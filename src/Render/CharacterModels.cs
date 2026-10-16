using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>
/// Presents structured low-poly character art without feeding anything back into simulation.
/// Raylib mutates a model's pose when an animation is applied, so one loaded model
/// is re-posed immediately before each draw instead of copying pointer-backed Model
/// structs or making a texture-heavy model instance for every actor.
/// </summary>
public static unsafe class CharacterModels
{
    private readonly record struct CharacterArtwork(string Name, string Path, float YawOffset,
        bool HasIntegratedCarbine);

    // The HGO capsule operator is original game-owned art. The legacy conversion stays
    // only as an emergency development fallback; it is not part of the normal
    // runtime presentation.  The view-model path is wholly separate
    // (Renderer.DrawViewModel), so neither choice can replace Gun Game weapon
    // art or affect simulation state.
    private static readonly CharacterArtwork[] Artwork =
    {
        // The source capsule faces Blender -Y; after GLB export its visible
        // front is +Z while HGO yaw 0 faces -Z. Its compact carbine is part of
        // the rigged world model, correctly scaled to its hands.
        new("HGO Capsule Combatant", Cfg.SoldierModelPath, 180f, true),
        new("Soldier fallback", Cfg.SoldierFallbackModelPath, 0f, false),
    };

    private sealed class LoadedCharacter
    {
        public required CharacterArtwork Artwork;
        public Model Model;
        // LoadModelAnimations allocates this native array. It must remain a
        // native pointer until UnloadModelAnimations releases that same block;
        // materialising it as a managed array and unloading that copy corrupts
        // the native heap after screenshot/game shutdown.
        public ModelAnimation* Animations;
        public int AnimationCount;
        public int IdleClip;
        public int RunClip;
        public int WalkClip;
        public int CrouchClip;
        public int ProneClip;
        public float Scale;
        public float GroundLift;
        /// <summary>
        /// World-space lift that puts the lowest lateral vertex on the floor
        /// after the dedicated prone renderer rolls the model forward.
        /// </summary>
        public float ProneGroundLift;
    }

    private static LoadedCharacter? _character;
    private static bool _loadAttempted;

    /// <summary>
    /// Updates the local player's shared character pose without drawing it through
    /// their own camera. This is deliberate first-person behaviour: the full body
    /// remains ready for external viewpoints without clipping into the local view.
    /// </summary>
    public static void UpdateHiddenPose(Actor actor, float simTime)
    {
        LoadedCharacter? character = Get();
        if (character is not null) ApplyPose(character, actor, simTime);
    }

    /// <summary>Draws a visible actor's correctly scaled, independently selected pose.</summary>
    public static bool TryDraw(Actor actor, float simTime)
    {
        LoadedCharacter? character = Get();
        if (character is null) return false;

        ApplyPose(character, actor, simTime);
        bool prone = actor is Player { IsProne: true };
        if (prone)
        {
            DrawProneModel(character, actor);
            // The generated HGO operator contains its own braced world rifle.
            // Do not paint the legacy upright proxy over a prone silhouette.
            return true;
        }

        // The simulation crouch hitbox is 54 rather than 72 units high.  Keep
        // the capsule body grounded at that same height while its dedicated
        // Crouch clip provides the leg motion; this avoids a full-height model
        // visually standing inside a crouched collision volume.
        float crouchHeightScale = actor.Crouching ? Cfg.CrouchHeight / Cfg.StandHeight : 1f;
        float crouchWidthScale = actor.Crouching ? 1.035f : 1f;
        Vector3 position = actor.Position + Vector3.UnitY * character.GroundLift * crouchHeightScale;
        Vector3 scale = new(character.Scale * crouchWidthScale,
            character.Scale * crouchHeightScale, character.Scale * crouchWidthScale);
        Raylib.DrawModelEx(character.Model, position, Vector3.UnitY,
            actor.Yaw + character.Artwork.YawOffset, scale, Color.White);
        if (character.Artwork.HasIntegratedCarbine) DrawCapsuleHeadAccessory(actor, character.Artwork.YawOffset);
        // The preferred HGO operator carries a compact original carbine as a
        // rigid mesh skinned to its right hand. Do not overlay the old proxy on
        // top of that pose; retain the proxy only for the legacy fallback.
        if (!character.Artwork.HasIntegratedCarbine) DrawCarriedWeapon(actor);
        return true;
    }

    /// <summary>
    /// Gives the shared capsule rig a stable, actor-specific high silhouette
    /// without adding duplicate character meshes. These original box-built
    /// accessories are visual-only: actor identity, collision, AI, weapon state
    /// and Gun Game's first-person path are completely unchanged.
    /// </summary>
    private static void DrawCapsuleHeadAccessory(Actor actor, float yawOffset)
    {
        // The mesh's source height is normalised to the same actor head height
        // used by the simulation. Crouch scaling therefore lowers accessories
        // with the visible head without needing a second skeleton or socket API.
        float headTop = actor.HeadHeight;
        int style = Math.Abs(actor.Id) % 4;
        Color warm = U.Rgb(255, 142, 37);
        Color cool = U.Rgb(35, 178, 209);
        Color dark = U.Rgb(26, 43, 54);

        Rlgl.PushMatrix();
        Rlgl.Translatef(actor.Position.X, actor.Position.Y, actor.Position.Z);
        Rlgl.Rotatef(actor.Yaw + yawOffset, 0f, 1f, 0f);
        switch (style)
        {
            case 0: // compact pixel cone
                Raylib.DrawCubeV(new Vector3(0f, headTop + 3f, 0f), new Vector3(18f, 5f, 18f), warm);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 7f, 0f), new Vector3(12f, 5f, 12f), warm);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 11f, 0f), new Vector3(6f, 4f, 6f), warm);
                break;

            case 1: // two leaning horns made from simple low-poly blocks
                DrawAccessoryBlock(new Vector3(-9f, headTop + 6f, 0f), new Vector3(5f, 15f, 6f), -24f, warm);
                DrawAccessoryBlock(new Vector3(9f, headTop + 6f, 0f), new Vector3(5f, 15f, 6f), 24f, warm);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 1.5f, 0f), new Vector3(22f, 3f, 12f), dark);
                break;

            case 2: // square low-poly halo, horizontal above the head
                Raylib.DrawCubeV(new Vector3(0f, headTop + 8f, -9f), new Vector3(27f, 2.5f, 3f), cool);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 8f, 9f), new Vector3(27f, 2.5f, 3f), cool);
                Raylib.DrawCubeV(new Vector3(-12f, headTop + 8f, 0f), new Vector3(3f, 2.5f, 21f), cool);
                Raylib.DrawCubeV(new Vector3(12f, headTop + 8f, 0f), new Vector3(3f, 2.5f, 21f), cool);
                break;

            default: // top hat: brim, crown and bright hatband
                Raylib.DrawCubeV(new Vector3(0f, headTop + 1.5f, 0f), new Vector3(26f, 3f, 24f), dark);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 8f, 0f), new Vector3(16f, 12f, 16f), dark);
                Raylib.DrawCubeV(new Vector3(0f, headTop + 5.5f, 0f), new Vector3(17f, 2f, 17f), warm);
                break;
        }
        Rlgl.PopMatrix();
    }

    /// <summary>Draws a local accessory block with a playful diagonal lean.</summary>
    private static void DrawAccessoryBlock(Vector3 centre, Vector3 size, float roll, Color colour)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(centre.X, centre.Y, centre.Z);
        Rlgl.Rotatef(roll, 0f, 0f, 1f);
        Raylib.DrawCubeV(Vector3.Zero, size, colour);
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Presents the player-only prone stance as a genuinely horizontal model
    /// aligned with their forward axis.  Its pose clip is authored in the GLB;
    /// this matrix roll supplies the final world-space orientation without
    /// changing the actor collision, hit scan or first-person Gun Game path.
    /// </summary>
    private static void DrawProneModel(LoadedCharacter character, Actor actor)
    {
        Vector3 position = actor.Position + Vector3.UnitY * character.ProneGroundLift;
        Rlgl.PushMatrix();
        Rlgl.Translatef(position.X, position.Y, position.Z);
        Rlgl.Rotatef(actor.Yaw + character.Artwork.YawOffset, 0f, 1f, 0f);
        // The exported source stands on +Y. Rotating its local +Y toward +Z
        // places the chest/head in the same forward direction as the actor.
        Rlgl.Rotatef(90f, 1f, 0f, 0f);
        Rlgl.Scalef(character.Scale, character.Scale, character.Scale);
        Raylib.DrawModel(character.Model, Vector3.Zero, 1f, Color.White);
        Rlgl.PopMatrix();
    }

    /// <summary>
    /// Draws the fallback-only world-space weapon. The preferred HGO operator
    /// uses its integrated right-hand-skinned compact carbine instead.
    /// This layer remains presentation-only and cannot affect hit scans, weapon
    /// state or the independent first-person Gun Game view model.
    /// </summary>
    private static void DrawCarriedWeapon(Actor actor)
    {
        WeaponDef def = actor.Current.Def;
        float h = actor.HeadHeight;
        Vector3 forward = U.DirFromAngles(actor.Yaw, 0f);
        Vector3 right = U.RightFromYaw(actor.Yaw);

        // Low-ready rather than aim-down-sights: it reads from the front as a
        // weapon across the chest, stays aligned when the actor turns, and gives
        // the generated hand pose a stable visual target.
        // The imported arm pose places its hands roughly 52 world units above a
        // standing actor's feet and about 10 units forward of the sternum. Keep
        // the compact world model at that same physical point rather than using
        // the larger first-person weapon dimensions.
        Vector3 anchor = actor.Position + Vector3.UnitY * (h * 0.69f)
            + forward * 8.5f + right * 0.5f;
        Color metal = U.Rgb(35, 39, 43);
        Color receiver = def.Class == "Sniper" ? U.Rgb(51, 58, 61) : U.Rgb(48, 52, 55);
        Color furniture = def.Name.StartsWith("AK", StringComparison.OrdinalIgnoreCase)
            ? U.Rgb(92, 61, 37)
            : U.Rgb(29, 32, 35);
        Color detail = U.Rgb(104, 111, 110);

        Rlgl.PushMatrix();
        Rlgl.Translatef(anchor.X, anchor.Y, anchor.Z);
        // Local -Z is the muzzle direction.  The small diagonal is intentional:
        // it makes a low-ready long gun legible from front and side game cameras.
        Rlgl.Rotatef(actor.Yaw + 22f, 0f, 1f, 0f);
        // Long-gun dimensions below are authored in the pre-existing view-model
        // unit language.  At 45%, the largest rifle spans about 36 world units:
        // a believable 0.9 m weapon next to a 72-unit actor, instead of a
        // full-body-width proxy.
        Rlgl.Scalef(0.45f, 0.45f, 0.45f);

        switch (def.Class)
        {
            case "Melee":
                Raylib.DrawCubeV(new Vector3(0f, -2f, -15f), new Vector3(2f, 4f, 25f), metal);
                Raylib.DrawCubeV(new Vector3(0f, -2f, 1f), new Vector3(4.5f, 5f, 10f), furniture);
                break;

            case "Pistol":
                Raylib.DrawCubeV(new Vector3(0f, 1f, -7f), new Vector3(4.8f, 5f, 20f), receiver);
                Raylib.DrawCubeV(new Vector3(0f, -5f, -1f), new Vector3(4.8f, 11f, 8f), furniture);
                Raylib.DrawCubeV(new Vector3(0f, 2f, -18f), new Vector3(2f, 2f, 8f), metal);
                break;

            case "SMG":
                DrawLongGun(receiver, furniture, metal, detail, barrel: 35f, body: 29f,
                    stock: 15f, magazine: 12f, scope: false, pump: false);
                break;

            case "Shotgun":
                DrawLongGun(receiver, U.Rgb(91, 61, 37), metal, detail, barrel: 43f, body: 31f,
                    stock: 20f, magazine: 0f, scope: false, pump: true);
                break;

            case "Sniper":
                DrawLongGun(receiver, furniture, metal, detail, barrel: 51f, body: 37f,
                    stock: 23f, magazine: 11f, scope: true, pump: false);
                break;

            default: // Rifle and future long-gun classes.
                DrawLongGun(receiver, furniture, metal, detail, barrel: 42f, body: 34f,
                    stock: 19f, magazine: 15f, scope: false, pump: false);
                break;
        }

        Rlgl.PopMatrix();
    }

    /// <summary>Constructs an economical world-model silhouette in the current actor-local frame.</summary>
    private static void DrawLongGun(Color receiver, Color furniture, Color metal, Color detail,
        float barrel, float body, float stock, float magazine, bool scope, bool pump)
    {
        float bodyCenter = -body * 0.34f;
        float barrelCenter = -body * 0.5f - barrel * 0.5f;
        Raylib.DrawCubeV(new Vector3(0f, 0f, bodyCenter), new Vector3(6.8f, 7.8f, body), receiver);
        Raylib.DrawCubeV(new Vector3(0f, 0f, barrelCenter), new Vector3(3.1f, 3.1f, barrel), metal);
        Raylib.DrawCubeV(new Vector3(0f, -0.5f, 10f), new Vector3(7.5f, 6.5f, stock), furniture);
        Raylib.DrawCubeV(new Vector3(0f, 5.1f, bodyCenter - 4f), new Vector3(2.2f, 2.8f, 12f), detail);

        if (magazine > 0f)
            Raylib.DrawCubeV(new Vector3(0f, -8f, bodyCenter + 2f), new Vector3(5.2f, 15f, magazine), furniture);
        if (pump)
            Raylib.DrawCubeV(new Vector3(0f, -4.3f, -body * 0.68f), new Vector3(6.2f, 5.5f, 19f), furniture);
        if (scope)
        {
            Raylib.DrawCubeV(new Vector3(0f, 7.2f, -body * 0.53f), new Vector3(4.4f, 4.4f, 19f), metal);
            Raylib.DrawCubeV(new Vector3(0f, 7.2f, -body * 0.82f), new Vector3(5.7f, 5.7f, 3.5f), detail);
        }
    }

    /// <summary>Releases native model and animation data while the graphics context still exists.</summary>
    public static void Shutdown()
    {
        if (_character is not null)
        {
            if (_character.Animations is not null && _character.AnimationCount > 0)
                Raylib.UnloadModelAnimations(_character.Animations, _character.AnimationCount);
            Raylib.UnloadModel(_character.Model);
            _character = null;
        }
        _loadAttempted = false;
    }

    private static void ApplyPose(LoadedCharacter character, Actor actor, float simTime)
    {
        int clip = ClipFor(character, actor);
        ModelAnimation animation = character.Animations[clip];
        if (animation.KeyFrameCount <= 0) return;

        float rate = clip == character.WalkClip || clip == character.CrouchClip
            ? Cfg.CharacterWalkRate
            : clip == character.RunClip ? Cfg.CharacterRunRate
            : 1f;
        float frame = simTime * Cfg.CharacterAnimationFrameRate * rate
            + actor.Id * Cfg.CharacterAnimationPhasePerId;
        frame %= animation.KeyFrameCount;
        if (frame < 0f) frame += animation.KeyFrameCount;
        Raylib.UpdateModelAnimation(character.Model, animation, frame);
    }

    private static int ClipFor(LoadedCharacter character, Actor actor)
    {
        if (actor is Player { IsProne: true }) return character.ProneClip;
        if (!actor.OnGround) return character.IdleClip;
        if (actor.Crouching) return character.CrouchClip;
        if (actor.HorizontalSpeed < Cfg.CharacterIdleSpeed) return character.IdleClip;
        if (actor.HorizontalSpeed < Cfg.CharacterRunSpeed) return character.WalkClip;
        return character.RunClip;
    }

    private static LoadedCharacter? Get()
    {
        if (_loadAttempted) return _character;
        _loadAttempted = true;

        foreach (CharacterArtwork artwork in Artwork)
        {
            LoadedCharacter? loaded = TryLoad(artwork);
            if (loaded is null) continue;
            _character = loaded;
            return _character;
        }

        Console.Error.WriteLine("no compatible character model could be loaded; bots will use the built-in fallback shape");
        return null;
    }

    private static LoadedCharacter? TryLoad(CharacterArtwork artwork)
    {
        string? file = Resolve(artwork.Path);
        if (file is null)
        {
            Console.Error.WriteLine($"character model not found: {artwork.Path} "
                + $"(looked next to the executable and in {Directory.GetCurrentDirectory()})");
            return null;
        }

        Model model = default;
        bool modelLoaded = false;
        ModelAnimation* animations = null;
        int animationCount = 0;
        try
        {
            model = Raylib.LoadModel(file);
            modelLoaded = true;
            if (model.MeshCount <= 0)
            {
                Raylib.UnloadModel(model);
                modelLoaded = false;
                Console.Error.WriteLine($"character model {artwork.Path} contains no meshes");
                return null;
            }

            animations = Raylib.LoadModelAnimations(file, ref animationCount);
            if (animations is null || animationCount <= 0)
            {
                Raylib.UnloadModel(model);
                modelLoaded = false;
                if (animations is not null && animationCount > 0) Raylib.UnloadModelAnimations(animations, animationCount);
                animations = null;
                Console.Error.WriteLine($"character model {artwork.Path} contains no animation clips");
                return null;
            }

            for (int i = 0; i < animationCount; i++)
            {
                ModelAnimation animation = animations[i];
                if (Raylib.IsModelAnimationValid(model, animation)) continue;
                Raylib.UnloadModel(model);
                modelLoaded = false;
                Raylib.UnloadModelAnimations(animations, animationCount);
                animations = null;
                Console.Error.WriteLine($"character model {artwork.Path} has an incompatible animation skeleton");
                return null;
            }

            int idle = FindClip(animations, animationCount, "Idle", fallback: 0);
            int run = FindClip(animations, animationCount, "Run", fallback: 1);
            int walk = FindClip(animations, animationCount, "Walk", fallback: 3);
            if (idle < 0 || run < 0 || walk < 0)
            {
                Raylib.UnloadModel(model);
                modelLoaded = false;
                Raylib.UnloadModelAnimations(animations, animationCount);
                animations = null;
                Console.Error.WriteLine($"character model {artwork.Path} is missing usable Idle, Run or Walk clips");
                return null;
            }
            int crouch = FindClip(animations, animationCount, "Crouch", fallback: walk);
            int prone = FindClip(animations, animationCount, "Prone", fallback: crouch);

            BoundingBox bounds = Raylib.GetModelBoundingBox(model);
            Vector3 size = bounds.Max - bounds.Min;
            if (size.Y <= 0.0001f)
            {
                Raylib.UnloadModel(model);
                modelLoaded = false;
                Raylib.UnloadModelAnimations(animations, animationCount);
                animations = null;
                Console.Error.WriteLine($"character model {artwork.Path} has no measurable standing height");
                return null;
            }
            float scale = Cfg.StandHeight / size.Y;
            float groundLift = -bounds.Min.Y * scale;
            // During prone rendering a +90 degree local-X roll maps local +Z
            // to vertical -Y, so the normal model's maximum +Z is the amount
            // needed to keep the resulting silhouette touching the floor.
            float proneGroundLift = bounds.Max.Z * scale;
            Console.WriteLine($"character model {artwork.Name} ({artwork.Path}): {model.MeshCount} meshes, "
                + $"{animationCount} clips [{ClipSummary(animations, animationCount)}], "
                + $"size {size.X:0.000} x {size.Y:0.000} x {size.Z:0.000}, "
                + $"scale {scale:0.0000}, ground lift {groundLift:0.0000}, prone lift {proneGroundLift:0.0000}");
            return new LoadedCharacter
            {
                Artwork = artwork,
                Model = model,
                Animations = animations,
                AnimationCount = animationCount,
                IdleClip = idle,
                RunClip = run,
                WalkClip = walk,
                CrouchClip = crouch,
                ProneClip = prone,
                Scale = scale,
                GroundLift = groundLift,
                ProneGroundLift = proneGroundLift,
            };
        }
        catch (Exception ex)
        {
            if (animations is not null && animationCount > 0)
                Raylib.UnloadModelAnimations(animations, animationCount);
            if (modelLoaded) Raylib.UnloadModel(model);
            Console.Error.WriteLine($"character model {artwork.Path} could not load: {ex.Message}");
            return null;
        }
    }

    private static int FindClip(ModelAnimation* animations, int count, string name, int fallback)
    {
        for (int i = 0; i < count; i++)
        {
            if (string.Equals(animations[i].NameToString(), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return fallback >= 0 && fallback < count ? fallback : -1;
    }

    private static string ClipSummary(ModelAnimation* animations, int count)
        => string.Join(", ", Enumerable.Range(0, count).Select(i =>
            $"{i}:{animations[i].NameToString()}"));

    private static string? Resolve(string path)
    {
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
        };
        return candidates.FirstOrDefault(File.Exists);
    }
}
