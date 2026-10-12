using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Процедурный топливный бак: тело вращения с плоскими крышками
    /// (скруглений нет), ось — локальная Y, низ в начале координат.
    /// Игрок задаёт нижний радиус, верхний радиус и высоту обечайки:
    /// радиусы равны — идеальный цилиндр, разные — обрезанный конус
    /// (бак-переходник между ступенями разных диаметров). Сетка
    /// перестраивается кодом, поэтому размеры любые, UV не растягиваются,
    /// а полигонаж минимальный.
    ///
    /// Два подмеша: 0 — боковина (металл, UV в метрах, тайлится), 1 — плоские
    /// крышки (UV 0..1 на диск, поэтому рисунок ложится ровно на крышку).
    /// Поверхность крышек остаётся плоскостью: рисунок — только текстура.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralFuelTank : MonoBehaviour
    {
        [Header("Геометрия (параметры игрока)")]
        [Tooltip("Нижний радиус бака, м.")]
        [Min(0.05f)] public float Radius = 1.5f;

        [Tooltip("Верхний радиус, м. Равен нижнему — цилиндр, иначе обрезанный конус (переходник).")]
        [Min(0.05f)] public float TopRadius = 1.5f;

        [Tooltip("Высота обечайки, м.")]
        [Min(0.05f)] public float Height = 6f;

        [Tooltip("Сегментов по окружности. Больше — глаже силуэт.")]
        [Range(8, 128)] public int Segments = 48;

        [Header("Материалы")]
        [Tooltip("Металл боковины.")]
        public Material Material;

        [Tooltip("Материал плоских крышек с рисунком (UV 0..1 на диск).")]
        public Material CapMaterial;

        [Tooltip("Метров поверхности на один тайл текстуры боковины (для UV).")]
        [Min(0.01f)] public float TextureMetersPerTile = 2f;

        [Header("Физика (не подключено, данные для расчётов)")]
        [Tooltip("Погонная плотность обечайки, кг/м² (сталь ~15, алюминий ~8).")]
        [Min(0f)] public float ShellArealDensityKgM2 = 15f;

        [Tooltip("Плотность топлива, кг/м³ (LOX ~1140, LH2 ~71, RP-1 ~810).")]
        [Min(0f)] public float PropellantDensityKgM3 = 1000f;

        [Tooltip("Заполнение бака топливом, 0..1.")]
        [Range(0f, 1f)] public float Fill = 1f;

        private Mesh mesh;

        /// <summary>Площадь оболочки: обечайка + две плоские крышки, м².</summary>
        public float ShellAreaM2
        {
            get { return ShellArea(Radius, TopRadius, Height); }
        }

        /// <summary>Внутренний объём, м³.</summary>
        public float VolumeM3
        {
            get { return Volume(Radius, TopRadius, Height); }
        }

        /// <summary>
        /// Площадь оболочки тела вращения: боковина (наклонная образующая) +
        /// две плоские крышки. Равные радиусы дают формулу цилиндра.
        /// </summary>
        public static float ShellArea(float bottomRadius, float topRadius, float height)
        {
            float deltaRadius = topRadius - bottomRadius;
            float slant = Mathf.Sqrt((deltaRadius * deltaRadius) + (height * height));
            return (Mathf.PI * (bottomRadius + topRadius) * slant)
                + (Mathf.PI * bottomRadius * bottomRadius)
                + (Mathf.PI * topRadius * topRadius);
        }

        /// <summary>Внутренний объём тела вращения, м³ (обрезанный конус).</summary>
        public static float Volume(float bottomRadius, float topRadius, float height)
        {
            return (Mathf.PI * height * ((bottomRadius * bottomRadius) + (bottomRadius * topRadius) + (topRadius * topRadius))) / 3f;
        }

        public double DryMassKg
        {
            get { return ShellAreaM2 * ShellArealDensityKgM2; }
        }

        public double FuelMassKg
        {
            get { return VolumeM3 * PropellantDensityKgM3 * Fill; }
        }

        public double TotalMassKg
        {
            get { return DryMassKg + FuelMassKg; }
        }

        /// <summary>
        /// Сетка цилиндра (частный случай тела вращения с равными радиусами).
        /// </summary>
        public static Mesh BuildCylinder(float radius, float height, int segments, float metersPerTile)
        {
            return BuildBody(radius, radius, height, segments, metersPerTile);
        }

        /// <summary>
        /// Сетка тела вращения: боковина + две плоские крышки. Равные радиусы —
        /// цилиндр, разные — обрезанный конус (бак-переходник).
        /// Подмеш 0 — боковина (гладкая, UV в метрах), подмеш 1 — крышки
        /// (нормали ±Y, UV 0..1 по диску). Вершины крышек отдельные, поэтому
        /// кромка остаётся жёсткой и они не сглаживаются с боковиной.
        /// Нормали боковины перпендикулярны образующей: у конуса они наклонены
        /// (сужение кверху — наружу-вверх, расширение — наружу-вниз), иначе
        /// освещение выдавало бы цилиндр.
        /// </summary>
        public static Mesh BuildBody(float bottomRadius, float topRadius, float height, int segments, float metersPerTile)
        {
            var mesh = new Mesh();
            FillBody(mesh, bottomRadius, topRadius, height, segments, metersPerTile);
            return mesh;
        }

        /// <summary>
        /// Заполнить СУЩЕСТВУЮЩИЙ меш телом вращения. Меш переиспользуется, а не
        /// пересоздаётся: GPU Resident Drawer (HDRP) ругается на уничтожение
        /// меша, на который ещё смотрят батчи («invalid Batch/Mesh ID»), а
        /// превью пересобирается на каждом шаге размера.
        /// </summary>
        public static void FillBody(Mesh mesh, float bottomRadius, float topRadius, float height, int segments, float metersPerTile)
        {
            segments = Mathf.Max(3, segments);
            float tile = Mathf.Max(0.01f, metersPerTile);
            bottomRadius = Mathf.Max(0.01f, bottomRadius);
            topRadius = Mathf.Max(0.01f, topRadius);

            float deltaRadius = topRadius - bottomRadius;
            float slant = Mathf.Sqrt((deltaRadius * deltaRadius) + (height * height));
            bool cylinder = Mathf.Abs(deltaRadius) < 1e-4f;

            // Метраж UV боковины — по средней окружности: растяжение текстуры
            // вдоль образующей для конуса и цилиндра одинаковое.
            float circumference = Mathf.PI * (bottomRadius + topRadius);

            int sideBottom = 0;
            int sideTop = segments;
            int capBottomCenter = 2 * segments;
            int capBottomRing = capBottomCenter + 1;
            int capTopCenter = capBottomRing + segments;
            int capTopRing = capTopCenter + 1;

            var vertices = new Vector3[capTopRing + segments];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var sideTriangles = new int[segments * 6];
            var capTriangles = new int[segments * 6];

            for (int i = 0; i < segments; i++)
            {
                float a = (2f * Mathf.PI * i) / segments;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);
                float u = (i / (float)segments) * (circumference / tile);

                vertices[sideBottom + i] = new Vector3(bottomRadius * cs, 0f, bottomRadius * sn);
                vertices[sideTop + i] = new Vector3(topRadius * cs, height, topRadius * sn);
                Vector3 sideNormal = cylinder
                    ? new Vector3(cs, 0f, sn)
                    : new Vector3(height * cs, -deltaRadius, height * sn).normalized;
                normals[sideBottom + i] = sideNormal;
                normals[sideTop + i] = sideNormal;
                uvs[sideBottom + i] = new Vector2(u, 0f);
                uvs[sideTop + i] = new Vector2(u, slant / tile);

                vertices[capBottomRing + i] = new Vector3(bottomRadius * cs, 0f, bottomRadius * sn);
                vertices[capTopRing + i] = new Vector3(topRadius * cs, height, topRadius * sn);
                normals[capBottomRing + i] = new Vector3(0f, -1f, 0f);
                normals[capTopRing + i] = new Vector3(0f, 1f, 0f);

                // UV крышки: диск вписан в квадрат 0..1 (по своему радиусу),
                // поэтому рисунок ложится один-в-один при любом радиусе.
                float cu = (0.5f * cs) + 0.5f;
                float cv = (0.5f * sn) + 0.5f;
                uvs[capBottomRing + i] = new Vector2(cu, cv);
                uvs[capTopRing + i] = new Vector2(cu, cv);
            }

            vertices[capBottomCenter] = new Vector3(0f, 0f, 0f);
            vertices[capTopCenter] = new Vector3(0f, height, 0f);
            normals[capBottomCenter] = new Vector3(0f, -1f, 0f);
            normals[capTopCenter] = new Vector3(0f, 1f, 0f);
            uvs[capBottomCenter] = new Vector2(0.5f, 0.5f);
            uvs[capTopCenter] = new Vector2(0.5f, 0.5f);

            int s = 0;
            int c = 0;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;

                sideTriangles[s++] = sideBottom + i;
                sideTriangles[s++] = sideTop + i;
                sideTriangles[s++] = sideTop + j;

                sideTriangles[s++] = sideBottom + i;
                sideTriangles[s++] = sideTop + j;
                sideTriangles[s++] = sideBottom + j;

                capTriangles[c++] = capBottomCenter;
                capTriangles[c++] = capBottomRing + i;
                capTriangles[c++] = capBottomRing + j;

                capTriangles[c++] = capTopCenter;
                capTriangles[c++] = capTopRing + j;
                capTriangles[c++] = capTopRing + i;
            }

            mesh.Clear();
            mesh.name = "FuelTank_" + bottomRadius.ToString("F2") + "x" + topRadius.ToString("F2") + "x" + height.ToString("F2");
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sideTriangles, 0);
            mesh.SetTriangles(capTriangles, 1);
            mesh.RecalculateBounds();
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

