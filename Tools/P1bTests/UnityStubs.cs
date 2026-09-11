using System;

namespace UnityEngine
{
    [Serializable]
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    [Serializable]
    public struct Vector3
    {        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 back => new Vector3(0f, 0f, -1f);
        public float magnitude => (float)System.Math.Sqrt((x * x) + (y * y) + (z * z));
        public float sqrMagnitude => (x * x) + (y * y) + (z * z);
        public Vector3 normalized => magnitude > 0f ? new Vector3(x / magnitude, y / magnitude, z / magnitude) : this;
        public void Normalize() { float m = magnitude; if (m > 0f) { x /= m; y /= m; z /= m; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float Distance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dy = a.y - b.y, dz = a.z - b.z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static Vector3 operator -(Vector3 v) => new Vector3(-v.x, -v.y, -v.z);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object obj) => obj is Vector3 v && this == v;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public static float Dot(Vector3 a, Vector3 b) => (a.x * b.x) + (a.y * b.y) + (a.z * b.z);
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            (a.y * b.z) - (a.z * b.y), (a.z * b.x) - (a.x * b.z), (a.x * b.y) - (a.y * b.x));
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal)
        {
            Vector3 n = planeNormal.normalized;
            return vector - (n * Dot(vector, n));
        }
        public static float Angle(Vector3 a, Vector3 b)
        {
            float denom = a.magnitude * b.magnitude;
            if (denom <= 0f)
            {
                return 0f;
            }

            float c = Dot(a, b) / denom;
            c = c < -1f ? -1f : (c > 1f ? 1f : c);
            return (float)System.Math.Acos(c) * 57.29578f;
        }
    }

    public sealed class Transform
    {
        public Transform parent;
        public Vector3 position;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
        public Vector3 lossyScale => localScale;
        public Quaternion rotation;
        public Vector3 right => new Vector3(1f, 0f, 0f);
        public Vector3 up => new Vector3(0f, 1f, 0f);
        public Vector3 forward => new Vector3(0f, 0f, 1f);
        public GameObject gameObject => new GameObject();
        public Vector3 InverseTransformPoint(Vector3 worldPoint) => worldPoint - position;
        public Vector3 InverseTransformDirection(Vector3 worldDirection) => worldDirection;
        public Vector3 TransformPoint(Vector3 localPoint) => localPoint + position;
        public Vector3 TransformDirection(Vector3 localDirection) => localDirection;
        public T GetComponent<T>() => default;
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color Lerp(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new Color(a.r + ((b.r - a.r) * t), a.g + ((b.g - a.g) * t), a.b + ((b.b - a.b) * t), a.a + ((b.a - a.a) * t));
        }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator *(Color a, float d) => new Color(a.r * d, a.g * d, a.b * d, a.a * d);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion Inverse(Quaternion q) => q;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        public static Quaternion LookRotation(Vector3 forward) => new Quaternion();
        public static Quaternion LookRotation(Vector3 forward, Vector3 up) => new Quaternion();
        public static Quaternion AngleAxis(float angle, Vector3 axis) => new Quaternion();
        public static Quaternion FromToRotation(Vector3 from, Vector3 to) => new Quaternion();
        public static Quaternion identity => new Quaternion();
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
        public static bool operator ==(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);
        public override bool Equals(object obj) => obj is Quaternion q && this == q;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() << 1);
    }

    public class GameObject
    {
        public string name;
        public Transform transform => new Transform();
        public bool activeSelf;
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public static GameObject CreatePrimitive(PrimitiveType type) => new GameObject("Primitive");
        public T GetComponent<T>() => default;
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : new() => new T();
    }

    public class Component
    {
        public GameObject gameObject => new GameObject();
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public static void Destroy(GameObject target) { }
        public static void Destroy(Material target) { }
        public static void Destroy(Component target) { }
        public static void Destroy(Texture2D target) { }
    }

    public static class Application
    {
        public static string streamingAssetsPath => string.Empty;
        public static bool isPlaying => false;
        public static bool isFocused => true;
    }

    public static class Screen
    {
        public static int width => 1920;
        public static int height => 1080;
    }

    public enum PrimitiveType { Sphere, Quad }

    public class Object
    {
        public static void Destroy(Object target) { }
        public static void Destroy(Component target) { }
    }

    public class Light : Component
    {
        public float intensity;
        public Color color;
    }

    public class Component2 { }

    public class Collider : Component { }

    public class Mesh
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Color[] colors;
        public int[] triangles;
        public Vector2[] uv;
        public void MarkDynamic() { }
        public void Clear() { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh;
    }

    public class MeshRenderer : Renderer
    {
        public Material sharedMaterial;
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode;
        public bool receiveShadows;
    }

    public class Shader
    {
        public string name;
        public static Shader Find(string name) => new Shader();
        public static void SetGlobalVector(string name, Vector3 value) { }
        public static void SetGlobalFloat(string name, float value) { }
        public static void SetGlobalColor(string name, Color value) { }
        public static void SetGlobalInt(string name, int value) { }
        public static void SetGlobalTexture(string name, Texture2D value) { }
    }

    public class Material
    {
        public Color color;
        public Shader shader;
        public Material(Shader shader) { this.shader = shader; }
        public Material(Material source) { shader = source?.shader; color = source != null ? source.color : default; }
        public void SetColor(string name, Color value) { }
        public void SetFloat(string name, float value) { }
        public void SetTexture(string name, Texture2D value) { }
    }

    public enum FilterMode { Point, Bilinear, Trilinear }

    public enum TextureWrapMode { Repeat, Clamp, Mirror }

    public enum TextureFormat { Alpha8, RGBA32, ARGB32, RGB24, RGBAHalf, RGBAFloat, RFloat }

    public class Texture2D
    {
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain, bool linear) { }
        public void SetPixels(Color[] pixels) { }
        public void Apply() { }
    }

    public class Camera : Component
    {
        public float nearClipPlane;
        public float farClipPlane;
        public static Camera main => new Camera();
        public Vector3 WorldToViewportPoint(Vector3 worldPoint) => worldPoint;
        public Vector3 WorldToScreenPoint(Vector3 worldPoint) => worldPoint;
    }

    public class Renderer : Component
    {
        public bool isVisible;
        public bool enabled;
    }

    public static class Vector3Ops
    {
    }

    public static class Input
    {
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKey(KeyCode key) => false;
        public static float GetAxis(string axis) => 0f;
        public static float GetAxisRaw(string axis) => 0f;
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public static class GUI
    {
        public static void Label(Rect position, string text) { }
    }

    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
    }

    public struct RaycastHit
    {
        public Transform transform;
        public Collider collider;
    }

    public static class Physics
    {
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
        {
            hitInfo = default;
            return false;
        }
    }

    public enum CursorLockMode { None, Locked }

    public enum KeyCode { Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Escape = 27, LeftShift = 304, LeftControl = 306, P = 112, E = 101, W = 119, A = 97, S = 115, D = 100, Space = 32 }

    public static class Cursor
    {
        public static CursorLockMode lockState;
        public static bool visible;
    }

    public static class Mathf
    {
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float PI = 3.14159274f;
        public const float Infinity = float.PositiveInfinity;
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Abs(float v) => v >= 0f ? v : -v;
        public static int Abs(int v) => v >= 0 ? v : -v;
        public static float Sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float Sin(float v) => (float)System.Math.Sin(v);
        public static float Cos(float v) => (float)System.Math.Cos(v);
        public static float Tan(float radians) => (float)System.Math.Tan(radians);
        public static float Asin(float v) => (float)System.Math.Asin(v);
        public static float Acos(float v) => (float)System.Math.Acos(v);
        public static float Atan(float v) => (float)System.Math.Atan(v);
        public static float Atan2(float y, float x) => (float)System.Math.Atan2(y, x);
        public static float Lerp(float a, float b, float t) => a + ((b - a) * t);
        public static float Pow(float v, float p) => (float)System.Math.Pow(v, p);
        public static float Exp(float v) => (float)System.Math.Exp(v);
        public static float Floor(float v) => (float)System.Math.Floor(v);
        public static int FloorToInt(float v) => (int)System.Math.Floor(v);
        public static float Ceil(float v) => (float)System.Math.Ceiling(v);
        public static int CeilToInt(float v) => (int)System.Math.Ceiling(v);
        public static int RoundToInt(float v) => (int)System.Math.Round(v);
        public static float Sign(float v) => v >= 0f ? 1f : -1f;
    }

    public sealed class LineRenderer
    {
        public int positionCount;
        public void SetPositions(Vector3[] p) { }
    }

    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) { }
    }

    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string h) { }
    }

    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string t) { }
    }

    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    public sealed class ContextMenuAttribute : Attribute
    {
        public ContextMenuAttribute(string itemName) { }
    }

    public sealed class DefaultExecutionOrderAttribute : Attribute
    {
        public DefaultExecutionOrderAttribute(int order) { }
    }

    public static class Time
    {
        public static float deltaTime => 0.016f;
    }

    public static class SystemInfo
    {
        public static int processorCount => 4;
    }
}

