using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Каталог деталей: имя, пределы размеров, метрики (масса/форма/узлы) и
    /// сборка визуала. Бака и отделитель — процедурные (радиус/высота задаёт
    /// игрок), двигатель — модель RS-25 (габариты мерятся по мешам).
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
        public const float TankMinRadius = 0.25f;
        public const float TankMaxRadius = 4f;
        public const float TankMinHeight = 1f;
        public const float TankMaxHeight = 15f;

        // Отделитель
        public const float SeparatorDefaultRadius = 1.5f;
        public const float SeparatorMinRadius = 0.25f;
        public const float SeparatorMaxRadius = 4f;

        // Плейсхолдеры физики
        private const double TankShellArealDensity = 15d;
        private const double TankPropellantDensity = 1000d;
        private const double SeparatorSteelDensity = 7850d;
        private const double EngineMassKg = 3500d;

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
                case PartKind.Engine: return "Двигатель RS-25";
                case PartKind.FuelTank: return "Топливный бак";
                default: return "Отделитель";
            }
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
                default:
                    radius = 1f;
                    height = 1f;
                    break;
            }
        }

        /// <summary>Метрики детали под данные размеры (двигатель — из шаблона модели).</summary>
        public static PartMetrics Measure(PartKind kind, float sizeA, float sizeB, EngineTemplate engine)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    float radius = Mathf.Clamp(sizeA, TankMinRadius, TankMaxRadius);
                    float height = Mathf.Clamp(sizeB, TankMinHeight, TankMaxHeight);
                    double shellArea = (2d * Math.PI * radius * height) + (2d * Math.PI * radius * radius);
                    double volume = Math.PI * radius * radius * height;
                    double mass = (shellArea * TankShellArealDensity) + (volume * TankPropellantDensity);
                    return new PartMetrics(radius, height, height * 0.5f, 0f, height, mass, TankJointNewtons, 1e12d);
                }

                case PartKind.Separator:
                {
                    float radius = Mathf.Clamp(sizeA, SeparatorMinRadius, SeparatorMaxRadius);
                    float width = ProceduralSeparator.RingWidthFraction * radius;
                    float thickness = ProceduralSeparator.Thickness;
                    float midRadius = radius - (width * 0.5f);
                    double volume = 2d * Math.PI * midRadius * width * thickness;
                    double mass = volume * SeparatorSteelDensity;
                    return new PartMetrics(radius, thickness, 0f, -thickness * 0.5f, thickness * 0.5f, mass, SeparatorJointNewtons, 1e12d);
                }

                default:
                    return engine != null ? engine.Metrics : default;
            }
        }

        // ─── Визуал ──────────────────────────────────────────────────────────

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
        /// withCollider: у поставленных деталей — капсульный коллайдер с
        /// маркером VesselPartCollider (игрок не проходит сквозь аппарат);
        /// призрак постановки и превью — без коллайдера.
        /// </summary>
        public static PartVisual CreateVisual(
            PartKind kind, PartMetrics metrics, EngineTemplate engine,
            Material tankSide, Material tankCap, Material separatorMaterial, Transform parent,
            bool withCollider = true)
        {
            var root = new GameObject(DisplayName(kind));
            root.transform.SetParent(parent, false);

            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    var tank = root.AddComponent<ProceduralFuelTank>();
                    tank.Radius = metrics.Radius;
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

                default:
                {
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
                // Коллайдер детали для игрока: капсула по габаритам (ось — локальный
                // Y; у отделителя высота меньше диаметра, PhysX клампит её до
                // сферы — плоское кольцо всё равно перекрывает проход).
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.direction = 1;
                capsule.radius = metrics.Radius;
                capsule.height = Mathf.Max(metrics.Height, metrics.Radius * 2f);
                capsule.center = new Vector3(0f, metrics.Center, 0f);
                root.AddComponent<VesselPartCollider>();
            }

            return visual;
        }

        /// <summary>
        /// Шаблон двигателя: модель-префаб и её габариты, посчитанные по мешам
        /// (пивот — низ по центру оси). Меряется один раз при старте.
        /// </summary>
        public sealed class EngineTemplate
        {
            public GameObject Prefab;
            public PartMetrics Metrics;
            public Vector3 ModelOffset;

            public static EngineTemplate Measure(GameObject prefab)
            {
                if (prefab == null)
                {
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
                    Prefab = prefab,
                    Metrics = new PartMetrics(radius, height, height * 0.5f, 0f, height, EngineMassKg, EngineJointNewtons, 1e12d),
                    ModelOffset = new Vector3(-center.x, -bounds.min.y, -center.z),
                };
            }

            /// <summary>
            /// Мировые габариты иерархии. Именно мировые: у FBX-модели масштаб
            /// единиц (×100, сантиметры) стоит на её корневом узле, и габариты,
            /// посчитанные «в локальных осях корня», теряют этот масштаб —
            /// двигатель получался 5 см и проваливался сквозь землю.
            /// </summary>
            private static Bounds MeasureWorldBounds(GameObject root)
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
