using System;

namespace UnityEngine
{
    [Serializable]
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public sealed class Transform
    {
        public Transform parent;
        public Vector3 position;
        public Vector3 localScale;
        public Quaternion rotation;
        public GameObject gameObject => new GameObject();
        public void SetParent(Transform p, bool worldPositionStays) { parent = p; }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color Lerp(Color a, Color b, float t) => a;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion Inverse(Quaternion q) => q;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public class GameObject
    {
        public string name;
        public Transform transform => new Transform();
        public bool activeSelf;
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public static GameObject CreatePrimitive(PrimitiveType type) => new GameObject("Primitive");
        public T GetComponent<T>() where T : new() => new T();
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : new() => new T();
    }

    public class Component
    {
        public GameObject gameObject => new GameObject();
        public Transform transform => gameObject.transform;
        public string name => gameObject.name;
        public T GetComponent<T>() where T : new() => new T();
    }

    public class Behaviour : Component
    {
        public bool enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
    }

    public static class Application
    {
        public static string streamingAssetsPath => string.Empty;
    }

    public enum PrimitiveType { Sphere }

    public class Object
    {
        public static void Destroy(Object target) { }
    }

    public class Component2 { }

    public class Collider : Object { }

    public class Mesh
    {
        public Vector3[] vertices;
        public Color[] colors;
        public int[] triangles;
        public void MarkDynamic() { }
        public void Clear() { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh;
    }

    public class MeshRenderer : Component
    {
        public Material sharedMaterial;
    }

    public class Shader
    {
        public static Shader Find(string name) => new Shader();
    }

    public class Material
    {
        public Material(Shader shader) { }
    }

    public class Camera : Component
    {
        public float nearClipPlane;
        public float farClipPlane;
    }

    public static class Input
    {
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static float GetAxis(string axis) => 0f;
        public static float GetAxisRaw(string axis) => 0f;
    }

    public enum CursorLockMode { None, Locked }

    public enum KeyCode { Alpha1 = 49, Alpha7 = 55, Escape = 27, LeftShift = 304, LeftControl = 306, P = 112 }

    public static class Cursor
    {
        public static CursorLockMode lockState;
    }

    public static class Mathf
    {
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
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
    }

    // Настоящее имя Unity.Mathematics.math (строчные буквы) теперь в вызове
    // OrbitalElements.CalculateBatch. CS8981 (lowercase-тип как будущий
    // ключевик) — warning, подавлен локально: стаб не должен расходиться
    // с реальным API пакета.
#pragma warning disable CS8981
    public static class math
    {
        public static int max(int a, int b) => a > b ? a : b;
    }
#pragma warning restore CS8981
}

namespace Unity.Collections
{
    /// <summary>Тестовый стаб: только сигнатура для компиляции CalculateBatch, тесты его не вызывают.</summary>
    public struct NativeArray<T> where T : struct
    {
        public int Length => 0;
        public T this[int index] { get => default; set { } }
    }
}

namespace Unity.Jobs
{
    public struct JobHandle { }

    public interface IJobParallelFor
    {
        void Execute(int index);
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(T job, int arrayLength, int innerloopBatchCount, JobHandle dependency)
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
