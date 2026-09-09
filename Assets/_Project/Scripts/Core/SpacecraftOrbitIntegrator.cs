using System;

namespace Galilego.Core
{
    /// <summary>
    /// DOPRI5-степпер для полного состояния корабля (позиция, скорость, масса).
    /// Использует ТЕ ЖЕ коэффициенты DOPRI5Coefficients, что и OrbitIntegrator, —
    /// это не альтернативный интегратор, а тот же метод Dormand-Prince 5(4),
    /// применённый к более широкому вектору состояния. OrbitIntegrator.cs при
    /// этом не изменён и не должен изменяться этим файлом.
    ///
    /// Логика управления шагом (адаптивное удвоение/половинение, FSAL, формулы
    /// пересчёта шага) зеркалит OrbitIntegrator.StepForward. ЕДИНСТВЕННОЕ
    /// смысловое отличие — норма ошибки считается раздельно по Position /
    /// Velocity / Mass, каждая со своим абсолютным допуском: мерить ошибку
    /// одним числом для метров, м/с и килограммов физически бессмысленно,
    /// это разные величины с разными масштабами.
    /// </summary>
    public static class SpacecraftOrbitIntegrator
    {
        /// <summary>
        /// Правая часть ОДУ корабля: по состоянию на подшаге и времени возвращает
        /// производную (dPosition/dt = velocity, dVelocity/dt = acceleration,
        /// dMass/dt = -массовый расход). Обязана быть pure (без побочных эффектов):
        /// вызывается много раз на одном шаге, включая отклонённые шаги, поэтому
        /// внутри нельзя списывать топливо, менять состояние корабля или источников.
        /// </summary>
        public delegate SpacecraftIntegrationState DerivativeFunction(
            SpacecraftIntegrationState state, double timeSeconds);

        // Коэффициенты таблицы Бутчера — тот же единый источник правды, что и
        // у OrbitIntegrator. Ни одно число здесь не продублировано буквально.
        private const double a21 = DOPRI5Coefficients.a21;
        private const double a31 = DOPRI5Coefficients.a31;
        private const double a32 = DOPRI5Coefficients.a32;
        private const double a41 = DOPRI5Coefficients.a41;
        private const double a42 = DOPRI5Coefficients.a42;
        private const double a43 = DOPRI5Coefficients.a43;
        private const double a51 = DOPRI5Coefficients.a51;
        private const double a52 = DOPRI5Coefficients.a52;
        private const double a53 = DOPRI5Coefficients.a53;
        private const double a54 = DOPRI5Coefficients.a54;
        private const double a61 = DOPRI5Coefficients.a61;
        private const double a62 = DOPRI5Coefficients.a62;
        private const double a63 = DOPRI5Coefficients.a63;
        private const double a64 = DOPRI5Coefficients.a64;
        private const double a65 = DOPRI5Coefficients.a65;
        private const double a71 = DOPRI5Coefficients.a71;
        private const double a73 = DOPRI5Coefficients.a73;
        private const double a74 = DOPRI5Coefficients.a74;
        private const double a75 = DOPRI5Coefficients.a75;
        private const double a76 = DOPRI5Coefficients.a76;

        private const double b1 = DOPRI5Coefficients.b1;
        private const double b3 = DOPRI5Coefficients.b3;
        private const double b4 = DOPRI5Coefficients.b4;
        private const double b5 = DOPRI5Coefficients.b5;
        private const double b6 = DOPRI5Coefficients.b6;

        private const double bStar1 = DOPRI5Coefficients.bStar1;
        private const double bStar3 = DOPRI5Coefficients.bStar3;
        private const double bStar4 = DOPRI5Coefficients.bStar4;
        private const double bStar5 = DOPRI5Coefficients.bStar5;
        private const double bStar6 = DOPRI5Coefficients.bStar6;
        private const double bStar7 = DOPRI5Coefficients.bStar7;

