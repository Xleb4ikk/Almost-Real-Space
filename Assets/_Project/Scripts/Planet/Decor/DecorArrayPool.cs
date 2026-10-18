using System;
using System.Collections.Generic;
using Unity.Collections;

namespace Galilego.Universe
{
    /// <summary>
    /// Pool of native decor arrays: grass assembly borrows buffers and
    /// returns them instead of creating/freeing them on every rebuild.
    /// Arrays use Allocator.Persistent, so on teardown the pool is drained
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

    /// <summary>Buckets keyed by length: chunk buffer sizes are stable, so a
    /// pool hit is almost always available.</summary>
    internal static class DecorPool<T> where T : struct
    {
        private static readonly Dictionary<int, Stack<NativeArray<T>>> Buckets =
            new Dictionary<int, Stack<NativeArray<T>>>();

        private static readonly int ElemSize =
            Unity.Collections.LowLevel.Unsafe.UnsafeUtility.SizeOf<T>();

        /// <summary>Cap of idle native bytes per type T: buckets are keyed by EXACT
        /// length (which varies per chunk), so without it the pool grows unbounded.</summary>
        private const long MaxPooledBytes = 48L << 20;

        private static long pooledBytes;

        static DecorPool()
        {
            DecorArrayPool.Register(Clear);
        }

        public static NativeArray<T> Rent(int length)
        {
            if (Buckets.TryGetValue(length, out Stack<NativeArray<T>> bucket) && bucket.Count > 0)
            {
                pooledBytes -= (long)length * ElemSize;
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

            long bytes = (long)array.Length * ElemSize;
            if (pooledBytes + bytes > MaxPooledBytes)
            {
                // Releases idle buffers only: buffers in use are not in Buckets,
                // so there is nothing else to free here.
                Clear();
            }

            if (!Buckets.TryGetValue(array.Length, out Stack<NativeArray<T>> bucket))
            {
                bucket = new Stack<NativeArray<T>>();
                Buckets[array.Length] = bucket;
            }

            bucket.Push(array);
            pooledBytes += bytes;
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
            pooledBytes = 0;
        }
    }
}
