using System;
using System.Collections.Generic;
using System.Globalization;
using Galilego.Core;
using GalilegoPhysicsTests.Support.Artifacts;
using GalilegoPhysicsTests.Support.Oracles;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit.Sdk;

namespace GalilegoPhysicsTests.Support
{
    internal readonly struct RunSample
    {
        public readonly double Time;
        public readonly SpacecraftIntegrationState State;
        public readonly OracleState Oracle;
        public readonly double PositionError;
        public readonly double VelocityError;
        public readonly double MassError;
        public readonly double EnergyDrift;
        public readonly double AngularMomentumDrift;
        public readonly double LrlDrift;

        public RunSample(
            double time,
            SpacecraftIntegrationState state,
            OracleState oracle,
            double positionError,
            double velocityError,
            double massError,
            double energyDrift,
            double angularMomentumDrift,
            double lrlDrift)
        {
            Time = time;
            State = state;
            Oracle = oracle;
            PositionError = positionError;
            VelocityError = velocityError;
            MassError = massError;
            EnergyDrift = energyDrift;
            AngularMomentumDrift = angularMomentumDrift;
            LrlDrift = lrlDrift;
        }
    }

    internal sealed class RunData
    {
        public SpacecraftIntegrationState Initial;
        public SpacecraftIntegrationState Final;
        public List<DenseSegment> Segments;
        public long RhsCalls;
        public int Steps;
    }

    internal static class IntegratorDriver
    {
        public static RunData Integrate(
            Scenario scenario,
            IntegratorKind integrator,
            double tolerance,
            SpacecraftIntegrationState initial,
            double durationSeconds,
            int chunks)
        {
            if (chunks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(chunks));
            }

            var segments = new List<DenseSegment>();
            long calls = 0L;
            SpacecraftIntegrationState current = initial;
            double chunk = durationSeconds / chunks;
            double time = 0d;

            SpacecraftOrbitIntegrator.DerivativeFunction derivative = (state, t) =>
            {
                calls++;
                return scenario.Derivative(state, t);
            };

            for (int i = 0; i < chunks; i++)
            {
                double dt = (i == chunks - 1) ? (durationSeconds - time) : chunk;
                if (integrator == IntegratorKind.Dopri5)
                {
                    current = SpacecraftOrbitIntegrator.StepForward(
                        current, time, dt, derivative,
                        tolerance, tolerance, tolerance, tolerance,
                        segments, null).State;
                }
                else
                {
                    current = SpacecraftOrbitIntegrator.StepForwardDop853(
                        current, time, dt, derivative,
                        tolerance, tolerance, tolerance, tolerance,
                        segments, null).State;
                }

                time += dt;
            }

