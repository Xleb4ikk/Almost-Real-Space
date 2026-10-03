using System;
using UnityEngine;

namespace Galilego.Core
{
    /// <summary>
    /// Immutable trajectory buffer for deterministic rendering.
    /// 
    /// Double-buffering pattern:
    ///   - Back buffer: being built by prediction coroutine
    ///   - Front buffer: being rendered
    ///   - Swap: atomic exchange when build complete
    /// 
    /// This eliminates coroutine/render race conditions.
    /// </summary>
    public sealed class TrajectoryBuffer
    {
        public readonly Vector3[] Points;
        public readonly double[] Times;
        public readonly int Count;
        public readonly double StartTime;
        public readonly double EndTime;

        public TrajectoryBuffer(Vector3[] points, double[] times, int count, double startTime, double endTime)
        {
            Points = points;
            Times = times;
            Count = count;
            StartTime = startTime;
            EndTime = endTime;
        }

        public static TrajectoryBuffer Empty => new TrajectoryBuffer(
            Array.Empty<Vector3>(), Array.Empty<double>(), 0, 0d, 0d);
    }
}
