using UnityEngine;

namespace Galilego.Core
{
    /// <summary>
    /// Double-buffered trajectory renderer.
    /// Eliminates visual artifacts during rebuild.
    /// </summary>
    public sealed class TrajectoryRenderer
    {
        private TrajectoryBuffer frontBuffer = TrajectoryBuffer.Empty;
        private TrajectoryBuffer backBuffer = TrajectoryBuffer.Empty;

        public TrajectoryBuffer FrontBuffer => frontBuffer;

        /// <summary>
        /// Begin building trajectory into back buffer.
        /// </summary>
        public void BeginBuild(int capacity)
        {
            backBuffer = new TrajectoryBuffer(
                new Vector3[capacity],
                new double[capacity],
                0,
                0d,
                0d);
        }

        /// <summary>
        /// Add a point to the back buffer.
        /// </summary>
        public void AddPoint(int index, Vector3 point, double time)
        {
            if (index < backBuffer.Points.Length)
            {
                backBuffer.Points[index] = point;
                backBuffer.Times[index] = time;
            }
        }

        /// <summary>
        /// Complete build and swap buffers atomically.
        /// </summary>
        public void CompleteBuild(int count, double startTime, double endTime)
        {
            backBuffer = new TrajectoryBuffer(
                backBuffer.Points,
                backBuffer.Times,
                count,
                startTime,
                endTime);

            // Atomic swap
            frontBuffer = backBuffer;
        }

        /// <summary>
        /// Apply front buffer to LineRenderer.
        /// Call this from LateUpdate.
        /// </summary>
        public void ApplyToLineRenderer(LineRenderer lineRenderer)
        {
            if (lineRenderer == null || frontBuffer.Count == 0)
            {
                if (lineRenderer != null)
                    lineRenderer.positionCount = 0;
                return;
            }

            lineRenderer.positionCount = frontBuffer.Count;
            lineRenderer.SetPositions(frontBuffer.Points);
        }

        /// <summary>
        /// Check if front buffer has valid data.
        /// </summary>
        public bool HasValidData => frontBuffer.Count > 0;
    }
}