            return new RunData
            {
                Initial = initial,
                Final = current,
                Segments = segments,
                RhsCalls = calls,
                Steps = segments.Count
            };
        }
    }

    internal sealed class ScenarioExecution
    {
        private readonly RunData data;
        private readonly List<RunSample> samples;

        public Scenario Scenario { get; }
        public IntegratorKind Integrator { get; }
        public double Tolerance { get; }
        public int Chunks { get; }
        public SpacecraftIntegrationState FinalState => data.Final;
        public IReadOnlyList<RunSample> Samples => samples;
        public IReadOnlyList<DenseSegment> Segments => data.Segments;
        public long RhsCalls => data.RhsCalls;
        public int Steps => data.Steps;
        public double MaxPositionError { get; }
        public double MaxVelocityError { get; }
        public double MaxMassError { get; }
        public double MaxEnergyDrift { get; }
        public double MaxAngularMomentumDrift { get; }
        public double MaxLrlDrift { get; }

        public ScenarioExecution(Scenario scenario, IntegratorKind integrator, double tolerance, int chunks, RunData data)
        {
            Scenario = scenario;
            Integrator = integrator;
            Tolerance = tolerance;
            Chunks = chunks;
            this.data = data;
            samples = BuildSamples();

            double maxPos = 0d;
            double maxVel = 0d;
            double maxMass = 0d;
            double maxEnergy = 0d;
            double maxAngular = 0d;
            double maxLrl = 0d;
            foreach (RunSample sample in samples)
            {
                maxPos = Math.Max(maxPos, sample.PositionError);
                maxVel = Math.Max(maxVel, sample.VelocityError);
                maxMass = Math.Max(maxMass, sample.MassError);
                maxEnergy = Math.Max(maxEnergy, sample.EnergyDrift);
                maxAngular = Math.Max(maxAngular, sample.AngularMomentumDrift);
                maxLrl = Math.Max(maxLrl, sample.LrlDrift);
            }

            MaxPositionError = maxPos;
            MaxVelocityError = maxVel;
            MaxMassError = maxMass;
            MaxEnergyDrift = maxEnergy;
            MaxAngularMomentumDrift = maxAngular;
            MaxLrlDrift = maxLrl;

            if (Env.Dump)
            {
                ArtifactWriter.Write(ArtifactName, samples);
            }
        }

        public ScenarioExecution ConservesEnergy(double relativeTolerance)
        {
            if (!Scenario.CheckEnergy)
            {
                throw new XunitException("Сценарий \"" + Scenario.Id + "\" не является консервативным: проверка энергии неприменима.");
            }

            if (!(MaxEnergyDrift <= relativeTolerance))
            {
                ThrowFailure(
                    "сохранение удельной энергии",
                    MaxEnergyDrift,
                    relativeTolerance,
                    "отн.",
                    "Энергия дрейфует сильнее допуска.");
            }

            return this;
        }

        public ScenarioExecution ConservesAngularMomentum(double relativeTolerance)
        {
            if (!Scenario.CheckAngularMomentum)
            {
                throw new XunitException("Сценарий \"" + Scenario.Id + "\" не консервативен по моменту: проверка неприменима.");
            }

            if (!(MaxAngularMomentumDrift <= relativeTolerance))
            {
                ThrowFailure(
                    "сохранение удельного момента импульса",
                    MaxAngularMomentumDrift,
                    relativeTolerance,
                    "отн.",
                    "Вектор момента дрейфует сильнее допуска.");
            }

            return this;
        }

        public ScenarioExecution ConservesLrl(double absoluteTolerance)
        {
            if (!Scenario.CheckLrl)
            {
                throw new XunitException("Сценарий \"" + Scenario.Id + "\" не кеплеровский: проверка ЛРЛ неприменима.");
            }

            if (!(MaxLrlDrift <= absoluteTolerance))
            {
                ThrowFailure(
                    "сохранение вектора Лапласа–Рунге–Ленца",
                    MaxLrlDrift,
                    absoluteTolerance,
                    "абс. (безразмерный)",
                    "Вектор ЛРЛ дрейфует сильнее допуска.");
            }

            return this;
        }

        public ScenarioExecution MatchesOracle(double positionTolerance, double velocityTolerance)
        {
            if (Scenario.Oracle == null)
            {
                throw new XunitException("У сценария \"" + Scenario.Id + "\" нет независимого оракула.");
            }

            if (!(MaxPositionError <= positionTolerance))
            {
                ThrowFailure("ошибка позиции против оракула", MaxPositionError, positionTolerance, "м",
                    "Численное решение разошлось с независимым оракулом.");
            }

            if (!(MaxVelocityError <= velocityTolerance))
            {
                ThrowFailure("ошибка скорости против оракула", MaxVelocityError, velocityTolerance, "м/с",
                    "Скорость разошлась с независимым оракулом.");
            }

            return this;
        }

        public ScenarioExecution MatchesMass(double relativeTolerance)
        {
            if (Scenario.Oracle == null)
            {
                throw new XunitException("У сценария \"" + Scenario.Id + "\" нет независимого оракула.");
            }

            if (!(MaxMassError <= relativeTolerance))
            {
                ThrowFailure("относительная ошибка массы", MaxMassError, relativeTolerance, "отн.",
                    "Масса разошлась с аналитическим законом.");
            }

            return this;
        }

        public ScenarioExecution IsTimeReversible(double positionTolerance, double velocityTolerance)
        {
            SpacecraftIntegrationState reversedInitial = data.Final;
            reversedInitial.Velocity = -reversedInitial.Velocity;
            RunData reversed = IntegratorDriver.Integrate(
                Scenario, Integrator, Tolerance, reversedInitial, Scenario.DurationSeconds, 1);
            double positionError = (reversed.Final.Position - data.Initial.Position).Magnitude;
            // Time reversal maps (r, v) to (r, -v): the reverse run must reach
            // the initial position and the negated initial velocity.
            double velocityError = (reversed.Final.Velocity + data.Initial.Velocity).Magnitude;
            if (!(positionError <= positionTolerance))
            {
                ThrowFailure("обратимость по времени: позиция", positionError, positionTolerance, "м",
                    "Обратный прогон не вернулся в начальное положение.");
            }

            if (!(velocityError <= velocityTolerance))
            {
                ThrowFailure("обратимость по времени: скорость", velocityError, velocityTolerance, "м/с",
                    "Обратный прогон не вернулся в начальную скорость.");
            }

            return this;
        }

        public ScenarioExecution IsDeterministic()
        {
            RunData repeat = IntegratorDriver.Integrate(
                Scenario, Integrator, Tolerance, data.Initial, Scenario.DurationSeconds, Chunks);
            bool bitwise =
                repeat.Final.Position.X == data.Final.Position.X &&
                repeat.Final.Position.Y == data.Final.Position.Y &&
                repeat.Final.Position.Z == data.Final.Position.Z &&
                repeat.Final.Velocity.X == data.Final.Velocity.X &&
                repeat.Final.Velocity.Y == data.Final.Velocity.Y &&
                repeat.Final.Velocity.Z == data.Final.Velocity.Z &&
                repeat.Final.Mass == data.Final.Mass &&
                repeat.Steps == data.Steps &&
                repeat.RhsCalls == data.RhsCalls;
            if (!bitwise)
            {
                ThrowFailure("детерминизм", 0d, 0d, "бит-в-бит",
                    "Повторный прогон не совпал побитово. steps=" + data.Steps + "/" + repeat.Steps +
                    ", RHS=" + data.RhsCalls + "/" + repeat.RhsCalls);
            }

            return this;
        }

        public ScenarioExecution IsChunkingInvariant(double positionTolerance, double velocityTolerance)
        {
            ScenarioExecution chunked = Execute(Scenario, Integrator, Tolerance, 7);
            double positionError = (chunked.FinalState.Position - data.Final.Position).Magnitude;
            double velocityError = (chunked.FinalState.Velocity - data.Final.Velocity).Magnitude;
            if (!(positionError <= positionTolerance))
            {
                ThrowFailure("независимость от разбиения интервала: позиция", positionError, positionTolerance, "м",
                    "Разбиение на 7 кусков изменило конечное положение.");
            }

            if (!(velocityError <= velocityTolerance))
            {
                ThrowFailure("независимость от разбиения интервала: скорость", velocityError, velocityTolerance, "м/с",
                    "Разбиение на 7 кусков изменило конечную скорость.");
            }

            return this;
        }

        public ScenarioExecution ConvergesWith(double tighterTolerance)
        {
            if (!(tighterTolerance < Tolerance))
            {
                throw new XunitException("ConvergesWith: ожидался допуск строго меньше текущего.");
            }

            ScenarioExecution tighter = Execute(Scenario, Integrator, tighterTolerance, 1);
            bool errorNotWorse = tighter.MaxPositionError <= Math.Max(MaxPositionError * 1.05d, 1e-9d);
            bool moreSteps = tighter.Steps >= Steps;
            if (!errorNotWorse || !moreSteps)
            {
                ThrowFailure(
                    "сходимость при ужесточении допуска",
                    tighter.MaxPositionError,
                    Math.Max(MaxPositionError * 1.05d, 1e-9d),
                    "м",
                    "При tol=" + tighterTolerance.ToString("R") +
                    " ошибка не уменьшилась или шагов не прибавилось. steps=" + Steps + " -> " +
                    tighter.Steps + ".");
            }

            return this;
        }

        public static ScenarioExecution Execute(Scenario scenario, IntegratorKind integrator, double tolerance, int chunks)
        {
            RunData data = IntegratorDriver.Integrate(
                scenario, integrator, tolerance, scenario.InitialState, scenario.DurationSeconds, chunks);
            return new ScenarioExecution(scenario, integrator, tolerance, chunks, data);
        }

        private List<RunSample> BuildSamples()
        {
            const int sampleCount = 65;
            var result = new List<RunSample>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                double fraction = sampleCount == 1 ? 0d : (double)i / (sampleCount - 1);
                double time = Scenario.DurationSeconds * fraction;
                SpacecraftIntegrationState state;
                if (i == 0)
                {
                    state = data.Initial;
                }
                else if (i == sampleCount - 1)
                {
                    state = data.Final;
                }
                else if (data.Segments.Count > 0)
                {
                    state = DenseSegment.EvaluateTrack(data.Segments, time);
                }
                else
                {
                    state = data.Final;
                }

                OracleState oracle = Scenario.Oracle != null ? Scenario.Oracle(time) : default;
                double positionError = Scenario.Oracle != null
                    ? (state.Position - oracle.Position.ToVector3d()).Magnitude
                    : double.NaN;
                double velocityError = Scenario.Oracle != null
                    ? (state.Velocity - oracle.Velocity.ToVector3d()).Magnitude
                    : double.NaN;
                double massError = Scenario.Oracle != null && oracle.Mass > 0d
                    ? Math.Abs(state.Mass - oracle.Mass) / Math.Max(Math.Abs(oracle.Mass), 1d)
                    : double.NaN;
                ConservationDrift drift = Scenario.Mu > 0d
                    ? PropagationMetrics.Drift(data.Initial, state, Scenario.Mu)
                    : default;
                result.Add(new RunSample(
                    time, state, oracle, positionError, velocityError, massError,
                    drift.Energy, drift.AngularMomentum, drift.LaplaceRungeLenz));
            }

            return result;
        }

        private string ArtifactName => Scenario.Id + "-" + Scenario.IntegratorLabel(Integrator) + "-tol-" +
                                       Tolerance.ToString("R", CultureInfo.InvariantCulture);

        private void ThrowFailure(string check, double measured, double tolerance, string unit, string detail)
        {
            string csvPath = ArtifactWriter.Write(ArtifactName, samples);
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("ПАДЕНИЕ: " + check);
            builder.AppendLine("Сценарий:   " + Scenario.Id + " — " + Scenario.Title);
            builder.AppendLine("Интегратор: " + Scenario.IntegratorLabel(Integrator) + ", tol=" + Tolerance.ToString("R"));
            builder.AppendLine("Измерено:   " + measured.ToString("R") + " " + unit +
                               " > допуск " + tolerance.ToString("R") + " " + unit);
            if (!string.IsNullOrEmpty(detail))
            {
                builder.AppendLine(detail);
            }

            builder.AppendLine("Шагов: " + Steps + "; вызовов RHS: " + RhsCalls +
                               "; кусков: " + Chunks + "; сегментов dense: " + Segments.Count + ".");
            builder.AppendLine("Таблица (t, |Δpos|, м; |Δvel|, м/с; дрейф E, отн.):");
            builder.AppendLine("        t,          |Δpos|,          |Δvel|,         дрейф E");
            int stride = Math.Max(1, samples.Count / 10);
            for (int i = 0; i < samples.Count; i += stride)
            {
                RunSample sample = samples[i];
                builder.AppendLine(
                    sample.Time.ToString("0.000E+0", CultureInfo.InvariantCulture).PadLeft(10) + "  " +
                    sample.PositionError.ToString("0.000E+0", CultureInfo.InvariantCulture).PadLeft(15) + "  " +
                    sample.VelocityError.ToString("0.000E+0", CultureInfo.InvariantCulture).PadLeft(15) + "  " +
                    sample.EnergyDrift.ToString("0.000E+0", CultureInfo.InvariantCulture).PadLeft(15));
            }

            builder.AppendLine("Повторить: " + Scenario.FilterCommand(Integrator));
            builder.AppendLine("C#:        " + Scenario.Snippet(Integrator, Tolerance));
            builder.AppendLine("CSV:       " + csvPath);
            throw new XunitException(builder.ToString());
        }
    }
}
