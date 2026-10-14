using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Условия, в которых считается ΔV: тело (для TWR — g на поверхности) и
    /// характеристики двигателей в этих условиях. СЕЙЧАС — только вакуум:
    /// тяга и Isp берутся вакуумные. Когда появится выбор планеты и высоты,
    /// менять нужно ТОЛЬКО этот класс: ThrustN / IspS начнут учитывать
    /// давление на высоте (F = F_вак − p·A_сопла при постоянном расходе,
    /// Isp = F / (ṁ·g0)), а сам расчёт ступеней трогать не придётся.
    /// </summary>
    public sealed class DvEnvironment
    {
        /// <summary>Стандартное ускорение свободного падения для Isp, м/с².</summary>
        public const double G0 = 9.80665d;

        /// <summary>Вакуум без тела: g = g0 (только для TWR).</summary>
        public static readonly DvEnvironment Default = new DvEnvironment(null, G0);

        public readonly string BodyName;

        /// <summary>g на поверхности тела, м/с² (для TWR).</summary>
        public readonly double SurfaceGravity;

        /// <summary>Высота над поверхностью, м (в вакууме не влияет).</summary>
        public readonly double AltitudeMeters;

        public DvEnvironment(string bodyName, double surfaceGravity, double altitudeMeters = 0d)
        {
            BodyName = string.IsNullOrEmpty(bodyName) ? "—" : bodyName;
            SurfaceGravity = surfaceGravity > 0d ? surfaceGravity : G0;
            AltitudeMeters = altitudeMeters;
        }

        /// <summary>Подпись условий для интерфейса.</summary>
        public string Label { get { return "вакуум"; } }

        /// <summary>Тяга двигателя в этих условиях, Н.</summary>
        public double ThrustN(EngineSpec engine)
        {
            return engine.ThrustVacuumNewtons;
        }

        /// <summary>Удельный импульс двигателя в этих условиях, с.</summary>
        public double IspS(EngineSpec engine)
        {
            return engine.IspVacuumSeconds;
        }

        /// <summary>Массовый расход двигателя, кг/с (постоянен: F_вак / (Isp_вак·g0)).</summary>
        public double MassFlowKgS(EngineSpec engine)
        {
            return engine.ThrustVacuumNewtons / (engine.IspVacuumSeconds * G0);
        }
    }

    /// <summary>Итог по одной ступени (0 — нижняя, горит первой).</summary>
    public sealed class DvStage
    {
        public int Index;

        /// <summary>Индексы деталей черновика, входящих в ступень (снизу вверх).</summary>
        public readonly List<int> PartIndices = new List<int>();

        /// <summary>Двигатели ступени: «RS-25 ×2». Пусто, если двигателя нет.</summary>
        public string EngineLabel = string.Empty;

        /// <summary>Топливо двигателей ступени: «LOX/LH2». Пусто, если двигателя нет.</summary>
        public string PropellantLabel = string.Empty;

        public int EngineCount;

        /// <summary>Сухая масса ступени (металл, двигатели, отделитель), кг.</summary>
        public double DryMassKg;

        /// <summary>Топливо, залитое в баки ступени при запуске, кг.</summary>
        public double PropellantMassKg;

        /// <summary>Топливо в баках ступени СЕЙЧАС, кг (до полёта равно залитому; у сброшенной — 0).</summary>
        public double PropellantRemainingKg;

        /// <summary>Топливо, которое ещё сгорит в этой ступени, кг.</summary>
        public double BurnedMassKg;

        /// <summary>Масса аппарата в момент (запуска) ступени, с верхними ступенями, кг.</summary>
        public double StartMassKg;

        /// <summary>Масса аппарата в момент выгорания ступени (до отделения), кг.</summary>
        public double EndMassKg;

        /// <summary>Тяга при запуске, Н.</summary>
        public double ThrustN;

        /// <summary>Расход при запуске, кг/с.</summary>
        public double MassFlowKgS;

        /// <summary>Эффективный Isp ступени (по всей оставшейся выработке), с.</summary>
        public double IspS;

        /// <summary>Время работы (в полёте — оставшееся), с.</summary>
        public double BurnTimeS;

        /// <summary>ΔV ступени (в полёте — оставшаяся), м/с.</summary>
        public double DeltaVMs;

        /// <summary>ΔV ступени по плану (полный бак), м/с. До полёта равна DeltaVMs.</summary>
        public double PlanDeltaVMs;

        /// <summary>Время работы по плану (полный бак), с.</summary>
        public double PlanBurnTimeS;

        /// <summary>TWR в момент (запуска) ступени и при выгорании (к g поверхности тела).</summary>
        public double TwrStart;
        public double TwrEnd;

        /// <summary>В полёте: по плану у ступени был ΔV, а теперь его не осталось (ступень ещё может быть на борту).</summary>
        public bool IsSpent;

        /// <summary>В полёте: ступень отделена и больше не часть корабля (из состояния модели).</summary>
        public bool IsJettisoned;

        /// <summary>В полёте: ступень, которая будет/работает (первая, где ещё есть ΔV).</summary>
        public bool IsActive;

        public bool HasEngine { get { return EngineCount > 0; } }

        /// <summary>Полная масса самой ступени (без верхних) при запуске, кг.</summary>
        public double OwnMassKg { get { return DryMassKg + PropellantMassKg; } }

        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Результат расчёта всего аппарата.</summary>
    public sealed class DvResult
    {
        public readonly List<DvStage> Stages = new List<DvStage>();
        public double TotalDeltaVMs;
        public double TotalBurnTimeS;

        /// <summary>Полная масса основной сборки при запуске, кг.</summary>
        public double TotalMassKg;

        /// <summary>Деталей, не соединённых с основной сборкой (в расчёт не входят).</summary>
        public int DetachedParts;

        public DvEnvironment Environment;

        /// <summary>Результат построен для полёта (по остатку топлива).</summary>
        public bool InFlight;

        /// <summary>
        /// Текущая масса корабля, кг. По массе (legacy) — переданное значение;
        /// по состоянию модели — Σ (сухая + топливо) не сброшенных ступеней.
        /// </summary>
        public double CurrentMassKg;

        /// <summary>Индекс активной ступени, −1 — ΔV не осталось (только в полёте).</summary>
        public int ActiveStageIndex = -1;

        /// <summary>
        /// Масса выработанных, но ещё не отделённых ступеней под активной, кг.
        /// ΔV посчитана в предположении, что они отделены.
        /// </summary>
        public double AttachedSpentMassKg;

        /// <summary>
        /// (legacy, по массе) На сколько корабль стал легче, чем может объяснить
        /// выгорание топлива сборки, кг (0 — всё объясняется).
        /// </summary>
        public double UnexplainedMassLossKg;

        /// <summary>Пояснения расчёта для интерфейса (в полёте).</summary>
        public readonly List<string> Notes = new List<string>();

        public bool IsEmpty { get { return Stages.Count == 0; } }
    }

    /// <summary>
    /// Чистый (без Unity) расчёт ΔV по ядрам ступеней. Вход — StageCore (из
    /// DeltaV.BuildStageAssembly или собранные вручную, например в стенде),
    /// никакой PlacedPart. Ядра НЕ меняются: расчёт идёт на копиях.
    ///
    /// ΔV ступени — формула Циолковского по участкам постоянной тяги (см.
    /// StageCore.Burn), масса на участке убывает на выгоревшее:
    ///   ΔV = Σ (F / ṁ) · ln(m_нач / m_кон).
    /// Масса при (запуске) ступени k = Σ по несброшенным j ≥ k (сухая_j +
    /// топливо_j): выработанные ступени ниже активной считаются отделёнными
    /// (то, что получится при штатном разделении); их ещё не сброшенная масса
    /// возвращается в AttachedSpentMassKg и пояснением в Notes.
    /// </summary>
    public static class StageDeltaV
    {
        /// <summary>
        /// ΔV по текущему состоянию ядер. inFlight=false — план (топливо
        /// считается залитым полностью, jettisoned игнорируется).
        /// jettisoned[k]=true — ступень k сброшена: массы не даёт, ΔV 0.
        /// </summary>
        public static DvResult Evaluate(
            DvEnvironment env,
            IReadOnlyList<StageCore> cores,
            IReadOnlyList<bool> jettisoned,
            int detachedParts,
            bool inFlight)
        {
            var result = new DvResult { Environment = env, DetachedParts = detachedParts, InFlight = inFlight };
            if (cores == null || cores.Count == 0)
            {
                return result;
            }

            // План — те же ядра с полными баками и без сброса.
            DvResult plan = null;
            if (inFlight)
            {
                var planCores = new List<StageCore>(cores.Count);
                for (int k = 0; k < cores.Count; k++)
                {
                    StageCore c = cores[k].Clone();
                    c.FillToStart();
                    planCores.Add(c);
                }

                plan = Evaluate(env, planCores, null, detachedParts, false);
            }

            int n = cores.Count;
            var gone = new bool[n];
            double launch = 0d;
            var work = new StageCore[n];
            for (int k = 0; k < n; k++)
            {
                work[k] = cores[k].Clone();
                if (!inFlight)
                {
                    work[k].FillToStart();
                }

                gone[k] = inFlight && jettisoned != null && k < jettisoned.Count && jettisoned[k];
                launch += work[k].DryMassKg + work[k].StartPropellantKg;
            }

            result.TotalMassKg = launch;

            // Масса «ступень + все верхние» по несброшенным ступеням.
            var above = new double[n + 1];
            for (int k = n - 1; k >= 0; k--)
            {
                above[k] = above[k + 1] + (gone[k] ? 0d : work[k].TotalMassKg);
            }

            result.CurrentMassKg = above[0];
            double g = env.SurfaceGravity;

            for (int k = 0; k < n; k++)
            {
                StageCore core = work[k];
                var stage = new DvStage
                {
                    Index = core.Index,
                    DryMassKg = core.DryMassKg,
                    PropellantMassKg = core.StartPropellantKg,
                    PropellantRemainingKg = gone[k] ? 0d : core.PropellantKg,
                    EngineCount = core.EngineCount,
                    IsJettisoned = gone[k],
                };
                stage.PartIndices.AddRange(core.PartIndices);
                FillLabels(stage, core);

                if (inFlight)
                {
                    stage.PlanDeltaVMs = plan.Stages[k].DeltaVMs;
                    stage.PlanBurnTimeS = plan.Stages[k].BurnTimeS;
                }

                if (gone[k])
                {
                    result.Stages.Add(stage);
                    continue;
                }

                stage.StartMassKg = above[k];

                // Для предупреждений остаток — это «залито» (иначе выработанная
                // ступень ругалась бы на отсутствие топлива).
                var basis = new double[core.Pools.Count];
                for (int i = 0; i < basis.Length; i++)
                {
                    basis[i] = inFlight ? core.Pools[i].Kg : core.Pools[i].StartKg;
                }

                StageBurnResult o = core.Burn(above[k], double.PositiveInfinity, true);
                stage.DeltaVMs = o.DeltaV;
                stage.BurnTimeS = o.TimeS;
                stage.EndMassKg = o.EndMassKg;
                stage.BurnedMassKg = o.BurnedKg;
                stage.ThrustN = o.ThrustFirstN;
                stage.MassFlowKgS = o.FlowFirstKgS;
                if (o.Started)
                {
                    stage.TwrStart = o.ThrustFirstN / (above[k] * g);
                    stage.TwrEnd = o.ThrustLastN / (o.EndMassKg * g);
                    double ratio = Math.Log(above[k] / o.EndMassKg);
                    stage.IspS = ratio > 0d ? o.DeltaV / (DvEnvironment.G0 * ratio) : 0d;
                }

                if (inFlight)
                {
                    stage.IsSpent = stage.PlanDeltaVMs > 0d && stage.DeltaVMs < 1e-3d;
                }
                else
                {
                    stage.PlanDeltaVMs = stage.DeltaVMs;
                    stage.PlanBurnTimeS = stage.BurnTimeS;
                }

                if (stage.IsSpent)
                {
                    stage.DeltaVMs = 0d;
                    stage.BurnTimeS = 0d;
                    stage.ThrustN = 0d;
                    stage.TwrStart = 0d;
                    stage.TwrEnd = 0d;
                    stage.IspS = 0d;
                }
                else
                {
                    double basisSum = 0d;
                    for (int i = 0; i < basis.Length; i++)
                    {
                        basisSum += basis[i];
                    }

                    FillWarnings(stage, core, basis, basisSum, o.Started);
                }

                result.Stages.Add(stage);
                result.TotalDeltaVMs += stage.DeltaVMs;
                result.TotalBurnTimeS += stage.BurnTimeS;
            }

            if (inFlight)
            {
                FinishFlight(result, cores, gone);
            }

            return result;
        }

        /// <summary>
        /// ΔV по состоянию, сообщённому физикой: остатки по пулам и флаги
        /// сброшенных ступеней. Состояния применяются к копиям ядер; пул без
        /// записи остаётся залитым. Записи, которым нет пула в ступени,
        /// считаются и выводятся в Notes (состояние не от этой сборки).
        /// </summary>
        public static DvResult EvaluateFromState(
            DvEnvironment env,
            IReadOnlyList<StageCore> freshCores,
            IReadOnlyList<StagePoolState> states,
            IReadOnlyList<bool> jettisoned,
            int detachedParts)
        {
            var cores = new List<StageCore>(freshCores.Count);
            for (int k = 0; k < freshCores.Count; k++)
            {
                StageCore c = freshCores[k].Clone();
                c.FillToStart();
                cores.Add(c);
            }

            int unmatched = 0;
            if (states != null)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    StagePoolState s = states[i];
                    StageCore target = null;
                    for (int k = 0; k < cores.Count; k++)
                    {
                        if (cores[k].Index == s.StageIndex)
                        {
                            target = cores[k];
                        }
                    }

                    if (target == null || !target.ApplyState(s.Component, s.Kg))
                    {
                        unmatched++;
                    }
                }
            }

            DvResult result = Evaluate(env, cores, jettisoned, detachedParts, true);
            if (unmatched > 0)
            {
                result.Notes.Add("Состояние двигательной установки не совпадает со сборкой ("
                    + unmatched + " зап.) — расчёт остатка неточен");
            }

            return result;
        }

        /// <summary>
        /// (legacy) Остаток по текущей массе корабля: сгоревшее = масса при
        /// запуске − currentMassKg, распределяется по ступеням в порядке плана
        /// (нижняя горит первой). Нужен, пока физика не отдаёт состояние модели.
        /// </summary>
        public static DvResult EvaluateByMass(
            DvEnvironment env, IReadOnlyList<StageCore> freshCores, double currentMassKg, int detachedParts)
        {
            var cores = new List<StageCore>(freshCores.Count);
            double launch = 0d;
            for (int k = 0; k < freshCores.Count; k++)
            {
                StageCore c = freshCores[k].Clone();
                c.FillToStart();
                cores.Add(c);
                launch += c.DryMassKg + c.StartPropellantKg;
            }

            double consumed = Math.Max(0d, launch - currentMassKg);
            double budget = consumed;
            for (int k = 0; k < cores.Count && budget > 0d; k++)
            {
                budget -= cores[k].Commit(budget);
            }

            DvResult result = Evaluate(env, cores, null, detachedParts, true);
            result.CurrentMassKg = currentMassKg;

            // Бюджет остался: корабль легче, чем сборка может объяснить выгоранием
            // (топливо кончилось, а масса всё падает) — данные полёта и сборка расходятся.
            if (budget > Math.Max(0.5d, consumed * 1e-4d))
            {
                result.UnexplainedMassLossKg = budget;
                result.Notes.Insert(0, "Масса упала на " + FormatKg(budget)
                    + " больше, чем может сгореть по сборке — расчёт остатка неточен");
            }

            return result;
        }

        private static void FinishFlight(DvResult result, IReadOnlyList<StageCore> cores, bool[] gone)
        {
            for (int k = 0; k < result.Stages.Count; k++)
            {
                if (!result.Stages[k].IsJettisoned && result.Stages[k].DeltaVMs > 0d)
                {
                    result.ActiveStageIndex = k;
                    result.Stages[k].IsActive = true;
                    break;
                }
            }

            // Выработанные ступени под активной — ещё на корабле (не сброшены).
            int active = result.ActiveStageIndex >= 0 ? result.ActiveStageIndex : result.Stages.Count;
            double attached = 0d;
            for (int k = 0; k < active && k < cores.Count; k++)
            {
                if (!gone[k])
                {
                    attached += result.Stages[k].DryMassKg + result.Stages[k].PropellantRemainingKg;
                }
            }

            result.AttachedSpentMassKg = attached;
            if (result.ActiveStageIndex >= 0 && attached > 1d)
            {
                result.Notes.Add("Выработанные ступени (" + FormatKg(attached)
                    + ") ещё на борту: ΔV посчитана для отделённых");
            }
        }

        // ─── Подписи и предупреждения ────────────────────────────────────────

        private static void FillLabels(DvStage stage, StageCore core)
        {
            var names = new List<string>();
            var fuels = new List<string>();
            for (int e = 0; e < core.Engines.Count; e++)
            {
                StageEngineGroup eg = core.Engines[e];
                names.Add(eg.Count > 1 ? eg.Name + " ×" + eg.Count : eg.Name);
                if (eg.Mix != null)
                {
                    string pair = eg.Mix.Oxidizer.Id + "/" + eg.Mix.Fuel.Id;
                    if (!fuels.Contains(pair))
                    {
                        fuels.Add(pair);
                    }
                }
            }

            stage.EngineLabel = string.Join(", ", names);
            stage.PropellantLabel = string.Join(", ", fuels);

            // Ступень без двигателя: показываем, чем залиты баки (если залиты).
            if (core.EngineCount == 0 && core.Pools.Count > 0)
            {
                var ids = new List<string>();
                for (int i = 0; i < core.Pools.Count; i++)
                {
                    ids.Add(core.Pools[i].Component.Id);
                }

                stage.PropellantLabel = string.Join("/", ids);
            }
        }

        /// <summary>
        /// core — ядро ПОСЛЕ выработки (Kg = то, что не сгорит); basis[i] — сколько
        /// в пуле i было на начало расчёта (залито/остаток); basisSum — сумма.
        /// </summary>
        private static void FillWarnings(DvStage stage, StageCore core, double[] basis, double basisSum, bool burned)
        {
            if (core.Engines.Count == 0)
            {
                stage.Warnings.Add(basisSum > 1d
                    ? "Нет двигателя — топливо ступени не используется"
                    : "Нет двигателя — ступень не разгоняет");
                return;
            }

            // Чего двигателю не хватает с самого начала.
            for (int e = 0; e < core.Engines.Count; e++)
            {
                StageEngineGroup eg = core.Engines[e];
                if (eg.Mix == null)
                {
                    stage.Warnings.Add(eg.Name + ": нет данных о топливе");
                    continue;
                }

                int ox = IndexOf(core, eg.Mix.Oxidizer);
                int fuel = IndexOf(core, eg.Mix.Fuel);
                string missing = string.Empty;
                if (ox < 0 || basis[ox] <= StageCore.Eps)
                {
                    missing = eg.Mix.Oxidizer.Id;
                }

                if (fuel < 0 || basis[fuel] <= StageCore.Eps)
                {
                    missing += (missing.Length > 0 ? " и " : string.Empty) + eg.Mix.Fuel.Id;
                }

                if (missing.Length > 0)
                {
                    stage.Warnings.Add(eg.Name + ": нет топлива " + missing);
                }
            }

            // Топливо, которое не подходит ни одному двигателю ступени.
            var foreign = new List<string>();
            for (int i = 0; i < core.Pools.Count; i++)
            {
                if (basis[i] <= StageCore.Eps)
                {
                    continue;
                }

                bool used = false;
                for (int e = 0; e < core.Engines.Count; e++)
                {
                    BipropellantMix mix = core.Engines[e].Mix;
                    if (mix != null && (mix.Oxidizer == core.Pools[i].Component || mix.Fuel == core.Pools[i].Component))
                    {
                        used = true;
                    }
                }

                if (!used)
                {
                    foreign.Add(core.Pools[i].Component.Id);
                }
            }

            if (foreign.Count > 0)
            {
                stage.Warnings.Add("Топливо " + string.Join(", ", foreign) + " не подходит двигателю");
            }

            // Остаток после выработки: заправлено не в пропорцию двигателя.
            double left = core.PropellantKg;
            if (burned && foreign.Count == 0 && left > Math.Max(1d, basisSum * 0.005d))
            {
                stage.Warnings.Add("Не сгорит " + FormatKg(left) + " топлива — баки не в пропорцию двигателя");
            }
        }

        private static int IndexOf(StageCore core, Propellant component)
        {
            for (int i = 0; i < core.Pools.Count; i++)
            {
                if (core.Pools[i].Component == component)
                {
                    return i;
                }
            }

            return -1;
        }

        public static string FormatKg(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("0.0") + " т" : kg.ToString("0") + " кг";
        }
    }
}
