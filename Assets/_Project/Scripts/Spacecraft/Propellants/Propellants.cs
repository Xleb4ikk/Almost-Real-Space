using System;
using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Один вид топлива (компонент). Неизменяемый. Всё, что зависит от вида
    /// топлива при расчёте бака, лежит здесь: плотность, запас на газовую
    /// подушку и масса металла/изоляции на м² стенки. Новый вид топлива —
    /// одна строка в Propellants (см. ниже).
    /// </summary>
    public sealed class Propellant
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Плотность жидкости при температуре хранения, кг/м³.</summary>
        public readonly double DensityKgM3;

        /// <summary>
        /// Масса конструкции (металл стенки) на м² оболочки бака с радиусом
        /// TankMassModel.ReferenceRadiusM, кг/м². Для других радиусов
        /// масштабируется (см. TankMassModel.ShellScale).
        /// </summary>
        public readonly double TankStructureKgM2;

        /// <summary>Теплоизоляция на м² оболочки, кг/м² (не зависит от радиуса).</summary>
        public readonly double TankInsulationKgM2;

        /// <summary>Доля объёма бака, не заливаемая (газовая подушка), 0..1.</summary>
        public readonly double UllageFraction;

        public Propellant(
            string id, string displayName, double densityKgM3,
            double tankStructureKgM2, double tankInsulationKgM2, double ullageFraction)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id топлива обязателен.", nameof(id));
            }

            if (!(densityKgM3 > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(densityKgM3), "Плотность должна быть > 0.");
            }

            Id = id;
            DisplayName = displayName ?? id;
            DensityKgM3 = densityKgM3;
            TankStructureKgM2 = tankStructureKgM2;
            TankInsulationKgM2 = tankInsulationKgM2;
            UllageFraction = ullageFraction;
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    /// Реестр КОМПОНЕНТОВ топлива (окислитель или горючее по отдельности) —
    /// точные данные: плотность при температуре хранения, стенка и изоляция
    /// бака, газовая подушка. НОВЫЙ КОМПОНЕНТ — одна строка Register(...):
    ///   public static readonly Propellant Rp1 = Register(new Propellant(
    ///       "RP1", "Керосин RP-1", 810d, 10.7d, 0d, 0.02d));
    /// Стенку бака для нового топлива можно взять как у LOX (оба хранятся
    /// при умеренном давлении). ВИД ТОПЛИВА ЦЕЛИКОМ (пара компонентов, O/F,
    /// расход и наполнение бака) — одной строкой в PropellantPairs ниже.
    /// </summary>
    public static class Propellants
    {
        // Список объявлен ПЕРВЫМ: статические поля инициализируются по порядку.
        private static readonly List<Propellant> all = new List<Propellant>();

        // Данные — бак Space Shuttle ET (NASA KLASS): Ø 8.41 м.
        //   LOX-бак: 15.04 м, 12 000 фунтов (5443 кг) пустой → 10.7 кг/м²
        //   LH2-бак: 29.46 м, 29 000 фунтов (13154 кг) пустой → 14.8 кг/м²
        // Площадь считается по форме бака в игре (цилиндр + плоские крышки).
        // Изоляция LH2 (напылённая пена ~40 кг/м³ × ~40 мм) — оценка, не из
        // источника; у LOX-бака ET изоляция тонкая, здесь 0.
        public static readonly Propellant Lox = Register(new Propellant(
            "LOX", "Жидкий кислород", 1141d, 10.7d, 0d, 0.02d));

        public static readonly Propellant Lh2 = Register(new Propellant(
            "LH2", "Жидкий водород", 70.8d, 14.8d, 1.5d, 0.02d));

        // Долгохранимая (стеллажная) пара AJ10-118K: N2O4/Аэрозин-50. Оба
        // компонента живут при обычной температуре в баках под давлением
        // наддува — стенку и изоляцию берём как у LOX (изоляция не нужна).
        // Плотности: N2O4 — 1.44246 г/см³ при 21 °C (Wikipedia/NIST ≈ 1443
        // кг/м³ при 20 °C); Аэрозин-50 — Astronautix 0.903 г/см³.
        public static readonly Propellant N2o4 = Register(new Propellant(
            "N2O4", "Азотный тетраоксид", 1443d, 10.7d, 0d, 0.02d));

        public static readonly Propellant Aerozine50 = Register(new Propellant(
            "AZ50", "Аэрозин-50", 903d, 10.7d, 0d, 0.02d));

        public static IReadOnlyList<Propellant> All
        {
            get { return all; }
        }

        /// <summary>Топливо по умолчанию (когда вид не задан).</summary>
        public static Propellant Default
        {
            get { return Lox; }
        }

        /// <summary>Найти по Id; неизвестный Id — Default.</summary>
        public static Propellant Get(string id)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        private static Propellant Register(Propellant propellant)
        {
            all.Add(propellant);
            return propellant;
        }
    }

    /// <summary>
    /// Пара «окислитель + горючее» двигателя: соотношение расхода по массе
    /// (O/F). Отсюда — во сколько раз бак горючего должен быть больше бака
    /// окислителя по объёму и сколько топлива реально сгорит, если баки
    /// заправлены не в пропорцию (остаток — мёртвый груз).
    /// Экземпляры создаёт реестр видов топлива PropellantPairs (там же точные
    /// O/F и расход двигателей).
    /// </summary>
    public sealed class BipropellantMix
    {
        public readonly Propellant Oxidizer;
        public readonly Propellant Fuel;

        /// <summary>Отношение массовых расходов окислитель/горючее (O/F).</summary>
        public readonly double OxidizerToFuelMassRatio;

        /// <summary>Полный расход смеси на полной тяге, кг/с.</summary>
        public readonly double MassFlowKgS;

        public BipropellantMix(
            Propellant oxidizer, Propellant fuel, double oxidizerToFuelMassRatio, double massFlowKgS)
        {
            Oxidizer = oxidizer;
            Fuel = fuel;
            OxidizerToFuelMassRatio = oxidizerToFuelMassRatio;
            MassFlowKgS = massFlowKgS;
        }

        public double OxidizerFlowKgS { get { return MassFlowKgS * OxidizerMassFraction; } }
        public double FuelFlowKgS { get { return MassFlowKgS - OxidizerFlowKgS; } }

        public double OxidizerMassFraction
        {
            get { return OxidizerToFuelMassRatio / (1d + OxidizerToFuelMassRatio); }
        }

        /// <summary>Объём горючего на единицу объёма окислителя (RS-25 ≈ 2.67).</summary>
        public double FuelToOxidizerVolumeRatio
        {
            get
            {
                return (1d / Fuel.DensityKgM3) / (OxidizerToFuelMassRatio / Oxidizer.DensityKgM3);
            }
        }

        /// <summary>
        /// Сколько массы смеси реально сгорит при данных запасах: ограничивает
        /// тот компонент, что кончится первым.
        /// </summary>
        public double BurnableMassKg(double oxidizerKg, double fuelKg)
        {
            double fromOxidizer = oxidizerKg / OxidizerMassFraction;
            double fromFuel = fuelKg / (1d - OxidizerMassFraction);
            return Math.Min(fromOxidizer, fromFuel);
        }
    }

    /// <summary>
    /// Наполнение бака: из каких компонентов и в каких долях ОБЪЁМА он заправлен.
    /// Бак под двигатель держит окислитель и горючее сразу (внутри перегородка),
    /// пропорция берётся из BipropellantMix двигателя — ракета не окажется с
    /// одним LOX без LH2. Однокомпонентное наполнение — PropellantLoad.Single.
    /// </summary>
    public sealed class PropellantLoad
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Двигательная пара, под которую рассчитано наполнение (или null).</summary>
        public readonly BipropellantMix Mix;

        private readonly Propellant[] components;
        private readonly double[] volumeFractions;

        private PropellantLoad(
            string id, string displayName, BipropellantMix mix, Propellant[] components, double[] volumeFractions)
        {
            Id = id;
            DisplayName = displayName ?? id;
            Mix = mix;
            this.components = components;
            this.volumeFractions = volumeFractions;
        }

        /// <summary>Наполнение под двигатель: объёмы компонентов — по O/F двигателя.</summary>
        public static PropellantLoad ForMix(string id, string displayName, BipropellantMix mix)
        {
            double fuelPerOxidizer = mix.FuelToOxidizerVolumeRatio;
            double oxidizerShare = 1d / (1d + fuelPerOxidizer);
            return new PropellantLoad(
                id, displayName, mix,
                new[] { mix.Oxidizer, mix.Fuel },
                new[] { oxidizerShare, 1d - oxidizerShare });
        }

        /// <summary>Бак с одним компонентом (для будущих видов: керосин, метан и т.п.).</summary>
        public static PropellantLoad Single(string id, string displayName, Propellant propellant)
        {
            return new PropellantLoad(id, displayName, null, new[] { propellant }, new[] { 1d });
        }

        public int Count { get { return components.Length; } }

        public Propellant Component(int index) { return components[index]; }

        public double VolumeFraction(int index) { return volumeFractions[index]; }

        /// <summary>Есть перегородка между компонентами (несколько компонентов).</summary>
        public bool HasBulkhead { get { return components.Length > 1; } }

        /// <summary>Масса конструкции на м² стенки (опорный радиус): среднее по объёму.</summary>
        public double StructureKgM2
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < components.Length; i++)
                {
                    sum += volumeFractions[i] * components[i].TankStructureKgM2;
                }

                return sum;
            }
        }

        /// <summary>Изоляция на м² стенки: среднее по объёму.</summary>
        public double InsulationKgM2
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < components.Length; i++)
                {
                    sum += volumeFractions[i] * components[i].TankInsulationKgM2;
                }

                return sum;
            }
        }

        /// <summary>Заливаемый объём компонента (без подушки), м³.</summary>
        public double UsableVolumeM3(int index, double tankVolumeM3)
        {
            return tankVolumeM3 * volumeFractions[index] * (1d - components[index].UllageFraction);
        }

        /// <summary>Масса компонента в баке заданного объёма при заполнении fill (0..1), кг.</summary>
        public double ComponentMassKg(int index, double tankVolumeM3, double fill)
        {
            return UsableVolumeM3(index, tankVolumeM3) * components[index].DensityKgM3 * fill;
        }
    }

    /// <summary>
    /// ВИД ТОПЛИВА ЦЕЛИКОМ — пара компонентов под группу двигателей: точные
    /// характеристики (O/F и расход) и наполнение бака, которое строится по
    /// ним само. Двигатель ссылается на пару в своём паспорте (EngineSpecs):
    /// PropellantPairs.X.Mix — карточка бака и кнопка наполнения появятся сами.
    /// </summary>
    public sealed class PropellantPair
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Смесь двигателя: компоненты, O/F, расход.</summary>
        public readonly BipropellantMix Mix;

        /// <summary>Наполнение бака под пару (пропорция — по O/F).</summary>
        public readonly PropellantLoad Load;

        public PropellantPair(
            string id, string displayName, Propellant oxidizer, Propellant fuel,
            double oxidizerToFuelMassRatio, double massFlowKgS)
        {
            Id = id;
            DisplayName = displayName ?? id;
            Mix = new BipropellantMix(oxidizer, fuel, oxidizerToFuelMassRatio, massFlowKgS);
            Load = PropellantLoad.ForMix(id, displayName, Mix);
        }
    }

    /// <summary>
    /// Реестр видов топлива (пар) — ЕДИНОЕ место добавления: одна строка
    /// Register(...) с компонентами, точными O/F и расходом; наполнение бака
    /// и кнопка в панели создаются сами, а двигатель ссылается на пару в своём
    /// паспорте (EngineSpecs):
    ///   public static readonly PropellantPair X = Register(new PropellantPair(
    ///       "X", "AJ10-118K · N2O4/Аэрозин-50",
    ///       Propellants.N2o4, Propellants.Aerozine50, 1.9d, 13.8d));
    /// Компонентов, которых ещё нет, сначала добавь в Propellants выше.
    /// </summary>
    public static class PropellantPairs
    {
        // Список объявлен ПЕРВЫМ: статические поля инициализируются по порядку.
        private static readonly List<PropellantPair> all = new List<PropellantPair>();

        // RS-25: LOX/LH2, O/F = 6.03, расход 514.49 кг/с (при 2279 кН в вакууме):
        // LOX ≈ 441 кг/с, LH2 ≈ 73 кг/с. Сверка: магистрали Shuttle ET при 104 %
        // несут 2787 и 465 фунтов/с, то есть 5.99.
        public static readonly PropellantPair Rs25 = Register(new PropellantPair(
            "RS25", "RS-25 · LOX/LH2", Propellants.Lox, Propellants.Lh2, 6.03d, 514.49d));

        // AJ10-118K (2-я ступень Delta II): N2O4/Аэрозин-50, O/F = 1.9,
        // расход 13.8 кг/с при 43.4 кН в вакууме и Isp 320.5 с
        // (43400 / (320.5 · 9.80665) = 13.81 кг/с). Сверка компонентов:
        // N2O4 ≈ 9.05 кг/с, Аэрозин-50 ≈ 4.76 кг/с (данные ULA/Astronautix).
        public static readonly PropellantPair Aj10 = Register(new PropellantPair(
            "AJ10", "AJ10-118K · N2O4/Аэрозин-50",
            Propellants.N2o4, Propellants.Aerozine50, 1.9d, 13.8d));

        public static IReadOnlyList<PropellantPair> All { get { return all; } }

        /// <summary>Вид топлива по умолчанию (первая запись реестра).</summary>
        public static PropellantPair Default { get { return Rs25; } }

        /// <summary>Найти по Id; неизвестный Id — Default.</summary>
        public static PropellantPair Get(string id)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        /// <summary>
        /// Дотронуться до реестра, чтобы его статическая инициализация успела
        /// зарегистрировать наполнения в PropellantLoads: порядок статической
        /// инициализации классов не гарантирован, а PropellantLoads читает
        /// список, который наполняют пары.
        /// </summary>
        public static void EnsureRegistered()
        {
        }

        private static PropellantPair Register(PropellantPair pair)
        {
            all.Add(pair);
            PropellantLoads.Register(pair.Load);   // кнопка в панели бака — сама
            return pair;
        }
    }

    /// <summary>
    /// Реестр наполнений бака: кнопки в панели строятся по All. Наполнения
    /// топливных пар регистрирует реестр PropellantPairs — отдельная запись
    /// нужна только однокомпонентным бакам (керосин и т.п.):
    ///   Register(PropellantLoad.Single("RP1", "Керосин RP-1", Propellants.Rp1))
    /// </summary>
    public static class PropellantLoads
    {
        private static readonly List<PropellantLoad> all = new List<PropellantLoad>();

        public static IReadOnlyList<PropellantLoad> All
        {
            get
            {
                PropellantPairs.EnsureRegistered();
                return all;
            }
        }

        /// <summary>Наполнение по умолчанию — вид топлива по умолчанию.</summary>
        public static PropellantLoad Default
        {
            get { return PropellantPairs.Default.Load; }
        }

        /// <summary>Найти по Id; неизвестный Id — Default.</summary>
        public static PropellantLoad Get(string id)
        {
            PropellantPairs.EnsureRegistered();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        /// <summary>Зарегистрировать наполнение (зовёт PropellantPairs.Register).</summary>
        public static PropellantLoad Register(PropellantLoad load)
        {
            all.Add(load);
            return load;
        }
    }
}
