using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Galilego.Events
{
    /// <summary>
    /// Общий бюджет 4мс на кадр на всю фоновую физику (варп-догон активного
    /// корабля + обновление мусора). Активный корабль обслуживается первым
    /// без урезания; мусору достаётся остаток, недообработанные объекты
    /// переходят в очередь следующего кадра — не теряются и не режутся.
    /// Очередь хранится между кадрами внутри планировщика.
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
        /// Один кадр: сначала активный корабль безусловно, затем мусор пока
        /// хватает бюджета. Остаток очереди сохраняется на следующий вызов.
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
