using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// ДОРОГА / АСФАЛЬТОВАЯ ПЛОЩАДКА внутри МЕСТА (SurfaceSite).
    ///
    /// Кладётся ребёнком места (локальная позиция 0, поворот 0). Из списка точек
    /// строит Mesh и сажает каждую вершину на рельеф тем же способом, что
    /// SurfaceGrounded: TerrainNoise.SampleHeight по направлению точки.
    ///
    /// Две формы:
    ///   Ribbon  — лента заданной ширины вдоль линии точек (обычная дорога).
    ///   Polygon — произвольный замкнутый контур, закрашенный целиком
    ///             (площадка как на референсе). Рассчитан на РОВНУЮ площадку:
    ///             внутри контура вершин нет, поэтому рельеф он повторяет только
    ///             по углам. Под ним должна лежать «Площадка рельефа».
    ///
    /// Текстура одинаковая везде, потому что UV считаются не от меша, а от
    /// метровых координат места: u = восток / TileMeters, v = север / TileMeters.
    /// Любые куски дороги с одним материалом стыкуются без шва.
    ///
    /// Меш не сохраняется в префаб: он строится при включении компонента.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SurfaceRoad : MonoBehaviour
    {
        public enum RoadShape
        {
            Ribbon,
            Polygon
        }

        [Header("Форма")]
        [Tooltip("Ribbon — лента вдоль точек. Polygon — замкнутый контур, залитый целиком (для ровной площадки).")]
        public RoadShape Shape = RoadShape.Ribbon;

        [Tooltip("Точки в локальных осях места: X — север, Z — восток. Y игнорируется (высоту берём с земли).")]
        public List<Vector3> Points = new List<Vector3> { new Vector3(-20f, 0f, 0f), new Vector3(20f, 0f, 0f) };

        [Tooltip("Ширина ленты, м. Только для Ribbon.")]
        [Min(0.5f)]
        public float WidthMeters = 6f;

        [Tooltip("Сгладить линию по точкам (кривая Катмулла-Рома). Только для Ribbon.")]
        public bool Smooth = true;

        [Header("Качество")]
        [Tooltip("Шаг вершин вдоль ленты, м. Меньше — точнее повторяет рельеф, но больше вершин.")]
        [Min(0.25f)]
        public float StepMeters = 2f;

        [Tooltip("Сколько кусков поперёк ленты. 3 достаточно, чтобы лента повторяла поперечный наклон склона.")]
        [Range(1, 16)]
        public int CrossSegments = 3;

        [Header("Посадка на землю")]
        [Tooltip("Подъём над землёй, м. Нужен против мерцания (z-fighting). На склоне, где меш рельефа " +
                 "грубее точной функции высоты, поднимите до 0.1-0.3.")]
        public float LiftMeters = 0.05f;

        [Header("Текстура")]
        [Tooltip("Сколько метров занимает один повтор текстуры. Одно значение на всю дорогу = одинаковый масштаб.")]
        [Min(0.1f)]
        public float TileMeters = 4f;

        [Tooltip("Если дорогу не видно сверху (видно только снизу) — включите.")]
        public bool FlipFaces;

        private Mesh mesh;
        private bool built;
        private int signature;

        /// <summary>Построена ли дорога (место найдено и тело разрешилось).</summary>
        public bool IsBuilt => built;

        private void OnEnable()
        {
            built = false;
            TryBuild();
        }

        private void OnDisable()
        {
            ReleaseMesh();
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += DelayedRebuild;
#endif
        }

#if UNITY_EDITOR
        private void DelayedRebuild()
        {
            UnityEditor.EditorApplication.delayCall -= DelayedRebuild;
            if (this != null && isActiveAndEnabled)
            {
                TryBuild();
            }
        }
#endif

        private void Update()
        {
            if (!built)
            {
                // В Play тело места может разрешиться на пару кадров позже,
                // чем включится дорога — пробуем, пока не получится.
                TryBuild();
                return;
            }

            if (!Application.isPlaying)
            {
                SurfaceSite site = GetComponentInParent<SurfaceSite>();
                if (site != null && SiteSignature(site) != signature)
                {
                    TryBuild();
                }
            }
        }

        /// <summary>Перестроить меш (после правки площадки рельефа или места).</summary>
        [ContextMenu("Rebuild road")]
        public void Rebuild()
        {
            TryBuild();
        }

        /// <summary>Точки в мировых координатах, уже посаженные на землю (для хендлов в Scene view).</summary>
        public Vector3[] GetPointsWorld()
        {
            SurfaceSite site = GetComponentInParent<SurfaceSite>();
            if (site == null || Points == null)
            {
                return null;
            }

            GroundSampler ground = GroundSampler.Create(site);
            if (ground == null)
            {
                return null;
            }

            var result = new Vector3[Points.Count];
            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 s = site.transform.InverseTransformPoint(transform.TransformPoint(Points[i]));
                float y = ground.LocalHeight(s.x, s.z, LiftMeters);
                result[i] = site.transform.TransformPoint(new Vector3(s.x, y, s.z));
            }

            return result;
        }

        /// <summary>Передвинуть точку в мировую позицию (высота отбрасывается).</summary>
        public void SetPointFromWorld(int index, Vector3 world)
        {
            if (Points == null || index < 0 || index >= Points.Count)
            {
                return;
            }

            Vector3 local = transform.InverseTransformPoint(world);
            local.y = 0f;
            Points[index] = local;
        }

        /// <summary>Добавить точку в конец списка по мировой позиции.</summary>
        public void AddPointFromWorld(Vector3 world)
        {
            if (Points == null)
            {
                Points = new List<Vector3>();
            }

            Vector3 local = transform.InverseTransformPoint(world);
            local.y = 0f;
            Points.Add(local);
        }

        private bool TryBuild()
        {
            SurfaceSite site = GetComponentInParent<SurfaceSite>();
            if (site == null || !site.isActiveAndEnabled)
            {
                built = false;
                return false;
            }

            GroundSampler ground = GroundSampler.Create(site);
            if (ground == null)
            {
                built = false;
                return false;
            }

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            bool ok = Shape == RoadShape.Polygon
                ? BuildPolygon(site, ground, verts, uvs, tris)
                : BuildRibbon(site, ground, verts, uvs, tris);

            if (!ok || verts.Count < 3 || tris.Count < 3)
            {
                // Точек пока мало — это не ошибка, просто рисовать нечего.
                ReleaseMesh();
                built = true;
                signature = SiteSignature(site);
                return false;
            }

            ApplyMesh(verts, uvs, tris);
            built = true;
            signature = SiteSignature(site);
            return true;
        }

        private void ApplyMesh(List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = "SurfaceRoad (generated)";
                mesh.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                mesh.Clear();
            }

            mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            GetComponent<MeshFilter>().sharedMesh = mesh;
        }

        private void ReleaseMesh()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = null;
            }

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

                mesh = null;
            }

            built = false;
        }

        private static int SiteSignature(SurfaceSite site)
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + site.LatitudeDegrees.GetHashCode();
                h = (h * 31) + site.LongitudeDegrees.GetHashCode();
                h = (h * 31) + site.SnapToTerrain.GetHashCode();
                h = (h * 31) + site.HeightOffsetMeters.GetHashCode();
                h = (h * 31) + site.AltitudeMeters.GetHashCode();
                return h;
            }
        }

        // ---------- Точки управления в осях места ----------

        /// <summary>Контрольные точки как (север, восток) в осях места, без почти-дубликатов подряд.</summary>
        private List<Vector2> ControlPoints2D(SurfaceSite site)
        {
            var result = new List<Vector2>();
            if (Points == null)
            {
                return result;
            }

            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 s = site.transform.InverseTransformPoint(transform.TransformPoint(Points[i]));
                var p = new Vector2(s.x, s.z);
                if (result.Count > 0 && (p - result[result.Count - 1]).sqrMagnitude < 1e-4f)
                {
                    continue;
                }

                result.Add(p);
            }

            return result;
        }

        private void AddVertex(
            SurfaceSite site, GroundSampler ground, float north, float east,
            List<Vector3> verts, List<Vector2> uvs)
        {
            float y = ground.LocalHeight(north, east, LiftMeters);
            Vector3 world = site.transform.TransformPoint(new Vector3(north, y, east));
            verts.Add(transform.InverseTransformPoint(world));

            float tile = Mathf.Max(0.1f, TileMeters);
            uvs.Add(new Vector2(east / tile, north / tile));
        }

        private void AddTriangle(List<int> tris, int a, int b, int c)
        {
            tris.Add(a);
            if (FlipFaces)
            {
                tris.Add(c);
                tris.Add(b);
            }
            else
            {
                tris.Add(b);
                tris.Add(c);
            }
        }

        // ---------- Лента ----------

        private bool BuildRibbon(
            SurfaceSite site, GroundSampler ground,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            List<Vector2> control = ControlPoints2D(site);
            if (control.Count < 2)
            {
                return false;
            }

            List<Vector2> path = Resample(control, Smooth, Mathf.Max(0.25f, StepMeters));
            int count = path.Count;
            int columns = Mathf.Clamp(CrossSegments, 1, 16) + 1;
            float half = Mathf.Max(0.25f, WidthMeters) * 0.5f;

            Vector2 lastDir = new Vector2(1f, 0f);
            for (int i = 0; i < count; i++)
            {
                Vector2 prev = path[Mathf.Max(i - 1, 0)];
                Vector2 next = path[Mathf.Min(i + 1, count - 1)];
                Vector2 dir = next - prev;
                if (dir.sqrMagnitude < 1e-8f)
                {
                    dir = lastDir;
                }
                else
                {
                    dir.Normalize();
                    lastDir = dir;
                }

                // Оси: x = север, y = восток. Вправо по ходу движения — (восток, -север).
                Vector2 side = new Vector2(dir.y, -dir.x);
                for (int j = 0; j < columns; j++)
                {
                    float t = j / (float)(columns - 1);
                    float offset = Mathf.Lerp(-half, half, t);
                    Vector2 p = path[i] + (side * offset);
                    AddVertex(site, ground, p.x, p.y, verts, uvs);
                }
            }

            for (int i = 0; i < count - 1; i++)
            {
                for (int j = 0; j < columns - 1; j++)
                {
                    int a = (i * columns) + j;
                    int b = ((i + 1) * columns) + j;
                    int c = a + 1;
                    int d = b + 1;
                    AddTriangle(tris, a, b, c);
                    AddTriangle(tris, c, b, d);
                }
            }

            return true;
        }

        private static List<Vector2> Resample(List<Vector2> control, bool smooth, float step)
        {
            var result = new List<Vector2>();
            int n = control.Count;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p1 = control[i];
                Vector2 p2 = control[i + 1];
                Vector2 p0 = i > 0 ? control[i - 1] : p1 + (p1 - p2);
                Vector2 p3 = i + 2 < n ? control[i + 2] : p2 + (p2 - p1);

                float length = Vector2.Distance(p1, p2);
                int sub = Mathf.Max(1, Mathf.CeilToInt(length / step));
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub;
                    result.Add(smooth ? CatmullRom(p0, p1, p2, p3, t) : Vector2.Lerp(p1, p2, t));
                }
            }

            result.Add(control[n - 1]);
            return result;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + ((p2 - p0) * t)
                + (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2)
                + (((3f * p1) - p0 - (3f * p2) + p3) * t3));
        }

        // ---------- Полигон ----------

        private bool BuildPolygon(
            SurfaceSite site, GroundSampler ground,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            List<Vector2> contour = ControlPoints2D(site);
            if (contour.Count > 1 && (contour[0] - contour[contour.Count - 1]).sqrMagnitude < 1e-4f)
            {
                contour.RemoveAt(contour.Count - 1);
            }

            if (contour.Count < 3)
            {
                return false;
            }

            var indices = new List<int>();
            if (!Triangulate(contour, indices))
            {
                Debug.LogWarning("SurfaceRoad: контур самопересекается или вырожден — не удалось залить. Расставьте точки без пересечений.", this);
                return false;
            }

            for (int i = 0; i < contour.Count; i++)
            {
                AddVertex(site, ground, contour[i].x, contour[i].y, verts, uvs);
            }

            // Ушная триангуляция даёт обход против часовой в плоскости (север, восток),
            // для вида сверху Unity нужен по часовой — поэтому (a, c, b).
            for (int i = 0; i < indices.Count; i += 3)
            {
                AddTriangle(tris, indices[i], indices[i + 2], indices[i + 1]);
            }

            return true;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.x * b.y) - (a.y * b.x);
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos);
        }

        /// <summary>Триангуляция простого многоугольника методом отсечения ушей. Выход: тройки индексов, против часовой.</summary>
        private static bool Triangulate(List<Vector2> pts, List<int> output)
        {
            int n = pts.Count;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++)
            {
                idx.Add(i);
            }

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                area += Cross(pts[i], pts[(i + 1) % n]);
            }

            if (Mathf.Abs(area) < 1e-6f)
            {
                return false;
            }

            if (area < 0f)
            {
                idx.Reverse();
            }

            int guard = 0;
            while (idx.Count > 3)
            {
                if (guard++ > 10000)
                {
                    return false;
                }

                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count];
                    int ib = idx[i];
                    int ic = idx[(i + 1) % idx.Count];
                    Vector2 a = pts[ia];
                    Vector2 b = pts[ib];
                    Vector2 c = pts[ic];

                    // Выпуклая вершина: поворот влево (против часовой).
                    if (Cross(b - a, c - b) <= 1e-6f)
                    {
                        continue;
                    }

                    bool blocked = false;
                    for (int k = 0; k < idx.Count; k++)
                    {
                        int ik = idx[k];
                        if (ik == ia || ik == ib || ik == ic)
                        {
                            continue;
                        }

                        if (PointInTriangle(pts[ik], a, b, c))
                        {
                            blocked = true;
                            break;
                        }
                    }

                    if (blocked)
                    {
                        continue;
                    }

                    output.Add(ia);
                    output.Add(ib);
                    output.Add(ic);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }

                if (!clipped)
                {
                    return false;
                }
            }

            output.Add(idx[0]);
            output.Add(idx[1]);
            output.Add(idx[2]);
            return true;
        }

        // ---------- Высота земли ----------

        /// <summary>
        /// Высота земли в точке места (north, east) в локальных осях места.
        /// Формула та же, что в SurfaceGrounded.Apply, поэтому дорога и здания
        /// садятся на одну и ту же землю.
        /// </summary>
        private sealed class GroundSampler
        {
            private readonly OrbitingBody body;
            private readonly HeightfieldTerrain terrain;
            private readonly TerrainNoiseParams noise;
            private readonly double latRad;
            private readonly double lonRad;
            private readonly double siteAltitude;

            private GroundSampler(SurfaceSite site, OrbitingBody body, HeightfieldTerrain terrain)
            {
                this.body = body;
                this.terrain = terrain;
                noise = terrain != null ? TerrainNoiseParams.FromTerrain(terrain) : default(TerrainNoiseParams);
                latRad = site.LatitudeDegrees * (System.Math.PI / 180d);
                lonRad = site.LongitudeDegrees * (System.Math.PI / 180d);
                siteAltitude = site.AltitudeMeters;
            }

            public static GroundSampler Create(SurfaceSite site)
            {
                OrbitingBody body = site.BodyState;
                if (body == null)
                {
                    return null;
                }

                return new GroundSampler(site, body, site.Terrain);
            }

            public float LocalHeight(double north, double east, float lift)
            {
                double ground = 0d;
                if (terrain != null)
                {
                    Vector3d direction = SurfaceFrameMath.DirectionOffset(latRad, lonRad, body.Radius, east, north);
                    ground = TerrainNoise.SampleHeight(
                        noise,
                        new Unity.Mathematics.double3(direction.X, direction.Y, direction.Z)) * terrain.AmplitudeMeters;
                }

                return (float)(ground - siteAltitude + lift);
            }
        }
    }
}
