// Шим-заглушка UnityEngine: ТОЛЬКО те типы, которые реально используются
// в компилируемом наборе (Core/Universe/Spacecraft без Burst-джобов).
// Нужен, чтобы физика собиралась обычным dotnet build без редактора Unity.

namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class HeaderAttribute : System.Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class TooltipAttribute : System.Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class MinAttribute : System.Attribute
    {
        public MinAttribute(float min) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class RangeAttribute : System.Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [System.AttributeUsage(System.AttributeTargets.Field, AllowMultiple = false)]
    public sealed class SerializeFieldAttribute : System.Attribute
    {
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
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
    }

    public static class Mathf
    {
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }

    public sealed class Transform
    {
    }

    public class MonoBehaviour
    {
    }

    public class GameObject
    {
    }

    public class Renderer : Component
    {
    }

    public class Component
    {
    }

    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
    public sealed class ExecuteAlwaysAttribute : System.Attribute
    {
    }

    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false)]
    public sealed class DisallowMultipleComponentAttribute : System.Attribute
    {
    }

    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponentAttribute : System.Attribute
    {
        public RequireComponentAttribute(System.Type requiredComponent) { }
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

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static Quaternion identity => new Quaternion { w = 1f };
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }

    // Заглушки для полей профилей/палитр: в тестах не читаются,
    // нужны только чтобы файлы конфигурации компилировались.
    public class Texture2D
    {
    }

    public class Gradient
    {
        public void SetKeys(GradientColorKey[] keys, GradientAlphaKey[] alphaKeys) { }
    }

    public struct GradientColorKey
    {
        public GradientColorKey(Color color, float time)
        {
            Color = color;
            Time = time;
        }

        public Color Color;
        public float Time;
    }

    public struct GradientAlphaKey
    {
        public GradientAlphaKey(float alpha, float time)
        {
            Alpha = alpha;
            Time = time;
        }

        public float Alpha;
        public float Time;
    }

    public static class SystemInfo
    {
        // Реальное число ядер здесь не важно: используется только внутри
        // OrbitalElements.CalculateBatch, который в тестах не вызывается.
        public static int processorCount => 4;
    }
}

// Шим-заглушка Unity.Mathematics / Unity.Jobs / Unity.Collections: существуют
// только ради компиляции OrbitalElements.CalculateBatch (в тестах A/B/C
// не вызывается — тело может кидать NotImplementedException).
namespace Unity.Mathematics
{
    public struct double3
    {
        public double x;
        public double y;
        public double z;

        public double3(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public double3(double v)
        {
            x = v;
            y = v;
            z = v;
        }
    }

    public static class math
    {
        public static int max(int a, int b) => a > b ? a : b;
    }
}

namespace Unity.Collections
{
    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
    }

    // Хранит данные в обычном массиве: таблицы рельефа/декораций реально
    // используются тестами (SphericalTerrain/HeightfieldTerrain), а вот
    // Burst-раскладка по джобам — нет.
    public struct NativeArray<T> : System.IDisposable where T : struct
    {
        private T[] data;

        public NativeArray(int length, Allocator allocator)
        {
            data = length <= 0 ? System.Array.Empty<T>() : new T[length];
        }

        public NativeArray(T[] source, Allocator allocator)
        {
            data = source ?? System.Array.Empty<T>();
        }

        public int Length => data?.Length ?? 0;

        public bool IsCreated => data != null;

        public T this[int index]
        {
            get => data[index];
            set => data[index] = value;
        }

        public void Dispose()
        {
            data = null;
        }

        public T[] ToArray()
        {
            if (data == null || data.Length == 0)
                return System.Array.Empty<T>();
            T[] copy = new T[data.Length];
            System.Array.Copy(data, copy, data.Length);
            return copy;
        }
    }
}

namespace Unity.Jobs
{
    public struct JobHandle
    {
    }

    public interface IJobParallelFor
    {
    }

    public static class IJobParallelForExtensions
    {
        public static JobHandle Schedule<T>(T job, int arrayLength, int innerloopBatchCount, JobHandle dependsOn = default)
            where T : struct, IJobParallelFor
        {
            throw new System.NotImplementedException("Тестовый шим: batch-расчёт не поддерживается.");
        }
    }
}

// Заглушка Burst-джоба, на который ссылается OrbitalElements.CalculateBatch.
// Сам метод в тестах не вызывается — достаточно совпадения имён полей.
namespace Galilego.Simulation
{
    public struct OrbitalElementsJob : Unity.Jobs.IJobParallelFor
    {
        public Unity.Collections.NativeArray<Unity.Mathematics.double3> Positions;
        public Unity.Collections.NativeArray<Unity.Mathematics.double3> Velocities;
        public Unity.Collections.NativeArray<double> Mus;
        public Unity.Collections.NativeArray<Galilego.Core.OrbitalElementsData> Results;
    }
}
