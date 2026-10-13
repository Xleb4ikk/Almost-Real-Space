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
    /// Isp = F / (ṁ·g0)), а сам расчёт ступеней (DeltaV) трогать не придётся.
    /// </summary>
    public sealed class DvEnvironment
    {
        /// <summary>Стандартное ускорение свободного падения для Isp, м/с².</summary>
        public const double G0 = 9.80665d;

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

        /// <summary>Топливо, залитое в баки ступени, кг.</summary>
        public double PropellantMassKg;

        /// <summary>Топливо, которое реально сгорит, кг.</summary>
        public double BurnedMassKg;

        /// <summary>Масса всего аппарата в момент запуска ступени (с верхними ступенями), кг.</summary>
        public double StartMassKg;

        /// <summary>Масса аппарата в момент выгорания ступени (до отделения), кг.</summary>
        public double EndMassKg;

        /// <summary>Тяга при запуске, Н.</summary>
        public double ThrustN;

        /// <summary>Расход при запуске, кг/с.</summary>
        public double MassFlowKgS;

        /// <summary>Эффективный Isp ступени (по всей выработке), с.</summary>
        public double IspS;

        public double BurnTimeS;
        public double DeltaVMs;

        /// <summary>TWR при запуске и при выгорании (к g поверхности тела).</summary>
        public double TwrStart;
        public double TwrEnd;

        public bool HasEngine { get { return EngineCount > 0; } }

        /// <summary>Полная масса самой ступени (без верхних), кг.</summary>
        public double OwnMassKg { get { return DryMassKg + PropellantMassKg; } }

        public readonly List<string> Warnings = new List<string>();
    }

    /// <summary>Результат расчёта всего аппарата.</summary>
    public sealed class DvResult
    {
        public readonly List<DvStage> Stages = new List<DvStage>();
        public double TotalDeltaVMs;
        public double TotalBurnTimeS;

        /// <summary>Полная масса основной сборки, кг.</summary>
        public double TotalMassKg;

        /// <summary>Деталей, не соединённых с основной сборкой (в расчёт не входят).</summary>
        public int DetachedParts;

        public DvEnvironment Environment;

        public bool IsEmpty { get { return Stages.Count == 0; } }
    }

    /// <summary>
    /// Расчёт ΔV собранного аппарата.
    ///
    /// Ступени. Основная сборка — связная по снапу группа деталей с наибольшей
    /// массой. Детали упорядочиваются снизу вверх по высоте пивота; ОТДЕЛИТЕЛЬ
    /// делит стопку: всё ниже него — одна ступень, и сам отделитель уходит
    /// вместе с ней. Нижняя ступень горит первой, затем отделяется, и горит
    /// следующая — с массой «своя + все верхние».
    ///
    /// Топливо ступени — только в её баках (перекачки между ступенями нет).
    /// Двигатель берёт компоненты своей пары из PropellantPairs в пропорции
    /// O/F; если один компонент кончился раньше (баки заправлены не по
    /// пропорции или подобраны не под двигатель), двигатель глохнет, а остаток
    /// остаётся мёртвым грузом. Двигатели ступени работают одновременно;
    /// когда часть из них глохнет, выработка продолжается на оставшихся.
    ///
    /// ΔV ступени — формула Циолковского по участкам постоянной тяги:
    ///   ΔV = Σ (F / ṁ) · ln(m_нач / m_кон).
    /// </summary>
    public static class DeltaV
    {
        private const double Eps = 1e-6d;

        public static DvResult Compute(IReadOnlyList<PlacedPart> parts, DvEnvironment env)
        {
            var result = new DvResult { Environment = env };
            if (parts == null || parts.Count == 0)
            {
                return result;
            }

            List<int> main = MainAssembly(parts, out int detached);
            result.DetachedParts = detached;
            if (main.Count == 0)
            {
                return result;
            }

            // Снизу вверх по высоте пивота (стабильно по индексу).
            main.Sort((a, b) =>
            {
                int c = parts[a].Pivot.y.CompareTo(parts[b].Pivot.y);
                return c != 0 ? c : a.CompareTo(b);
            });

            // Делим по отделителям: отделитель входит в ступень НИЖЕ себя.
            var groups = new List<List<int>>();
            var current = new List<int>();
            for (int i = 0; i < main.Count; i++)
            {
                current.Add(main[i]);
                if (parts[main[i]].Kind == PartKind.Separator)
                {
                    groups.Add(current);
                    current = new List<int>();
                }
            }

            if (current.Count > 0)
            {
                groups.Add(current);
            }

            // Масса каждой ступени и суффиксные суммы (масса «своя + верхние»).
            var ownMass = new double[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                for (int k = 0; k < groups[g].Count; k++)
                {
                    ownMass[g] += parts[groups[g][k]].Metrics.MassKg;
                }
            }

            var above = new double[groups.Count + 1];
            for (int g = groups.Count - 1; g >= 0; g--)
            {
                above[g] = above[g + 1] + ownMass[g];
            }

            result.TotalMassKg = above[0];
            for (int g = 0; g < groups.Count; g++)
            {
                DvStage stage = BurnStage(parts, groups[g], g, above[g], env);
                result.Stages.Add(stage);
                result.TotalDeltaVMs += stage.DeltaVMs;
                result.TotalBurnTimeS += stage.BurnTimeS;
            }

            return result;
        }

        // ─── Основная сборка ─────────────────────────────────────────────────

        /// <summary>Связная (по ParentIndex) группа с наибольшей массой.</summary>
        private static List<int> MainAssembly(IReadOnlyList<PlacedPart> parts, out int detached)
        {
            int n = parts.Count;
            var root = new int[n];
            for (int i = 0; i < n; i++)
            {
                root[i] = i;
            }

            for (int i = 0; i < n; i++)
            {
                int p = parts[i].ParentIndex;
                if (p >= 0 && p < n)
                {
                    Union(root, i, p);
                }
            }

            var mass = new Dictionary<int, double>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(root, i);
                mass.TryGetValue(r, out double m);
                mass[r] = m + parts[i].Metrics.MassKg;
            }

            int best = -1;
            double bestMass = -1d;
            for (int i = 0; i < n; i++)
            {
                int r = Find(root, i);
                if (r == i && mass[r] > bestMass)
                {
                    best = r;
                    bestMass = mass[r];
                }
            }

            var main = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (Find(root, i) == best)
                {
                    main.Add(i);
                }
            }

            detached = n - main.Count;
            return main;
        }

        private static int Find(int[] root, int x)
        {
            while (root[x] != x)
            {
                root[x] = root[root[x]];
                x = root[x];
            }

            return x;
        }

        private static void Union(int[] root, int a, int b)
        {
            int ra = Find(root, a);
            int rb = Find(root, b);
            if (ra != rb)
            {
                root[ra] = rb;
            }
        }

        // ─── Выработка одной ступени ─────────────────────────────────────────

        private sealed class Pool
        {
            public Propellant Component;
            public double LoadedKg;
            public double Kg;
            public double Flow;
        }

        private sealed class EngineGroup
        {
            public EngineSpec Spec;
            public int Count;
            public double ThrustN;
            public double MassFlowKgS;
            public int OxPool = -1;
            public int FuelPool = -1;
        }

        private static DvStage BurnStage(
            IReadOnlyList<PlacedPart> parts, List<int> members, int index, double startMass, DvEnvironment env)
        {
            var stage = new DvStage { Index = index, StartMassKg = startMass };
            var pools = new List<Pool>();
            var engines = new List<EngineGroup>();

            for (int k = 0; k < members.Count; k++)
            {
                int partIndex = members[k];
                PlacedPart part = parts[partIndex];
                stage.PartIndices.Add(partIndex);
                stage.DryMassKg += part.Metrics.DryMassKg;

                if (part.Kind == PartKind.FuelTank && part.Metrics.Load != null)
                {
                    PropellantLoad load = part.Metrics.Load;
                    for (int c = 0; c < load.Count; c++)
                    {
                        double kg = load.ComponentMassKg(c, part.Metrics.VolumeM3, 1d);
                        FindOrAddPool(pools, load.Component(c)).LoadedKg += kg;
                        stage.PropellantMassKg += kg;
                    }
                }
                else if (part.Kind == PartKind.Engine && part.EngineSpec != null)
                {
                    EngineGroup group = null;
                    for (int e = 0; e < engines.Count; e++)
                    {
                        if (engines[e].Spec == part.EngineSpec)
                        {
                            group = engines[e];
                        }
                    }

                    if (group == null)
                    {
                        group = new EngineGroup
                        {
                            Spec = part.EngineSpec,
                            ThrustN = env.ThrustN(part.EngineSpec),
                            MassFlowKgS = env.MassFlowKgS(part.EngineSpec),
                        };
                        engines.Add(group);
                    }

                    group.Count++;
                    stage.EngineCount++;
                }
            }

            for (int i = 0; i < pools.Count; i++)
            {
                pools[i].Kg = pools[i].LoadedKg;
            }

            // Привязка двигателей к пулам их компонентов.
            for (int e = 0; e < engines.Count; e++)
            {
                BipropellantMix mix = engines[e].Spec.Mix;
                if (mix != null)
                {
                    engines[e].OxPool = IndexOfPool(pools, mix.Oxidizer);
                    engines[e].FuelPool = IndexOfPool(pools, mix.Fuel);
                }
            }

            double mass = startMass;
            double dv = 0d;
            double time = 0d;
            double thrustFirst = 0d;
            double flowFirst = 0d;
            double thrustLast = 0d;
            bool started = false;

            int guard = pools.Count + engines.Count + 3;
            for (int iter = 0; iter < guard; iter++)
            {
                double thrust = 0d;
                double flow = 0d;
                for (int i = 0; i < pools.Count; i++)
                {
                    pools[i].Flow = 0d;
                }

                for (int e = 0; e < engines.Count; e++)
                {
                    EngineGroup eg = engines[e];
                    if (!IsFed(eg, pools))
                    {
                        continue;
                    }

                    double mdot = eg.MassFlowKgS * eg.Count;
                    double oxShare = eg.Spec.Mix.OxidizerMassFraction;
                    pools[eg.OxPool].Flow += mdot * oxShare;
                    pools[eg.FuelPool].Flow += mdot * (1d - oxShare);
                    thrust += eg.ThrustN * eg.Count;
                    flow += mdot;
                }

                if (flow <= 0d)
                {
                    break;
                }

                // Какой пул кончится первым при этом расходе.
                double tEvent = double.MaxValue;
                int first = -1;
                for (int i = 0; i < pools.Count; i++)
                {
                    if (pools[i].Flow > 0d)
                    {
                        double t = pools[i].Kg / pools[i].Flow;
                        if (t < tEvent)
                        {
                            tEvent = t;
                            first = i;
                        }
                    }
                }

                double after = mass - (flow * tEvent);
                dv += (thrust / flow) * Math.Log(mass / after);
                time += tEvent;

                if (!started)
                {
                    thrustFirst = thrust;
                    flowFirst = flow;
                    started = true;
                }

                thrustLast = thrust;

                for (int i = 0; i < pools.Count; i++)
                {
                    pools[i].Kg -= pools[i].Flow * tEvent;
                    if (i == first || pools[i].Kg < Eps)
                    {
                        pools[i].Kg = 0d;
                    }
                }

                mass = after;
            }

            stage.DeltaVMs = dv;
            stage.BurnTimeS = time;
            stage.EndMassKg = mass;
            stage.BurnedMassKg = startMass - mass;
            stage.ThrustN = thrustFirst;
            stage.MassFlowKgS = flowFirst;

            double g = env.SurfaceGravity;
            if (started)
            {
                stage.TwrStart = thrustFirst / (startMass * g);
                stage.TwrEnd = thrustLast / (mass * g);
                double ratio = Math.Log(startMass / mass);
                stage.IspS = ratio > 0d ? dv / (DvEnvironment.G0 * ratio) : 0d;
            }

            FillLabels(stage, engines, pools);
            FillWarnings(stage, engines, pools, started);
            return stage;
        }

        private static bool IsFed(EngineGroup eg, List<Pool> pools)
        {
            return eg.Spec.Mix != null
                && eg.OxPool >= 0 && eg.FuelPool >= 0
                && pools[eg.OxPool].Kg > Eps && pools[eg.FuelPool].Kg > Eps;
        }

        private static Pool FindOrAddPool(List<Pool> pools, Propellant component)
        {
            int i = IndexOfPool(pools, component);
            if (i >= 0)
            {
                return pools[i];
            }

            var pool = new Pool { Component = component };
            pools.Add(pool);
            return pool;
        }

        private static int IndexOfPool(List<Pool> pools, Propellant component)
        {
            for (int i = 0; i < pools.Count; i++)
            {
                if (pools[i].Component == component)
                {
                    return i;
                }
            }

            return -1;
        }

        // ─── Подписи и предупреждения ────────────────────────────────────────

        private static void FillLabels(DvStage stage, List<EngineGroup> engines, List<Pool> pools)
        {
            var names = new List<string>();
            var fuels = new List<string>();
            for (int e = 0; e < engines.Count; e++)
            {
                EngineGroup eg = engines[e];
                names.Add(eg.Count > 1 ? eg.Spec.DisplayName + " ×" + eg.Count : eg.Spec.DisplayName);
                if (eg.Spec.Mix != null)
                {
                    string pair = eg.Spec.Mix.Oxidizer.Id + "/" + eg.Spec.Mix.Fuel.Id;
                    if (!fuels.Contains(pair))
                    {
                        fuels.Add(pair);
                    }
                }
            }

            stage.EngineLabel = string.Join(", ", names);
            stage.PropellantLabel = string.Join(", ", fuels);

            // Ступень без двигателя: показываем, чем залиты баки (если залиты).
            if (stage.EngineCount == 0 && pools.Count > 0)
            {
                var ids = new List<string>();
                for (int i = 0; i < pools.Count; i++)
                {
                    ids.Add(pools[i].Component.Id);
                }

                stage.PropellantLabel = string.Join("/", ids);
            }
        }

        private static void FillWarnings(
            DvStage stage, List<EngineGroup> engines, List<Pool> pools, bool burned)
        {
            if (engines.Count == 0)
            {
                stage.Warnings.Add(stage.PropellantMassKg > 1d
                    ? "Нет двигателя — топливо ступени не используется"
                    : "Нет двигателя — ступень не разгоняет");
                return;
            }

            // Чего двигателю не хватает с самого начала.
            for (int e = 0; e < engines.Count; e++)
            {
                EngineGroup eg = engines[e];
                if (eg.Spec.Mix == null)
                {
                    stage.Warnings.Add(eg.Spec.DisplayName + ": нет данных о топливе");
                    continue;
                }

                string missing = string.Empty;
                if (eg.OxPool < 0 || pools[eg.OxPool].LoadedKg <= Eps)
                {
                    missing = eg.Spec.Mix.Oxidizer.Id;
                }

                if (eg.FuelPool < 0 || pools[eg.FuelPool].LoadedKg <= Eps)
                {
                    missing += (missing.Length > 0 ? " и " : string.Empty) + eg.Spec.Mix.Fuel.Id;
                }

                if (missing.Length > 0)
                {
                    stage.Warnings.Add(eg.Spec.DisplayName + ": нет топлива " + missing);
                }
            }

            // Топливо, которое не подходит ни одному двигателю ступени.
            var foreign = new List<string>();
            for (int i = 0; i < pools.Count; i++)
            {
                if (pools[i].LoadedKg <= Eps)
                {
                    continue;
                }

                bool used = false;
                for (int e = 0; e < engines.Count; e++)
                {
                    BipropellantMix mix = engines[e].Spec.Mix;
                    if (mix != null && (mix.Oxidizer == pools[i].Component || mix.Fuel == pools[i].Component))
                    {
                        used = true;
                    }
                }

                if (!used)
                {
                    foreign.Add(pools[i].Component.Id);
                }
            }

            if (foreign.Count > 0)
            {
                stage.Warnings.Add("Топливо " + string.Join(", ", foreign) + " не подходит двигателю");
            }

            // Остаток после выработки: заправлено не в пропорцию двигателя.
            double left = 0d;
            for (int i = 0; i < pools.Count; i++)
            {
                left += pools[i].Kg;
            }

            double usable = stage.PropellantMassKg;
            if (burned && foreign.Count == 0 && left > Math.Max(1d, usable * 0.005d))
            {
                stage.Warnings.Add("Не сгорит " + FormatKg(left) + " топлива — баки не в пропорцию двигателя");
            }
        }

        private static string FormatKg(double kg)
        {
            return kg >= 1000d ? (kg / 1000d).ToString("0.0") + " т" : kg.ToString("0") + " кг";
        }
    }
}
