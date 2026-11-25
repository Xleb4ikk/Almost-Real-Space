using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Процедурное кольцо корпуса: двойная стенка, пустая полость под будущие трубы
    /// и провода. Ось — локальный Y, низ кольца в начале координат.
    ///
    /// Слои стены: наружная обшивка (OuterSkin), пустая полость (Cavity),
    /// внутренний лайнер (InnerLiner). В открытом состоянии (Sealed = false)
    /// лайнера нет — видна полость. В запаянном 4 панели лайнера закрывают её.
    ///
    /// 6 подмешей: 0 — обшивка/фланцы, 1 — грунт полости, 2 — лайнер,
    /// 3 — припой швов, 4 — сталь болтов, 5 — индикаторная полоса состояния.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralHullRing : MonoBehaviour
    {
        [Header("Кольцо")]
        [Min(0.20f)] public float Radius = 2.5f;
        [Min(0.10f)] public float Height = 1f;
        [Range(16, 256)] public int Segments = 128;

        [Header("Двойная стенка")]
        [Min(0.002f)] public float OuterSkin = 0.016f;
        [Min(0.005f)] public float Cavity = 0.096f;
        [Min(0.002f)] public float InnerLiner = 0.016f;

        [Header("Состояние")]
        [Tooltip("Закрыть полость панелями внутреннего лайнера.")]
        public bool Sealed = true;
        [Range(4, 12)] public int LinerPanels = 4;

        [Header("Фланцы")]
        [Min(0.005f)] public float FlangeHeight = 0.075f;
        [Min(0f)] public float FlangeOverhang = 0.008f;
        [Range(0, 64)] public int BoltCount = 32;

        [Header("Материалы")]
        public Material HullMaterial;
        public Material PrimerMaterial;
        public Material LinerMaterial;
        public Material SolderMaterial;
        public Material SteelMaterial;
        [Tooltip("Индикаторная полоса снаружи: зелёная — полость открыта, янтарная — запаяно.")]
        public Material IndicatorMaterial;

        [Header("УФ")]
        [Min(0.05f)] public float TextureMetersPerTile = 0.5f;

        private Mesh mesh;
        private MeshFilter filter;
        private MeshRenderer renderer;

        /// <summary>Радиус внутренней поверхности лайнера (или проёма полости), м.</summary>
        public float InnerRadius
        {
            get { return Mathf.Max(0.05f, Radius - OuterSkin - Cavity - InnerLiner); }
        }

        /// <summary>Наружный радиус полости (внутренняя поверхность наружной обшивки), м.</summary>
        public float CavityOuterRadius
        {
            get { return Radius - OuterSkin; }
        }

        /// <summary>Внутренний радиус полости (под лайнером), м.</summary>
        public float CavityInnerRadius
        {
            get { return InnerRadius + InnerLiner; }
        }

        /// <summary>Полная толщина стены, м.</summary>
        public float WallThickness
        {
            get { return OuterSkin + Cavity + InnerLiner; }
        }

        private void OnEnable()
        {
            Ensure();
            Rebuild();
        }

        private void OnValidate()
        {
            Ensure();
            Rebuild();
        }

        private void Ensure()
        {
            if (filter == null)
            {
                filter = GetComponent<MeshFilter>();
            }

            if (renderer == null)
            {
                renderer = GetComponent<MeshRenderer>();
            }

            if (filter != null && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
            }

            if (mesh == null)
            {
                mesh = new Mesh { name = "HullRing" };
                if (filter != null)
                {
                    filter.sharedMesh = mesh;
                }
            }
        }

        /// <summary>Перестроить меш по текущим размерам и состоянию.</summary>
        public void Rebuild()
        {
            Ensure();
            if (mesh == null)
            {
                return;
            }

            FillHullRing(mesh, Radius, Height, OuterSkin, Cavity, InnerLiner,
                FlangeHeight, FlangeOverhang, Segments, LinerPanels, Sealed,
                BoltCount, TextureMetersPerTile);

            if (renderer != null)
            {
                Material indicator = IndicatorMaterial != null
                    ? IndicatorMaterial
                    : (SteelMaterial != null ? SteelMaterial : HullMaterial);
                renderer.sharedMaterials = new[]
                {
                    HullMaterial, PrimerMaterial, LinerMaterial, SolderMaterial, SteelMaterial, indicator
                };
            }
        }

        /// <summary>
        /// Оценка массы кольца, кг: обшивка + (если запаяно) лайнер + фланцы,
        /// которые считаются не сплошным бруском, а наборным каркасом (18 % объёма).
        /// Болты — сталь. Нужна билдеру для метрик массы и прочности стыка.
        /// </summary>
        public static double EstimateMassKg(float radius, float height, bool sealedRing)
        {
            const double AluminumDensity = 2700d;
            const double SteelDensity = 7850d;
            const float Outer = 0.016f;
            const float Cavity = 0.096f;
            const float Liner = 0.016f;
            const float Flange = 0.075f;
            const float Overhang = 0.008f;
            const int Bolts = 32;

            radius = Mathf.Max(0.20f, radius);
            height = Mathf.Max(0.10f, height);
            float innerRadius = Mathf.Max(0.05f, radius - Outer - Cavity - Liner);

            double outerArea = 2d * Mathf.PI * radius * height;
            double linerArea = 2d * Mathf.PI * innerRadius * height;
            double skinVolume = (outerArea * Outer) + (sealedRing ? linerArea * Liner : 0d);

            // Two bolted flanges: an annular disc of the full wall width. Real
            // frames are formed sheet with lightening holes, not a solid billet.
            double flangeArea = Mathf.PI * (((radius + Overhang) * (radius + Overhang))
                - (innerRadius * innerRadius));
            double flangeVolume = 2d * flangeArea * Flange * 0.18d;

            float boltRadius = Mathf.Clamp(radius * 0.0033f, 0.003f, 0.02f);
            double boltArea = 2.5980762d * boltRadius * boltRadius;      // regular hexagon
            double boltVolume = Bolts * boltArea * boltRadius * 0.95d;

            return (skinVolume + flangeVolume) * AluminumDensity + (boltVolume * SteelDensity);
        }

        /// <summary>Создать отдельный меш (для превью/иконок).</summary>
        public static Mesh BuildHullRing(float radius, float height, float outerSkin, float cavity,
            float innerLiner, float flangeHeight, float flangeOverhang, int segments,
            int linerPanels, bool sealedRing, int boltCount, float metersPerTile)
        {
            var result = new Mesh();
            FillHullRing(result, radius, height, outerSkin, cavity, innerLiner,
                flangeHeight, flangeOverhang, segments, linerPanels, sealedRing,
                boltCount, metersPerTile);
            return result;
        }

        /// <summary>
        /// Заполнить готовый меш. Меш переиспользуется (не уничтожается) — так
        /// HDRP Resident Drawer не теряет Batch/Mesh ID при пересборке.
        /// </summary>
        public static void FillHullRing(Mesh target, float radius, float height, float outerSkin,
            float cavity, float innerLiner, float flangeHeight, float flangeOverhang, int segments,
            int linerPanels, bool sealedRing, int boltCount, float metersPerTile)
        {
            const float Tau = Mathf.PI * 2f;

            radius = Mathf.Max(0.20f, radius);
            height = Mathf.Max(0.10f, height);
            outerSkin = Mathf.Clamp(outerSkin, 0.002f, radius * 0.15f);
            innerLiner = Mathf.Clamp(innerLiner, 0.002f, radius * 0.15f);
            float maxCavity = radius - outerSkin - innerLiner - 0.10f;
            cavity = Mathf.Clamp(cavity, 0.005f, Mathf.Max(0.005f, maxCavity));
            flangeHeight = Mathf.Clamp(flangeHeight, 0.005f, height * 0.45f);
            flangeOverhang = Mathf.Clamp(flangeOverhang, 0f, radius * 0.03f);
            segments = Mathf.Clamp(segments, 16, 256);
            linerPanels = Mathf.Clamp(linerPanels, 4, 12);
            boltCount = Mathf.Clamp(boltCount, 0, 64);
            metersPerTile = Mathf.Max(0.05f, metersPerTile);

            float innerRadius = radius - outerSkin - cavity - innerLiner;
            float cavityInner = innerRadius + innerLiner;
            float cavityOuter = radius - outerSkin;
            float y0 = flangeHeight;
            float y1 = height - flangeHeight;

            var b = new Builder(segments, metersPerTile);

            // Наружная обшивка (между фланцами) и дно полости.
            b.Cylinder(radius, y0, y1, 0f, Tau, segments, true, 0);
            b.Cylinder(cavityOuter, y0, y1, 0f, Tau, segments, false, 1);

            // Нижний фланец: наружная/внутренняя стенки, дно и дно полости.
            b.Cylinder(radius + flangeOverhang, 0f, y0, 0f, Tau, segments, true, 0);
            b.Cylinder(innerRadius, 0f, y0, 0f, Tau, segments, false, 1);
            b.Face(innerRadius, radius + flangeOverhang, 0f, false, 0);
            b.Face(innerRadius, radius + flangeOverhang, y0, true, 1);

            // Верхний фланец: наружная/внутренняя стенки, потолок полости и торец.
            b.Cylinder(radius + flangeOverhang, y1, height, 0f, Tau, segments, true, 0);
            b.Cylinder(innerRadius, y1, height, 0f, Tau, segments, false, 1);
            b.Face(innerRadius, radius + flangeOverhang, y1, false, 1);
            b.Face(innerRadius, radius + flangeOverhang, height, true, 0);

            if (sealedRing)
            {
                // 4 панели лайнера с тонким швом; полость за ними закрыта.
                // The panel face is recessed a few mm behind the flange lip so the
                // installed liner reads as a panel over a cavity, with a shadow gap.
                float linerFace = innerRadius + Mathf.Clamp(radius * 0.0012f, 0.002f, 0.004f);
                float seamGap = Mathf.Clamp(radius * 0.0008f, 0.001f, 0.004f);
                float gapHalf = (seamGap * 0.5f) / innerRadius;
                float stripHalf = Mathf.Clamp(radius * 0.0072f, 0.008f, 0.040f) * 0.5f / innerRadius;
                float frameHeight = Mathf.Clamp(height * 0.012f, 0.006f, 0.012f);

                for (int p = 0; p < linerPanels; p++)
                {
                    float a0 = Tau * p / linerPanels + gapHalf;
                    float a1 = Tau * (p + 1) / linerPanels - gapHalf;
                    int seg = Mathf.Max(4, Mathf.RoundToInt(segments * (a1 - a0) / Tau));

                    b.Cylinder(linerFace, y0, y1, a0, a1, seg, false, 2);
                    b.Cylinder(cavityInner, y0, y1, a0, a1, seg, true, 1);
                    b.Edge(a0, cavityInner, linerFace, y0, y1, false, 2);
                    b.Edge(a1, cavityInner, linerFace, y0, y1, true, 2);
                }

                // Solder seams and a dark frame around every panel: a panel that
                // was welded over the cavity, not a smooth continuation of the hull.
                for (int p = 0; p < linerPanels; p++)
                {
                    float a = Tau * p / linerPanels;
                    int seg = Mathf.Max(3, Mathf.RoundToInt(segments * stripHalf * 2f / Tau));
                    b.Cylinder(linerFace - 0.0010f, y0, y1, a - stripHalf, a + stripHalf,
                        seg, false, 3);
                }

                b.Cylinder(linerFace - 0.0010f, y0, y0 + frameHeight, 0f, Tau, segments, false, 3);
                b.Cylinder(linerFace - 0.0010f, y1 - frameHeight, y1, 0f, Tau, segments, false, 3);
            }

            // Индикаторная полоса состояния на наружной обшивке: её материал
            // задаёт компонент (зелёный — открыто, янтарный — запаяно).
            float bandHeight = Mathf.Clamp(height * 0.06f, 0.025f, 0.060f);
            float bandY = (height - bandHeight) * 0.5f;
            b.Cylinder(radius + 0.0015f, bandY, bandY + bandHeight, 0f, Tau, segments, true, 5);

            // Болты верхнего фланца.
            if (boltCount > 0)
            {
                float boltRadius = Mathf.Clamp(radius * 0.0033f, 0.003f, 0.02f);
                float inset = Mathf.Clamp(radius * 0.0088f, 0.008f, 0.05f);
                float ring = radius - inset;
                float headHeight = boltRadius * 0.95f;
                for (int i = 0; i < boltCount; i++)
                {
                    float a = Tau * i / boltCount;
                    b.Bolt(a, ring, height, boltRadius, headHeight, 4);
                }
            }

            target.Clear();
            target.name = "HullRing_" + radius.ToString("F2") + (sealedRing ? "_sealed" : "_open");
            target.subMeshCount = 6;
            target.SetVertices(b.Vertices);
            target.SetNormals(b.Normals);
            target.SetUVs(0, b.Uvs);
            for (int i = 0; i < 6; i++)
            {
                target.SetTriangles(b.Triangles[i], i, false);
            }

            target.RecalculateBounds();
        }

        private sealed class Builder
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Triangles;

            private readonly int ringSegments;
            private readonly float tile;

            public Builder(int segments, float metersPerTile)
            {
                ringSegments = segments;
                tile = metersPerTile;
                Triangles = new List<int>[6];
                for (int i = 0; i < Triangles.Length; i++)
                {
                    Triangles[i] = new List<int>();
                }
            }

            private int Add(Vector3 position, Vector3 normal, Vector2 uv, int material)
            {
                Vertices.Add(position);
                Normals.Add(normal);
                Uvs.Add(uv);
                return Vertices.Count - 1;
            }

            private void Tri(int a, int b, int c, int material)
            {
                Triangles[material].Add(a);
                Triangles[material].Add(b);
                Triangles[material].Add(c);
            }

            /// <summary>Цилиндрическая полоса (полная или сектор).</summary>
            public void Cylinder(float r, float y0, float y1, float a0, float a1, int segments,
                bool outward, int material)
            {
                segments = Mathf.Max(2, segments);
                int start = Vertices.Count;
                Vector3 sign = outward ? Vector3.one : -Vector3.one;

                for (int i = 0; i <= segments; i++)
                {
                    float a = Mathf.Lerp(a0, a1, (float)i / segments);
                    float ca = Mathf.Cos(a);
                    float sa = Mathf.Sin(a);
                    var normal = new Vector3(ca * sign.x, 0f, sa * sign.z);
                    float u = (a - a0) * r / tile;
                    Add(new Vector3(ca * r, y0, sa * r), normal, new Vector2(u, y0 / tile), material);
                    Add(new Vector3(ca * r, y1, sa * r), normal, new Vector2(u, y1 / tile), material);
                }

                for (int i = 0; i < segments; i++)
                {
                    int b0 = start + (i * 2);
                    int t0 = b0 + 1;
                    int b1 = b0 + 2;
                    int t1 = b0 + 3;
                    if (outward)
                    {
                        Tri(b0, b1, t1, material);
                        Tri(b0, t1, t0, material);
                    }
                    else
                    {
                        Tri(b0, t1, b1, material);
                        Tri(b0, t0, t1, material);
                    }
                }
            }

            /// <summary>Плоское кольцо (торец фланца, дно полости).</summary>
            public void Face(float rIn, float rOut, float y, bool up, int material)
            {
                int start = Vertices.Count;
                Vector3 normal = up ? Vector3.up : Vector3.down;

                for (int i = 0; i <= ringSegments; i++)
                {
                    float a = Mathf.PI * 2f * i / ringSegments;
                    float ca = Mathf.Cos(a);
                    float sa = Mathf.Sin(a);
                    Add(new Vector3(ca * rIn, y, sa * rIn), normal,
                        new Vector2((ca * rIn) / tile, (sa * rIn) / tile), material);
                    Add(new Vector3(ca * rOut, y, sa * rOut), normal,
                        new Vector2((ca * rOut) / tile, (sa * rOut) / tile), material);
                }

                for (int i = 0; i < ringSegments; i++)
                {
                    int i0 = start + (i * 2);
                    int o0 = i0 + 1;
                    int i1 = i0 + 2;
                    int o1 = i0 + 3;
                    if (up)
                    {
                        Tri(i0, o0, o1, material);
                        Tri(i0, o1, i1, material);
                    }
                    else
                    {
                        Tri(i0, o1, o0, material);
                        Tri(i0, i1, o1, material);
                    }
                }
            }

            /// <summary>Торцевая кромка панели лайнера (радиальная плоскость).</summary>
            public void Edge(float a, float r0, float r1, float y0, float y1, bool positiveTangent,
                int material)
            {
                Vector3 normal = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a))
                    * (positiveTangent ? 1f : -1f);
                float ca = Mathf.Cos(a);
                float sa = Mathf.Sin(a);
                int i0 = Add(new Vector3(ca * r0, y0, sa * r0), normal,
                    new Vector2(r0 / tile, y0 / tile), material);
                int i1 = Add(new Vector3(ca * r1, y0, sa * r1), normal,
                    new Vector2(r1 / tile, y0 / tile), material);
                int i2 = Add(new Vector3(ca * r1, y1, sa * r1), normal,
                    new Vector2(r1 / tile, y1 / tile), material);
                int i3 = Add(new Vector3(ca * r0, y1, sa * r0), normal,
                    new Vector2(r0 / tile, y1 / tile), material);

                Tri(i0, i1, i2, material);
                Tri(i0, i2, i3, material);
            }

            /// <summary>Шестигранная головка болта на верхнем торце.</summary>
            public void Bolt(float angle, float r, float y, float headRadius, float headHeight,
                int material)
            {
                const int Sides = 6;
                float ca = Mathf.Cos(angle);
                float sa = Mathf.Sin(angle);
                var center = new Vector3(ca * r, y, sa * r);
                int start = Vertices.Count;

                for (int i = 0; i < Sides; i++)
                {
                    float a = angle + (Mathf.PI * 2f * i / Sides);
                    float x = Mathf.Cos(a);
                    float z = Mathf.Sin(a);
                    var side = new Vector3(x, 0f, z);
                    var bottom = new Vector3(center.x + x * headRadius, y, center.z + z * headRadius);
                    var top = new Vector3(center.x + x * headRadius, y + headHeight, center.z + z * headRadius);
                    Add(bottom, side, new Vector2(a * headRadius / tile, y / tile), material);
                    Add(top, side, new Vector2(a * headRadius / tile, (y + headHeight) / tile), material);
                }

                for (int i = 0; i < Sides; i++)
                {
                    int b0 = start + (i * 2);
                    int t0 = b0 + 1;
                    int b1 = start + (((i + 1) % Sides) * 2);
                    int t1 = b1 + 1;
                    Tri(b0, b1, t1, material);
                    Tri(b0, t1, t0, material);
                }

                int capCenter = Add(new Vector3(center.x, y + headHeight, center.z),
                    Vector3.up, new Vector2(center.x / tile, center.z / tile), material);
                int capStart = Vertices.Count;
                for (int i = 0; i < Sides; i++)
                {
                    float a = angle + (Mathf.PI * 2f * i / Sides);
                    float x = Mathf.Cos(a);
                    float z = Mathf.Sin(a);
                    Add(new Vector3(center.x + x * headRadius, y + headHeight,
                        center.z + z * headRadius), Vector3.up,
                        new Vector2((center.x + x * headRadius) / tile,
                            (center.z + z * headRadius) / tile), material);
                }

                for (int i = 0; i < Sides; i++)
                {
                    Tri(capCenter, capStart + i, capStart + ((i + 1) % Sides), material);
                }
            }
        }
    }
}
