using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// ROAD / ASPHALT PAD inside a SITE (SurfaceSite).
    ///
    /// Placed as a child of the site (local position 0, rotation 0). From a list of
    /// points it builds a Mesh and seats each vertex on the terrain the same way as
    /// SurfaceGrounded: TerrainNoise.SampleHeight along the point direction.
    ///
    /// Two shapes: Ribbon is a strip along the point line; Polygon is an arbitrary
    /// closed contour filled entirely, meant for a FLAT pad (no interior vertices,
    /// so it follows the terrain only at the corners). A "terrain pad" must lie
    /// under it.
    ///
    /// The texture is identical everywhere because UVs come from the site's metre
    /// coordinates: u = east / TileMeters, v = north / TileMeters, so any road
    /// pieces with one material join seamlessly.
    ///
    /// PLATE: the surface sits LiftMeters above the ground, with edges dropping
    /// ThicknessMeters and a bevel of EdgeSlopeDegrees (<= 30 deg is the player walk
    /// limit). AddCollider adds a MeshCollider and a SiteBox marker, since the player
    /// support (SiteBoxSupport) accepts only colliders tied to a SiteBox.
    ///
    /// Keep the road object at the SITE ZERO (position 0, rotation 0, scale 1):
    /// width and thickness come from the Width/Thickness fields, not from scale.
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

        [Tooltip("Ширина, м. Ribbon — ширина ленты. Polygon — габарит по северу; правка масштабирует контур.")]
        [Min(0.5f)]
        public float WidthMeters = 6f;

        [Tooltip("Длина, м. Ribbon — расстояние первая↔последняя точка (лента тянется от первой точки), " +
                 "Polygon — габарит по востоку; правка масштабирует контур. Правка точек в сцене обновляет поле.")]
        [Min(0.5f)]
        public float LengthMeters = 100f;

        [Tooltip("Сгладить линию по точкам (кривая Катмулла-Рома). Только для Ribbon.")]
        public bool Smooth = true;

        [Header("Качество")]
        [Tooltip("Шаг вершин вдоль ленты, м. Меньше — точнее повторяет рельеф, но больше вершин.")]
        [Min(0.25f)]
        public float StepMeters = 2f;

        [Tooltip("Сколько кусков поперёк ленты. 3 достаточно, чтобы лента повторяла поперечный наклон склона.")]
        [Range(1, 16)]
        public int CrossSegments = 3;

        [Header("Плита")]
        [Tooltip("Подъём верхней поверхности над землёй, м. 0.05 — только против мерцания; " +
                 "0.2-0.3 — дорога заметно возвышается над грунтом (игрок поднимается шагом до 0.5 м).")]
        public float LiftMeters = 0.2f;

        [Tooltip("Толщина плиты, м: борта уходят вниз от верхней поверхности. " +
                 "0 = плоский лист (старое поведение). 0.5-1 делает дорогу насыпью с видимым краем.")]
        [Min(0f)]
        public float ThicknessMeters = 0.5f;

        [Tooltip("Скос кромки: угол борта от горизонта, °. Верхний предел — 30: это предел ходимости игрока " +
                 "(MaxSlopeDegrees); круче — он не зайдёт и может зарыться внутрь плиты. 15-25 — плавный бордюр.")]
        [Range(10f, 30f)]
        public float EdgeSlopeDegrees = 25f;

        [Tooltip("MeshCollider по дороге: по ней ходит игрок (через SiteBox-маркер) и работает физика. " +
                 "Выключите только для чисто декоративной дороги.")]
        public bool AddCollider = true;

        [Header("Текстура")]
        [Tooltip("Сколько метров занимает один повтор текстуры. Одно значение на всю дорогу = одинаковый масштаб.")]
        [Min(0.1f)]
        public float TileMeters = 4f;

        [Tooltip("Если дорогу не видно сверху (видно только снизу) — включите.")]
        public bool FlipFaces;

        private Mesh mesh;
        private bool built;
        private int signature;
        private MeshCollider roadCollider;
        private SiteBox solidMarker;
        private float lastAppliedLength;
        private float lastAppliedWidth;

        /// <summary>Whether the road is built (site found and body resolved).</summary>
        public bool IsBuilt => built;

        private void OnEnable()
        {
            built = false;
            // Base mark for the length field: on load the truth is in the points (in
            // saved state the field already matches them; for old data it is filled in).
            RefreshLengthFromPoints();
            TryBuild();
        }

        private void OnDisable()
        {
            ReleaseMesh();
        }

        private void OnValidate()
        {
            SyncSize();
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
                // In Play the site body may resolve a couple of frames after the road
                // is enabled — keep trying until it works.
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

        /// <summary>Rebuild the mesh (after editing the terrain pad or the site).</summary>
        [ContextMenu("Rebuild road")]
        public void Rebuild()
        {
            RefreshLengthFromPoints();
            TryBuild();
        }

        // ---------- Length and width ----------

        private struct ControlBounds2D
        {
            public float MinNorth;
            public float MaxNorth;
            public float MinEast;
            public float MaxEast;

            public float SizeNorth => MaxNorth - MinNorth;
            public float SizeEast => MaxEast - MinEast;
            public float CenterNorth => (MinNorth + MaxNorth) * 0.5f;
            public float CenterEast => (MinEast + MaxEast) * 0.5f;
        }

        private bool TryControlBounds(out ControlBounds2D bounds)
        {
            bounds = default;
            if (Points == null || Points.Count == 0)
            {
                return false;
            }

            bounds.MinNorth = float.MaxValue;
            bounds.MaxNorth = float.MinValue;
            bounds.MinEast = float.MaxValue;
            bounds.MaxEast = float.MinValue;
            for (int i = 0; i < Points.Count; i++)
            {
                bounds.MinNorth = Mathf.Min(bounds.MinNorth, Points[i].x);
                bounds.MaxNorth = Mathf.Max(bounds.MaxNorth, Points[i].x);
                bounds.MinEast = Mathf.Min(bounds.MinEast, Points[i].z);
                bounds.MaxEast = Mathf.Max(bounds.MaxEast, Points[i].z);
            }

            return true;
        }

        /// <summary>
        /// Update the size fields from the current geometry: Ribbon — Length by the
        /// first↔last point; Polygon — Width/Length by the contour bounds. Does not
        /// touch the geometry.
        /// </summary>
        public void RefreshLengthFromPoints()
        {
            if (Points == null || Points.Count < 2)
            {
                return;
            }

            if (Shape == RoadShape.Ribbon)
            {
                LengthMeters = Mathf.Max(0.5f, ControlLength());
                lastAppliedLength = LengthMeters;
                lastAppliedWidth = WidthMeters;
                return;
            }

            ControlBounds2D bounds;
            if (!TryControlBounds(out bounds))
            {
                return;
            }

            WidthMeters = Mathf.Max(0.5f, bounds.SizeNorth);
            LengthMeters = Mathf.Max(0.5f, bounds.SizeEast);
            lastAppliedWidth = WidthMeters;
            lastAppliedLength = LengthMeters;
        }

        private float ControlLength()
        {
            return (Points[Points.Count - 1] - Points[0]).magnitude;
        }

        /// <summary>Stretch the strip from the first point so the distance to the last becomes LengthMeters.</summary>
        private void ApplyLengthToPoints()
        {
            if (Shape != RoadShape.Ribbon || Points == null || Points.Count < 2)
            {
                return;
            }

            float actual = ControlLength();
            if (actual < 1e-3f)
            {
                return;
            }

            float k = Mathf.Max(0.5f, LengthMeters) / actual;
            Vector3 start = Points[0];
            for (int i = 1; i < Points.Count; i++)
            {
                Points[i] = start + ((Points[i] - start) * k);
            }
        }

        /// <summary>Scale the polygon contour to Width×Length bounds around the bounds centre.</summary>
        private void ApplySizeToPoints(ControlBounds2D bounds)
        {
            float kx = bounds.SizeNorth > 1e-3f ? Mathf.Max(0.5f, WidthMeters) / bounds.SizeNorth : 1f;
            float kz = bounds.SizeEast > 1e-3f ? Mathf.Max(0.5f, LengthMeters) / bounds.SizeEast : 1f;
            float cn = bounds.CenterNorth;
            float ce = bounds.CenterEast;
            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 p = Points[i];
                Points[i] = new Vector3(cn + ((p.x - cn) * kx), p.y, ce + ((p.z - ce) * kz));
            }
        }

        /// <summary>
        /// The size fields and the points are two views of one value. Field changed —
        /// stretch the geometry; points changed (handles, list, undo) — update the fields.
        /// </summary>
        private void SyncSize()
        {
            if (Points == null || Points.Count < 2)
            {
                return;
            }

            if (lastAppliedLength <= 0f)
            {
                lastAppliedLength = LengthMeters;
                lastAppliedWidth = WidthMeters;
                return;
            }

            if (Shape == RoadShape.Ribbon)
            {
                if (Mathf.Abs(LengthMeters - lastAppliedLength) > 1e-3f)
                {
                    ApplyLengthToPoints();
                    lastAppliedLength = LengthMeters;
                }
                else
                {
                    RefreshLengthFromPoints();
                }

                return;
            }

            if (Points.Count < 3)
            {
                return;
            }

            ControlBounds2D bounds;
            if (!TryControlBounds(out bounds))
            {
                return;
            }

            bool widthChanged = Mathf.Abs(WidthMeters - lastAppliedWidth) > 1e-3f;
            bool lengthChanged = Mathf.Abs(LengthMeters - lastAppliedLength) > 1e-3f;
            if (widthChanged || lengthChanged)
            {
                ApplySizeToPoints(bounds);
                lastAppliedWidth = WidthMeters;
                lastAppliedLength = LengthMeters;
            }
            else
            {
                RefreshLengthFromPoints();
            }
        }

        /// <summary>Points in world coordinates, already seated on the ground (for Scene view handles).</summary>
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

        /// <summary>Move a point to a world position (height is discarded).</summary>
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

        /// <summary>Append a point by world position.</summary>
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
                // Too few points yet — not an error, just nothing to draw.
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
            UpdateCollider();
            EnsureGpuDrivenExcluded();
        }

        private static readonly System.Type GpuDrivenExclusionType =
            System.Type.GetType("UnityEngine.Rendering.DisallowGPUDrivenRendering, Unity.RenderPipelines.GPUDriven.Runtime");

        /// <summary>
        /// Exclude the road from the GPU Resident Drawer: its mesh is built at runtime
        /// (HideAndDontSave), and the drawer with such meshes without GPU registration
        /// gives "A BatchDrawCommand was submitted with an invalid Batch, Mesh, or
        /// Material ID". The stock exclusion component (Unity shows it in Add
        /// Component) is declared internal — the type is fetched by reflection.
        /// </summary>
        private void EnsureGpuDrivenExcluded()
        {
            if (GpuDrivenExclusionType == null || GetComponent(GpuDrivenExclusionType) != null)
            {
                return;
            }

            gameObject.AddComponent(GpuDrivenExclusionType);
        }

        /// <summary>
        /// Plate collider: a non-convex MeshCollider over the generated mesh. The
        /// SiteBox alongside is the "solid" marker: SiteBoxSupport accepts only
        /// colliders tied to a SiteBox, otherwise the player support does not see the
        /// road. The marker bounds are degenerate so the old box path of the Registry
        /// (PushOutMesh does not touch it — there is no baking) pushes nothing.
        /// </summary>
        private void UpdateCollider()
        {
            if (!AddCollider)
            {
                RemoveCollider();
                return;
            }

            if (roadCollider == null)
            {
                roadCollider = GetComponent<MeshCollider>();
            }

            if (roadCollider == null)
            {
                roadCollider = gameObject.AddComponent<MeshCollider>();
            }

            roadCollider.convex = false;
            // Reassigning the same mesh forces PhysX to rebake the collider after
            // mesh.Clear() — the MeshCollider itself does not track the mesh contents.
            roadCollider.sharedMesh = null;
            roadCollider.sharedMesh = mesh;

            if (solidMarker == null)
            {
                solidMarker = GetComponent<SiteBox>();
            }

            if (solidMarker == null)
            {
                solidMarker = gameObject.AddComponent<SiteBox>();
            }

            solidMarker.UseMeshCollision = false;
            solidMarker.Center = Vector3.zero;
            solidMarker.Size = new Vector3(0.02f, 0.02f, 0.02f);
        }

        private void RemoveCollider()
        {
            MeshCollider collider = GetComponent<MeshCollider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            roadCollider = null;

            SiteBox marker = GetComponent<SiteBox>();
            if (marker != null && solidMarker == marker)
            {
                if (Application.isPlaying)
                {
                    Destroy(marker);
                }
                else
                {
                    DestroyImmediate(marker);
                }
            }

            solidMarker = null;
        }

        private void ReleaseMesh()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = null;
            }

            MeshCollider collider = GetComponent<MeshCollider>();
            if (collider != null)
            {
                collider.sharedMesh = null;
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

        // ---------- Control points in site axes ----------

        /// <summary>
        /// Road point in site axes. By contract the road is a child of the site at
        /// zero (position 0, rotation 0, scale 1) — then the points are ALREADY in
        /// site axes and no world transform is needed. This matters beyond precision:
        /// the decor exclusion table is built from the points, and running them
        /// through the world matrix jitters the floats every frame.
        /// </summary>
        private Vector3 ToSitePoint(SurfaceSite site, Vector3 point)
        {
            Transform t = transform;
            Vector3 pos = t.localPosition;
            Vector3 scale = t.localScale;
            bool identity = pos.sqrMagnitude < 1e-12f
                && Quaternion.Angle(t.localRotation, Quaternion.identity) < 1e-4f
                && Mathf.Abs(scale.x - 1f) < 1e-5f
                && Mathf.Abs(scale.y - 1f) < 1e-5f
                && Mathf.Abs(scale.z - 1f) < 1e-5f;
            if (identity)
            {
                return point;
            }

            return site.transform.InverseTransformPoint(t.TransformPoint(point));
        }

        /// <summary>Control points as (north, east) in site axes, without near-duplicates in a row.</summary>
        private List<Vector2> ControlPoints2D(SurfaceSite site)
        {
            var result = new List<Vector2>();
            if (Points == null)
            {
                return result;
            }

            for (int i = 0; i < Points.Count; i++)
            {
                Vector3 s = ToSitePoint(site, Points[i]);
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

        // ---------- Ribbon ----------

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

            var directions = new Vector2[count];
            var sideNormals = new Vector2[count];
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

                directions[i] = dir;
                // Axes: x = north, y = east. To the right of travel is (east, -north).
                sideNormals[i] = new Vector2(dir.y, -dir.x);
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < columns; j++)
                {
                    float t = j / (float)(columns - 1);
                    float offset = Mathf.Lerp(-half, half, t);
                    Vector2 p = path[i] + (sideNormals[i] * offset);
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

            if (ThicknessMeters > 0.0001f)
            {
                for (int i = 0; i < count - 1; i++)
                {
                    Vector2 leftOut = (-(sideNormals[i] + sideNormals[i + 1])).normalized;
                    AddWallQuad(
                        site, ground, verts, uvs, tris,
                        path[i] - (sideNormals[i] * half),
                        path[i + 1] - (sideNormals[i + 1] * half),
                        leftOut,
                        true);

                    Vector2 rightOut = (sideNormals[i] + sideNormals[i + 1]).normalized;
                    AddWallQuad(
                        site, ground, verts, uvs, tris,
                        path[i] + (sideNormals[i] * half),
                        path[i + 1] + (sideNormals[i + 1] * half),
                        rightOut,
                        true);
                }

                AddRibbonCap(site, ground, verts, uvs, tris, path[0], sideNormals[0], half, -directions[0]);
                AddRibbonCap(site, ground, verts, uvs, tris, path[count - 1], sideNormals[count - 1], half, directions[count - 1]);
            }

            return true;
        }

        /// <summary>Ribbon cap: a wall across the full width, facing outward along the path.</summary>
        private void AddRibbonCap(
            SurfaceSite site, GroundSampler ground,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector2 center, Vector2 side, float half, Vector2 outward)
        {
            int columns = Mathf.Clamp(CrossSegments, 1, 16) + 1;
            for (int j = 0; j < columns - 1; j++)
            {
                float t0 = j / (float)(columns - 1);
                float t1 = (j + 1) / (float)(columns - 1);
                AddWallQuad(
                    site, ground, verts, uvs, tris,
                    center + (side * Mathf.Lerp(-half, half, t0)),
                    center + (side * Mathf.Lerp(-half, half, t1)),
                    outward,
                    false);
            }
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

        // ---------- Polygon ----------

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

            // Ear triangulation gives counter-clockwise winding in the (north, east)
            // plane; for a top view Unity needs clockwise — hence (a, c, b).
            for (int i = 0; i < indices.Count; i += 3)
            {
                AddTriangle(tris, indices[i], indices[i + 2], indices[i + 1]);
            }

            if (ThicknessMeters > 0.0001f)
            {
                float area = SignedArea(contour);
                for (int i = 0; i < contour.Count; i++)
                {
                    Vector2 a = contour[i];
                    Vector2 b = contour[(i + 1) % contour.Count];
                    Vector2 d = b - a;
                    if (d.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    // For a CCW contour (north, east) outward is (d.y, -d.x); for CW it is the opposite.
                    Vector2 outward = area >= 0f
                        ? new Vector2(d.y, -d.x).normalized
                        : new Vector2(-d.y, d.x).normalized;
                    AddWallQuad(site, ground, verts, uvs, tris, a, b, outward, true);
                }
            }

            return true;
        }

        private static float SignedArea(List<Vector2> pts)
        {
            float area = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % pts.Count];
                area += (a.x * b.y) - (b.x * a.y);
            }

            return area * 0.5f;
        }

        /// <summary>
        /// Plate edge on segment a→b (site axes: x north, y east): down from the
        /// surface by ThicknessMeters, with a bevel of EdgeSlopeDegrees (bevel=true)
        /// or vertical (bevel=false, ribbon caps — a joint with the next piece with
        /// no wedge). outward2 is the outer side of the edge in the (north, east)
        /// plane; the vertex order depends on it.
        /// </summary>
        private void AddWallQuad(
            SurfaceSite site, GroundSampler ground,
            List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector2 a, Vector2 b, Vector2 outward2, bool bevel)
        {
            float thickness = Mathf.Max(0f, ThicknessMeters);
            if (thickness <= 0.0001f)
            {
                return;
            }

            Vector2 shift = Vector2.zero;
            if (bevel && outward2.sqrMagnitude > 1e-8f)
            {
                // 30° is the player walk limit (MaxSlopeDegrees): any steeper and the
                // floor rejects the slope while the sweep passes through — you get buried.
                float angle = Mathf.Clamp(EdgeSlopeDegrees, 10f, 30f);
                float run = thickness / Mathf.Tan(angle * Mathf.Deg2Rad);
                shift = outward2.normalized * run;
            }

            float tile = Mathf.Max(0.1f, TileMeters);
            float topA = ground.LocalHeight(a.x, a.y, LiftMeters);
            float topB = ground.LocalHeight(b.x, b.y, LiftMeters);
            Vector2 bottomA = a + shift;
            Vector2 bottomB = b + shift;
            float length = Vector2.Distance(a, b);
            float u = Mathf.Max(1e-4f, length / tile);
            float v = Mathf.Max(1e-4f, (Mathf.Sqrt((shift.sqrMagnitude) + (thickness * thickness))) / tile);

            int ia = verts.Count;
            AddWallVertex(site, verts, uvs, a.x, topA, a.y, 0f, 0f);
            int ib = verts.Count;
            AddWallVertex(site, verts, uvs, b.x, topB, b.y, u, 0f);
            int ida = verts.Count;
            AddWallVertex(site, verts, uvs, bottomA.x, topA - thickness, bottomA.y, 0f, v);
            int idb = verts.Count;
            AddWallVertex(site, verts, uvs, bottomB.x, topB - thickness, bottomB.y, u, v);

            Vector2 d = b - a;
            float cross = (d.x * outward2.y) - (d.y * outward2.x);
            if (cross > 0f)
            {
                AddTriangle(tris, ia, ida, ib);
                AddTriangle(tris, ib, ida, idb);
            }
            else
            {
                AddTriangle(tris, ia, ib, ida);
                AddTriangle(tris, ib, idb, ida);
            }
        }

        private void AddWallVertex(
            SurfaceSite site, List<Vector3> verts, List<Vector2> uvs,
            float north, float y, float east, float u, float v)
        {
            Vector3 world = site.transform.TransformPoint(new Vector3(north, y, east));
            verts.Add(transform.InverseTransformPoint(world));
            uvs.Add(new Vector2(u, v));
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

        /// <summary>Triangulation of a simple polygon by ear clipping. Output: index triples, counter-clockwise.</summary>
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

                    // Convex vertex: a left turn (counter-clockwise).
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

        // ---------- Patch for decor exclusion ----------

        /// <summary>
        /// Road patch rectangles in site axes (north/east, m) — for the decor
        /// exclusion table. Ribbon — a corridor along the smoothed path in ~12 m
        /// chunks; Polygon — the contour bounds. Includes the visible edge bevel.
        /// </summary>
        public void CollectExclusionRects(List<RoadExclusionRect> output)
        {
            if (output == null)
            {
                return;
            }

            SurfaceSite site = GetComponentInParent<SurfaceSite>();
            if (site == null || Points == null || Points.Count == 0)
            {
                return;
            }

            List<Vector2> control = ControlPoints2D(site);
            if (control.Count == 0)
            {
                return;
            }

            float bevel = VisibleBevelMeters();
            if (Shape == RoadShape.Polygon)
            {
                float minN = float.MaxValue;
                float maxN = float.MinValue;
                float minE = float.MaxValue;
                float maxE = float.MinValue;
                for (int i = 0; i < control.Count; i++)
                {
                    minN = Mathf.Min(minN, control[i].x);
                    maxN = Mathf.Max(maxN, control[i].x);
                    minE = Mathf.Min(minE, control[i].y);
                    maxE = Mathf.Max(maxE, control[i].y);
                }

                if ((maxN - minN) < 0.5f && (maxE - minE) < 0.5f)
                {
                    return;
                }

                output.Add(new RoadExclusionRect
                {
                    CenterNorth = (minN + maxN) * 0.5f,
                    CenterEast = (minE + maxE) * 0.5f,
                    AxisXNorth = 1f,
                    AxisXEast = 0f,
                    AxisZNorth = 0f,
                    AxisZEast = 1f,
                    HalfX = ((maxN - minN) * 0.5f) + bevel,
                    HalfZ = ((maxE - minE) * 0.5f) + bevel
                });
                return;
            }

            if (control.Count < 2)
            {
                return;
            }

            List<Vector2> path = Resample(control, Smooth, 10f);
            if (path.Count < 2)
            {
                return;
            }

            float half = Mathf.Max(0.25f, WidthMeters) * 0.5f;
            const float chunkMeters = 12f;
            int start = 0;
            float acc = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                acc += Vector2.Distance(path[i - 1], path[i]);
                if (acc < chunkMeters && i < path.Count - 1)
                {
                    continue;
                }

                Vector2 a = path[start];
                Vector2 b = path[i];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-6f)
                {
                    start = i;
                    acc = 0f;
                    continue;
                }

                // Quantize the angle (as in build zones): even with an unaligned
                // transform, float jitter must not change the zone table — changing it
                // resets the terrain chunk cache.
                double yaw = System.Math.Atan2(d.y, d.x);
                yaw = System.Math.Round(yaw * (180d / System.Math.PI) * 10d) / 10d * (System.Math.PI / 180d);
                Vector2 dir = new Vector2((float)System.Math.Cos(yaw), (float)System.Math.Sin(yaw));
                Vector2 side = new Vector2(dir.y, -dir.x);
                output.Add(new RoadExclusionRect
                {
                    CenterNorth = (a.x + b.x) * 0.5f,
                    CenterEast = (a.y + b.y) * 0.5f,
                    AxisXNorth = dir.x,
                    AxisXEast = dir.y,
                    AxisZNorth = side.x,
                    AxisZEast = side.y,
                    HalfX = (d.magnitude * 0.5f) + 1f,
                    HalfZ = half + bevel + 0.25f
                });

                start = i;
                acc = 0f;
            }
        }

        /// <summary>Visible width of the edge bevel above the ground, m (road patch margin).</summary>
        private float VisibleBevelMeters()
        {
            if (ThicknessMeters <= 0.0001f)
            {
                return 0f;
            }

            float angle = Mathf.Clamp(EdgeSlopeDegrees, 10f, 30f);
            float run = ThicknessMeters / Mathf.Tan(angle * Mathf.Deg2Rad);
            return run * Mathf.Clamp01(LiftMeters / ThicknessMeters);
        }

        // ---------- Ground height ----------

        /// <summary>
        /// Ground height at a site point (north, east) in site local axes. The formula
        /// is the same as in SurfaceGrounded.Apply, so the road and the buildings sit
        /// on the same ground.
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