namespace Unity.Mathematics
{
    public struct double3
    {
        public double x, y, z;
        public double3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public double3(double v) { x = v; y = v; z = v; }
        public static double3 operator +(double3 a, double3 b) => new double3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static double3 operator -(double3 a, double3 b) => new double3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static double3 operator *(double3 a, double b) => new double3(a.x * b, a.y * b, a.z * b);
        public static double3 operator *(double b, double3 a) => new double3(a.x * b, a.y * b, a.z * b);
        public static double3 operator /(double3 a, double b) => new double3(a.x / b, a.y / b, a.z / b);
    }

    public struct float3
    {
        public float x, y, z;
        public float3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float3(float v) { x = v; y = v; z = v; }
        public static float3 operator +(float3 a, float3 b) => new float3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static float3 operator -(float3 a, float3 b) => new float3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static float3 operator *(float3 a, float b) => new float3(a.x * b, a.y * b, a.z * b);
        public static float3 operator *(float b, float3 a) => new float3(a.x * b, a.y * b, a.z * b);
        public static float3 operator /(float3 a, float b) => new float3(a.x / b, a.y / b, a.z / b);
    }

    // Настоящее имя Unity.Mathematics.math (строчные буквы) теперь в вызове
    // OrbitalElements.CalculateBatch. CS8981 (lowercase-тип как будущий
    // ключевик) — warning, подавлен локально: стаб не должен расходиться
    // с реальным API пакета.
#pragma warning disable CS8981
    public static class math
    {
        public static int max(int a, int b) => a > b ? a : b;
        public static int min(int a, int b) => a < b ? a : b;
        public static float max(float a, float b) => a > b ? a : b;
        public static float min(float a, float b) => a < b ? a : b;
        public static float clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float abs(float v) => v >= 0f ? v : -v;
        public static float sqrt(float v) => (float)System.Math.Sqrt(v);
        public static float floor(float v) => (float)System.Math.Floor(v);
        public static double max(double a, double b) => a > b ? a : b;
        public static double min(double a, double b) => a < b ? a : b;
        public static double abs(double v) => v >= 0d ? v : -v;
        public static double sqrt(double v) => System.Math.Sqrt(v);
        public static double floor(double v) => System.Math.Floor(v);
    }
#pragma warning restore CS8981
}

