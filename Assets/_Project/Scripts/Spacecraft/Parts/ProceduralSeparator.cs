using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Procedural separator: a flat ring with a square profile, axis is local Y, origin
    /// at the ring's mid-height. The player sets the outer radius; ring width is 15% of
    /// the radius, thickness a constant 40 mm (independent of the player's radius).
    /// UVs around the circumference are metric (texture tile = segment-seam spacing,
    /// ~250 mm), so seams line up at any radius and don't stretch. Wall edges sample only
    /// the background zone of the texture: pad bolts projected onto a 40-mm edge would be
    /// squashed into ovals. Edges are hard: wall and end-face vertices are separate.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralSeparator : MonoBehaviour
    {
        [Header("Геометрия (параметры игрока)")]
        [Tooltip("Наружный радиус отделителя, м.")]
        [Min(0.1f)] public float Radius = 1.5f;

        [Tooltip("Сегментов по окружности. Больше — глаже силуэт.")]
        [Range(16, 160)] public int Segments = 64;

        [Header("Материал")]
        [Tooltip("Анодированный чёрный металл с рисунком сегментных швов.")]
        public Material Material;

        [Tooltip("Метров поверхности на тайл текстуры: задаёт шаг сегментных швов (~250 мм).")]
        [Min(0.05f)] public float TextureMetersPerTile = 0.25f;

        /// <summary>Ring width as a fraction of the radius (15%, metal from the edge inwards).</summary>
        public const float RingWidthFraction = 0.15f;

        /// <summary>Ring thickness (height along Y), constant 40 mm.</summary>
        public const float Thickness = 0.04f;

        /// <summary>Texture row where the background zone for the walls starts (no bolts).</summary>
        private const float WallTextureV = 0.10f;

        private Mesh mesh;

        /// <summary>Ring width at the current radius, m.</summary>
        public float RingWidthM
        {
            get { return RingWidthFraction * Radius; }
        }

        /// <summary>
        /// Ring mesh: outer and inner barrels plus two flat end faces, one submesh.
        /// UVs: around the circumference in metres (tile = metersPerTile); on the end
        /// faces v is the fraction of the ring width (0 — inner edge, 1 — outer), on the
        /// walls v is the texture's background zone. Group vertices are separate, edges hard.
        /// </summary>
        public static Mesh BuildRing(float radius, float ringWidth, float thickness, int segments, float metersPerTile)
        {
            var mesh = new Mesh();
            FillRing(mesh, radius, ringWidth, thickness, segments, metersPerTile);
            return mesh;
        }

        /// <summary>
        /// Fill an EXISTING mesh with a ring. The mesh is reused, not recreated:
        /// AddComponent immediately calls OnEnable (build with default sizes), then the
        /// UI sets the radius and requests Rebuild — destroying the mesh under the GPU
        /// Resident Drawer (HDRP) gives "invalid Batch/Mesh ID".
        /// </summary>
        public static void FillRing(Mesh mesh, float radius, float ringWidth, float thickness, int segments, float metersPerTile)
        {
            segments = Mathf.Max(8, segments);
            float tile = Mathf.Max(0.05f, metersPerTile);
            ringWidth = Mathf.Clamp(ringWidth, 0.005f, radius * 0.8f);
            float rIn = Mathf.Max(0.01f, radius - ringWidth);

            float y0 = -thickness * 0.5f;
            float y1 = thickness * 0.5f;
            float wallV1 = WallTextureV + thickness / tile;

            float circumferenceMid = 2f * Mathf.PI * 0.5f * (rIn + radius);
            int tiles = Mathf.Max(1, Mathf.RoundToInt(circumferenceMid / tile));

            int ob = 0;
            int ot = segments;
            int ib = 2 * segments;
            int it = 3 * segments;
            int ti = 4 * segments;
            int to = 5 * segments;
            int bi = 6 * segments;
            int bo = 7 * segments;

            var vertices = new Vector3[8 * segments];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[segments * 24];

            int t = 0;
            for (int i = 0; i < segments; i++)
            {
                int j = (i + 1) % segments;
                float a = (2f * Mathf.PI * i) / segments;
                float cs = Mathf.Cos(a);
                float sn = Mathf.Sin(a);
                float u = (i / (float)segments) * tiles;

                vertices[ob + i] = new Vector3(radius * cs, y0, radius * sn);
                vertices[ot + i] = new Vector3(radius * cs, y1, radius * sn);
                vertices[ib + i] = new Vector3(rIn * cs, y0, rIn * sn);
                vertices[it + i] = new Vector3(rIn * cs, y1, rIn * sn);
                vertices[ti + i] = new Vector3(rIn * cs, y1, rIn * sn);
                vertices[to + i] = new Vector3(radius * cs, y1, radius * sn);
                vertices[bi + i] = new Vector3(rIn * cs, y0, rIn * sn);
                vertices[bo + i] = new Vector3(radius * cs, y0, radius * sn);

                normals[ob + i] = new Vector3(cs, 0f, sn);
                normals[ot + i] = new Vector3(cs, 0f, sn);
                normals[ib + i] = new Vector3(-cs, 0f, -sn);
                normals[it + i] = new Vector3(-cs, 0f, -sn);
                normals[ti + i] = new Vector3(0f, 1f, 0f);
                normals[to + i] = new Vector3(0f, 1f, 0f);
                normals[bi + i] = new Vector3(0f, -1f, 0f);
                normals[bo + i] = new Vector3(0f, -1f, 0f);

                uvs[ob + i] = new Vector2(u, WallTextureV);
                uvs[ot + i] = new Vector2(u, wallV1);
                uvs[ib + i] = new Vector2(u, WallTextureV);
                uvs[it + i] = new Vector2(u, wallV1);
                uvs[ti + i] = new Vector2(u, 0f);
                uvs[to + i] = new Vector2(u, 1f);
                uvs[bi + i] = new Vector2(u, 0f);
                uvs[bo + i] = new Vector2(u, 1f);

                triangles[t++] = ob + i; triangles[t++] = ot + i; triangles[t++] = ot + j;
                triangles[t++] = ob + i; triangles[t++] = ot + j; triangles[t++] = ob + j;

                triangles[t++] = ib + i; triangles[t++] = it + j; triangles[t++] = it + i;
                triangles[t++] = ib + i; triangles[t++] = ib + j; triangles[t++] = it + j;

                triangles[t++] = ti + i; triangles[t++] = to + j; triangles[t++] = to + i;
                triangles[t++] = ti + i; triangles[t++] = ti + j; triangles[t++] = to + j;

                triangles[t++] = bi + i; triangles[t++] = bo + i; triangles[t++] = bo + j;
                triangles[t++] = bi + i; triangles[t++] = bo + j; triangles[t++] = bi + j;
            }

            mesh.Clear();
            mesh.name = "Separator_" + radius.ToString("F2");
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
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
        /// Rebuild the mesh for the current parameters. Call it after changing Radius
        /// from the UI (VAB) so as not to wait for OnValidate. The mesh is reused
        /// (FillRing): destroying a mesh under the GPU Resident Drawer (HDRP) gives
        /// "invalid Batch/Mesh ID", since AddComponent builds the ring before the UI
        /// sets the radius.
        /// </summary>
        public void Rebuild()
        {
            if (Radius <= 0.01f)
            {
                return;
            }

            MeshFilter filter = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                mesh = new Mesh { hideFlags = HideFlags.DontSave };
            }

            FillRing(mesh, Radius, RingWidthM, Thickness, Segments, TextureMetersPerTile);
            filter.sharedMesh = mesh;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material;
        }
    }
}
