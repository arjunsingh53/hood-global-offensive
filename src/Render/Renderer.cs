using System.Numerics;
using HoodGlobalOffensive.Combat;
using HoodGlobalOffensive.Core;
using HoodGlobalOffensive.Entities;
using HoodGlobalOffensive.Sim;
using HoodGlobalOffensive.World;
using Raylib_cs;

namespace HoodGlobalOffensive.Render;

/// <summary>Draws the world, the other actors, the effects and the first-person weapon.</summary>
public static class Renderer
{
    // The supplied reference calls for a high-contrast arena at sunset, rather
    // than the old neutral blue-grey clear colour.  These are HGO's own palette
    // and cloud shapes; they are deliberately procedural so this build does not
    // ship an unlicensed third-party skybox.
    private static readonly Color SkyZenith = U.Rgb(54, 61, 117);
    private static readonly Color SkyViolet = U.Rgb(110, 79, 140);
    private static readonly Color SkyRose = U.Rgb(219, 126, 145);
    private static readonly Color SkyHorizon = U.Rgb(255, 177, 128);
    private static readonly Color SkyHaze = U.Rgb(255, 185, 147);

    // An authorised, equirectangular panorama can be supplied in this slot in
    // a later art pass. It is intentionally not bundled here: the linked Unity
    // Asset Store skybox has not been downloaded or imported into this repo.
    // When the file is absent, corrupt, or not a 2:1 panorama, HGO retains its
    // original procedural sky instead of stretching arbitrary artwork.
    private const string OptionalSkyPanoramaRelativePath = "assets/sky/hgo-sunset-panorama.png";
    private const int MinimumSkyPanoramaWidth = 512;
    private const int MinimumSkyPanoramaHeight = 256;
    private const float SkyPanoramaAspectTolerance = 0.035f;

    // Small, original screen-space grade for the world render target.  The HUD,
    // crosshair and separate viewmodel target are composited after this shader,
    // keeping aim information crisp and colour-neutral.
    private const string WorldGradeFragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;

