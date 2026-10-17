using System;

namespace UnityEngine
{
    // Compile-only stand-ins for the slice of the Unity API the presentation layer
    // touches. They exist so CI can compile HGO.Presentation (MatchDriver) without a
    // Unity editor. This is NOT a runtime emulation — behaviour is whatever the real
    // engine does; the value of this project is that the bridge must COMPILE against
    // the sim's public API. Keep it to the members the presentation code actually uses.

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);

        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator *(Vector3 a, float s) =>
            new Vector3(a.x * s, a.y * s, a.z * s);
    }

    public struct Quaternion
    {
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
    }

    public class Transform
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
    }

    public class Component
    {
        public Transform transform { get; set; } = new Transform();
    }

    public class Behaviour : Component { }

    public class MonoBehaviour : Behaviour { }

    public enum CursorLockMode
    {
        None,
        Locked,
        Confined,
    }

    public static class Cursor
    {
        public static CursorLockMode lockState { get; set; }
    }

    public static class Time
    {
        public static float deltaTime => 0f;
    }

    public enum KeyCode
    {
        Alpha1,
        Alpha2,
        Alpha3,
        Alpha4,
        R,
        Q,
        E,
        C,
        V,
        F,
        LeftControl,
    }

    public static class Input
    {
        public static Vector2 mouseScrollDelta => default;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetButton(string name) => false;
        public static float GetAxisRaw(string name) => 0f;
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => a < b ? a : b;
    }
}
