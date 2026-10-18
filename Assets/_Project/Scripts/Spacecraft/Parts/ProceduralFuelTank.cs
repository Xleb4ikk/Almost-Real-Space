using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Procedural fuel tank: a surface of revolution with flat caps (no rounding),
    /// axis is local Y, bottom at the origin. The player sets bottom radius, top
    /// radius and barrel height: equal radii give a perfect cylinder, different ones
    /// a truncated cone (an adapter tank between stages of different diameters).
    /// The mesh is rebuilt in code, so sizes are arbitrary, UVs don't stretch and
    /// the polycount is minimal.
    ///
    /// Two submeshes: 0 — side wall (metal, UVs in metres, tiled), 1 — flat caps
    /// (UV 0..1 over the disc, so the pattern lands squarely on the cap). Cap surface
    /// stays flat: the pattern is texture only.
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

        [Header("Физика")]
        [Tooltip("Наполнение бака: Id из реестра PropellantLoads (RS25, ...).")]
        public string LoadId = "RS25";

        [Tooltip("Заполнение бака топливом, 0..1.")]
        [Range(0f, 1f)] public float Fill = 1f;

        private Mesh mesh;

        /// <summary>Shell area: barrel + two flat caps, m².</summary>
        public float ShellAreaM2
        {
            get { return ShellArea(Radius, TopRadius, Height); }
        }

        /// <summary>Internal volume, m³.</summary>
        public float VolumeM3
        {
            get { return Volume(Radius, TopRadius, Height); }
        }

        /// <summary>
        /// Shell area of a surface of revolution: side wall (slanted generatrix) plus
        /// two flat caps. Equal radii give the cylinder formula.
        /// </summary>
        public static float ShellArea(float bottomRadius, float topRadius, float height)
        {
            float deltaRadius = topRadius - bottomRadius;
            float slant = Mathf.Sqrt((deltaRadius * deltaRadius) + (height * height));
            return (Mathf.PI * (bottomRadius + topRadius) * slant)
                + (Mathf.PI * bottomRadius * bottomRadius)
                + (Mathf.PI * topRadius * topRadius);
        }

        /// <summary>Internal volume of a surface of revolution, m³ (truncated cone).</summary>
        public static float Volume(float bottomRadius, float topRadius, float height)
        {
            return (Mathf.PI * height * ((bottomRadius * bottomRadius) + (bottomRadius * topRadius) + (topRadius * topRadius))) / 3f;
        }

        /// <summary>Tank fill (by Id from the PropellantLoads registry).</summary>
        public PropellantLoad Load
        {
            get { return PropellantLoads.Get(LoadId); }
            set { LoadId = value != null ? value.Id : "RS25"; }
        }

        /// <summary>Tank calculation: volume, metal, insulation and propellant (TankMassModel).</summary>
        public TankMassInfo Mass
        {
            get { return TankMassModel.Compute(Radius, TopRadius > 0.01f ? TopRadius : Radius, Height, Load, Fill); }
        }

        public double DryMassKg
        {
            get { return Mass.DryMassKg; }
        }

        public double FuelMassKg
        {
            get { return Mass.PropellantMassKg; }
        }

        public double TotalMassKg
        {
            get { return Mass.TotalMassKg; }
        }

        /// <summary>
        /// Cylinder mesh (special case of a surface of revolution with equal radii).
        /// </summary>
        public static Mesh BuildCylinder(float radius, float height, int segments, float metersPerTile)
        {
            return BuildBody(radius, radius, height, segments, metersPerTile);
        }

        /// <summary>
        /// Surface-of-revolution mesh: side wall + two flat caps. Equal radii — cylinder,
        /// different — truncated cone (adapter tank). Submesh 0 — side wall (smooth, UVs in
        /// metres), submesh 1 — caps (normals ±Y, UV 0..1 over the disc). Cap vertices are
        /// separate, so the edge stays hard and they don't smooth with the side wall.
        /// Side-wall normals are perpendicular to the generatrix: on a cone they are
        /// slanted (narrowing upward — outward-up, widening — outward-down), otherwise the
        /// lighting would read as a cylinder.
        /// </summary>
        public static Mesh BuildBody(float bottomRadius, float topRadius, float height, int segments, float metersPerTile)
        {
            var mesh = new Mesh();
            FillBody(mesh, bottomRadius, topRadius, height, segments, metersPerTile);
            return mesh;
        }

        /// <summary>
        /// Fill an EXISTING mesh with a surface of revolution. The mesh is reused, not
        /// recreated: the GPU Resident Drawer (HDRP) complains about destroying a mesh
        /// that batches still reference ("invalid Batch/Mesh ID"), and the preview is
        /// rebuilt on every size change.
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

            // Side-wall UV metrage by the mean circumference: texture stretch along the
            // generatrix is the same for cone and cylinder.
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

                // Cap UV: the disc is inscribed in a 0..1 square (by its own radius), so
                // the pattern lands one-to-one at any radius.
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
            // The mesh cannot be destroyed directly inside OnValidate, so the rebuild is
            // deferred to the end of the frame.
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
        /// Rebuild the mesh for the current Radius/TopRadius/Height. Call it after
        /// changing parameters from the UI (VAB) so as not to wait for OnValidate.
        /// The mesh is reused (FillBody): the GPU Resident Drawer (HDRP) complains about
        /// destroying a mesh that batches still reference.
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
