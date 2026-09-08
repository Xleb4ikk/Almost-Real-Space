using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Граница между физикой и движком. Физика живёт в инерциальном
    /// астродинамическом кадре (Z-up, СИ, double, см. KeplerMath): там считаются
    /// гравитация, интеграторы, события. Движку нужен Y-up float-кадр.
    /// Граница ТИПИЗИРОВАНА: sim-вектор — отдельный тип (SimVector3, а в Unity —
    /// UnityEngine.Vector3), случайно скормить его функции, ждущей Vector3d,
    /// нельзя без явного прохода через этот мост. Маппинг ровно один, зафиксирован
    /// здесь, а не размазан по ToVector3():
    /// sim = (x, z, −y) — поворот на −90° вокруг X (det=+1, праворукость
    /// сохранена, cross product коммутирует с мостом); север +Z становится
    /// up +Y. Обратное — транспонированная матрица: astro = (sim.X, −sim.Z,
    /// sim.Y). Старая перестановка (x,z,y) была отражением (det=−1) и молча
    /// переворачивала знак всем величинам через cross product — поймано
    /// рецензией до кода. Vector3d.ToVector3() (голый каст без перестановки
    /// осей) помечен [Obsolete] — это был единственный оставшийся путь
    /// перепутать кадр мимо моста. Миграция Unity-boundary callers на мост —
    /// явным списком, вне ядра.
    /// </summary>
    public static class AstroFrame
    {
#if UNITY_5_3_OR_NEWER
        /// <summary>Астродинамический double-вектор в симуляционный float-кадр.</summary>
        public static UnityEngine.Vector3 ToSimulation(Vector3d astro)
        {
            return new UnityEngine.Vector3((float)astro.X, (float)astro.Z, (float)-astro.Y);
        }

        /// <summary>Симуляционный кадр обратно в астродинамический.</summary>
        public static Vector3d ToAstro(UnityEngine.Vector3 sim)
        {
            return new Vector3d(sim.x, -sim.z, sim.y);
        }

        /// <summary>Ориентация ядра (QuaternionD, body→world) в кватернион Unity:
        /// та же перестановка осей сопряжением — угол сохраняется, ось идёт через
        /// позиционный мост. Дёшево на double, конверсия в float — на выходе.</summary>
        public static UnityEngine.Quaternion ToSimulation(QuaternionD astro)
        {
            QuaternionD sim = ToSimulationFrame(astro);
            return new UnityEngine.Quaternion((float)sim.X, (float)sim.Y, (float)sim.Z, (float)sim.W);
        }
#else
        /// <summary>
        /// Вне Unity (тестовый стенд): тот же маппинг на double-тройке,
        /// чтобы roundtrip-тесты гоняли боевую перестановку осей, а не заглушку.
        /// </summary>
        public static SimVector3 ToSimulation(Vector3d astro)
        {
            return new SimVector3(astro.X, astro.Z, -astro.Y);
        }

        public static Vector3d ToAstro(SimVector3 sim)
        {
            return new Vector3d(sim.X, -sim.Z, sim.Y);
        }
#endif

        /// <summary>
        /// Мост ОРИЕНТАЦИЙ: q_sim = R·q_astro·R⁻¹, где R — тот же поворот
        /// (x, z, −y) позиционного моста (R_x(−90°), det=+1). Чистая
        /// double-функция — тестируется на стенде (T76); Unity-ветка выше лишь
        /// пакует результат в UnityEngine.Quaternion. Сопряжение сохраняет
        /// угол и переводит ось позиционным мостом: вращение вокруг севера
        /// +Z становится вращением вокруг up +Y.
        /// </summary>
        public static QuaternionD ToSimulationFrame(QuaternionD astro)
        {
            QuaternionD frame = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), -System.Math.PI / 2d);
            return (frame * astro * frame.Conjugated).Normalized;
        }
    }

#if !UNITY_5_3_OR_NEWER
    /// <summary>
    /// Официальный тип sim-кадра (Y-up) вне Unity — типизированная граница
    /// astro/sim: sim-вектор не совместим по типу с Vector3d, перепутать кадр
    /// мимо AstroFrame невозможно без явного каста полей. В Unity роль играет
    /// сам UnityEngine.Vector3 (float, тот же порядок полей x/y/z).
    /// </summary>
    public struct SimVector3
    {
        public double X;
        public double Y;
        public double Z;

        public SimVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
#endif
}