namespace Unity.Burst
{
    /// <summary>Стаб атрибута: на стенде Burst-компиляции нет, код идёт как обычный C#.</summary>
    public sealed class BurstCompileAttribute : Attribute
    {
        public BurstCompileAttribute() { }
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid, None, Temp, TempJob, Persistent }

    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    /// <summary>
    /// Функциональный стаб: backing-массив, чтобы тесты гоняли настоящий код
    /// job'ов (Execute по индексам), а не заглушки. Семантика копирования —
    /// разделяемое хранилище, как у настоящего NativeArray.
    /// </summary>
    public struct NativeArray<T> where T : struct
    {
        private T[] data;
        public NativeArray(int length, Allocator allocator) { data = new T[length]; }
        public int Length => data != null ? data.Length : 0;
        public bool IsCreated => data != null;
        public T this[int index] { get => data[index]; set => data[index] = value; }
        public void Dispose() { data = null; }
        public T[] ToArray() => data != null ? (T[])data.Clone() : new T[0];
    }
}

namespace Unity.Jobs
{
    public struct JobHandle
    {
        public void Complete() { }
    }

    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(this T job, int arrayLength, int innerloopBatchCount, JobHandle dependency = default)
            where T : struct, IJobParallelFor
        {
            return default;
        }
    }
}

namespace Simulation
{
    /// <summary>
    /// Тестовый стаб отсутствующего Burst-job (в 1game нет реализации):
    /// только чтобы компилировался настоящий OrbitalElements.CalculateBatch.
    /// </summary>
    public struct OrbitalElementsJob : Unity.Jobs.IJobParallelFor
    {
        public Unity.Collections.NativeArray<Unity.Mathematics.double3> Positions;
        public Unity.Collections.NativeArray<Unity.Mathematics.double3> Velocities;
        public Unity.Collections.NativeArray<double> Mus;
        public Unity.Collections.NativeArray<Galilego.Core.OrbitalElementsData> Results;

        public void Execute(int index)
        {
            throw new System.NotImplementedException("Тестовый стаб: batch не выполняется в стенде.");
        }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, DoubleSided, ShadowsOnly }
}

namespace UnityEngine.Rendering.HighDefinition
{
    public sealed class HDAdditionalCameraData : UnityEngine.Component
    {
        public enum AntialiasingMode { None, FastApproximateAntialiasing, TemporalAntialiasing }
        public enum ClearColorMode { Sky, Color, None }
        public AntialiasingMode antialiasing;
        public ClearColorMode clearColorMode;
        public UnityEngine.Color backgroundColorHDR;
    }
}
