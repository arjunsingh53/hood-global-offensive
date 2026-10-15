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
    private static readonly Color Sky = U.Rgb(96, 118, 148);
    private static readonly Color Horizon = U.Rgb(150, 158, 168);

    public static Camera3D BuildCamera(Simulation sim, float aspectUnused = 1f)
    {
        _ = aspectUnused;
        Player p = sim.Player;

        float yaw = p.Yaw + p.Punch.X;
        float pitch = U.Clamp(p.Pitch + p.Punch.Y, -Cfg.MaxPitch, Cfg.MaxPitch);
        Vector3 dir = U.DirFromAngles(yaw, pitch);
        Vector3 right = U.RightFromYaw(yaw);

        float bobX = MathF.Sin(p.BobPhase) * 1.7f * p.BobAmount;
        float bobY = MathF.Cos(p.BobPhase * 2f) * -1.4f * p.BobAmount;

        Vector3 eye = p.EyePosition
            + right * bobX
            + new Vector3(0f, bobY - p.LandDip * 9f, 0f);

        return new Camera3D
        {
            Position = eye,
            Target = eye + dir * 120f,
            Up = new Vector3(0f, 1f, 0f),
            FovY = p.CurrentFov,
            Projection = CameraProjection.Perspective,
        };
    }

    public static void DrawSky(int width, int height)
    {
        Raylib.ClearBackground(Sky);
        // Cheap gradient band near the horizon so the flat sky reads as depth.
        int bands = 14;
        for (int i = 0; i < bands; i++)
        {
            float t = i / (float)bands;
            int h = (int)(height * 0.42f * (1f - t)) + 1;
            Color c = U.Rgba(
                (int)U.Lerp(Sky.R, Horizon.R, t),
                (int)U.Lerp(Sky.G, Horizon.G, t),
                (int)U.Lerp(Sky.B, Horizon.B, t),
                (int)(26 * (1f - t) + 4));
            Raylib.DrawRectangle(0, (int)(height * 0.42f * t), width, h, c);
        }
    }

    public static void DrawWorld(Simulation sim, Camera3D cam)
    {
        DrawBrushes(sim, cam);
        DrawFloorGrid(cam);
        DrawDecals(sim);
        DrawActors(sim, cam);
        DrawParticles(sim);
        DrawTracers(sim);
    }

    private static void DrawBrushes(Simulation sim, Camera3D cam)
    {
        foreach (Brush brush in sim.Map.Brushes)
        {
            Vector3 center = brush.Box.Center;
            Vector3 size = brush.Box.Size;

            float distance = (center - cam.Position).Length();
            if (distance > 4800f) continue;

            Raylib.DrawCubeV(center, size, brush.Color);

            // Top cap in a lighter tone fakes a directional light.
            Color cap = Shade(brush.Color, 1.20f);
            Raylib.DrawCubeV(
                new Vector3(center.X, brush.Box.Max.Y + 0.5f, center.Z),
                new Vector3(size.X - 0.4f, 1.0f, size.Z - 0.4f),
                cap);

            // Darken the lower skirt so boxes read as solid volumes.
            Color skirt = Shade(brush.Color, 0.72f);
            Raylib.DrawCubeV(
                new Vector3(center.X, brush.Box.Min.Y + 3f, center.Z),
                new Vector3(size.X + 0.6f, 6f, size.Z + 0.6f),
                skirt);

            Raylib.DrawCubeWiresV(center, size, Shade(brush.Color, 0.55f));
        }
    }

    private static void DrawFloorGrid(Camera3D cam)
    {
        Color line = U.Rgba(96, 90, 78, 70);
        const float half = Cfg.MapHalf - 42f;
        const int step = 200;

        for (float x = -half; x <= half; x += step)
        {
            if (MathF.Abs(x - cam.Position.X) > 2200f) continue;
            Raylib.DrawLine3D(new Vector3(x, 0.12f, -half), new Vector3(x, 0.12f, half), line);
        }
        for (float z = -half; z <= half; z += step)
        {
            if (MathF.Abs(z - cam.Position.Z) > 2200f) continue;
            Raylib.DrawLine3D(new Vector3(-half, 0.12f, z), new Vector3(half, 0.12f, z), line);
        }
    }

    private static void DrawDecals(Simulation sim)
    {
        foreach (Decal d in sim.Decals)
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
            if (!actor.Alive || actor.IsPlayer) continue;
            DrawCharacter(actor, cam);
        }
    }

    private static void DrawCharacter(Actor actor, Camera3D cam)
    {
        Vector3 feet = actor.Position;
        if ((feet - cam.Position).Length() > 5200f) return;

        float h = actor.HeadHeight;
        Vector3 forward = U.DirFromAngles(actor.Yaw, 0f);
        Vector3 right = U.RightFromYaw(actor.Yaw);

        // Ground contact shadow.
        Raylib.DrawCubeV(new Vector3(feet.X, feet.Y + 0.4f, feet.Z), new Vector3(30f, 0.5f, 30f), U.Rgba(0, 0, 0, 70));

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
        foreach (Particle p in sim.Particles)
        {
            float t = U.Clamp01(p.Life / p.MaxLife);
            Color c = U.Rgba(p.Color.R, p.Color.G, p.Color.B, (int)(255 * t));
            float s = p.Size * (0.5f + t * 0.5f);
            Raylib.DrawCubeV(p.Position, new Vector3(s, s, s), c);
        }
    }

    private static void DrawTracers(Simulation sim)
    {
        foreach (Tracer t in sim.Tracers)
        {
            float a = U.Clamp01(t.Life / t.MaxLife);
            Color c = U.Rgba(t.Color.R, t.Color.G, t.Color.B, (int)(220 * a));
            Raylib.DrawLine3D(t.From, t.To, c);
        }
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
    public static void DrawViewModel(Simulation sim, int width, int height)
    {
        // Cleared first: if the weapon is stowed this frame the render target still
        // holds the previous frame's image, which must not be composited or the
        // player would see a frozen ghost of the gun while scoped.
        _viewModelDrawn = false;

        if (sim.MatchOver) return;
        Player p = sim.Player;
        WeaponDef def = p.Current.Def;

        // Zooming stows the weapon: it is gone well before the scope is fully up.
        if (p.ZoomBlend > 0.70f) return;

        float reloadDip = 0f;
        if (p.Current.Reloading)
        {
            float duration = MathF.Max(0.001f, def.ReloadTime);
            float reloadT = U.Clamp01((sim.Time - p.Current.ReloadStartTime) / duration);
            reloadDip = MathF.Sin(reloadT * MathF.PI);
        }

        float kick = p.ViewKick;
        float bobX = MathF.Sin(p.BobPhase) * 2.4f * p.BobAmount;
        float bobY = MathF.Cos(p.BobPhase * 2f) * 1.8f * p.BobAmount;

        ViewModelPose pose = PoseFor(def);

        // Local offsets: -Z is forward, +X right, +Y up.
        Vector3 offset = AnchorFor(pose) + new Vector3(
            p.Sway.X * 2.4f + bobX,
            -p.Sway.Y * 2.4f + bobY - p.LandDip * 6f - reloadDip * 3f,
            kick * 0.22f);

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
            FovY = p.CurrentFov,
            Projection = CameraProjection.Perspective,
        };

        Rlgl.SetClipPlanes(0.5, 600.0);
        Raylib.BeginMode3D(viewModelCamera);

        PushViewModelTransform(sim, offset, pose, kick, reloadDip);
        DrawWeaponShape(def, p, kick, reloadDip);
        if (p.MuzzleFlash > 0f) DrawMuzzleFlash(sim, pose);
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

    /// <summary>Releases the weapon render target. Call before the window closes.</summary>
    public static void Shutdown()
    {
        if (_viewModelTarget.Id == 0) return;
        Raylib.UnloadRenderTexture(_viewModelTarget);
        _viewModelTarget = default;
        _viewModelWidth = 0;
        _viewModelHeight = 0;
    }

    /// <summary>
    /// Sets up the camera-relative basis for the view model. A few degrees of
    /// inward yaw means the player sees the side of the weapon rather than its muzzle.
    /// </summary>
    private static void PushViewModelTransform(Simulation sim, Vector3 localOffset,
        in ViewModelPose pose, float kick, float reloadDip)
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

        // A few degrees of inward yaw shows the side of the weapon without
        // swinging the muzzle across the screen centre.
        Rlgl.Rotatef(3f, 0f, 1f, 0f);
        Rlgl.Scalef(pose.Scale, pose.Scale, pose.Scale);
    }

    private static void DrawWeaponShape(WeaponDef def, Player p, float kick, float reloadDip)
    {
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
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -14f), new Vector3(7f, 9f, 34f), body);             // receiver
                Raylib.DrawCubeV(new Vector3(-1f, 1.5f, -4f), new Vector3(8f, 6f, 14f), bodyLight);        // upper
                Raylib.DrawCubeV(new Vector3(-1f, -3f, -9f), new Vector3(5f, 14f, 7f), body);             // magazine
                Raylib.DrawCubeV(new Vector3(-1f, -6f, -2f), new Vector3(8f, 11f, 9f), bodyLight);        // stock
                Raylib.DrawCubeV(new Vector3(-6f, -9f, -12f), new Vector3(9f, 10f, 9f), skin);
                Raylib.DrawCubeV(new Vector3(5f, -9f, -20f), new Vector3(9f, 10f, 9f), skin);
                break;

            case "Rifle":
                bool ak = def.Name.StartsWith("AK");
                Color furniture = ak ? wood : body;
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -42f), new Vector3(3.4f, 3.4f, 16f), metal);         // barrel
                Raylib.DrawCubeV(new Vector3(-1f, 1f, -18f), new Vector3(8f, 10f, 42f), body);            // receiver
                Raylib.DrawCubeV(new Vector3(-1f, 7f, -30f), new Vector3(2.6f, 4f, 10f), metal);          // front sight
                Raylib.DrawCubeV(new Vector3(-1f, -6f, -6f), new Vector3(8f, 6f, 8f), body);              // handguard
                Raylib.DrawCubeV(new Vector3(-1f, -4f, -12f), new Vector3(6f, 18f, 9f), ak ? furniture : body);   // magazine
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
    }

    private static void DrawMuzzleFlash(Simulation sim, in ViewModelPose pose)
    {
        Vector3 bore = new(-1f, pose.Bore, -pose.Forward + 2f);
        float t = sim.Player.MuzzleFlash / 0.045f;
        float size = t * 7f + 2f;

        Raylib.DrawCubeV(bore, new Vector3(size, size, size * 1.8f), U.Rgba(255, 236, 170, 220));
        Raylib.DrawCubeV(bore - new Vector3(0f, 0f, 4f), new Vector3(size * 1.7f, size * 1.7f, 3f), U.Rgba(255, 210, 120, 150));
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
