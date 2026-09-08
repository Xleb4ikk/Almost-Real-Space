using System;

namespace UnityEngine
{
    [Serializable]
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public sealed class Transform { }

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
