using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Процедурный топливный бак: идеальный цилиндр с плоскими крышками
    /// (скруглений нет), ось — локальная Y, низ в начале координат.
    /// Игрок задаёт радиус и высоту обечайки; сетка перестраивается кодом,
    /// поэтому размеры любые, UV не растягиваются, а полигонаж минимальный.
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
        [Tooltip("Радиус бака, м.")]
        [Min(0.05f)] public float Radius = 1.5f;

        [Tooltip("Высота обечайки (цилиндра), м.")]
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
            get { return (2f * Mathf.PI * Radius * Height) + (2f * Mathf.PI * Radius * Radius); }
        }

        /// <summary>Внутренний объём, м³.</summary>
        public float VolumeM3
        {
            get { return Mathf.PI * Radius * Radius * Height; }
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
        /// Сетка цилиндра: боковина + две плоские крышки.
        /// Подмеш 0 — боковина (гладкая, UV в метрах), подмеш 1 — крышки
        /// (нормали ±Y, UV 0..1 по диску). Вершины крышек отдельные, поэтому
        /// кромка остаётся жёсткой и они не сглаживаются с боковиной.
        /// </summary>
        public static Mesh BuildCylinder(float radius, float height, int segments, float metersPerTile)
        {
            segments = Mathf.Max(3, segments);
            float tile = Mathf.Max(0.01f, metersPerTile);
            float circumference = 2f * Mathf.PI * radius;
            float invDiameter = 0.5f / radius;

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

                vertices[sideBottom + i] = new Vector3(radius * cs, 0f, radius * sn);
                vertices[sideTop + i] = new Vector3(radius * cs, height, radius * sn);
                normals[sideBottom + i] = new Vector3(cs, 0f, sn);
                normals[sideTop + i] = new Vector3(cs, 0f, sn);
                uvs[sideBottom + i] = new Vector2(u, 0f);
                uvs[sideTop + i] = new Vector2(u, height / tile);

                vertices[capBottomRing + i] = new Vector3(radius * cs, 0f, radius * sn);
                vertices[capTopRing + i] = new Vector3(radius * cs, height, radius * sn);
                normals[capBottomRing + i] = new Vector3(0f, -1f, 0f);
                normals[capTopRing + i] = new Vector3(0f, 1f, 0f);

                // UV крышки: диск вписан в квадрат 0..1, поэтому рисунок
                // ложится один-в-один на плоскость крышки при любом радиусе.
                float cu = (radius * cs) * invDiameter + 0.5f;
                float cv = (radius * sn) * invDiameter + 0.5f;
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

            var mesh = new Mesh();
            mesh.name = "FuelTank_" + radius.ToString("F2") + "x" + height.ToString("F2");
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.subMeshCount = 2;
            mesh.SetTriangles(sideTriangles, 0);
            mesh.SetTriangles(capTriangles, 1);
            mesh.RecalculateBounds();
            return mesh;
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
        /// Перестроить сетку под текущие Radius/Height. Вызывать после
        /// изменения параметров из UI (VAB), чтобы не ждать OnValidate.
        /// </summary>
        public void Rebuild()
        {
            if (Radius <= 0.01f || Height <= 0.01f)
            {
                return;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh);
                }
            }

            mesh = BuildCylinder(Radius, Height, Segments, TextureMetersPerTile);
            mesh.hideFlags = HideFlags.DontSave;
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
