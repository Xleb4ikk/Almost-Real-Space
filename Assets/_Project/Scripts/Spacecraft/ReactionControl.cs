using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Источник момента для AttitudePhysics. Отдельный от IDynamicsSource
    /// осознанно: силе момент не нужен, а вращению не нужна сила; IDynamicsSource
    /// вообще не имеет канала ориентации (Evaluate без attitude), поэтому RCS-сила
    /// идёт только через EvaluateFull ниже. Расширение IDynamicsSource каналом
    /// attitude — будущий breaking change, когда понадобится аэродинамический
    /// момент; здесь зафиксирован, не забыт.
    /// </summary>
    public interface ITorqueSource
    {
        Vector3d EvaluateTorque(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds);
    }

    /// <summary>
    /// Один RCS-двигатель: точка приложения и направление — в body-frame,
    /// активность щёлкает игровой цикл (или тест). Класс, не struct: активность
    /// мутирует, аллокаций в тике нет (пул на старте).
    /// </summary>
    public sealed class RcsThruster
    {
        public Vector3d Position;
        public Vector3d Direction;
        public double ThrustNewtons;
        public bool Active;

        public RcsThruster(Vector3d position, Vector3d direction, double thrustNewtons)
        {
            // Нулевое/вырожденное направление молча давало нулевой вклад (Normalized
            // от нуля — ноль): опечатка в конфиге = тихая потеря тяги. Громко здесь,
            // на конфигурации, а не на горячем пути Evaluate.
            if (!(direction.SqrMagnitude > 0d) || !direction.IsFinite)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction), "Направление дюза обязано быть конечным ненулевым вектором, получено " + direction + ".");
            }

            Position = position;
            Direction = direction;
            ThrustNewtons = thrustNewtons;
            Active = false;
        }
    }

    /// <summary>
    /// Блок RCS: сумма моментов активных двигателей τ = Σ r×F (body-frame).
    /// Полный канал через IDynamicsSource: задайте AttitudeProvider и добавьте
    /// блок в SpacecraftPhysics.Sources — сила (в мировой рамке) пойдёт в
    /// орбитальную RHS, момент (body-frame) — в снимок SpacecraftPhysics.
    /// TotalTorqueBody, откуда его читает вращение. Так сила+момент идут из
    /// одного источника; будущий аэродинамический момент ляжет в тот же канал.
    /// </summary>
    public sealed class RcsBlock : ITorqueSource, IDynamicsSource
    {
        public readonly List<RcsThruster> Thrusters = new List<RcsThruster>();

        /// <summary>
        /// Ориентация body→world для полного канала. null → Evaluate громко
        /// отказывает: молча нулевая сила RCS — тихая потеря управления.
        /// </summary>
        public Func<QuaternionD> AttitudeProvider;

        public Vector3d EvaluateTorque(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds)
        {
            Vector3d total = Vector3d.Zero;
            for (int i = 0; i < Thrusters.Count; i++)
            {
                RcsThruster thruster = Thrusters[i];
                if (thruster == null || !thruster.Active)
                {
                    continue;
                }

                total += Vector3d.Cross(thruster.Position, thruster.Direction.Normalized * thruster.ThrustNewtons);
            }

            return total;
        }

        public DynamicsContribution EvaluateFull(QuaternionD attitude, Vector3d angularVelocityBody, double timeSeconds)
        {
            Vector3d bodyForce = Vector3d.Zero;
            for (int i = 0; i < Thrusters.Count; i++)
            {
                RcsThruster thruster = Thrusters[i];
                if (thruster == null || !thruster.Active)
                {
                    continue;
                }

                bodyForce += thruster.Direction.Normalized * thruster.ThrustNewtons;
            }

            return new DynamicsContribution(
                Vector3d.Zero,
                attitude.Normalized.Rotate(bodyForce),
                0d,
                EvaluateTorque(attitude, angularVelocityBody, timeSeconds));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            if (AttitudeProvider == null)
            {
                throw new InvalidOperationException(
                    "RcsBlock в орбитальном пути без AttitudeProvider: сила и момент остались бы нулями молча. Задайте провайдер ориентации (снимок из AttitudePhysics) или уберите блок из Sources.");
            }

            // Угловая скорость в канал момента не идёт (τ от RCS не зависит от ω).
            return EvaluateFull(AttitudeProvider(), Vector3d.Zero, timeSeconds);
        }
    }
}
