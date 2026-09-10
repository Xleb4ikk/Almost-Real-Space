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

    public sealed class Transform
    {
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
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
    public struct NativeArray<T> where T : struct
    {
        public int Length => 0;
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
