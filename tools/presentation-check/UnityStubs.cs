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

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
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

        public static Vector2 zero => new Vector2(0f, 0f);
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

        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator *(Vector3 a, float s) =>
            new Vector3(a.x * s, a.y * s, a.z * s);
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static Quaternion identity => default;

        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;

        public static Quaternion Euler(float x, float y, float z) => default;

        public static Quaternion operator *(Quaternion a, Quaternion b) => default;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Transform parent => null!;

        public void SetParent(Transform newParent) { }
    }

    public class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
    }

    public class Object
    {
        public string name { get; set; } = "";

        public static void Destroy(Object obj) { }
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = 1f;
        }

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }
    }

    public class Material : Object
    {
        public Color color { get; set; }
    }

    public class Renderer : Component
    {
        public Material material { get; set; } = new Material();
        public Material sharedMaterial => material;
    }

    public class Camera : Behaviour
    {
        public float fieldOfView { get; set; } = 60f;
        public Vector3 WorldToViewportPoint(Vector3 position) => default;
    }

    public class CanvasRenderer : Component { }

    public class Canvas : Component
    {
        public RenderMode renderMode { get; set; }
        public Camera worldCamera { get; set; }
        public int sortingOrder { get; set; }
    }

    public enum RenderMode
    {
        ScreenSpaceOverlay,
        ScreenSpaceCamera,
        WorldSpace,
    }

    public class CanvasScaler : Component
    {
        public enum ScaleMode
        {
            ConstantPixelSize,
            ScaleWithScreenSize,
        }

        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }

    public class Font : Object
    {
        public static Font CreateDynamicFontFromOSFont(string fontName, int size) => new Font();
    }

    public static class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object, new() => null!;
    }

    public static class Debug
    {
        public static void Log(string message) { }
        public static void LogWarning(string message) { }
        public static void LogError(string message) { }
    }

    public enum PrimitiveType
    {
        Sphere,
        Plane,
        Quad,
        Capsule,
        Cylinder,
        Cube,
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }

        public Transform transform { get; set; } = new Transform();

        public bool activeSelf => true;

        public void SetActive(bool value) { }

        public static GameObject CreatePrimitive(PrimitiveType type) => new GameObject(type.ToString());

        public T AddComponent<T>() where T : Component, new() => new T();

        public T GetComponent<T>() where T : Component => null!;
    }

    public class Component : Object
    {
        public Transform transform { get; set; } = new Transform();
        public GameObject gameObject { get; set; } = new GameObject();

        public T GetComponent<T>() where T : Component => null!;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    public class MonoBehaviour : Behaviour { }

    public class AudioClip : Object
    {
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream)
            => new AudioClip();
        public bool SetData(float[] data, int offsetSamples) => true;
    }

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; } = 1f;
        public float panStereo { get; set; }
        public float spatialBlend { get; set; }
        public bool playOnAwake { get; set; } = true;
        public void Play() { }
    }

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
        public static float timeScale { get; set; } = 1f;
    }

    public static class PlayerPrefs
    {
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetInt(string key, int value) { }
        public static bool HasKey(string key) => false;
        public static void Save() { }
    }

    public static class Application
    {
        public static void Quit() { }
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
        Escape,
        Return,
        Space,
        Tab,
        UpArrow,
        DownArrow,
        LeftArrow,
        RightArrow,
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
        public static float Clamp(float value, float min, float max) =>
            value < min ? min : (value > max ? max : value);
        public static float Deg2Rad => 0.017453292519943295f;
    }
}

namespace UnityEngine.UI
{
    using UnityEngine;

    public enum TextAnchor
    {
        UpperLeft,
        UpperCenter,
        UpperRight,
        MiddleLeft,
        MiddleCenter,
        MiddleRight,
        LowerLeft,
        LowerCenter,
        LowerRight,
    }

    public enum HorizontalWrapMode
    {
        Wrap,
        Overflow,
    }

    public enum VerticalWrapMode
    {
        Truncate,
        Overflow,
    }

    public class Graphic : Component
    {
        public Color color { get; set; }
        public RectTransform rectTransform => transform as RectTransform ?? new RectTransform();
    }

    public class Text : Graphic
    {
        public string text { get; set; } = "";
        public Font font { get; set; }
        public int fontSize { get; set; } = 13;
        public TextAnchor alignment { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }

    public class Image : Graphic
    {
        public override string ToString() => name;
    }
}
