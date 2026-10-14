using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Тестовый источник: «живой» полёт на орбиту с придуманными числами. Нужен,
    /// чтобы смотреть и настраивать планшет без симуляции; кнопки на нём
    /// работают (САС, РСУ, система отсчёта, тело). Когда будет настоящий
    /// мост (наследник TabletLink), демо просто убирается.
    /// </summary>
    public sealed class DemoTabletLink : TabletLink
    {
        private bool sasEnabled = true;
        private SasMode sasMode = SasMode.Prograde;
        private bool rcsEnabled;
        private SpeedFrame frame = SpeedFrame.Surface;
        private int bodyIndex;

        public override void SetSasEnabled(bool on) { sasEnabled = on; }
        public override void SetSasMode(SasMode mode) { sasMode = mode; }
        public override void SetRcsEnabled(bool on) { rcsEnabled = on; }
        public override void SetSpeedFrame(SpeedFrame f) { frame = f; }
        public override void SetReferenceBody(int index) { bodyIndex = index; }

        public override void Fill(TabletTelemetry t)
        {
            Sample(t, Time.time, sasEnabled, sasMode, rcsEnabled, frame, bodyIndex);
        }

        /// <summary>Детерминированный кадр «в момент time» (используется и тестовым стендом).</summary>
        public static void Sample(TabletTelemetry t, float time, bool sas, SasMode mode, bool rcs, SpeedFrame frame, int body)
        {
            t.ReferenceBodies = new[] { "ЗЕМЛЯ", "ЛУНА" };
            t.ReferenceBodyIndex = body;
            t.Frame = frame;

            float heading = 92f + (6f * Mathf.Sin(time * 0.3f));
            float pitch = 24f + (5f * Mathf.Sin(time * 0.21f));
            float roll = 18f * Mathf.Sin(time * 0.17f);
            t.Attitude = Quaternion.Euler(-pitch, heading, -roll);

            float vh = Mathf.Deg2Rad * (heading + 34f);
            float vp = Mathf.Deg2Rad * (pitch - 38f);
            Vector3 v = new Vector3(Mathf.Sin(vh) * Mathf.Cos(vp), Mathf.Sin(vp), Mathf.Cos(vh) * Mathf.Cos(vp));
            Vector3 n = Vector3.Cross(Vector3.up, v).normalized;
            t.ProgradeDir = v;
            t.NormalDir = n;
            t.RadialOutDir = Vector3.Cross(v, n).normalized;
            t.MarkersValid = true;

            t.SasEnabled = sas;
            t.SasMode = mode;
            t.RcsEnabled = rcs;

            bool orbit = frame == SpeedFrame.Orbit;
            t.SpeedMs = orbit ? 2455.6 + (time * 3.1) : 1812.4 + (time * 3.1);
            t.VerticalSpeedMs = 412.7 - (time * 1.5);
            t.AccelerationMs2 = 21.37;
            t.GForce = 2.18;
            t.GravityG = 0.912;
            t.AltitudeSeaLevelM = 42150.8 + (time * 410d);
            t.AltitudeSurfaceM = 41890.3 + (time * 410d);
            t.PressurePa = 118.4;

            t.Orbit = new TabletOrbit
            {
                Valid = true,
                HasApoapsis = true,
                ApAltitudeM = 218400d,
                ApTimeS = 1384d,
                ApSpeedMs = 7392.4d,
                PeAltitudeM = -1240d,
                PeTimeS = 4510d,
                PeSpeedMs = 7918.7d,
                PeriodS = 5326d,
                Eccentricity = 0.0187d,
                InclinationDeg = 28.52d,
            };

            t.CurrentMassKg = 184650d;
            t.ThrustN = 2279000d;
            t.Twr = 1.26d;
            t.InFlight = true;
            t.TotalDeltaVMs = 9015d;
            t.PlanDeltaVMs = 10300d;
            t.TotalBurnTimeS = 842d;
            t.DvEnvLabel = "ВАКУУМ";
            t.Stages = new[]
            {
                new TabletStage
                {
                    Index = 0, EngineLabel = "RS-25 ×3", PropellantLabel = "LOX + LH2", EngineCount = 3,
                    DryMassKg = 21000d, PropellantMassKg = 98000d, PropellantRemainingKg = 41000d,
                    StartMassKg = 184650d, EndMassKg = 108400d, ThrustN = 6837000d, IspS = 452.3,
                    BurnTimeS = 301d, PlanBurnTimeS = 480d, DeltaVMs = 2510d, PlanDeltaVMs = 4200d,
                    TwrStart = 3.78d, TwrEnd = 6.43d, IsActive = true,
                },
                new TabletStage
                {
                    Index = 1, EngineLabel = "RS-25 ×1", PropellantLabel = "LOX + LH2", EngineCount = 1,
                    DryMassKg = 9000d, PropellantMassKg = 52000d, PropellantRemainingKg = 52000d,
                    StartMassKg = 71000d, EndMassKg = 19800d, ThrustN = 2279000d, IspS = 452.3,
                    BurnTimeS = 228d, PlanBurnTimeS = 228d, DeltaVMs = 4060d, PlanDeltaVMs = 4060d,
                    TwrStart = 3.27d, TwrEnd = 11.7d,
                },
                new TabletStage
                {
                    Index = 2, EngineLabel = "AJ10-118K ×1", PropellantLabel = "N2O4 + АЭРОЗИН-50", EngineCount = 1,
                    DryMassKg = 2200d, PropellantMassKg = 6000d, PropellantRemainingKg = 6000d,
                    StartMassKg = 9800d, EndMassKg = 3800d, ThrustN = 43400d, IspS = 319d,
                    BurnTimeS = 134d, PlanBurnTimeS = 134d, DeltaVMs = 2445d, PlanDeltaVMs = 2040d,
                    TwrStart = 0.45d, TwrEnd = 1.16d,
                    Warnings = new[] { "TWR меньше 1: старт с земли невозможен" },
                },
            };
            t.Notes = new[]
            {
                "Физика полёта берёт тягу 6 837 кН из SimulationRunner, а не из двигателей ступени: реальный разгон будет другим",
                "Отделитель в физике пока не сбрасывает ступень",
            };
        }
    }
}
