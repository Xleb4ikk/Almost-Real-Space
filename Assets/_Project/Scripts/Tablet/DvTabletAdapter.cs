using Galilego.Building;

namespace Galilego.Tablet
{
    /// <summary>
    /// Конвертация готового ΔV-расчёта (DvResult из DeltaVModel.cs) в данные
    /// планшета. Вызывается из будущего TabletLink.Fill:
    ///   DvTabletAdapter.Apply(panel.Live ?? panel.Plan, telemetry);
    /// Поля копируются 1-в-1, ничего не пересчитывается.
    /// </summary>
    public static class DvTabletAdapter
    {
        public static void Apply(DvResult r, TabletTelemetry t)
        {
            if (r == null)
            {
                t.Stages = new TabletStage[0];
                t.Notes = new string[0];
                t.TotalDeltaVMs = 0d;
                return;
            }

            t.InFlight = r.InFlight;
            t.TotalDeltaVMs = r.TotalDeltaVMs;
            t.TotalBurnTimeS = r.TotalBurnTimeS;
            t.DvEnvLabel = r.Environment != null ? r.Environment.Label.ToUpperInvariant() : "ВАКУУМ";
            if (r.InFlight)
            {
                t.CurrentMassKg = r.CurrentMassKg;
            }

            double plan = 0d;
            int n = r.Stages.Count;
            if (t.Stages == null || t.Stages.Length != n)
            {
                t.Stages = new TabletStage[n];
            }

            for (int i = 0; i < n; i++)
            {
                DvStage s = r.Stages[i];
                TabletStage d = t.Stages[i] ?? (t.Stages[i] = new TabletStage());
                d.Index = s.Index;
                d.EngineLabel = s.EngineLabel;
                d.PropellantLabel = s.PropellantLabel;
                d.EngineCount = s.EngineCount;
                d.DryMassKg = s.DryMassKg;
                d.PropellantMassKg = s.PropellantMassKg;
                d.PropellantRemainingKg = s.PropellantRemainingKg;
                d.StartMassKg = s.StartMassKg;
                d.EndMassKg = s.EndMassKg;
                d.ThrustN = s.ThrustN;
                d.IspS = s.IspS;
                d.BurnTimeS = s.BurnTimeS;
                d.DeltaVMs = s.DeltaVMs;
                d.PlanDeltaVMs = s.PlanDeltaVMs;
                d.PlanBurnTimeS = s.PlanBurnTimeS;
                d.TwrStart = s.TwrStart;
                d.TwrEnd = s.TwrEnd;
                d.IsSpent = s.IsSpent;
                d.IsJettisoned = s.IsJettisoned;
                d.IsActive = s.IsActive;
                d.Warnings = s.Warnings.ToArray();
                plan += s.PlanDeltaVMs;
            }

            t.PlanDeltaVMs = plan;
            t.Notes = r.Notes.ToArray();
        }
    }
}
