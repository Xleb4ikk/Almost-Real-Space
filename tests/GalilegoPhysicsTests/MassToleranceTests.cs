using Galilego.Core;
using Galilego.Spacecraft;
using Xunit;
using Xunit.Abstractions;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// Синтетический источник с массовым расходом — быстрой явной функцией
    /// времени: massFlow = k·(1 + 0.9·sin(2πt/P)). Физического смысла не несёт.
    /// Ключевое свойство: при отсутствии гравитации и тяги движение корабля —
    /// равномерное прямолинейное, которое DOPRI5 интегрирует ТОЧНО при любом
    /// размере шага, то есть ошибки по позиции/скорости тождественно нулевые.
    /// Единственный источник ошибки усечения — масса, и любое дробление шага
    /// мельче максимального может быть решением ТОЛЬКО массового допуска.
    /// (Связанный вариант massFlow = k·|v|² на эксцентричной орбите для этого
    /// не годится: там масса меняется в том же темпе, что и орбита, и шаговый
    /// контроль гравитации везде доминирует над массовым — проверено, эффект
    /// тонет на уровне ~10%. Живёт ТОЛЬКО в тестовом проекте.)
    /// </summary>
    internal sealed class OscillatingMassFlowSource : IDynamicsSource
    {
        private readonly double rate;
        private readonly double period;

        public OscillatingMassFlowSource(double rate, double period)
        {
            this.rate = rate;
            this.period = period;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            double flow = rate * (1d + 0.9d * System.Math.Sin(2d * System.Math.PI * timeSeconds / period));
            return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, flow);
        }
    }

    /// <summary>
    /// Декоратор-счётчик вызовов RHS. Число вызовов производной — наблюдаемый
    /// снаружи прокси числа шагов интегратора (6 новых вычислений на попытку
    /// шага + FSAL-переиспользование): если допуск реально участвует в
    /// управлении шагом, его ужесточение обязано увеличить счётчик.
    /// </summary>
    internal sealed class CountingSource : IDynamicsSource
    {
        private readonly IDynamicsSource inner;
        public int Calls;

        public CountingSource(IDynamicsSource inner)
        {
            this.inner = inner;
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            Calls++;
            return inner.Evaluate(position, velocity, mass, timeSeconds);
        }
    }

    /// <summary>
    /// Тест D — MassAbsoluteTolerance реально управляет шагом, а не просто
    /// существует как параметр. Контролируемый эксперимент: одна и та же сцена
    /// (дрейф без гравитации + быстро осциллирующий расход), одни и те же
    /// допуски по позиции/скорости и rtol — отличается ТОЛЬКО
    /// MassAbsoluteTolerance (жёсткий 1e-9 vs фактически отключающий 1e3).
    /// При мягком допуске интегратор обязан идти максимальными шагами 600 с;
    /// при жёстком — дробить шаг под 50-секундную осцилляцию расхода.
    /// Дополнительно: жёсткий прогон обязан сойтись к аналитике
    /// m(T) = m0 − k·T (синус за целое число периодов интегрируется в ноль).
    /// </summary>
    public sealed class MassToleranceTests
    {
        private readonly ITestOutputHelper output;

        public MassToleranceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double Rate = 0.1;
        private const double OscillationPeriod = 50.0;
        private const double TotalTime = 3600.0; // 72 полных периода осцилляции
        private const double InitialMass = 1000.0;

        private static int RunCase(double massAtol, out double finalMass)
        {
            var counter = new CountingSource(new OscillatingMassFlowSource(Rate, OscillationPeriod));

            var physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-2,
                VelocityAbsoluteTolerance = 1e-5,
                MassAbsoluteTolerance = massAtol,
                RelativeTolerance = 1e-10
            };
            physics.Sources.Add(counter);

            var ship = new Spacecraft(Vector3d.Zero, Vector3d.Zero, InitialMass);
            physics.Step(ship, 0d, TotalTime);

            finalMass = ship.Mass;
            return counter.Calls;
        }

        [Fact]
        public void TightMassTolerance_IncreasesStepCount()
        {
            int callsLoose = RunCase(1e3, out double mLoose);
            int callsTight = RunCase(1e-9, out double mTight);

            double ratio = (double)callsTight / callsLoose;

            // Аналитика: за целое число периодов синус даёт ноль.
            double expectedMass = InitialMass - Rate * TotalTime;
            double tightRelErr = System.Math.Abs(mTight - expectedMass) / expectedMass;

            output.WriteLine($"Вызовов RHS: мягкий допуск = {callsLoose}, жёсткий = {callsTight}, отношение = {ratio:F1}.");
            output.WriteLine($"Масса: мягкая = {mLoose:R} кг, жёсткая = {mTight:R} кг, аналитика = {expectedMass:R} кг.");
            output.WriteLine($"Отн. ошибка жёсткого прогона vs аналитики: {tightRelErr:G}.");

            Assert.True(callsTight > 3 * callsLoose,
                $"Жёсткий MassAbsoluteTolerance не дробит шаг: {callsTight} vs {callsLoose}. Допуск не участвует в управлении шагом.");
            Assert.True(tightRelErr < 1e-6,
                $"Жёсткий прогон не сошёлся к аналитике m(T) = m0 − kT: ошибка {tightRelErr:G}.");
        }
    }
}