        private const double d1 = DOPRI5Coefficients.d1;
        private const double d3 = DOPRI5Coefficients.d3;
        private const double d4 = DOPRI5Coefficients.d4;
        private const double d5 = DOPRI5Coefficients.d5;
        private const double d6 = DOPRI5Coefficients.d6;
        private const double d7 = DOPRI5Coefficients.d7;

        // Границы шага — те же, что у OrbitIntegrator, чтобы поведение
        // управления шагом совпадало, а не разъезжалось двумя наборами чисел.
        private const double MaxStepSize = OrbitIntegrator.DefaultMaxStepSize;
        private const double MinStepSize = OrbitIntegrator.DefaultMinStepSize;

        /// <summary>
        /// Интегрирует полное состояние вперёд на целевой шаг по времени
        /// адаптивным DOPRI5. Управление шагом — как в OrbitIntegrator.StepForward.
        ///
        /// Опциональный коллектор dense output: на каждый принятый внутренний шаг
        /// добавляется DenseSegment с коэффициентами Shampine (считаются из уже
        /// готовых стадий, k7 — FSAL-оценка: 0 новых RHS-оценок). «0-diff»
        /// означает поведение: segments == null даёт бит-в-бит тот же результат,
        /// что и раньше — сигнатура при этом расширена.
        /// </summary>
        public static SpacecraftIntegrationResult StepForward(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetDt,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null)
        {
            if (derivativeProvider == null)
                throw new ArgumentNullException(nameof(derivativeProvider));

            // Forward-only контракт: при targetDt<0 стадии RHS идут вперёд, а
            // часы назад — траектория молча мусор (аудит S1/A1). Все вызывающие
            // идут вперёд; обратный ход — громкий отказ, а не тихая порча.
            if (targetDt < 0d)
                throw new ArgumentOutOfRangeException(nameof(targetDt),
                    "Интегратор forward-only: targetDt=" + targetDt + " < 0. Обратный ход времени не поддержан.");

            if (targetDt == 0.0)
                return new SpacecraftIntegrationResult(initialState);

            if (!initialState.IsFinite)
                throw new InvalidOperationException(
                    "Начальное состояние корабля не-конечно: " + initialState + " — дальнейшая интеграция бессмысленна.");

            // Инициализация FSAL (First Same As Last): k7 принятого шага = k1 следующего.
            // Здесь кэшируется ПОЛНАЯ производная (включая dMass/dt), а не только
            // ускорение — иначе переиспользование k1 для массы было бы неверным.
            SpacecraftIntegrationState fsalDerivative = derivativeProvider(initialState, currentTimeSeconds);
            bool fsalValid = true;

            SpacecraftIntegrationState current = initialState;
            double time = currentTimeSeconds;
            double remainingDt = Math.Abs(targetDt);
            double direction = Math.Sign(targetDt);
            double dt = Math.Min(remainingDt, MaxStepSize);

            while (remainingDt > MinStepSize)
            {
                dt = Math.Min(dt, remainingDt);

                if (!fsalValid)
                {
                    fsalDerivative = derivativeProvider(current, time);
                    fsalValid = true;
                }

                DoPri5Step(
                    current, time,
                    dt,
                    derivativeProvider,
                    fsalDerivative,
                    out SpacecraftIntegrationState fifth,
                    out SpacecraftIntegrationState newFsalDerivative,
                    out double errorPos,
                    out double errorVel,
                    out double errorMass,
                    out ShampineCoeffs denseStep);

                // Раздельная нормировка ошибки: у каждой физической величины
                // свой абсолютный допуск, относительный — общий.
                double scalePos = positionAbsoluteTolerance + relativeTolerance * Math.Max(current.Position.Magnitude, fifth.Position.Magnitude);
                double scaleVel = velocityAbsoluteTolerance + relativeTolerance * Math.Max(current.Velocity.Magnitude, fifth.Velocity.Magnitude);
                double scaleMass = massAbsoluteTolerance + relativeTolerance * Math.Max(Math.Abs(current.Mass), Math.Abs(fifth.Mass));
                double normalizedError = Math.Max(errorPos / scalePos, Math.Max(errorVel / scaleVel, errorMass / scaleMass));

                if (normalizedError <= 1.0)
                {
                    if (segments != null)
                    {
                        segments.Add(new DenseSegment(time, time + (dt * direction), dt, current, fifth, denseStep));
                    }

                    current = fifth;
                    time += dt * direction;
                    remainingDt -= dt;

                    if (!current.IsFinite)
                    {
                        throw new InvalidOperationException(
                            "Интегратор получил не-конечное состояние (t=" + time.ToString("R") +
                            ", dt=" + dt.ToString("R") + ") — RHS вернул NaN/Inf (масса ≤ 0? сингулярность?).");
                    }

                    fsalDerivative = newFsalDerivative;
                    fsalValid = true;

                    double stepScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.2), 5.0);
                    dt = Math.Min(Math.Max(dt * stepScale, MinStepSize), MaxStepSize);
                }
                else
                {
                    double rejectScale = Math.Min(Math.Max(
                        0.9 * Math.Pow(1.0 / Math.Max(normalizedError, 1e-10), 0.2),
                        0.1), 0.5);
                    // Force-accept при клампе к MinStepSize: шаг пересчитывается
                    // с УМЕНЬШЕННЫМ dt (см. тот же фикс в OrbitIntegrator.StepForward),
                    // иначе пятый порядок принят, а dense-сегмент описывал бы чужой
                    // (старый, крупный) шаг.
                    double candidateDt = Math.Max(dt * rejectScale, MinStepSize);
                    if (candidateDt <= MinStepSize)
                    {
                        dt = MinStepSize;
                        DoPri5Step(
                            current, time,
                            dt,
                            derivativeProvider,
                            fsalDerivative,
                            out SpacecraftIntegrationState forced,
                            out SpacecraftIntegrationState forcedFsal,
                            out double forcedErrPos,
                            out double forcedErrVel,
                            out double forcedErrMass,
                            out ShampineCoeffs forcedDense);

                        if (segments != null)
                        {
                            segments.Add(new DenseSegment(time, time + (dt * direction), dt, current, forced, forcedDense));
                        }

                        current = forced;
                        time += dt * direction;
                        remainingDt -= dt;
                        if (!current.IsFinite)
                        {
                            throw new InvalidOperationException(
                                "Интегратор получил не-конечное состояние при force-accept (t=" + time.ToString("R") +
                                ", dt=" + dt.ToString("R") + ") — RHS вернул NaN/Inf.");
                        }

                        fsalDerivative = forcedFsal;
                        fsalValid = true;
                    }
                    else
                    {
                        dt = candidateDt;
                        fsalValid = false;
                    }
                }
            }

