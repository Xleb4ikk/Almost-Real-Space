using System;
using Galilego.Planning;

namespace Galilego.Tablet
{
    /// <summary>Перенос черновика планировщика в основной план манёвров («В ПЛАН»).</summary>
    public static class PlanExport
    {
        private static ManeuverRole Map(ImpulseRole r)
        {
            switch (r)
            {
                case ImpulseRole.PrepOrbit: return ManeuverRole.PrepOrbit;
                case ImpulseRole.Departure: return ManeuverRole.Departure;
                case ImpulseRole.FlybyBurn: return ManeuverRole.FlybyBurn;
                case ImpulseRole.Capture: return ManeuverRole.Capture;
                default: return ManeuverRole.Correction;
            }
        }

        /// <summary>Заменяет манёвры предыдущего решения с тем же plannerId. Возвращает число добавленных.</summary>
        public static int Apply(ManeuverPlan plan, PlanDraft draft, int plannerId, double universeNow)
        {
            plan.RemoveByPlanner(plannerId);
            if (draft == null || !draft.Valid) { return 0; }
            foreach (PlannedImpulse i in draft.Impulses)
            {
                plan.AddPlanned(Map(i.Role), plannerId, i.FrameBody, i.Time - universeNow, i.DvWorld, i.RelPos, i.RelVel, i.WorldPos, i.WorldVel, i.Time);
            }

            return draft.Impulses.Count;
        }
    }
}
