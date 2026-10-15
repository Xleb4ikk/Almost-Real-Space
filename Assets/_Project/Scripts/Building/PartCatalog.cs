using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Каталог деталей: имя, пределы размеров, метрики (масса/форма/узлы) и
    /// сборка визуала. Бака и отделитель — процедурные (радиусы/высота задаёт
    /// игрок), двигатель — модель RS-25 (габариты мерятся по мешам). Бак может
    /// быть цилиндром или обрезанным конусом-переходником: тогда нижний и
    /// верхний радиусы разные, а форма — тело вращения с наклонной образующей.
    /// Массы: бак — оболочка + топливо (формулы ProceduralFuelTank), отделитель —
    /// объём кольца из стали, двигатель — плейсхолдер до реальных расчётов.
    /// Прочности стыков — параметры (плейсхолдеры); отделитель слабее всех:
    /// он и должен разрываться первым.
    /// </summary>
    public static class PartCatalog
    {
        // Бак
        public const float TankDefaultRadius = 1.5f;
        public const float TankDefaultHeight = 6f;
        public const float TankMinRadius = 0.3f;
        public const float TankMaxRadius = 5f;
        public const float TankMinHeight = 1f;
        public const float TankMaxHeight = 15f;

        // Отделитель
        public const float SeparatorDefaultRadius = 1.5f;
        public const float SeparatorMinRadius = 0.3f;
        public const float SeparatorMaxRadius = 5f;

        // Плейсхолдеры физики
        private const double SeparatorSteelDensity = 7850d;

        /// <summary>Масса капсулы Orion (плейсхолдер, как у двигателей), кг.</summary>
        private const double CapsuleMassKg = 10400d;

        /// <summary>
        /// Верхний радиус физического тела капсулы (доля нижнего): капсула —
        /// конус от теплозащиты Ø5 м к носу. Для пересечений/лучей этого
        /// достаточно, точный силуэт даёт визуал.
        /// </summary>
        private const float CapsuleTopRadiusFraction = 0.38f;

        // Прочности стыков, Н: запас на переходные усилия PhysX (первый шаг
        // стыка держит вес мгновенно — пик в десятки раз выше веса) и на удар
        // при падении: 42-тонный бак при посадочном ударе даёт импульс
        // m·v/dt ~ десятки МН за шаг солвера, поэтому пределы высокие —
        // «ракета» не должна разваливаться от обычного падения с нескольких
        // метров. Отделитель слабее прочих (он и должен рваться первым —
        // ступени позже), но тоже переживает падение.
        private const double EngineJointNewtons = 1.5e8d;
        private const double TankJointNewtons = 1.5e8d;
        private const double SeparatorJointNewtons = 3.0e7d;

        /// <summary>Какой это вид детали (для UI/иконок/подсказок).</summary>
        public static string DisplayName(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Engine: return "Двигатель";
                case PartKind.FuelTank: return "Топливный бак";
                case PartKind.Capsule: return "Капсула Orion";
                default: return "Отделитель";
            }
        }

        /// <summary>
        /// Название детали с паспортом двигателя: у двигателя добавляется
        /// модель («Двигатель AJ10-118K»), у прочих паспорт не значим.
        /// </summary>
        public static string DisplayName(PartKind kind, EngineSpec engine)
        {
            if (kind != PartKind.Engine || engine == null)
            {
                return DisplayName(kind);
            }

            return "Двигатель " + engine.DisplayName;
        }

        /// <summary>Зажать размеры в допустимые пределы (иначе меш/физика ломаются).</summary>
        public static void ClampSizes(PartKind kind, ref float radius, ref float height)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                    radius = Mathf.Clamp(radius, TankMinRadius, TankMaxRadius);
                    height = Mathf.Clamp(height, TankMinHeight, TankMaxHeight);
                    break;
                case PartKind.Separator:
                    radius = Mathf.Clamp(radius, SeparatorMinRadius, SeparatorMaxRadius);
                    height = ProceduralSeparator.Thickness;
                    break;
                case PartKind.Capsule:
                    // Размеры капсулы фиксированы моделью — не трогаем.
                    break;
                default:
                    radius = 1f;
                    height = 1f;
                    break;
            }
        }

        /// <summary>Метрики детали под данные размеры (двигатель — из шаблона модели).</summary>
        public static PartMetrics Measure(PartKind kind, float sizeA, float sizeB, EngineTemplate engine)
        {
            return Measure(kind, TankForm.Cylinder, sizeA, sizeA, sizeB, null, engine);
        }

        /// <summary>
        /// Метрики детали под данные размеры (двигатель — из шаблона модели).
        /// Для бака: form — цилиндр или конус, bottomRadius/topRadius — радиусы
        /// торцов (у цилиндра верхний игнорируется), height — длина.
        /// </summary>
        public static PartMetrics Measure(
            PartKind kind, TankForm form, float bottomRadius, float topRadius, float height,
            EngineTemplate engine)
        {
            return Measure(kind, form, bottomRadius, topRadius, height, null, engine);
        }

        /// <summary>
        /// То же, что Measure выше, но с наполнением бака (load):
        /// масса бака считается от наполнения (TankMassModel). null —
        /// наполнение по умолчанию из реестра PropellantLoads.
        /// </summary>
        public static PartMetrics Measure(
            PartKind kind, TankForm form, float bottomRadius, float topRadius, float height,
            PropellantLoad load, EngineTemplate engine)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    float radius = Mathf.Clamp(bottomRadius, TankMinRadius, TankMaxRadius);
                    bool cone = form == TankForm.Cone;
                    float top = Mathf.Clamp(cone ? topRadius : radius, TankMinRadius, TankMaxRadius);
                    height = Mathf.Clamp(height, TankMinHeight, TankMaxHeight);

                    // Масса бака от формы, размера и наполнения: объём,
                    // металл стенки (с перегородкой), изоляция и топливо —
                    // в TankMassModel (объём/площадь — из ProceduralFuelTank).
                    TankMassInfo info = TankMassModel.Compute(radius, top, height, load, 1d);
                    return new PartMetrics(radius, top, height, height * 0.5f, 0f, height,
                        info.TotalMassKg, TankJointNewtons, 1e12d,
                        info.DryMassKg, info.PropellantMassKg, info.VolumeM3, info.Load);
                }

                case PartKind.Separator:
                {
                    float radius = Mathf.Clamp(bottomRadius, SeparatorMinRadius, SeparatorMaxRadius);
                    float width = ProceduralSeparator.RingWidthFraction * radius;
                    float thickness = ProceduralSeparator.Thickness;
                    float midRadius = radius - (width * 0.5f);
                    double volume = 2d * Math.PI * midRadius * width * thickness;
                    double mass = volume * SeparatorSteelDensity;
                    return new PartMetrics(radius, radius, thickness, 0f, -thickness * 0.5f, thickness * 0.5f, mass, SeparatorJointNewtons, 1e12d);
                }

                case PartKind.Capsule:
                    // Размеры — из модели (CapsuleTemplate), входные числа не значимы.
                    return Capsule != null ? Capsule.Metrics : default;

                default:
                    return engine != null ? engine.Metrics : default;
            }
        }

        // ─── Визуал ──────────────────────────────────────────────────────────

        /// <summary>
        /// Шаблон капсулы (Orion): префаб и габариты, посчитанные по мешам
        /// (пивот — низ по центру оси, как у двигателя). Регистрируется
        /// строителем на старте (RegisterCapsule): капсула одна на каталог, и
        /// таскать её через все вызовы CreateVisual/Measure не нужно.
        /// </summary>
        public sealed class CapsuleTemplate
        {
            public GameObject Prefab;
            public PartMetrics Metrics;
            public Vector3 ModelOffset;
        }

        /// <summary>Шаблон капсулы; null — не зарегистрирован (нет префаба в сцене).</summary>
        public static CapsuleTemplate Capsule { get; private set; }

        /// <summary>
        /// Замерить капсулу по префабу (один раз, старт строителя). Габариты —
        /// мировые, как у двигателей; физическое тело — конус от теплозащиты
        /// (нижний радиус) к носу (верхний — доля CapsuleTopRadiusFraction).
        /// </summary>
        public static void RegisterCapsule(GameObject prefab)
        {
            if (prefab == null)
            {
                Capsule = null;
                return;
            }

            GameObject probe = UnityEngine.Object.Instantiate(prefab);
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.position = Vector3.zero;
            Bounds bounds = EngineTemplate.MeasureWorldBounds(probe);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(probe);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            var center = new Vector3(
                (bounds.min.x + bounds.max.x) * 0.5f,
                0f,
                (bounds.min.z + bounds.max.z) * 0.5f);
            float height = Mathf.Max(0.05f, bounds.size.y);
            float radius = Mathf.Max(0.05f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f);
            float topRadius = radius * CapsuleTopRadiusFraction;

            Capsule = new CapsuleTemplate
            {
                Prefab = prefab,
                Metrics = new PartMetrics(
                    radius, topRadius, height, height * 0.5f, 0f, height,
                    CapsuleMassKg, TankJointNewtons, 1e12d),
                ModelOffset = new Vector3(-center.x, -bounds.min.y, -center.z),
            };
        }

        /// <summary>Собранный визуал детали: корень-пивот и его рендереры.</summary>
        public sealed class PartVisual
        {
            public GameObject Root;
            public Renderer[] Renderers;

            private Material[][] natural;

            /// <summary>Запомнить родные материалы (для возврата после призрака).</summary>
            public void CaptureNatural()
            {
                natural = new Material[Renderers.Length][];
                for (int i = 0; i < Renderers.Length; i++)
                {
                    natural[i] = Renderers[i] != null ? Renderers[i].sharedMaterials : null;
                }
            }

            /// <summary>Покрасить все рендереры одним материалом (призрак/невалид).</summary>
            public void SetMaterial(Material material)
            {
                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (Renderers[i] == null)
                    {
                        continue;
                    }

                    var materials = new Material[Renderers[i].sharedMaterials.Length];
                    for (int m = 0; m < materials.Length; m++)
                    {
                        materials[m] = material;
                    }

                    Renderers[i].sharedMaterials = materials;
                }
            }

            /// <summary>Вернуть родные материалы.</summary>
            public void RestoreNatural()
            {
                if (natural == null)
                {
                    return;
                }

                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (Renderers[i] != null && natural[i] != null)
                    {
                        Renderers[i].sharedMaterials = natural[i];
                    }
                }
            }
        }

        /// <summary>
        /// Собрать визуал детали под готовые метрики. Родные материалы
        /// запоминаются; призрак поверх красится отдельно (SetMaterial).
        /// withCollider: у поставленных деталей — коллайдер с маркером
        /// VesselPartCollider (игрок не проходит сквозь аппарат): капсула по
        /// габаритам, а у конусного бака — выпуклый меш боковины; призрак
        /// постановки и превью — без коллайдера.
        /// </summary>
        public static PartVisual CreateVisual(
            PartKind kind, PartMetrics metrics, EngineTemplate engine,
            Material tankSide, Material tankCap, Material separatorMaterial, Transform parent,
            bool withCollider = true)
        {
            var root = new GameObject(DisplayName(kind, engine != null ? engine.Spec : null));
            root.transform.SetParent(parent, false);

            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    var tank = root.AddComponent<ProceduralFuelTank>();
                    tank.Radius = metrics.Radius;
                    tank.TopRadius = metrics.TopRadius > 0.01f ? metrics.TopRadius : metrics.Radius;
                    tank.Height = metrics.Height;
                    tank.Material = tankSide;
                    tank.CapMaterial = tankCap != null ? tankCap : tankSide;
                    tank.Rebuild();
                    break;
                }

                case PartKind.Separator:
                {
                    var separator = root.AddComponent<ProceduralSeparator>();
                    separator.Radius = metrics.Radius;
                    separator.Material = separatorMaterial;
                    separator.Rebuild();
                    break;
                }

                case PartKind.Capsule:
                {
                    if (Capsule == null || Capsule.Prefab == null)
                    {
                        break;                           // капсула не зарегистрирована — только корень
                    }

                    GameObject model = UnityEngine.Object.Instantiate(Capsule.Prefab, root.transform);
                    // Поворот модели НЕ трогаем: у корня префаба стоит разворот
                    // осей Blender, как у двигателей — обнуление кладёт капсулу на бок.
                    model.transform.localPosition = Capsule.ModelOffset;
                    break;
                }

                default:
                {
                    if (engine == null || engine.Prefab == null)
                    {
                        break;                           // нет модели — только корень детали
                    }

                    GameObject model = UnityEngine.Object.Instantiate(engine.Prefab, root.transform);
                    // Поворот модели НЕ трогаем: у корня FBX стоит разворот на 270°
                    // по X (данные меша в осях Blender) — обнуление укладывало
                    // двигатель на бок.
                    model.transform.localPosition = engine.ModelOffset;
                    break;
                }
            }

            var visual = new PartVisual
            {
                Root = root,
                Renderers = root.GetComponentsInChildren<Renderer>(true)
            };
            visual.CaptureNatural();

            if (withCollider && metrics.Radius > 0.01f)
            {
                if (kind == PartKind.Capsule)
                {
                    // Капсула: коллайдеры — из САМИХ МЕШЕЙ модели (внешняя
                    // оболочка, пол и стены кабины, открытая дверь люка). Общая
                    // блоб-капсула для неё не годится: шире и выше детали, а люк
                    // был бы наглухо запечатан — игрок не мог войти внутрь.
                    // Меши с уже готовым коллайдером (корпуса мониторов) не трогаем.
                    MeshFilter[] modelMeshes = root.GetComponentsInChildren<MeshFilter>(true);
                    for (int i = 0; i < modelMeshes.Length; i++)
                    {
                        MeshFilter filter = modelMeshes[i];
                        if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                        {
                            continue;
                        }

                        var hull = filter.gameObject.AddComponent<MeshCollider>();
                        hull.sharedMesh = filter.sharedMesh;
                        hull.convex = false;
                    }
                }
                else
                {
                    bool conical = kind == PartKind.FuelTank
                        && metrics.TopRadius > 0.01f
                        && Mathf.Abs(metrics.TopRadius - metrics.Radius) > 1e-3f;
                    if (conical)
                    {
                        // Конус капсулой не описать: выпуклый меш боковины повторяет
                        // сужение, игрок не упирается в невидимый цилиндр нижнего
                        // радиуса. Меш уже собран выше (Rebuild бака).
                        var hull = root.AddComponent<MeshCollider>();
                        hull.convex = true;
                        MeshFilter filter = root.GetComponent<MeshFilter>();
                        hull.sharedMesh = filter != null ? filter.sharedMesh : null;
                    }
                    else
                    {
                        // Коллайдер детали для игрока: капсула по габаритам (ось — локальный
                        // Y; у отделителя высота меньше диаметра, PhysX клампит её до
                        // сферы — плоское кольцо всё равно перекрывает проход).
                        var capsule = root.AddComponent<CapsuleCollider>();
                        capsule.direction = 1;
                        capsule.radius = metrics.Radius;
                        capsule.height = Mathf.Max(metrics.Height, metrics.Radius * 2f);
                        capsule.center = new Vector3(0f, metrics.Center, 0f);
                    }
                }

                root.AddComponent<VesselPartCollider>();
            }
            else
            {
                // Призрак постановки и превью: коллайдеры САМОЙ МОДЕЛИ
                // (например, корпусов мониторов в капсуле Orion) обязаны
                // молчать. Призрак живёт под «Площадкой стартовой» — её
                // SurfaceSite делает такие коллайдеры «постройками места»,
                // и призрак ловил собственный объём в проверке постановки:
                // капсулу было нельзя поставить нигде. Заодно собственные
                // коллайдеры перестают заслонять луч постановки.
                Collider[] ownColliders = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < ownColliders.Length; i++)
                {
                    ownColliders[i].enabled = false;
                }
            }

            return visual;
        }

        /// <summary>
        /// Шаблон двигателя: модель-префаб и её габариты, посчитанные по мешам
        /// (пивот — низ по центру оси), плюс паспорт (EngineSpec) — топливная
        /// пара, тяга, импульс, масса. Меряется один раз при старте; паспорт
        /// находится по имени модели (EngineSpecs.MatchPrefab).
        /// </summary>
        public sealed class EngineTemplate
        {
            public EngineSpec Spec;
            public GameObject Prefab;
            public PartMetrics Metrics;
            public Vector3 ModelOffset;

            public static EngineTemplate Measure(GameObject prefab)
            {
                if (prefab == null)
                {
                    return null;
                }

                EngineSpec spec = EngineSpecs.MatchPrefab(prefab.name);
                if (spec == null)
                {
                    Debug.LogError("[Build] нет паспорта двигателя для модели «" + prefab.name
                        + "» — добавь запись в EngineSpecs.");
                    return null;
                }

                GameObject probe = UnityEngine.Object.Instantiate(prefab);
                probe.hideFlags = HideFlags.HideAndDontSave;
                probe.transform.position = Vector3.zero;
                Bounds bounds = MeasureWorldBounds(probe);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(probe);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }

                var center = new Vector3(
                    (bounds.min.x + bounds.max.x) * 0.5f,
                    0f,
                    (bounds.min.z + bounds.max.z) * 0.5f);
                float height = Mathf.Max(0.05f, bounds.size.y);
                float radius = Mathf.Max(0.05f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f);

                return new EngineTemplate
                {
                    Spec = spec,
                    Prefab = prefab,
                    Metrics = new PartMetrics(radius, radius, height, height * 0.5f, 0f, height, spec.MassKg, EngineJointNewtons, 1e12d),
                    ModelOffset = new Vector3(-center.x, -bounds.min.y, -center.z),
                };
            }

            /// <summary>
            /// Мировые габариты иерархии. Именно мировые: у FBX-модели масштаб
            /// единиц (×100, сантиметры) стоит на её корневом узле, и габариты,
            /// посчитанные «в локальных осях корня», теряют этот масштаб —
            /// двигатель получался 5 см и проваливался сквозь землю.
            /// </summary>
            internal static Bounds MeasureWorldBounds(GameObject root)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool has = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                    {
                        continue;
                    }

                    if (!has)
                    {
                        bounds = renderers[i].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }

                return bounds;
            }
        }
    }
}
