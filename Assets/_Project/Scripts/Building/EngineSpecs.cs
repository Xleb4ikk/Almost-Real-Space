using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Паспорт двигателя: модель, топливная пара, тяга, импульс, масса —
    /// числа «для игры» из открытых источников (NASA/ULA/Astronautix).
    /// Путь к FBX-модели лежит в паспорте, поэтому EngineSpecs — ЕДИНОЕ место
    /// добавления двигателя: положи модель в Assets/_Project/Models/Parts/
    /// Engines/&lt;имя&gt;/ и добавь одну строку Register(...). ModelKey — подстрока
    /// в имени модели (RS25_Engine → RS25), по ней паспорт находится в сцене.
    /// Порядок записей — порядок кнопок и карточек в интерфейсе, первая
    /// запись — двигатель по умолчанию.
    /// </summary>
    public sealed class EngineSpec
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Подстрока в имени префаба модели (RS25_Engine → RS25).</summary>
        public readonly string ModelKey;

        /// <summary>Путь к FBX-модели от корня проекта (Assets/...).</summary>
        public readonly string ModelPath;

        /// <summary>Сухая масса двигателя, кг.</summary>
        public readonly double MassKg;

        /// <summary>Степень расширения сопла (площадь выхода/критики).</summary>
        public readonly double ExpansionRatio;

        /// <summary>Тяга в вакууме, Н.</summary>
        public readonly double ThrustVacuumNewtons;

        /// <summary>Удельный импульс в вакууме, с.</summary>
        public readonly double IspVacuumSeconds;

        /// <summary>
        /// Двигатель работает только в вакууме (верхняя ступень): в атмосфере
        /// на полную тягу не рассчитан.
        /// </summary>
        public readonly bool VacuumOnly;

        /// <summary>Топливная пара двигателя (окислитель, горючее, O/F, расход).</summary>
        public readonly BipropellantMix Mix;

        public EngineSpec(
            string id, string displayName, string modelKey, string modelPath, double massKg,
            double expansionRatio,
            double thrustVacuumNewtons, double ispVacuumSeconds, bool vacuumOnly,
            BipropellantMix mix)
        {
            Id = id;
            DisplayName = displayName ?? id;
            ModelKey = modelKey;
            ModelPath = modelPath;
            MassKg = massKg;
            ExpansionRatio = expansionRatio;
            ThrustVacuumNewtons = thrustVacuumNewtons;
            IspVacuumSeconds = ispVacuumSeconds;
            VacuumOnly = vacuumOnly;
            Mix = mix;
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    /// Реестр двигателей — ЕДИНОЕ место добавления: одна строка Register(...)
    /// с путём к модели; модели в сцене подхватываются сами (автосинхронизация
    /// EnginePrefabs или кнопка Galilego → Building → Setup Scene):
    ///   public static readonly EngineSpec X = Register(new EngineSpec(...));
    /// </summary>
    public static class EngineSpecs
    {
        // Список объявлен ПЕРВЫМ: статические поля инициализируются по порядку.
        private static readonly List<EngineSpec> all = new List<EngineSpec>();

        // RS-25 (SSME): 109 % RPL — 512 300 фунтов (2279 кН) в вакууме, Isp
        // 452 с, степень расширения 69:1, сухая масса 7 774 фунтов ≈ 3526 кг
        // (в игре пока плейсхолдер 3500 кг — как было раньше). Данные:
        // NASA/L3Harris. Работает и у земли (Isp 366 с) — НЕ вакуумный.
        // Топливо — пара PropellantPairs.Rs25 (LOX/LH2).
        public static readonly EngineSpec Rs25 = Register(new EngineSpec(
            "RS25", "RS-25", "RS25",
            "Assets/_Project/Models/Parts/Engines/RS-25/RS25_Engine.fbx",
            3500d, 69d, 2279000d, 452d, false,
            PropellantPairs.Rs25.Mix));

        // AJ10-118K (2-я ступень Delta II): 43.4 кН в вакууме, Isp 320.5 с,
        // степень расширения 65:1, масса 98 кг. Сопло 65:1 — вакуумный
        // двигатель верхней ступени. Источники: Astronautix/Purdue/ULA.
        // Топливо — пара PropellantPairs.Aj10 (N2O4/Аэрозин-50).
        public static readonly EngineSpec Aj10 = Register(new EngineSpec(
            "AJ10", "AJ10-118K", "AJ10",
            "Assets/_Project/Models/Parts/Engines/AJ10-118K/AJ10-118K_Engine.fbx",
            98d, 65d, 43400d, 320.5d, true,
            PropellantPairs.Aj10.Mix));

        public static IReadOnlyList<EngineSpec> All
        {
            get { return all; }
        }

        /// <summary>Двигатель по умолчанию (когда вид не задан).</summary>
        public static EngineSpec Default
        {
            get { return Rs25; }
        }

        /// <summary>Найти по Id; неизвестный Id — Default.</summary>
        public static EngineSpec Get(string id)
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
        /// Паспорт по имени префаба модели: у кого ModelKey встречается в имени
        /// (RS25_Engine → RS-25). Не нашлось — null (модель без паспорта).
        /// </summary>
        public static EngineSpec MatchPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (prefabName.IndexOf(all[i].ModelKey, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static EngineSpec Register(EngineSpec spec)
        {
            all.Add(spec);
            return spec;
        }
    }
}
