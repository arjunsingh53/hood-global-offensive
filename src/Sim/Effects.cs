using System.Numerics;
using Raylib_cs;

namespace HoodGlobalOffensive.Sim;

/// <summary>A bullet trail drawn for a couple of frames.</summary>
public struct Tracer
{
    public Vector3 From;
    public Vector3 To;
    public float Life;
    public float MaxLife;
    public Color Color;
    public float Width;
}

/// <summary>How an impact particle should be presented by the renderer.</summary>
public enum ParticleKind
{
    Debris,
    BloodMist,
    BloodDroplet,
}

/// <summary>Impact debris, mist, or a physical droplet.</summary>
public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Life;
    public float MaxLife;
    public float Size;
    public Color Color;
    public bool Gravity;
    public ParticleKind Kind;
}

/// <summary>
/// A gravity-driven actor-hit droplet.  It owns the mark data up front so a
/// later collision never consumes random gameplay or rendering state.
/// </summary>
public struct BloodDroplet
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Life;
    public float MaxLife;
    public float Size;
    public float SplatSize;
    public float SplatLife;
    public Color Color;
}

/// <summary>Floating damage number shown at the point of impact.</summary>
public struct FloatingDamage
{
    public Vector3 Position;
    public float Amount;
    public float Life;
    public float MaxLife;
    public bool Headshot;
    public bool IsPlayerDamage;   // true when the local player took the hit
}

/// <summary>How a projected surface mark should be presented.</summary>
public enum DecalKind
{
    SurfaceImpact,
    BloodSplat,
}

/// <summary>A short-lived bullet mark or combat splat left on geometry.</summary>
public struct Decal
{
    public Vector3 Position;
    public Vector3 Normal;
    public float Size;
    public float Life;
    public float MaxLife;
    public Color Color;
    public DecalKind Kind;
}

/// <summary>Muzzle flash sprite anchored to the view model.</summary>
public struct MuzzleLight
{
    public Vector3 Position;
    public float Life;
}

/// <summary>One line of the kill feed.</summary>
public struct KillFeedEntry
{
    public string Killer;
    public string Victim;
    public string Weapon;
    public bool Headshot;
    public bool KillerIsPlayer;
    public bool VictimIsPlayer;
    public float SpawnedAt;
}

/// <summary>Whether a cue exists in the world or is a private player confirmation.</summary>
public enum SoundEventScope
{
    World,
    Local,
}

/// <summary>A sound the audio layer should play this frame.</summary>
public readonly struct SoundEvent
{
    public readonly string Name;
    public readonly Vector3 Position;
    public readonly float Volume;
    public readonly float Pitch;
    public readonly SoundEventScope Scope;

    public SoundEvent(string name, Vector3 position, float volume, float pitch,
        SoundEventScope scope = SoundEventScope.World)
    {
        Name = name;
        Position = position;
        Volume = volume;
        Pitch = pitch;
        Scope = scope;
    }
}