            return new SpacecraftIntegrationResult(current);
        }

        /// <summary>Интегрирует полное состояние к точному целевому времени.</summary>
        public static SpacecraftIntegrationResult StepToTime(
            SpacecraftIntegrationState initialState,
            double currentTimeSeconds,
            double targetTimeSeconds,
            DerivativeFunction derivativeProvider,
            double positionAbsoluteTolerance,
            double velocityAbsoluteTolerance,
            double massAbsoluteTolerance,
            double relativeTolerance,
            System.Collections.Generic.IList<DenseSegment> segments = null)
        {
            double dt = targetTimeSeconds - currentTimeSeconds;
            return StepForward(
                initialState, currentTimeSeconds, dt, derivativeProvider,
                positionAbsoluteTolerance, velocityAbsoluteTolerance,
                massAbsoluteTolerance, relativeTolerance, segments);
        }

        /// <summary>
        /// Один шаг DOPRI5 над полным состоянием. Стадии и веса — те же, что в
        /// OrbitIntegrator.DoPri5Step, только состояние широкое: каждая стадия k
        /// хранит сразу (dPosition/dt, dVelocity/dt, dMass/dt).
        /// </summary>
        private static void DoPri5Step(
            SpacecraftIntegrationState state,
            double time,
            double dt,
            DerivativeFunction derivativeProvider,
            SpacecraftIntegrationState fsalDerivative,
            out SpacecraftIntegrationState fifth,
            out SpacecraftIntegrationState lastDerivative,
            out double errPos,
            out double errVel,
            out double errMass,
            out ShampineCoeffs dense)
        {
            // Стадия 1 (FSAL)
            SpacecraftIntegrationState k1 = fsalDerivative;

            // Стадия 2
            SpacecraftIntegrationState k2 = derivativeProvider(
                state + k1 * (dt * a21), time + dt * DOPRI5Coefficients.c2);

            // Стадия 3
            SpacecraftIntegrationState k3 = derivativeProvider(
                state + k1 * (dt * a31) + k2 * (dt * a32), time + dt * DOPRI5Coefficients.c3);

            // Стадия 4
            SpacecraftIntegrationState k4 = derivativeProvider(
                state + k1 * (dt * a41) + k2 * (dt * a42) + k3 * (dt * a43),
                time + dt * DOPRI5Coefficients.c4);

            // Стадия 5
            SpacecraftIntegrationState k5 = derivativeProvider(
                state + k1 * (dt * a51) + k2 * (dt * a52) + k3 * (dt * a53) + k4 * (dt * a54),
                time + dt * DOPRI5Coefficients.c5);

            // Стадия 6
            SpacecraftIntegrationState k6 = derivativeProvider(
                state + k1 * (dt * a61) + k2 * (dt * a62) + k3 * (dt * a63) + k4 * (dt * a64) + k5 * (dt * a65),
                time + dt * DOPRI5Coefficients.c6);

            // Стадия 7 (FSAL-кандидат: вычисляется в точке t+dt)
            SpacecraftIntegrationState s7 =
                state + k1 * (dt * a71) + k3 * (dt * a73) + k4 * (dt * a74) + k5 * (dt * a75) + k6 * (dt * a76);
            SpacecraftIntegrationState k7 = derivativeProvider(s7, time + dt * DOPRI5Coefficients.c7);

            // Решение 5-го порядка
            fifth = state
                + k1 * (dt * b1) + k3 * (dt * b3) + k4 * (dt * b4) + k5 * (dt * b5) + k6 * (dt * b6);

            // Решение 4-го порядка (для оценки ошибки)
            SpacecraftIntegrationState fourth = state
                + k1 * (dt * bStar1) + k3 * (dt * bStar3) + k4 * (dt * bStar4)
                + k5 * (dt * bStar5) + k6 * (dt * bStar6) + k7 * (dt * bStar7);

            errPos = (fifth.Position - fourth.Position).Magnitude;
            errVel = (fifth.Velocity - fourth.Velocity).Magnitude;
            errMass = Math.Abs(fifth.Mass - fourth.Mass);

            // Shampine dense output (Hairer dopri5.f CONTD5): k7 здесь — это
            // f(t0+h, y1), т.е. ровно тот K2, что использует формула C3/C4.
            // Считается всегда (несколько векторных операций), в сегмент идёт
            // только для принятых шагов — коллектор решает вызывающий.
            SpacecraftIntegrationState dc1 = fifth - state;
            SpacecraftIntegrationState dc2 = (k1 * dt) - dc1;
            SpacecraftIntegrationState dc3 = dc1 - dc2 - (k7 * dt);
            SpacecraftIntegrationState dc4 =
                ((k1 * d1) + (k3 * d3) + (k4 * d4) + (k5 * d5) + (k6 * d6) + (k7 * d7)) * dt;
            dense = new ShampineCoeffs(dc1, dc2, dc3, dc4);

            // FSAL: k7 этого шага = k1 следующего (если шаг принят)
            lastDerivative = k7;
        }
    }
}
