using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>Относительно чего считается скорость (и маркеры навбола).</summary>
    public enum SpeedFrame { Surface, Orbit }

    /// <summary>Какая высота показана крупно (переключается нажатием на неё).</summary>
    public enum AltitudeMode { SeaLevel, Surface }

    /// <summary>Режим удержания ориентации (САС). Stability — держать текущую.</summary>
    public enum SasMode { Stability, Prograde, Retrograde, Normal, Antinormal, RadialIn, RadialOut }

    /// <summary>Шесть маркеров навбола (порядок = порядок в массивах рендера).</summary>
    public enum NavMarker { Prograde = 0, Retrograde = 1, Normal = 2, Antinormal = 3, RadialIn = 4, RadialOut = 5 }

    /// <summary>
    /// Одна ступень для блока ΔV. Поля 1-в-1 как у DvStage из DeltaVModel.cs —
    /// конвертация в DvTabletAdapter.
    /// </summary>
    public sealed class TabletStage
    {
        public int Index;
        public string EngineLabel = string.Empty;
        public string PropellantLabel = string.Empty;
        public int EngineCount;
        public double DryMassKg;
        public double PropellantMassKg;
        public double PropellantRemainingKg;
        public double StartMassKg;
        public double EndMassKg;
        public double ThrustN;
        public double IspS;
        public double BurnTimeS;
        public double DeltaVMs;
        public double PlanDeltaVMs;
        public double PlanBurnTimeS;
        public double TwrStart;
        public double TwrEnd;
        public bool IsSpent;
        public bool IsJettisoned;
        public bool IsActive;
        public string[] Warnings = new string[0];

        public bool HasEngine { get { return EngineCount > 0; } }
    }

    /// <summary>Параметры орбиты. Нет апоцентра (гипербола) — HasApoapsis = false.</summary>
    public struct TabletOrbit
    {
        public bool Valid;
        public bool HasApoapsis;
        public double ApAltitudeM;
        public double ApTimeS;
        public double ApSpeedMs;
        public double PeAltitudeM;
        public double PeTimeS;
        public double PeSpeedMs;
        public double PeriodS;
        public double Eccentricity;
        public double InclinationDeg;
    }

    /// <summary>
    /// ВСЁ, что показывает планшет. Игра заполняет этот объект (Fill), планшет
    /// ничего больше о симуляции не знает. Единицы: СИ (м, м/с, с, кг, Н, Па),
    /// углы — градусы, перегрузки — в g (g0 = 9.80665).
    /// </summary>
    public sealed class TabletTelemetry
    {
        // ── Опорное тело и система отсчёта ───────────────────────────────
        public string[] ReferenceBodies = { "ЗЕМЛЯ" };
        public int ReferenceBodyIndex;
        public SpeedFrame Frame = SpeedFrame.Surface;

        // ── Скорость / ускорение / гравитация ───────────────────────────
        public double SpeedMs;           // модуль скорости в выбранной системе отсчёта
        public double VerticalSpeedMs;   // + вверх (по радиус-вектору от тела)
        public double AccelerationMs2;   // ускорение (как решит игра: модуль d|v|/dt или собственное)
        public double GForce;            // перегрузка = собственное ускорение / g0
        public double GravityG;          // гравитация в точке аппарата, в g

        // ── Высота и среда ───────────────────────────────────────────────
        public double AltitudeSeaLevelM;
        public double AltitudeSurfaceM;
        public double PressurePa;

        // ── Орбита ───────────────────────────────────────────────────────
        public TabletOrbit Orbit;

        // ── Ориентация (навбол) ──────────────────────────────────────────
        /// <summary>Поворот корабля относительно местного горизонта: корабль → (X=восток, Y=вверх, Z=север).</summary>
        public Quaternion Attitude = Quaternion.identity;
        public bool MarkersValid;
        /// <summary>Единичные векторы в системе горизонта (восток, вверх, север).</summary>
        public Vector3 ProgradeDir = Vector3.forward;
        public Vector3 NormalDir = Vector3.up;
        public Vector3 RadialOutDir = Vector3.up;

        // ── Системы управления ───────────────────────────────────────────
        public bool SasEnabled;
        public SasMode SasMode;
        public bool RcsEnabled;

        // ── Масса / тяга / ΔV ────────────────────────────────────────────
        public double CurrentMassKg;
        public double ThrustN;
        public double Twr;
        public bool InFlight;
        public double TotalDeltaVMs;
        public double PlanDeltaVMs;
        public double TotalBurnTimeS;
        public string DvEnvLabel = "ВАКУУМ";
        public TabletStage[] Stages = new TabletStage[0];
        public string[] Notes = new string[0];

        /// <summary>Направление маркера навбола в системе горизонта.</summary>
        public Vector3 MarkerDir(NavMarker m)
        {
            switch (m)
            {
                case NavMarker.Prograde: return ProgradeDir;
                case NavMarker.Retrograde: return -ProgradeDir;
                case NavMarker.Normal: return NormalDir;
                case NavMarker.Antinormal: return -NormalDir;
                case NavMarker.RadialOut: return RadialOutDir;
                default: return -RadialOutDir;
            }
        }
    }

    /// <summary>Команды, которые планшет отправляет игре. Всё необязательное — по умолчанию пустышки.</summary>
    public interface ITabletCommands
    {
        void SetSasEnabled(bool on);
        void SetSasMode(SasMode mode);
        void SetRcsEnabled(bool on);
        void SetSpeedFrame(SpeedFrame frame);
        void SetReferenceBody(int index);
    }

    /// <summary>
    /// Мост «игра ↔ планшет». Единственное, что надо написать для подключения:
    /// наследник, который в Fill кладёт данные симуляции и исполняет команды.
    /// </summary>
    public abstract class TabletLink : MonoBehaviour, ITabletCommands
    {
        /// <summary>Заполнить телеметрию текущим состоянием (вызывается планшетом до 20 раз/с).</summary>
        public abstract void Fill(TabletTelemetry t);

        public virtual void SetSasEnabled(bool on) { }
        public virtual void SetSasMode(SasMode mode) { }
        public virtual void SetRcsEnabled(bool on) { }
        public virtual void SetSpeedFrame(SpeedFrame frame) { }
        public virtual void SetReferenceBody(int index) { }
    }
}
