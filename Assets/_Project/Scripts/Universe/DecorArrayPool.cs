using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Galilego.Universe
{
    /// <summary>
    /// Пул нативных массивов декора: сборки травы берут буферы в аренду и
    /// возвращают их, а не создают/освобождают на каждую пересборку. Без этого
    /// финализация пула (до ~100k инстансов: Stored, WorldMatrices, кандидаты)
    /// давала десятки МБ native alloc/free в одном кадре — это и читалось
    /// пролагом при появлении чанка травы.
    /// Массивы Allocator.Persistent, поэтому на teardown пул дренируется
    /// (DecorArrayPool.ClearAll).
    /// </summary>
    internal static class DecorArrayPool
    {
        private static readonly List<Action> ClearActions = new List<Action>();

        public static NativeArray<T> Rent<T>(int length) where T : struct
        {
            return length <= 0 ? default : DecorPool<T>.Rent(length);
        }

        public static void Return<T>(ref NativeArray<T> array) where T : struct
        {
            if (!array.IsCreated)
            {
                return;
            }

            DecorPool<T>.Return(array);
            array = default;
        }

        public static void ClearAll()
        {
            for (int i = 0; i < ClearActions.Count; i++)
            {
                ClearActions[i]();
            }
        }

        internal static void Register(Action clear)
        {
            ClearActions.Add(clear);
        }
    }

    /// <summary>Бакеты по длине: размеры буферов у чанка стабильны, поэтому
    /// попадание в пул почти всегда есть.</summary>
    internal static class DecorPool<T> where T : struct
    {
        private static readonly Dictionary<int, Stack<NativeArray<T>>> Buckets =
            new Dictionary<int, Stack<NativeArray<T>>>();

        static DecorPool()
        {
            DecorArrayPool.Register(Clear);
        }

        public static NativeArray<T> Rent(int length)
        {
            if (Buckets.TryGetValue(length, out Stack<NativeArray<T>> bucket) && bucket.Count > 0)
            {
                return bucket.Pop();
            }

            return new NativeArray<T>(length, Allocator.Persistent);
        }

        public static void Return(NativeArray<T> array)
        {
            if (!array.IsCreated)
            {
                return;
            }

            if (array.Length <= 0)
            {
                array.Dispose();
                return;
            }

            if (!Buckets.TryGetValue(array.Length, out Stack<NativeArray<T>> bucket))
            {
                bucket = new Stack<NativeArray<T>>();
                Buckets[array.Length] = bucket;
            }

            bucket.Push(array);
        }

        private static void Clear()
        {
            foreach (KeyValuePair<int, Stack<NativeArray<T>>> kv in Buckets)
            {
                foreach (NativeArray<T> array in kv.Value)
                {
                    array.Dispose();
                }
            }

            Buckets.Clear();
        }
    }
}
