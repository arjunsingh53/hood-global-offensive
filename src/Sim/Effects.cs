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

/// <summary>Impact debris (wall dust or blood).</summary>
public struct Particle
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Life;
    public float MaxLife;
    public float Size;
    public Color Color;
    public bool Gravity;
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

/// <summary>A bullet hole / scorch mark left on geometry.</summary>
public struct Decal
{
    public Vector3 Position;
    public Vector3 Normal;
    public float Size;
    public float Life;
    public float MaxLife;
    public Color Color;
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

/// <summary>A sound the audio layer should play this frame.</summary>
public readonly struct SoundEvent
{
    public readonly string Name;
    public readonly Vector3 Position;
    public readonly float Volume;
    public readonly float Pitch;

    public SoundEvent(string name, Vector3 position, float volume, float pitch)
    {
        Name = name;
        Position = position;
        Volume = volume;
        Pitch = pitch;
    }
}