        void main()
        {
            vec4 sampleColor = texture(texture0, fragTexCoord) * fragColor * colDiffuse;
            vec3 color = max(sampleColor.rgb, vec3(0.0));
            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));

            // Restrained saturation avoids flattening the supplied Industrial
            // Zone materials while giving the arena a purposeful colour script.
            color = mix(vec3(luminance), color, 1.10);

            // Split-tone contrast: late-sun warmth in lit values and a compact
            // indigo lift in the darks.  It is intentionally subtle enough that
            // enemies and cover retain their authored material colours.
            float highlights = smoothstep(0.38, 0.94, luminance);
            float shadows = 1.0 - smoothstep(0.06, 0.43, luminance);
            color += vec3(0.090, 0.034, 0.010) * highlights;
            color = mix(color, color * vec3(0.73, 0.80, 1.15) + vec3(0.012, 0.004, 0.030), shadows * 0.34);

            // A gentle lens falloff gives the long lanes depth without touching
            // the centre aiming cone.  The procedural sky supplies the actual
            // warm horizon haze before the scene is rendered into this target.
            float distanceFromCentre = length(fragTexCoord - vec2(0.5));
            float vignette = 1.0 - 0.18 * smoothstep(0.32, 0.78, distanceFromCentre);
            color *= vignette;

            finalColor = vec4(clamp(color, 0.0, 1.0), sampleColor.a);
        }
        """;

    // The panorama is projected from the active camera basis on a full-screen
    // pass. Sampling it this way means looking or turning actually reveals a
    // different part of the 2:1 source; it is not a static stretched backdrop.
    // The shader stays deliberately independent from any third-party package.
    private const string EquirectangularSkyFragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        out vec4 finalColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec3 skyForward;
        uniform vec3 skyRight;
        uniform vec3 skyUp;
        uniform vec2 skyProjection;

        const float PI = 3.14159265358979323846;

        void main()
        {
            vec2 screen = fragTexCoord * 2.0 - 1.0;
            vec3 direction = normalize(
                skyForward
                + skyRight * (screen.x * skyProjection.x)
                - skyUp * (screen.y * skyProjection.y));

            // The centre of a conventional panorama is treated as the view
            // forward direction. Repeat wrapping on the source removes the
            // discontinuity when a player turns across the longitude seam.
            float longitude = atan(direction.x, -direction.z);
            float latitude = asin(clamp(direction.y, -1.0, 1.0));
            vec2 panoramaUv = vec2(0.5 + longitude / (2.0 * PI), 0.5 - latitude / PI);
            finalColor = texture(texture0, panoramaUv) * fragColor * colDiffuse;
        }
        """;

    /// <summary>Small viewmodel-local translation only; it never changes the aim direction.</summary>
    private readonly record struct FeedbackOffset(float Right, float Up, float Forward);

    public static Camera3D BuildCamera(Simulation sim, float feedbackStrength = 1f)
    {
        Player p = sim.Player;

        float yaw = p.Yaw + p.Punch.X;
        float pitch = U.Clamp(p.Pitch + p.Punch.Y, -Cfg.MaxPitch, Cfg.MaxPitch);
        Vector3 dir = U.DirFromAngles(yaw, pitch);
        Vector3 right = U.RightFromYaw(yaw);

        float bobX = MathF.Sin(p.BobPhase) * Cfg.ViewBobSide * p.BobAmount;
        float bobY = MathF.Cos(p.BobPhase * 2f) * -Cfg.ViewBobVertical * p.BobAmount;
        float roll = MathF.Sin(p.BobPhase) * Cfg.ViewBobRollDeg * p.BobAmount * U.Deg2Rad;
        Vector3 baseUp = Vector3.Normalize(Vector3.Cross(right, dir));
        Vector3 up = Vector3.Normalize(baseUp * MathF.Cos(roll) + right * MathF.Sin(roll));
        float feedbackFov = GetFeedbackFovKick(p, feedbackStrength);

        Vector3 eye = p.EyePosition
            + right * bobX
            + baseUp * (bobY - p.LandDip * Cfg.ViewLandDipDistance);

        return new Camera3D
        {
            Position = eye,
            Target = eye + dir * 120f,
            Up = up,
            FovY = p.CurrentFov + feedbackFov,
            Projection = CameraProjection.Perspective,
        };
    }

    public static void DrawSky(in Camera3D camera, int width, int height)
    {
        if (width <= 0 || height <= 0) return;

        if (TryDrawOptionalSkyPanorama(camera, width, height))
        {
            // Keep the same warm air-light over any later art-approved plate so
            // the world-grade remains visually coherent with the fallback.
            Raylib.DrawRectangleGradientV(0, (int)(height * 0.43f), width, (int)(height * 0.24f),
                U.Rgba(SkyRose.R, SkyRose.G, SkyRose.B, 24), U.Rgba(SkyHaze.R, SkyHaze.G, SkyHaze.B, 0));
            return;
        }

        Raylib.ClearBackground(SkyZenith);
        DrawProceduralSky(camera, width, height);
    }

    /// <summary>
    /// Opens a world-only render target.  DrawWorld and its 3D camera pass go
    /// between BeginWorldPass and EndWorldPass; UI and the isolated viewmodel
    /// are drawn only afterwards, therefore neither gets colour-graded.
    /// </summary>
    public static void BeginWorldPass(in Camera3D camera, int width, int height)
    {
        EnsureWorldTarget(width, height);
        _worldPassActive = _worldTarget.Id != 0;
        if (_worldPassActive) Raylib.BeginTextureMode(_worldTarget);
        DrawSky(camera, width, height);
    }

    /// <summary>Finishes the world render target and composites its post-grade.</summary>
    public static void EndWorldPass(int width, int height)
    {
        if (!_worldPassActive) return;

        Raylib.EndTextureMode();
        _worldPassActive = false;

        // Render textures are vertically flipped when sampled.  The grade is
        // deliberately applied here, before the viewmodel and HUD passes.
        var source = new Rectangle(0f, 0f, _worldTarget.Texture.Width, -_worldTarget.Texture.Height);
        var destination = new Rectangle(0f, 0f, width, height);
        EnsureWorldGradeShader();

        if (_worldGradeShader.Id != 0)
        {
            Raylib.BeginShaderMode(_worldGradeShader);
            Raylib.DrawTexturePro(_worldTarget.Texture, source, destination, Vector2.Zero, 0f, Color.White);
            Raylib.EndShaderMode();
        }
        else
        {
            // Shader compilation should succeed on the native desktop target;
            // preserve a usable world image if a future platform rejects it.
            Raylib.DrawTexturePro(_worldTarget.Texture, source, destination, Vector2.Zero, 0f, Color.White);
        }
    }

    private static Color SunsetGradient(float t)
    {
        t = U.Clamp01(t);
        if (t < 0.38f)
        {
            float local = SmoothStep(t / 0.38f);
            return Blend(SkyZenith, SkyViolet, local);
        }

        if (t < 0.72f)
        {
            float local = SmoothStep((t - 0.38f) / 0.34f);
            return Blend(SkyViolet, SkyRose, local);
        }

        return Blend(SkyRose, SkyHorizon, SmoothStep((t - 0.72f) / 0.28f));
    }

    /// <summary>
    /// One authored dusk cloud bank. Azimuth and elevation are sky angles in
    /// degrees, so every element keeps its real place as the player looks and
    /// turns; nothing is glued to the screen. Size is in 1080p pixels, phase
    /// seeds the lobe scatter, stretch widens the bank horizontally.
    /// </summary>
    private readonly record struct SkyCloud(float AzimuthDeg, float ElevationDeg, float Size,
        float Opacity, float Phase, float Stretch);

    private static readonly SkyCloud[] SkyClouds =
    {
        new(-162f, 8.5f, 122f, 0.50f, 1.7f, 1.9f),
        new(-128f, 12.0f, 76f, 0.38f, 4.1f, 1.7f),
        new(-96f, 6.5f, 146f, 0.58f, 0.4f, 2.0f),
        new(-60f, 10.0f, 96f, 0.46f, 2.9f, 1.8f),
        new(-26f, 6.0f, 122f, 0.55f, 5.6f, 2.0f),
        new(10f, 14.5f, 68f, 0.36f, 1.2f, 1.6f),
        new(46f, 8.0f, 136f, 0.58f, 3.4f, 2.0f),
        new(84f, 11.5f, 86f, 0.44f, 0.8f, 1.7f),
        new(120f, 6.5f, 124f, 0.54f, 4.9f, 1.9f),
        new(152f, 9.5f, 82f, 0.42f, 2.2f, 1.8f),
        new(-75f, 23.0f, 205f, 0.15f, 3.8f, 3.2f),
        new(-8f, 26.0f, 245f, 0.13f, 1.4f, 3.4f),
        new(70f, 24.0f, 212f, 0.14f, 5.9f, 3.0f),
    };

    /// <summary>Anchor azimuth/elevation of the dusk sun, kept low and warm.</summary>
    private const float SunAzimuthDeg = 34f;
    private const float SunElevationDeg = 5.2f;

    private static void DrawProceduralSky(in Camera3D camera, int width, int height)
    {
        Vector3 dir = Vector3.Normalize(camera.Target - camera.Position);
        float pitch = MathF.Asin(U.Clamp(dir.Y, -1f, 1f));
        float yaw = MathF.Atan2(-dir.X, -dir.Z);
        float halfTanY = MathF.Max(0.05f, MathF.Tan(camera.FovY * U.Deg2Rad * 0.5f));
        float halfTanX = halfTanY * width / MathF.Max(1f, height);

        // Screen y of the true horizon for this camera pose, and the mapping
        // used by every sky element: tan(angle) / tan(half fov). The sky moves
        // with pitch and yaw like real air instead of behaving as wallpaper.
        float horizon = height * 0.5f + height * 0.5f * MathF.Tan(U.Clamp(pitch, -1.35f, 1.35f)) / halfTanY;
        float pixel = height / 1080f;
        float SkyY(float elevationDeg)
            => horizon - height * 0.5f * MathF.Tan(elevationDeg * U.Deg2Rad) / halfTanY;
        float SkyX(float deltaAzimuthRad)
            => width * 0.5f + width * 0.5f * MathF.Tan(deltaAzimuthRad) / halfTanX;

        // Zenith-to-horizon ramp, then the dusk mist that sits below the
        // horizon line so map edges fade into atmosphere instead of void.
        float span = height * 1.15f;
        const int bands = 56;
        for (int i = 0; i < bands; i++)
        {
            float t0 = i / (float)bands;
            int y = (int)MathF.Floor(horizon - span * (1f - t0));
            int next = (int)MathF.Floor(horizon - span * (1f - (i + 1) / (float)bands));
            int bandHeight = Math.Max(1, next - y);
            if (y + bandHeight < 0 || y > height) continue;
            Raylib.DrawRectangle(0, y, width, bandHeight, SunsetGradient(t0 + 0.5f / bands));
        }

        int belowHeight = (int)MathF.Max(1f, height * 0.95f);
        Raylib.DrawRectangleGradientV(0, (int)horizon, width, belowHeight,
            U.Rgba(188, 128, 114, 255), U.Rgba(42, 36, 66, 255));

        // Warm glow smeared across the horizon seam in both directions so the
        // sky and the below-horizon mist never meet on a hard line.
        int glowAbove = (int)MathF.Max(1f, height * 0.11f);
        Raylib.DrawRectangleGradientV(0, (int)(horizon - glowAbove), width, glowAbove,
            U.Rgba(255, 185, 140, 0), U.Rgba(255, 185, 140, 58));
        int glowBelow = (int)MathF.Max(1f, height * 0.09f);
        Raylib.DrawRectangleGradientV(0, (int)horizon, width, glowBelow,
            U.Rgba(255, 185, 140, 52), U.Rgba(255, 185, 140, 0));

        // Warm air-light strongest at the horizon, fading upward.
        int hazeHeight = (int)(height * 0.24f);
        Raylib.DrawRectangleGradientV(0, (int)(horizon - hazeHeight), width, hazeHeight,
            U.Rgba(SkyHaze.R, SkyHaze.G, SkyHaze.B, 0), U.Rgba(SkyHaze.R, SkyHaze.G, SkyHaze.B, 36));

        // Sun glow, anchored to its sky angle.
        float sunDx = WrapAngle(SunAzimuthDeg * U.Deg2Rad - yaw);
        if (MathF.Abs(sunDx) < 1.08f)
        {
            var sun = new Vector2(SkyX(sunDx), SkyY(SunElevationDeg));
            float glowRadius = MathF.Max(30f, height * 0.24f);
            Raylib.DrawCircleGradient(sun, glowRadius, U.Rgba(255, 209, 139, 84), U.Rgba(255, 176, 133, 0));
            Raylib.DrawCircleGradient(sun, glowRadius * 0.30f, U.Rgba(255, 238, 196, 118), U.Rgba(255, 197, 144, 0));
        }

        // Clouds: soft layered puffs with lit tops and shaded undersides.
        // Lobes are deterministic (sine-scattered around the bank) so captures
        // stay reproducible, and every bank fades as it approaches the haze.
        foreach (SkyCloud cloud in SkyClouds)
        {
            float dx = WrapAngle(cloud.AzimuthDeg * U.Deg2Rad - yaw);
            if (MathF.Abs(dx) > 1.15f) continue;

            float cx = SkyX(dx);
            float cy = SkyY(cloud.ElevationDeg);
            float radius = cloud.Size * pixel;
            if (cy < -height * 0.6f || cy > height * 1.7f) continue;

            float elevate = SmoothStep(U.Clamp01((cloud.ElevationDeg - 1.5f) / 6.5f));
            float facing = 0.68f + 0.32f * MathF.Cos(WrapAngle((cloud.AzimuthDeg - SunAzimuthDeg) * U.Deg2Rad));
            int shadeAlpha = (int)(cloud.Opacity * elevate * facing * 142f);
            int litAlpha = (int)(cloud.Opacity * elevate * facing * 238f);
            // Backlit clouds sit against the brightest sunset band; a cool
            // violet underbelly near the sun reads there where a warm tone
            // would vanish into the glow, and drifts warmer farther away.
            Color shadeCool = U.Rgba(116, 96, 148, shadeAlpha);
            Color shadeWarm = U.Rgba(172, 118, 130, shadeAlpha);
            Color shade = Blend(shadeCool, shadeWarm, 1f - facing);
            Color lit = U.Rgba(255, 232, 205, litAlpha);

            // Cumulus silhouette: a row of base puffs on a flat underside with
            // a dome of caps above. The shade pass is offset downward so the
            // underbellies read; the lit pass rides the caps so warm tops catch
            // the dusk light. Deterministic sine scatter, no per-frame rng.
            int baseCount = 4 + (int)(cloud.Size / 50f);
            int capCount = Math.Max(2, baseCount - 1);
            float halfWidth = radius * cloud.Stretch * 1.35f;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int row = 0; row < 2; row++)
                {
                    int count = row == 0 ? baseCount : capCount;
                    for (int i = 0; i < count; i++)
                    {
                        float u = count <= 1 ? 0.5f : i / (float)(count - 1);
                        float jitter = 0.5f + 0.5f * MathF.Sin(i * 12.9898f + cloud.Phase * 5.3f + row * 2.7f);
                        float lx = cx + (u - 0.5f) * halfWidth * (row == 0 ? 1.9f : 1.5f)
                            + MathF.Sin(i * 4.7f + cloud.Phase) * radius * 0.08f;
                        float lr = row == 0
                            ? radius * (0.32f + 0.14f * jitter)
                            : radius * (0.40f + 0.26f * jitter);
                        float ly = row == 0
                            ? cy + lr * 0.16f
                            : cy - lr * (0.30f + 0.42f * jitter);

                        if (pass == 0)
                        {
                            DrawSkyBlob(lx, ly + lr * 0.18f, lr, cloud.Stretch, shade);
                        }
                        else
                        {
                            if (row == 0 && jitter < 0.45f) continue;
                            DrawSkyBlob(lx - radius * 0.06f, ly - lr * 0.16f, lr * 0.88f, cloud.Stretch, lit);
                        }
                    }
                }
            }
        }
    }

    private static void DrawSkyBlob(float x, float y, float radius, float stretch, Color core)
    {
        Rlgl.PushMatrix();
        Rlgl.Translatef(x, y, 0f);
        Rlgl.Scalef(stretch, 1f, 1f);
        // A soft halo plus a denser core reads as a puff with form instead of
        // a flat elliptical smudge.
        Raylib.DrawCircleGradient(Vector2.Zero, radius,
            U.Rgba(core.R, core.G, core.B, core.A / 2), U.Rgba(core.R, core.G, core.B, 0));
        Raylib.DrawCircleGradient(Vector2.Zero, radius * 0.62f, core, U.Rgba(core.R, core.G, core.B, 0));
        Rlgl.PopMatrix();
    }

    private static float WrapAngle(float radians)
    {
        radians %= MathF.Tau;
        if (radians > MathF.PI) radians -= MathF.Tau;
        if (radians < -MathF.PI) radians += MathF.Tau;
        return radians;
    }

    private static float SmoothStep(float value)
    {
        value = U.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static Color Blend(Color a, Color b, float t) => U.Rgba(
        (int)U.Lerp(a.R, b.R, t),
        (int)U.Lerp(a.G, b.G, t),
        (int)U.Lerp(a.B, b.B, t),
        (int)U.Lerp(a.A, b.A, t));

    /// <summary>
    /// Renders an unmodified, wide overview of the supplied Industrial Zone for
    /// the title screen. It is deliberately a camera-only presentation layer:
    /// no actors, collision brushes, replacement buildings or generated props
    /// are introduced here.
    /// </summary>
    public static void DrawMenuBackdrop(int width, int height)
    {
        // The title scene is still a real world camera pass, so use the same
        // grade as gameplay. Screen UI drawn by Screens.MainMenu follows this
        // method and remains ungraded.
        Vector3 target = IndustrialZoneLayout.RawToWorld(8f, -8f) + Vector3.UnitY * 72f;
        Vector3 position = IndustrialZoneLayout.RawToWorld(-78f, 62f) + new Vector3(0f, 460f, 0f);
        var camera = new Camera3D
        {
            Position = position,
            Target = target,
            Up = Vector3.UnitY,
            FovY = 58f,
            Projection = CameraProjection.Perspective,
        };

        BeginWorldPass(camera, width, height);

        Rlgl.SetClipPlanes(1f, 9000f);
        Raylib.BeginMode3D(camera);
        IndustrialZoneModel.TryDraw();
        Raylib.EndMode3D();
        EndWorldPass(width, height);
    }

    /// <summary>
    /// Converts the independent event envelopes into a restrained viewmodel-local
    /// offset. The world camera is deliberately not translated by this layer:
    /// physical camera origin, direction and hit-scan origin stay identical.
    /// </summary>
    private static FeedbackOffset GetFeedbackOffset(Player p, float viewYaw, float strength)
    {
        strength = U.Clamp01(strength);
        if (strength <= 0f) return default;

        // Scope use deliberately dampens the effect instead of muting the game
        // state. This keeps the sight picture stable while preserving feedback in
        // hip fire and while zoom transitions are in progress.
        float adsScale = U.Lerp(1f, Cfg.FeedbackAdsScale, p.ZoomBlend);
        float scale = strength * adsScale;

        float footstep = p.FootstepFeedback * scale;
        float landing = p.LandingFeedback * scale;
        float damage = p.DamageFeedback * scale;

        float cadence = 0.68f + 0.32f * MathF.Cos(p.BobPhase);
        float incomingRadians = (p.DamageFeedbackYaw - viewYaw) * U.Deg2Rad;
        float incomingSide = MathF.Sin(incomingRadians);

        return new FeedbackOffset(
            -footstep * cadence * Cfg.FeedbackFootstepSideDistance
                - incomingSide * damage * Cfg.FeedbackDamageSideDistance,
            -footstep * Cfg.FeedbackFootstepDropDistance
                - landing * Cfg.FeedbackLandingDropDistance
                - damage * Cfg.FeedbackDamageDropDistance,
            -damage * Cfg.FeedbackDamagePushDistance);
    }

    /// <summary>
    /// A small lens response is the world-camera portion of the feedback. Unlike
    /// a positional shake, changing FOV preserves the same centre ray at every
    /// range, so a user preference cannot introduce shot/reticle parallax.
    /// </summary>
    private static float GetFeedbackFovKick(Player p, float strength)
    {
        strength = U.Clamp01(strength);
        if (strength <= 0f) return 0f;

        float adsScale = U.Lerp(1f, Cfg.FeedbackAdsScale, p.ZoomBlend);
        float scale = strength * adsScale;
        return (p.FootstepFeedback * Cfg.FeedbackFootstepFovDegrees
            + p.LandingFeedback * Cfg.FeedbackLandingFovDegrees
            + p.DamageFeedback * Cfg.FeedbackDamageFovDegrees) * scale;
    }

    public static void DrawWorld(Simulation sim, Camera3D cam)
    {
        // "Industrial" draws the supplied GLB scene: physics brushes still exist
        // in GameMap but they are an invisible traversal sidecar there. The box
        // catalog maps invert that: the collision brushes ARE the level visuals.
        if (sim.Map.RendersGlb)
        {
            if (!IndustrialZoneModel.TryDraw()) DrawBoxMap(sim.Map);
        }
        else
        {
            DrawBoxMap(sim.Map);
        }

        DrawStations(sim);
        DrawGrappleRopes(sim);
        DrawFlashlight(sim);
        DrawDecals(sim);
        DrawActors(sim, cam);
        DrawParticles(sim);
        DrawTracers(sim, cam);
    }

    /// <summary>Draws collision brushes as lit blocks for the box-built maps.</summary>
    private static void DrawBoxMap(GameMap map)
    {
        foreach (Brush brush in map.Brushes)
        {
            Aabb box = brush.Box;
            Raylib.DrawCubeV(box.Center, box.Size, brush.Color);
            Raylib.DrawCubeWiresV(box.Center, box.Size, Shade(brush.Color, 0.62f));
        }
    }

    /// <summary>Draws the kill-point cabinets and their amber location beacons.</summary>
    private static void DrawStations(Simulation sim)
    {
        foreach (UpgradeStation station in sim.Stations)
        {
            Vector3 basePosition = station.Position;
            var body = new Vector3(46f, 58f, 36f);
            Raylib.DrawCubeV(basePosition + new Vector3(0f, 29f, 0f), body, U.Rgb(50, 62, 78));
            Raylib.DrawCubeWiresV(basePosition + new Vector3(0f, 29f, 0f), body, U.Rgb(96, 118, 140));
            Raylib.DrawCubeV(basePosition + new Vector3(0f, 46f, -19f), new Vector3(30f, 16f, 2f), U.Rgb(255, 196, 72));

            // A tall, faint light column makes the cabinet readable across the arena.
            Raylib.DrawCubeV(basePosition + new Vector3(0f, 128f, 0f), new Vector3(16f, 200f, 16f),
                U.Rgba(255, 186, 56, 26));
        }
    }

    /// <summary>Draws both grapple ropes from the player's eyes to their anchors.</summary>
    private static void DrawGrappleRopes(Simulation sim)
    {
        Player p = sim.Player;
        if (!p.Alive) return;

        Vector3 eye = p.EyePosition;
        DrawRope(eye, p.HookLeft);
        DrawRope(eye, p.HookRight);
    }

    private static void DrawRope(Vector3 eye, GrappleHook hook)
    {
        if (!hook.Attached) return;

        Color rope = U.Rgb(214, 196, 150);
        Raylib.DrawLine3D(eye, hook.Anchor, rope);

        // The anchor spike: a small amber cube buried at the attach point.
        Raylib.DrawCubeV(hook.Anchor + Vector3.Normalize(eye - hook.Anchor) * 4f,
            new Vector3(7f, 7f, 7f), U.Rgb(255, 186, 56));
    }

    /// <summary>
    /// The flashlight is pure presentation: a translucent cone plus a lens
    /// glow. It lights nothing in the simulation and never changes vision.
    /// </summary>
    private static void DrawFlashlight(Simulation sim)
    {
        Player p = sim.Player;
        if (!p.Alive || !p.FlashlightOn) return;

        Vector3 dir = p.AimDirection;
        Vector3 start = p.EyePosition + dir * 14f;
        Vector3 end = start + dir * 1500f;
        Raylib.DrawCylinderEx(start, end, 2f, 150f, 10, U.Rgba(255, 240, 180, 40));
        Raylib.DrawCylinderEx(start, start + dir * 500f, 2f, 60f, 10, U.Rgba(255, 240, 180, 30));
        Raylib.DrawSphere(start, 5f, U.Rgba(255, 244, 200, 120));
    }

    private static void DrawDecals(Simulation sim)
    {
        DrawDecalList(sim.Decals);
        DrawDecalList(sim.BloodSplats);
    }

    private static void DrawDecalList(IReadOnlyList<Decal> decals)
    {
        foreach (Decal d in decals)
        {
            float alpha = U.Clamp01(d.Life / d.MaxLife);
            Color c = U.Rgba(d.Color.R, d.Color.G, d.Color.B, (int)(d.Color.A * alpha));

            Vector3 n = d.Normal;
            float ax = MathF.Abs(n.X), ay = MathF.Abs(n.Y), az = MathF.Abs(n.Z);
            Vector3 size;
            if (ay >= ax && ay >= az) size = new Vector3(d.Size, 0.35f, d.Size);
            else if (ax >= az) size = new Vector3(0.35f, d.Size, d.Size);
            else size = new Vector3(d.Size, d.Size, 0.35f);

            Raylib.DrawCubeV(d.Position, size, c);
        }
    }

    private static void DrawActors(Simulation sim, Camera3D cam)
    {
        foreach (Actor actor in sim.Actors)
        {
            if (!actor.Alive) continue;

            if (actor.IsPlayer)
            {
                // The local player uses the same skinned model as every bot, but a
                // first-person camera must not draw the full body around its own eyes.
                // This keeps the actor's pose ready for a future remote/third-person pass.
                CharacterModels.UpdateHiddenPose(actor, sim.Time);
                continue;
            }

            DrawCharacter(actor, cam, sim.Time);
        }
    }

    private static void DrawCharacter(Actor actor, Camera3D cam, float simTime)
    {
        Vector3 feet = actor.Position;
        if ((feet - cam.Position).Length() > Cfg.CharacterCullDistance) return;

        // Ground contact keeps the character readable against bright floors even
        // though the default Raylib material has no dynamic shadow map. This is
        // a shallow two-layer ellipse, not a visible rectangular collision-like
        // block beneath every bot.
        DrawGroundContactShadow(feet);

        if (CharacterModels.TryDraw(actor, simTime)) return;

        DrawFallbackCharacter(actor);
    }

    private static void DrawGroundContactShadow(Vector3 feet)
    {
        const int segments = 20;
        float radius = Cfg.CharacterShadowDiameter * 0.5f;
        Vector3 center = feet + Vector3.UnitY * 0.08f;

        // Broad low-alpha outer penumbra followed by a compact umbra. The map
        // itself is left untouched and the triangles have no collision role.
        for (int layer = 0; layer < 2; layer++)
        {
            float scale = layer == 0 ? 1f : 0.63f;
            Color color = layer == 0 ? U.Rgba(0, 0, 0, 20) : U.Rgba(0, 0, 0, 36);
            float xRadius = radius * scale;
            float zRadius = radius * 0.60f * scale;

            for (int i = 0; i < segments; i++)
            {
                float angleA = i * MathF.Tau / segments;
                float angleB = (i + 1) * MathF.Tau / segments;
                Vector3 a = center + new Vector3(MathF.Cos(angleA) * xRadius, 0f, MathF.Sin(angleA) * zRadius);
                Vector3 b = center + new Vector3(MathF.Cos(angleB) * xRadius, 0f, MathF.Sin(angleB) * zRadius);
                // Winding faces upward (+Y), so this remains visible if a future
                // render pass enables backface culling.
                Raylib.DrawTriangle3D(center, b, a, color);
            }
        }
    }

    /// <summary>Temporary no-asset fallback so a bad local art file never hides a bot.</summary>
    private static void DrawFallbackCharacter(Actor actor)
    {
        Vector3 feet = actor.Position;

        float h = actor.HeadHeight;
        Vector3 forward = U.DirFromAngles(actor.Yaw, 0f);
        Vector3 right = U.RightFromYaw(actor.Yaw);

        Color palette = BotPalette(actor.Id);
        Color pants = U.Rgb(58, 58, 66);
        Color vest = palette;
        Color skin = U.Rgb(196, 152, 120);

        // legs
        Raylib.DrawCubeV(feet + new Vector3(0f, h * 0.22f, 0f), new Vector3(13f, h * 0.44f, 15f), pants);
        // torso
        Vector3 torsoCenter = feet + new Vector3(0f, h * 0.63f, 0f);
        Raylib.DrawCubeV(torsoCenter, new Vector3(23f, h * 0.38f, 16f), vest);
        Raylib.DrawCubeWiresV(feet + new Vector3(0f, h * 0.22f, 0f), new Vector3(13f, h * 0.44f, 15f), Shade(pants, 0.6f));
        // shoulders / arms
        Vector3 shoulder = feet + new Vector3(0f, h * 0.74f, 0f);
        Raylib.DrawCubeV(shoulder + right * 8f, new Vector3(7f, h * 0.30f, 8f), Shade(vest, 0.85f));
        Raylib.DrawCubeV(shoulder - right * 8f, new Vector3(7f, h * 0.30f, 8f), Shade(vest, 0.85f));
        // head + helmet
        Vector3 head = feet + new Vector3(0f, h - 7f, 0f);
        Raylib.DrawSphere(head, 7f, skin);
        Raylib.DrawCubeV(head + new Vector3(0f, 3.5f, 0f), new Vector3(15f, 7f, 15f), Shade(vest, 0.7f));

        // weapon held out in front
        Vector3 gunBase = feet + new Vector3(0f, h * 0.66f, 0f) + forward * 12f + right * 6f;
        Raylib.DrawCubeV(gunBase + forward * 10f, new Vector3(4f, 5f, 22f), U.Rgb(44, 44, 48));
        Raylib.DrawCubeV(gunBase - forward * 3f, new Vector3(5f, 9f, 8f), U.Rgb(66, 50, 34));
    }

    private static void DrawParticles(Simulation sim)
    {
        DrawParticleList(sim.Particles);
        DrawParticleList(sim.BloodMists);

        foreach (BloodDroplet d in sim.BloodDroplets)
        {
            float t = U.Clamp01(d.Life / d.MaxLife);
            Color c = U.Rgba(d.Color.R, d.Color.G, d.Color.B, (int)(d.Color.A * t));
            float s = d.Size * (0.45f + t * 0.55f);
            Raylib.DrawCubeV(d.Position, new Vector3(s, s, s), c);
        }
    }

    private static void DrawParticleList(IReadOnlyList<Particle> particles)
    {
        foreach (Particle p in particles)
        {
            float t = U.Clamp01(p.Life / p.MaxLife);
            Color c = U.Rgba(p.Color.R, p.Color.G, p.Color.B, (int)(p.Color.A * t));
            float s = p.Size * (0.5f + t * 0.5f);
            if (p.Kind == ParticleKind.BloodMist)
            {
                float radius = s * (1.0f + (1f - t) * 0.55f) * 0.5f;
                // A sparse faceted puff reads as mist rather than a single
                // opaque red balloon. Individual particles were already spread
                // at spawn, so this remains a compact long-range hit cue.
                Raylib.DrawSphereEx(p.Position, radius, 4, 5, c);
            }
            else
            {
                Raylib.DrawCubeV(p.Position, new Vector3(s, s, s), c);
            }
        }
    }

    private static void DrawTracers(Simulation sim, Camera3D cam)
    {
        Raylib.BeginBlendMode(BlendMode.Additive);
        foreach (Tracer t in sim.Tracers)
        {
            float a = U.Clamp01(t.Life / t.MaxLife);
            Vector3 delta = t.To - t.From;
            float lengthSquared = delta.LengthSquared();
            if (lengthSquared < 0.0001f) continue;

            Vector3 direction = Vector3.Normalize(delta);
            Vector3 midpoint = (t.From + t.To) * 0.5f;
            Vector3 toCamera = cam.Position - midpoint;
            if (toCamera.LengthSquared() < 0.0001f) toCamera = Vector3.UnitY;
            else toCamera = Vector3.Normalize(toCamera);

            // A crossed camera-facing ribbon gives the existing Width field real
            // screen presence at any viewing angle.  The thin white core remains
            // readable against the sunset, while two translucent shells sell the
            // hot air/luminance around an active round.
            Vector3 side = Vector3.Cross(direction, toCamera);
            if (side.LengthSquared() < 0.0001f) side = Vector3.Cross(direction, Vector3.UnitY);
            if (side.LengthSquared() < 0.0001f) side = Vector3.UnitX;
            else side = Vector3.Normalize(side);
            Vector3 up = Vector3.Normalize(Vector3.Cross(side, direction));

            float width = MathF.Max(0.08f, t.Width);
            Color outer = U.Rgba(t.Color.R, t.Color.G, t.Color.B, (int)(62f * a));
            Color middle = U.Rgba(255, Math.Max(t.Color.G, (byte)202), Math.Max(t.Color.B, (byte)112), (int)(112f * a));
            Color core = U.Rgba(255, 249, 220, (int)(236f * a));

            DrawTracerRibbon(t.From, t.To, side * (width * 0.90f), outer);
            DrawTracerRibbon(t.From, t.To, up * (width * 0.90f), outer);
            DrawTracerRibbon(t.From, t.To, side * (width * 0.38f), middle);
            DrawTracerRibbon(t.From, t.To, up * (width * 0.38f), middle);
            DrawTracerRibbon(t.From, t.To, side * (width * 0.12f), core);
            Raylib.DrawLine3D(t.From, t.To, core);
        }
        Raylib.EndBlendMode();
    }

    private static void DrawTracerRibbon(Vector3 from, Vector3 to, Vector3 halfWidth, Color colour)
    {
        Vector3 fromLeft = from - halfWidth;
        Vector3 fromRight = from + halfWidth;
        Vector3 toRight = to + halfWidth;
        Vector3 toLeft = to - halfWidth;

        // Submit both windings so an otherwise perfectly side-on tracer remains
        // visible regardless of render-state culling on a future platform.
        Raylib.DrawTriangle3D(fromLeft, fromRight, toRight, colour);
        Raylib.DrawTriangle3D(fromLeft, toRight, toLeft, colour);
        Raylib.DrawTriangle3D(toRight, fromRight, fromLeft, colour);
        Raylib.DrawTriangle3D(toLeft, toRight, fromLeft, colour);
    }

    // ---------------------------------------------------------------- view model

    /// <summary>
    /// Per-weapon view-model measurements, in the model's own local units:
    /// how far the muzzle reaches forward, how far the stock reaches back, the
    /// bore height and how much the whole model is scaled up.
    /// </summary>
    private readonly record struct ViewModelPose(float Forward, float Back, float Bore, float Scale);

    private static ViewModelPose PoseFor(WeaponDef def) => def.Class switch
    {
        "Melee" => new ViewModelPose(27f, 0f, -1f, 1.10f),
        "Pistol" => new ViewModelPose(25f, 2f, -1f, 1.20f),
        "SMG" => new ViewModelPose(40f, 3f, -1f, 1.10f),
        "Rifle" => new ViewModelPose(50f, 8f, 1f, 1.05f),
        "Shotgun" => new ViewModelPose(51f, 4f, 1f, 1.05f),
        "Sniper" => new ViewModelPose(64f, 11f, 1f, 1.00f),
        _ => new ViewModelPose(30f, 6f, 0f, 1.15f),
    };

    /// <summary>
    /// Places the weapon so its breech sits just in front of the camera and its
    /// muzzle always points well below the crosshair. The vertical offset grows
    /// with the weapon's length, which is what stops a long rifle from reaching
    /// up into the centre of the screen.
    /// </summary>
    private static Vector3 AnchorFor(in ViewModelPose pose) => new(
        12f,
        -(pose.Forward * pose.Scale) * 0.34f - 4f,
        -MathF.Max(0f, pose.Back) * pose.Scale - 5f);

    /// <summary>
    /// Draws the first-person weapon in a camera-local basis. Inside the pushed
    /// transform -Z is forward, +X right and +Y up, so the gun inherits the view
    /// angles and every part can be an axis-aligned box.
    /// </summary>
    public static void DrawViewModel(Simulation sim, int width, int height, float feedbackStrength = 1f)
    {
        // Cleared first: if the weapon is stowed this frame the render target still
        // holds the previous frame's image, which must not be composited or the
        // player would see a frozen ghost of the gun while scoped.
        _viewModelDrawn = false;

        if (sim.MatchOver) return;
        Player p = sim.Player;
        Weapon weapon = p.Current;
        WeaponDef def = weapon.Def;

        // Only magnified optics stow the model behind a scope mask.  Iron sights
        // stay rendered and are aligned with the centre ray as they come up.
        if (def.HasScope && p.ZoomBlend > 0.70f) return;

        float reloadDip = 0f;
        if (weapon.Reloading)
        {
            float duration = MathF.Max(0.001f, def.ReloadTime);
            float reloadT = U.Clamp01((sim.Time - weapon.ReloadStartTime) / duration);
            reloadDip = MathF.Sin(reloadT * MathF.PI);
            // HGO's own readability cue: an empty reload carries a slightly
            // fuller flourish, while the simulation timing/ammo transfer stays
            // exactly the same.
            reloadDip *= weapon.ReloadStartedEmpty ? 1.13f : 0.86f;
        }

        // Meshes are slimmer than the boxes the reload flourish was tuned against, so
        // they swing out of frame at the full angle; each art weapon scales it.
        if (Artwork.TryGetValue(def.Name, out WeaponArtwork art)) reloadDip *= art.ReloadSwing;

        // Pull-out raise: while the freshly drawn gun is still deploying, it
        // rides low in frame and settles up as the fire-ready moment arrives.
        float deployDip = 0f;
        float readyIn = weapon.ReadyTime - sim.Time;
        if (readyIn > 0f && def.DeployTime > 0.0001f)
        {
            float t = U.Clamp01(readyIn / def.DeployTime);
            deployDip = t * t * (3f - 2f * t);
        }

        // Animated rigs carry their own reload/deploy motion in the clip, so the
        // generic flourish is scaled down instead of stacking on top of it.
        if (AnimatedViewmodels.Has(def))
        {
            reloadDip *= AnimatedViewmodels.FlourishScale;
            deployDip *= AnimatedViewmodels.FlourishScale;
        }

        float kick = p.ViewKick;
        float bobX = MathF.Sin(p.BobPhase) * Cfg.ViewModelBobSide * p.BobAmount;
        float bobY = MathF.Cos(p.BobPhase * 2f) * Cfg.ViewModelBobVertical * p.BobAmount;
        FeedbackOffset feedback = GetFeedbackOffset(p, p.Yaw + p.Punch.X, feedbackStrength);
        float ironSight = def.HasCalibratedIronSights ? p.ZoomBlend : 0f;
        Vector3 springOffset = p.ViewModelOffsetSpring.Value;
        Vector3 springRotation = p.ViewModelRotationSpring.Value;

        ViewModelPose pose = PoseFor(def);

        // Local offsets: -Z is forward, +X right, +Y up.
        Vector3 offset = AnchorFor(pose) + new Vector3(
            p.Sway.X * 2.4f + bobX + feedback.Right * Cfg.FeedbackViewModelScale,
            -p.Sway.Y * 2.4f + bobY - p.LandDip * 6f - reloadDip * 3f - deployDip * 5.5f
                + feedback.Up * Cfg.FeedbackViewModelScale,
            kick * 0.22f - feedback.Forward * Cfg.FeedbackViewModelScale);

        offset += springOffset;

        // This is a presentation-only move toward the weapon's actual sight
        // plane. It never changes the world camera origin, AimDirection, spread,
        // hitscan, or recoil; the centre ray stays authoritative.
        offset += IronSightOffsetFor(def) * ironSight;

        // The weapon is drawn into its own render target and composited on top of the
        // scene. In the world's pass a wall closer than the barrel would hide the
        // weapon whenever the player hugs cover, and raylib's batched renderer does
        // not honour depth-test changes mid-pass, so a private depth buffer is the
        // reliable fix.
        EnsureViewModelTarget(width, height);

        Raylib.BeginTextureMode(_viewModelTarget);
        Raylib.ClearBackground(Color.Blank);

        // The camera must face the same way as the world camera, otherwise the
        // weapon's own yaw/pitch rotation is no longer cancelled out and the gun
        // swings across the screen as the player looks around.
        Vector3 viewDirection = U.DirFromAngles(
            p.Yaw + p.Punch.X,
            U.Clamp(p.Pitch + p.Punch.Y, -Cfg.MaxPitch, Cfg.MaxPitch));

        var viewModelCamera = new Camera3D
        {
            Position = Vector3.Zero,
            Target = viewDirection,
            Up = new Vector3(0f, 1f, 0f),
            FovY = Cfg.ViewModelFov,
            Projection = CameraProjection.Perspective,
        };

        Rlgl.SetClipPlanes(Cfg.ViewModelNearClip, Cfg.ViewModelFarClip);
        Raylib.BeginMode3D(viewModelCamera);

        PushViewModelTransform(sim, offset, pose, kick, reloadDip, ironSight, springRotation);
        Vector3? muzzle = DrawWeaponShape(sim, def, p, kick, reloadDip);
        // The knife already uses the simulation's melee slot; it must not inherit
        // the firearm-only flash just because it shares the general weapon pipeline.
        if (p.MuzzleFlash > 0f && def.Class != "Melee") DrawMuzzleFlash(sim, pose, muzzle);
        Rlgl.PopMatrix();

        Raylib.EndMode3D();
        Raylib.EndTextureMode();

        _viewModelDrawn = true;
    }

    /// <summary>Composites the weapon pass over the finished scene. Call after EndMode3D.</summary>
    public static void BlitViewModel(int width, int height)
    {
        if (!_viewModelDrawn || _viewModelTarget.Id == 0) return;

        // Render textures come back vertically flipped.
        var source = new Rectangle(0f, 0f, _viewModelTarget.Texture.Width, -_viewModelTarget.Texture.Height);
        var destination = new Rectangle(0f, 0f, width, height);

        Raylib.BeginBlendMode(BlendMode.Alpha);
        Raylib.DrawTexturePro(_viewModelTarget.Texture, source, destination, Vector2.Zero, 0f, Color.White);
        Raylib.EndBlendMode();
    }

    private static RenderTexture2D _viewModelTarget;
    private static int _viewModelWidth;
    private static int _viewModelHeight;
    private static bool _viewModelDrawn;

    // The world target is deliberately distinct from the viewmodel target. It
    // owns terrain, actors and combat effects only; the weapon and HUD are added
    // afterwards at native screen colour values.
    private static RenderTexture2D _worldTarget;
    private static int _worldWidth;
    private static int _worldHeight;
    private static bool _worldPassActive;
    private static Shader _worldGradeShader;
    private static bool _worldGradeShaderTried;

    // Optional local art slot; it remains empty in this build because no usable
    // licensed source panorama accompanied the linked Asset Store page.
    private static Texture2D _optionalSkyPanorama;
    private static bool _optionalSkyPanoramaChecked;
    private static Shader _equirectangularSkyShader;
    private static bool _equirectangularSkyShaderTried;
    private static int _skyForwardLocation = -1;
    private static int _skyRightLocation = -1;
    private static int _skyUpLocation = -1;
    private static int _skyProjectionLocation = -1;

    private static void EnsureViewModelTarget(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_viewModelTarget.Id != 0 && _viewModelWidth == width && _viewModelHeight == height) return;

        if (_viewModelTarget.Id != 0) Raylib.UnloadRenderTexture(_viewModelTarget);
        _viewModelTarget = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(_viewModelTarget.Texture, TextureFilter.Bilinear);
        _viewModelWidth = width;
        _viewModelHeight = height;
    }

    private static void EnsureWorldTarget(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_worldTarget.Id != 0 && _worldWidth == width && _worldHeight == height) return;

        if (_worldPassActive)
        {
            // This only occurs if a caller resizes in the middle of a pass. End
            // cleanly rather than unloading an active render target.
            Raylib.EndTextureMode();
            _worldPassActive = false;
        }

        if (_worldTarget.Id != 0) Raylib.UnloadRenderTexture(_worldTarget);
        _worldTarget = Raylib.LoadRenderTexture(width, height);
        if (_worldTarget.Id != 0) Raylib.SetTextureFilter(_worldTarget.Texture, TextureFilter.Bilinear);
        _worldWidth = width;
        _worldHeight = height;
    }

    private static void EnsureWorldGradeShader()
    {
        if (_worldGradeShaderTried) return;
        _worldGradeShaderTried = true;

        try
        {
            // Passing a null vertex source selects raylib's stock vertex shader;
            // the fragment source below is the only custom stage.
            _worldGradeShader = Raylib.LoadShaderFromMemory(null!, WorldGradeFragmentShader);
        }
        catch
        {
            // The ungraded blit in EndWorldPass is intentional platform fallback.
            _worldGradeShader = default;
        }
    }

    /// <summary>
    /// Draws a supplied 2:1 panorama as a camera-reactive sky. The source is
    /// rejected before it reaches the renderer when it cannot be a sensible
    /// equirectangular input, preserving the procedural fallback instead.
    /// </summary>
    private static bool TryDrawOptionalSkyPanorama(in Camera3D camera, int width, int height)
    {
        if (!_optionalSkyPanoramaChecked)
        {
            _optionalSkyPanoramaChecked = true;
            TryLoadOptionalSkyPanorama();
        }

        if (_optionalSkyPanorama.Id == 0) return false;

        EnsureEquirectangularSkyShader();
        if (_equirectangularSkyShader.Id == 0) return false;

        Vector3 forward = U.NormalizeOrZero(camera.Target - camera.Position);
        Vector3 cameraUp = U.NormalizeOrZero(camera.Up);
        Vector3 right = U.NormalizeOrZero(Vector3.Cross(forward, cameraUp));
        Vector3 up = U.NormalizeOrZero(Vector3.Cross(right, forward));
        if (forward == Vector3.Zero || right == Vector3.Zero || up == Vector3.Zero) return false;

        float verticalTangent = MathF.Tan(camera.FovY * U.Deg2Rad * 0.5f);
        if (!float.IsFinite(verticalTangent) || verticalTangent <= 0f) return false;

        Vector2 projection = new(verticalTangent * width / height, verticalTangent);
        Raylib.SetShaderValue(_equirectangularSkyShader, _skyForwardLocation, forward, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_equirectangularSkyShader, _skyRightLocation, right, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_equirectangularSkyShader, _skyUpLocation, up, ShaderUniformDataType.Vec3);
        Raylib.SetShaderValue(_equirectangularSkyShader, _skyProjectionLocation, projection, ShaderUniformDataType.Vec2);

        var source = new Rectangle(0f, 0f, _optionalSkyPanorama.Width, _optionalSkyPanorama.Height);
        var destination = new Rectangle(0f, 0f, width, height);
        Raylib.BeginShaderMode(_equirectangularSkyShader);
        Raylib.DrawTexturePro(_optionalSkyPanorama, source, destination, Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        return true;
    }

    private static void TryLoadOptionalSkyPanorama()
    {
        string candidate = Path.Combine(AppContext.BaseDirectory, OptionalSkyPanoramaRelativePath);
        if (!File.Exists(candidate)) return;

        Image image = default;
        try
        {
            image = Raylib.LoadImage(candidate);
            if (!IsValidSkyPanorama(image)) return;

            Texture2D texture = Raylib.LoadTexture(candidate);
            if (texture.Id == 0 || !IsValidSkyPanorama(texture))
            {
                if (texture.Id != 0) Raylib.UnloadTexture(texture);
                return;
            }

            _optionalSkyPanorama = texture;
            Raylib.SetTextureFilter(_optionalSkyPanorama, TextureFilter.Trilinear);
            Raylib.SetTextureWrap(_optionalSkyPanorama, TextureWrap.Repeat);
        }
        catch
        {
            // A malformed or GPU-incompatible file remains a non-fatal art
            // input failure; DrawSky will continue with the procedural sky.
            if (_optionalSkyPanorama.Id != 0)
            {
                Raylib.UnloadTexture(_optionalSkyPanorama);
                _optionalSkyPanorama = default;
            }
        }
        finally
        {
            if (HasImageData(image)) Raylib.UnloadImage(image);
        }
    }

    private static unsafe bool HasImageData(in Image image) => image.Data != null;

    private static bool IsValidSkyPanorama(in Image image)
        => IsValidSkyPanorama(image.Width, image.Height);

    private static bool IsValidSkyPanorama(in Texture2D texture)
        => IsValidSkyPanorama(texture.Width, texture.Height);

    private static bool IsValidSkyPanorama(int width, int height)
    {
        if (width < MinimumSkyPanoramaWidth || height < MinimumSkyPanoramaHeight) return false;
        float ratio = width / (float)height;
        return MathF.Abs(ratio - 2f) <= SkyPanoramaAspectTolerance;
    }

    private static void EnsureEquirectangularSkyShader()
    {
        if (_equirectangularSkyShaderTried) return;
        _equirectangularSkyShaderTried = true;

        try
        {
            _equirectangularSkyShader = Raylib.LoadShaderFromMemory(null!, EquirectangularSkyFragmentShader);
            if (_equirectangularSkyShader.Id == 0) return;

            _skyForwardLocation = Raylib.GetShaderLocation(_equirectangularSkyShader, "skyForward");
            _skyRightLocation = Raylib.GetShaderLocation(_equirectangularSkyShader, "skyRight");
            _skyUpLocation = Raylib.GetShaderLocation(_equirectangularSkyShader, "skyUp");
            _skyProjectionLocation = Raylib.GetShaderLocation(_equirectangularSkyShader, "skyProjection");
            if (_skyForwardLocation < 0 || _skyRightLocation < 0 || _skyUpLocation < 0 || _skyProjectionLocation < 0)
            {
                Raylib.UnloadShader(_equirectangularSkyShader);
                _equirectangularSkyShader = default;
            }
        }
        catch
        {
            // Use the original procedural sky when custom GLSL is unavailable.
            _equirectangularSkyShader = default;
        }
    }

    /// <summary>Releases the weapon render target. Call before the window closes.</summary>
    public static void Shutdown()
    {
        if (_worldPassActive)
        {
            Raylib.EndTextureMode();
            _worldPassActive = false;
        }

        if (_viewModelTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_viewModelTarget);
            _viewModelTarget = default;
            _viewModelWidth = 0;
            _viewModelHeight = 0;
        }

        if (_worldTarget.Id != 0)
        {
            Raylib.UnloadRenderTexture(_worldTarget);
            _worldTarget = default;
            _worldWidth = 0;
            _worldHeight = 0;
        }

        if (_worldGradeShader.Id != 0)
        {
            Raylib.UnloadShader(_worldGradeShader);
            _worldGradeShader = default;
        }
        _worldGradeShaderTried = false;

        if (_equirectangularSkyShader.Id != 0)
        {
            Raylib.UnloadShader(_equirectangularSkyShader);
            _equirectangularSkyShader = default;
        }
        _equirectangularSkyShaderTried = false;
        _skyForwardLocation = -1;
        _skyRightLocation = -1;
        _skyUpLocation = -1;
        _skyProjectionLocation = -1;

        if (_optionalSkyPanorama.Id != 0)
        {
            Raylib.UnloadTexture(_optionalSkyPanorama);
            _optionalSkyPanorama = default;
        }
        _optionalSkyPanoramaChecked = false;

        IndustrialZoneModel.Shutdown();
        WeaponModels.Shutdown();
        AnimatedViewmodels.Shutdown();
        CharacterModels.Shutdown();
    }

    /// <summary>
    /// Sets up the camera-relative basis for the view model. A few degrees of
    /// inward yaw means the player sees the side of the weapon rather than its muzzle.
    /// </summary>
    private static void PushViewModelTransform(Simulation sim, Vector3 localOffset,
        in ViewModelPose pose, float kick, float reloadDip, float ironSight,
        Vector3 springRotation)
    {
        Player p = sim.Player;
        float yaw = p.Yaw + p.Punch.X;
        float pitch = U.Clamp(p.Pitch + p.Punch.Y, -Cfg.MaxPitch, Cfg.MaxPitch);

        // The weapon pass has its own camera at the origin, so the basis starts
        // directly from the view angles.
        Rlgl.PushMatrix();
        Rlgl.Rotatef(yaw, 0f, 1f, 0f);
        Rlgl.Rotatef(pitch, 1f, 0f, 0f);

        // Anchor first, then animate about that pivot. Rotating the anchor frame
        // itself would swing the whole weapon up into the view during a reload.
        // Angles are deliberately small: this is a flourish, not a re-aim, and a
        // large rotation walks the weapon over the crosshair.
        Rlgl.Translatef(localOffset.X, localOffset.Y, localOffset.Z);
        Rlgl.Rotatef(kick * 0.30f - reloadDip * 22f, 1f, 0f, 0f);
        Rlgl.Rotatef(reloadDip * 15f, 0f, 0f, 1f);

        // The second-order spring is local to the viewmodel and never rotates
        // the physical camera.  This is the cosmetic half of dual-layer recoil:
        // weapon weight visibly lags and settles while combat remains deterministic.
        Rlgl.Rotatef(springRotation.X, 1f, 0f, 0f);
        Rlgl.Rotatef(springRotation.Y, 0f, 1f, 0f);
        Rlgl.Rotatef(springRotation.Z, 0f, 0f, 1f);

        // Hip fire shows a useful side profile. As iron sights come up that
        // presentation yaw resolves to zero, keeping the sights on the centre ray.
        Rlgl.Rotatef(U.Lerp(3f, 0f, ironSight), 0f, 1f, 0f);
        Rlgl.Scalef(pose.Scale, pose.Scale, pose.Scale);
    }

    /// <summary>
    /// Per-class centre offsets for the in-view iron-sight plane. The AK uses its
    /// measured mesh placement: its rear/front sights are deliberately on the
    /// original GLB rather than a 2D reticle overlay.
    /// </summary>
    private static Vector3 IronSightOffsetFor(WeaponDef def) => def.Class switch
    {
        // The animated rig pack carries its own hip-to-sights move in the
        // AnimatedViewmodels spec (AdsTarget), so rig weapons take no extra
        // shift here; the class entries below serve the box weapons.
        _ when AnimatedViewmodels.Has(def) => Vector3.Zero,
        _ when def.Class == "Rifle" => new Vector3(-9.15f, 4.5f, -8.0f),
        _ when def.Class == "Pistol" => new Vector3(-11.0f, 12.0f, 2.5f),
        _ when def.Class == "SMG" => new Vector3(-11.0f, 12.0f, 3.5f),
        _ when def.Class == "Shotgun" => new Vector3(-11.0f, 13.0f, 3.0f),
        _ => Vector3.Zero,
    };

    /// <summary>
    /// A weapon drawn from an art mesh: the file, how long its barrel should appear
    /// in view-model units, where its centre sits, and how much of the reload
    /// flourish to apply. The pose values are tuned so the weapon occupies the same
    /// space on screen as the box shapes it replaces.
    /// </summary>
    private readonly record struct WeaponArtwork(string Path, float Length, Vector3 Centre, float ReloadSwing, float YawDegrees);

    /// <summary>
    /// Weapons that have a real mesh. Anything not listed here is drawn from boxes,
    /// and a weapon stays on its boxes if its mesh fails to load.
    /// </summary>
    private static readonly Dictionary<string, WeaponArtwork> Artwork = new(StringComparer.OrdinalIgnoreCase)
    {
        // Side-on presentation keeps the label, top loop, linked chain and split ring
        // readable; the compact cap remains the negative-X striking end of the mesh.
        ["Knife"] = new("assets/weapons/inhaler-keychain.glb", 15f, new Vector3(0.0f, 7.0f, -18.0f), 0.20f, 90f),
    };

    /// <summary>
    /// Draws the weapon into the current view-model space. Returns the muzzle point
    /// when the weapon is a mesh, so the flash can be hung on the real barrel rather
    /// than on the per-class bore estimate; box weapons return null.
    /// </summary>
    private static Vector3? DrawWeaponShape(Simulation sim, WeaponDef def, Player p, float kick, float reloadDip)
    {
        // The animated rig pack owns Rifle/SMG/Sniper/Pistol; when its GLB is
        // missing the weapon drops through to the static artwork or boxes.
        if (AnimatedViewmodels.TryDraw(sim, def, out Vector3 rigMuzzle)) return rigMuzzle;

        if (Artwork.TryGetValue(def.Name, out WeaponArtwork art)
            && WeaponModels.TryDraw(art.Path, art.Length, art.Centre,
                def.HasCalibratedIronSights ? U.Lerp(art.YawDegrees, 0f, p.ZoomBlend) : art.YawDegrees,
                out Vector3 muzzle))
        {
            return muzzle;
        }

        Color body = U.Rgb(62, 63, 72);
        Color bodyLight = U.Rgb(88, 89, 100);
        Color wood = U.Rgb(126, 84, 44);
        Color metal = U.Rgb(142, 146, 156);
        Color skin = U.Rgb(206, 163, 130);

        // Depth testing is off for the weapon pass, so parts are emitted far-to-near
        // (muzzle first, gripping hands last) to get painter-correct occlusion.
        switch (def.Class)
        {
            case "Melee":
                Raylib.DrawCubeV(new Vector3(-2f, -1f, -18f), new Vector3(1.6f, 6f, 18f), metal);           // blade
                Raylib.DrawCubeV(new Vector3(-2f, -3.4f, -18f), new Vector3(1.7f, 2.2f, 18f), Shade(metal, 0.75f));
                Raylib.DrawCubeV(new Vector3(-2f, -2f, -9f), new Vector3(3.5f, 3.5f, 3f), skin);           // hand
                break;

            case "Pistol":
                Raylib.DrawCubeV(new Vector3(-1f, -1f, -22f), new Vector3(2.4f, 2.4f, 6f), metal);         // barrel
                Raylib.DrawCubeV(new Vector3(-1f, 2f, -10f), new Vector3(5f, 7f, 24f), body);             // slide
                Raylib.DrawCubeV(new Vector3(-1f, -3.5f, -8f), new Vector3(5.5f, 9f, 7f), bodyLight);      // frame
                Raylib.DrawCubeV(new Vector3(-1f, -8f, -4f), new Vector3(6f, 11f, 8f), body);             // grip
                Raylib.DrawCubeV(new Vector3(-1f, -11f, -6f), new Vector3(7f, 9f, 8f), skin);             // grip hand
                Raylib.DrawCubeV(new Vector3(2.5f, -12f, -14f), new Vector3(7f, 8f, 7f), skin);           // support hand
                break;

            case "SMG":
                Raylib.DrawCubeV(new Vector3(-1f, -1f, -34f), new Vector3(3f, 3f, 12f), metal);            // suppressor
                // Front sight post rising off the muzzle: its tip meets the
                // centre ray at full ADS with the class offset in IronSightOffsetFor.
                Raylib.DrawCubeV(new Vector3(-1f, 2.6f, -30.5f), new Vector3(0.9f, 5.5f, 1.1f), body);
                Raylib.DrawCubeV(new Vector3(-1f, -1f, -14f), new Vector3(7f, 9f, 34f), body);             // receiver
                Raylib.DrawCubeV(new Vector3(-1f, 1.5f, -4f), new Vector3(8f, 6f, 14f), bodyLight);        // upper
                // Rear sight: two blocks with a centre notch, top edge level with
                // the front post tip.
                Raylib.DrawCubeV(new Vector3(-3.2f, 4.9f, 0.5f), new Vector3(2.2f, 2.9f, 2.6f), body);
                Raylib.DrawCubeV(new Vector3(1f, 4.9f, 0.5f), new Vector3(2.2f, 2.9f, 2.6f), body);
                Raylib.DrawCubeV(new Vector3(-1f, 3.4f, 0.5f), new Vector3(6.6f, 1f, 2.6f), bodyLight);    // notch base
                Raylib.DrawCubeV(new Vector3(-1f, -3f, -9f), new Vector3(5f, 14f, 7f), body);             // magazine
                Raylib.DrawCubeV(new Vector3(-1f, -6f, -2f), new Vector3(8f, 11f, 9f), bodyLight);        // stock
                Raylib.DrawCubeV(new Vector3(-6f, -9f, -12f), new Vector3(9f, 10f, 9f), skin);
                Raylib.DrawCubeV(new Vector3(5f, -9f, -20f), new Vector3(9f, 10f, 9f), skin);
                break;

            case "Rifle":
                Color furniture = wood;
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -42f), new Vector3(3.4f, 3.4f, 16f), metal);         // barrel
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -18f), new Vector3(8f, 10f, 42f), body);            // receiver
                Raylib.DrawCubeV(new Vector3(-1f, 7f, -30f), new Vector3(2.6f, 4f, 10f), metal);          // front sight
                Raylib.DrawCubeV(new Vector3(-1f, -6f, -6f), new Vector3(8f, 6f, 8f), body);              // handguard
                Raylib.DrawCubeV(new Vector3(-1f, -4f, -12f), new Vector3(6f, 18f, 9f), furniture);   // magazine
                Raylib.DrawCubeV(new Vector3(-1f, 1.5f, -2f), new Vector3(9f, 7f, 20f), furniture);       // stock
                Raylib.DrawCubeV(new Vector3(-7f, -9f, -16f), new Vector3(9f, 10f, 9f), skin);
                Raylib.DrawCubeV(new Vector3(5f, -8f, -28f), new Vector3(9f, 10f, 9f), skin);
                break;

            case "Shotgun":
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -40f), new Vector3(5f, 5f, 22f), metal);            // barrel
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -16f), new Vector3(8f, 9f, 38f), body);             // receiver
                Raylib.DrawCubeV(new Vector3(-1f, -3f, -14f), new Vector3(6f, 6f, 26f), wood);            // pump
                Raylib.DrawCubeV(new Vector3(-1f, -5f, -4f), new Vector3(8f, 8f, 16f), wood);             // stock
                Raylib.DrawCubeV(new Vector3(-6f, -9f, -20f), new Vector3(9f, 10f, 10f), skin);
                break;

            case "Sniper":
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -52f), new Vector3(3.6f, 3.6f, 24f), metal);        // barrel
                Raylib.DrawCubeV(new Vector3(-1f, 8f, -37f), new Vector3(5.4f, 5.4f, 3f), metal);         // objective lens
                Raylib.DrawCubeV(new Vector3(-1f, 8f, -26f), new Vector3(4f, 4f, 20f), bodyLight);        // scope tube
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -22f), new Vector3(7f, 9f, 50f), body);             // receiver
                Raylib.DrawCubeV(new Vector3(-1f, -3f, -12f), new Vector3(6f, 10f, 9f), body);            // magazine
                Raylib.DrawCubeV(new Vector3(-1f, -3f, 2f), new Vector3(8f, 7f, 18f), bodyLight);         // stock
                Raylib.DrawCubeV(new Vector3(-7f, -9f, -22f), new Vector3(9f, 10f, 9f), skin);
                Raylib.DrawCubeV(new Vector3(5f, -8f, -34f), new Vector3(9f, 10f, 9f), skin);
                break;

            default:
                Raylib.DrawCubeV(new Vector3(-1f, 0f, -16f), new Vector3(7f, 8f, 32f), body);
                break;
        }

        _ = kick;
        return null;
    }

    private static void DrawMuzzleFlash(Simulation sim, in ViewModelPose pose, Vector3? muzzle)
    {
        // Box weapons have no idea where their bore really is, so they use the
        // per-class estimate; a mesh weapon reports its own muzzle.
        Vector3 bore = muzzle ?? new Vector3(-1f, pose.Bore, -pose.Forward + 2f);
        float pulse = U.Clamp01(sim.Player.MuzzleFlash / 0.045f);
        float coreRadius = 0.75f + pulse * 1.20f;
        float flameLength = 3.8f + pulse * 9.0f;
        float flameWidth = 1.1f + pulse * 2.5f;

        // The old opaque cube read like a floating prop.  This HGO-specific
        // flame uses a white-hot compact core plus four low-poly amber lobes.
        // It is additive inside the isolated viewmodel pass, so it cannot wash
        // out the world or the HUD after compositing.
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawSphereEx(bore, coreRadius, 8, 6, U.Rgba(255, 248, 221, (int)(238f * pulse)));

        Vector3 forward = -Vector3.UnitZ;
        DrawFlashLobe(bore, forward, flameLength, flameWidth, 0f, 0f,
            U.Rgba(255, 176, 62, (int)(176f * pulse)));
        DrawFlashLobe(bore, forward, flameLength * 0.76f, flameWidth * 0.72f, 0.58f, 0.15f,
            U.Rgba(255, 132, 34, (int)(142f * pulse)));
        DrawFlashLobe(bore, forward, flameLength * 0.63f, flameWidth * 0.64f, -0.50f, -0.20f,
            U.Rgba(255, 150, 38, (int)(132f * pulse)));
        DrawFlashLobe(bore, forward, flameLength * 0.49f, flameWidth * 0.55f, 0.05f, -0.63f,
            U.Rgba(255, 113, 28, (int)(106f * pulse)));
        Raylib.EndBlendMode();
    }

    /// <summary>Draws one faceted flame petal in the viewmodel's local basis.</summary>
    private static void DrawFlashLobe(Vector3 root, Vector3 forward, float length, float radius,
        float sideBias, float upBias, Color colour)
    {
        Vector3 center = root + Vector3.UnitX * (sideBias * radius) + Vector3.UnitY * (upBias * radius);
        Vector3 tip = center + forward * length;
        Vector3 x = Vector3.UnitX * radius;
        Vector3 y = Vector3.UnitY * radius;

        Vector3 left = center - x;
        Vector3 right = center + x;
        Vector3 top = center + y;
        Vector3 bottom = center - y;

        // Both windings make the lobes survive any backface-culling state and
        // keep the flash readable while the model rolls during recoil.
        Raylib.DrawTriangle3D(left, top, tip, colour);
        Raylib.DrawTriangle3D(top, right, tip, colour);
        Raylib.DrawTriangle3D(right, bottom, tip, colour);
        Raylib.DrawTriangle3D(bottom, left, tip, colour);
        Raylib.DrawTriangle3D(tip, top, left, colour);
        Raylib.DrawTriangle3D(tip, right, top, colour);
        Raylib.DrawTriangle3D(tip, bottom, right, colour);
        Raylib.DrawTriangle3D(tip, left, bottom, colour);
    }

    // ---------------------------------------------------------------- helpers

    public static Color BotPalette(int id)
    {
        Color[] palette =
        {
            U.Rgb(150, 76, 70), U.Rgb(92, 116, 88), U.Rgb(122, 104, 66),
            U.Rgb(96, 90, 130), U.Rgb(70, 108, 118), U.Rgb(140, 110, 92),
            U.Rgb(110, 88, 120), U.Rgb(84, 118, 108),
        };
        return palette[Math.Abs(id) % palette.Length];
    }

    public static Color Shade(Color c, float factor) => U.Rgba(
        (int)U.Clamp(c.R * factor, 0f, 255f),
        (int)U.Clamp(c.G * factor, 0f, 255f),
        (int)U.Clamp(c.B * factor, 0f, 255f),
        c.A);
}