#if UNITY_EDITOR
            // Меш нельзя удалять прямо внутри OnValidate, поэтому пересборка
            // откладывается на конец кадра.
            UnityEditor.EditorApplication.delayCall -= RebuildDelayed;
            UnityEditor.EditorApplication.delayCall += RebuildDelayed;
#else
            Rebuild();
#endif
        }

#if UNITY_EDITOR
        private void RebuildDelayed()
        {
            UnityEditor.EditorApplication.delayCall -= RebuildDelayed;
            if (this != null && isActiveAndEnabled)
            {
                Rebuild();
            }
        }
#endif

        /// <summary>
        /// Перестроить сетку под текущие Radius/TopRadius/Height. Вызывать после
        /// изменения параметров из UI (VAB), чтобы не ждать OnValidate.
        /// Меш переиспользуется (FillBody): GPU Resident Drawer (HDRP) ругается
        /// на уничтожение меша, на который ещё смотрят батчи.
        /// </summary>
        public void Rebuild()
        {
            if (Radius <= 0.01f || Height <= 0.01f)
            {
                return;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.DontSave };
            }

            FillBody(mesh, Radius, TopRadius > 0.01f ? TopRadius : Radius, Height, Segments, TextureMetersPerTile);
            filter.sharedMesh = mesh;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterials = new Material[]
            {
                Material,
                CapMaterial != null ? CapMaterial : Material,
            };
        }
    }
}
