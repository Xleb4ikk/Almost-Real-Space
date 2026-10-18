using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Galilego.Events
{
    /// <summary>
    /// Shared 4 ms per-frame budget for all background physics (warp catch-up of
    /// the active ship + debris updates). The active ship is serviced first
    /// without trimming; debris gets the remainder, and unprocessed objects move
    /// to the next frame's queue — never lost or truncated. The queue is kept
    /// between frames inside the scheduler.
    /// </summary>
    public sealed class FrameBudgetScheduler
    {
        public const double FrameBudgetMilliseconds = 4d;

        private readonly Queue<Action> pendingDebris = new Queue<Action>();

        public int PendingCount => pendingDebris.Count;

        public void EnqueueDebris(Action advance)
        {
            if (advance == null)
            {
                throw new ArgumentNullException(nameof(advance));
            }

            pendingDebris.Enqueue(advance);
        }

        public void EnqueueDebrisRange(IReadOnlyList<Action> advances)
        {
            if (advances == null)
            {
                return;
            }

            for (int i = 0; i < advances.Count; i++)
            {
                if (advances[i] != null)
                {
                    pendingDebris.Enqueue(advances[i]);
                }
            }
        }

        public void Clear()
        {
            pendingDebris.Clear();
        }

        /// <summary>
        /// One frame: first the active ship unconditionally, then debris while the
        /// budget lasts. The remaining queue is kept for the next call.
        /// </summary>
        public void RunFrame(Action advanceActiveShip, Stopwatch stopwatch)
        {
            if (advanceActiveShip == null)
            {
                throw new ArgumentNullException(nameof(advanceActiveShip));
            }

            if (stopwatch == null)
            {
                throw new ArgumentNullException(nameof(stopwatch));
            }

            advanceActiveShip();

            double budgetMs = FrameBudgetMilliseconds;
            while (pendingDebris.Count > 0)
            {
                if (stopwatch.Elapsed.TotalMilliseconds >= budgetMs)
                {
                    break;
                }

                Action next = pendingDebris.Dequeue();
                next();
            }
        }
    }
}
