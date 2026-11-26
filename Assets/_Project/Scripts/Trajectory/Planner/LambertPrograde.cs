using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>
    /// Ламберт с заданным смыслом обращения: h·n̂ &gt; 0 (prograde) либо &lt; 0. Решатель — LambertLagrange (форма Лагранжа,
    /// устойчив у дуги 180°); прежний LambertSolver (BMWS) остаётся для перекрёстной проверки.
    /// Проверяется автономным стендом Tools/PlannerCheck (mpmath/DOP853), не тестами Unity.
    /// </summary>
    public static class LambertPrograde
    {
        public static bool TrySolve(double mu, Vector3d r1, Vector3d r2, double dt, Vector3d normal, bool prograde,
            out Vector3d v1, out Vector3d v2)
        {
            return LambertLagrange.TrySolve(mu, r1, r2, dt, prograde ? normal : -normal, out v1, out v2);
        }
    }
}
