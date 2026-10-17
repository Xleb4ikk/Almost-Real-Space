using System;
using Galilego.Core;

namespace Galilego.Flight
{
    public enum ThrusterKind
    {
        Main = 0,          // маршевый двигатель (топливо из пулов ступени)
        Rcs = 1,           // сопло РСУ (своё топливо)
        AbortMotor = 2,    // двигатель увода САС (твёрдое топливо)
        JettisonMotor = 3  // двигатель отстрела САС
    }

    /// <summary>
    /// ОДНО сопло/двигатель с реальной точкой приложения. Position и Direction — в
    /// системе корабля (ось Y — вдоль ракеты, начало — низ сборки, как PartDefinition.Offset).
    /// Direction — направление СИЛЫ на корабль (противоположно выхлопу). Момент считается
    /// как (Position − ЦМ) × F, поэтому смещённый или выключенный двигатель, дрейф ЦМ
    /// от выгорания топлива и кант сопел дают реальный момент, а не «тягу из ЦМ».
    /// </summary>
    public sealed class Thruster
    {
        public const double G0 = 9.80665d;
        public const double SeaLevelPa = 101325d;

        public string Name;
        public ThrusterKind Kind;
        public int PartIndex;                       // деталь-хозяин (тело PhysX)
        public Vector3d Position;                   // точка приложения (для Main — шарнир подвеса)
        public Vector3d Direction = new Vector3d(0d, 1d, 0d);
        public double ThrustVacN;
        public double IspVacS = 300d;
        public double IspSlS;                       // ≤0 — как в вакууме
        public double MinThrottle;                  // нижний предел дросселя 0..1
        public double ThrottleRatePerS = 2d;        // скорость выхода на режим
        public double MaxOperatingPressurePa = double.PositiveInfinity; // выше — запуск невозможен
        public double GimbalMaxRad;
        public double GimbalRateRadS = 0.3d;
        public object EngineKey;                    // EngineSpec — связь с группой StageCore
        public int Stage = -1;
        public bool Enabled = true;
        public double OwnPropKg;                    // топливо РСУ / твёрдого мотора

        // ─ состояние ─
        public double Level;                        // фактическая доля тяги 0..1
        public double GimbalX, GimbalZ;             // углы подвеса (рад) вокруг осей X и Z корабля
        public bool Ignited;                        // твёрдые моторы: зажжён

        public double IspAt(double pressurePa)
        {
            if (!(IspSlS > 0d))
            {
                return IspVacS;
            }

            double k = Math.Min(1d, Math.Max(0d, pressurePa / SeaLevelPa));
            return IspVacS - ((IspVacS - IspSlS) * k);
        }

        /// <summary>Тяга при текущем уровне и давлении: F = ṁ·g0·Isp(p).</summary>
        public double ThrustAt(double pressurePa)
        {
            return Level * ThrustVacN * IspAt(pressurePa) / IspVacS;
        }

        /// <summary>Расход при текущем уровне, кг/с (от давления не зависит).</summary>
        public double MassFlow
        {
            get { return Level * ThrustVacN / (G0 * IspVacS); }
        }

        /// <summary>Направление силы с учётом подвеса.</summary>
        public Vector3d CurrentDirection()
        {
            Vector3d d = Direction.Normalized;
            if (GimbalX == 0d && GimbalZ == 0d)
            {
                return d;
            }

            QuaternionD rx = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), GimbalX);
            QuaternionD rz = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), GimbalZ);
            return rx.Rotate(rz.Rotate(d)).Normalized;
        }
    }

    /// <summary>Команды игрока/автопилота за кадр.</summary>
    public sealed class FlightControls
    {
        public double Pitch;     // −1..1, вращение вокруг оси X корабля
        public double Yaw;       // −1..1, вокруг Z
        public double Roll;      // −1..1, вокруг оси ракеты Y
        public Vector3d Translate; // РСУ-перемещение −1..1 по осям корабля
        public bool SasOn = true;  // удержание ориентации (автопилот)
        public bool RcsOn = true;
    }
}
