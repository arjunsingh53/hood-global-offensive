using System.Numerics;

namespace HoodGlobalOffensive.Render;

/// <summary>
/// Deterministic, presentation-only second-order spring state for first-person
/// weapon motion. The values represent local render offsets and rotations; this
/// type deliberately has no access to player aim, world position, collision, or
/// combat simulation state.
/// </summary>
/// <remarks>
/// Call <see cref="Tick"/> once per render/update with a local visual target
/// (normally <see cref="Vector3.Zero"/>), then add <see cref="Value"/> to the
/// viewmodel transform only. A semi-implicit Euler step is used: velocity is
/// advanced from spring acceleration before position is advanced from velocity.
/// No random values or clock reads are used, so equal inputs produce equal state.
/// </remarks>
public sealed class ViewModelSpring
{
    private const float Tau = 6.28318530717958647692f;
    private const float MaxFrameTime = 1f / 15f;
    private const float MaxStepTime = 1f / 120f;
    private const int MaxSubsteps = 8;

    /// <summary>Current local presentation offset or rotation.</summary>
    public Vector3 Value { get; private set; }

    /// <summary>Current local presentation velocity, in units per second.</summary>
    public Vector3 Velocity { get; private set; }

    /// <summary>Spring oscillation frequency in hertz.</summary>
    public float FrequencyHz { get; private set; }

    /// <summary>Dimensionless damping ratio: 1 is critically damped.</summary>
    public float DampingRatio { get; private set; }

    /// <summary>Maximum distance the visual state may stray from its tick target.</summary>
    public float MaxDisplacement { get; private set; }

    /// <summary>Maximum magnitude of the visual state velocity.</summary>
    public float MaxSpeed { get; private set; }

    /// <summary>
    /// Creates a spring suitable for small first-person visual offsets. The
    /// defaults are intentionally conservative so a missed frame cannot fling a
    /// weapon across the screen.
    /// </summary>
    public ViewModelSpring(
        float frequencyHz = 10f,
        float dampingRatio = 0.82f,
        float maxDisplacement = 48f,
        float maxSpeed = 960f)
    {
        Configure(frequencyHz, dampingRatio, maxDisplacement, maxSpeed);
        Reset();
    }

    /// <summary>
    /// Reconfigures only presentation limits. Invalid inputs are replaced by
    /// conservative values and existing state is sanitized immediately.
    /// </summary>
    public void Configure(float frequencyHz, float dampingRatio, float maxDisplacement, float maxSpeed)
    {
        FrequencyHz = ClampFinite(frequencyHz, 0.1f, 80f, 10f);
        DampingRatio = ClampFinite(dampingRatio, 0f, 4f, 0.82f);
        MaxDisplacement = ClampFinite(maxDisplacement, 0.001f, 4096f, 48f);
        MaxSpeed = ClampFinite(maxSpeed, 0.001f, 65536f, 960f);

        if (!IsFinite(Value)) Value = Vector3.Zero;
        if (!IsFinite(Velocity)) Velocity = Vector3.Zero;
        Velocity = ClampMagnitude(Velocity, MaxSpeed);
    }

    /// <summary>Clears visual motion and returns the spring to the origin.</summary>
    public void Reset() => Reset(Vector3.Zero, Vector3.Zero);

    /// <summary>
    /// Sets presentation state directly. This is intended for scene changes,
    /// weapon swaps, or respawns; it does not move or rotate any gameplay state.
    /// </summary>
    public void Reset(Vector3 value, Vector3 velocity)
    {
        Value = IsFinite(value) ? value : Vector3.Zero;
        Velocity = IsFinite(velocity) ? ClampMagnitude(velocity, MaxSpeed) : Vector3.Zero;
    }

    /// <summary>
    /// Adds an instantaneous velocity change for cosmetic recoil, sway, or
    /// landing feedback. The result is safely bounded by <see cref="MaxSpeed"/>.
    /// </summary>
    public void Impulse(Vector3 velocityChange)
    {
        if (!IsFinite(velocityChange)) return;

        if (!IsFinite(Velocity)) Velocity = Vector3.Zero;
        Velocity = ClampMagnitude(Velocity + velocityChange, MaxSpeed);
    }

    /// <summary>
    /// Advances this visual spring toward <paramref name="target"/> and returns
    /// the new local presentation value. Invalid targets or elapsed time never
    /// leak non-finite state into rendering; a long frame is bounded and split
    /// into a small, deterministic number of stable semi-implicit steps.
    /// </summary>
    public Vector3 Tick(Vector3 target, float deltaTime)
    {
        if (!IsFinite(target))
        {
            Reset();
            return Value;
        }

        if (!IsFinite(Value)) Value = target;
        if (!IsFinite(Velocity)) Velocity = Vector3.Zero;

        float remaining = ClampFinite(deltaTime, 0f, MaxFrameTime, 0f);
        int steps = 0;
        while (remaining > 0f && steps < MaxSubsteps)
        {
            float step = remaining > MaxStepTime ? MaxStepTime : remaining;
            Integrate(target, step);
            remaining -= step;
            steps++;
        }

        // A target-relative clamp preserves configurable rest poses while still
        // preventing an accidental impulse from taking the model off-screen.
        Vector3 displacement = Value - target;
        if (!IsFinite(displacement))
        {
            Value = target;
            Velocity = Vector3.Zero;
        }
        else
        {
            Value = target + ClampMagnitude(displacement, MaxDisplacement);
            Velocity = ClampMagnitude(Velocity, MaxSpeed);
        }

        return Value;
    }

    private void Integrate(Vector3 target, float deltaTime)
    {
        float omega = Tau * FrequencyHz;
        float spring = omega * omega;
        float damping = 2f * DampingRatio * omega;
        Vector3 acceleration = (target - Value) * spring - Velocity * damping;

        if (!IsFinite(acceleration))
        {
            Value = target;
            Velocity = Vector3.Zero;
            return;
        }

        // Semi-implicit Euler: integrate velocity first, then position.
        Velocity = ClampMagnitude(Velocity + acceleration * deltaTime, MaxSpeed);
        Value += Velocity * deltaTime;

        if (!IsFinite(Value))
        {
            Value = target;
            Velocity = Vector3.Zero;
        }
    }

    private static Vector3 ClampMagnitude(Vector3 value, float maximum)
    {
        if (!IsFinite(value)) return Vector3.Zero;

        float lengthSquared = value.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= maximum * maximum) return value;
        if (lengthSquared <= 0f) return Vector3.Zero;

        return value * (maximum / MathF.Sqrt(lengthSquared));
    }

    private static float ClampFinite(float value, float minimum, float maximum, float fallback)
    {
        if (!float.IsFinite(value)) return fallback;
        if (value < minimum) return minimum;
        return value > maximum ? maximum : value;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
