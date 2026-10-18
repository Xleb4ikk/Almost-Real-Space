using System.Collections.Generic;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// LAUNCH PAD as a build zone: attach to the pad object (a SurfaceRoad
    /// polygon) in the site prefab. Provides:
    ///   • the footprint polygon (site axes: x — north, z — east) for bounds checks;
    ///   • pad top height at a point (ray into the road's MeshCollider);
    ///   • site ↔ render ↔ double world (astro) conversion with per-frame velocity;
    ///   • vessel spawn point (footprint center) and player spawn (next to it).
    ///
    /// Polygon points are read from the SurfaceRoad itself (single source of
    /// truth), so editing the pad shape in the editor automatically edits the zone.
    /// In Play the site pose is updated by SurfaceSite each frame — the zone just
    /// rides its hierarchy (the pad's local coordinates don't change).
    /// </summary>
    [DefaultExecutionOrder(-120)]
    public sealed class LaunchPadZone : MonoBehaviour
    {
        [Tooltip("Дорога-плита (SurfaceRoad с Shape = Polygon). Пусто — берётся на этом же объекте.")]
        public SurfaceRoad Pad;

        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам при старте.")]
        public SimulationRunner Runner;

        [Tooltip("Смещение игрока от центра площадки вдоль востока, м (спавн рядом с аппаратом).")]
        public float PlayerSpawnEastOffset = 10f;

        /// <summary>Pad polygon in site axes: x — north, y — east.</summary>
        private readonly List<Vector2> polygon = new List<Vector2>();

        /// <summary>Pad polygon (x — north, y — east). Empty until ready.</summary>
        public IReadOnlyList<Vector2> Polygon => polygon;

        /// <summary>Footprint center in site axes (XZ; Y — pad top).</summary>
        public Vector3 CenterLocal { get; private set; }

        /// <summary>Vessel placement point (site axes).</summary>
        public Vector3 VesselSpawnLocal { get; private set; }

        /// <summary>Player spawn point (site axes).</summary>
        public Vector3 PlayerSpawnLocal { get; private set; }

        /// <summary>Everything collected: the site body resolved, polygon and heights computed.</summary>
        public bool Ready { get; private set; }

        /// <summary>Pad collider (the road's MeshCollider) — placement rays use it.</summary>
        public Collider PadCollider => padCollider;

        private SurfaceSite site;
        private Collider padCollider;
        private OrbitingBody body;

        /// <summary>Ready to work; while not ready, Update calls Refresh itself.</summary>
        private void Update()
        {
            if (!Ready)
            {
                Refresh();
            }
        }

        /// <summary>Collect the polygon, pad collider and spawn points.</summary>
        [ContextMenu("Refresh build zone")]
        public void Refresh()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Pad == null)
            {
                Pad = GetComponent<SurfaceRoad>();
            }

            if (site == null)
            {
                site = GetComponentInParent<SurfaceSite>();
            }

            if (Pad == null || site == null || Runner == null || Runner.SystemState == null)
            {
                return;
            }

            // The site's live body: in Play SurfaceSite resolves it on the first frame.
            body = site.BodyState;
            if (body == null)
            {
                return;
            }

            // The pad must be built (its mesh and collider) and the site pose
            // meaningful: for the first frames the site may sit at astro
            // coordinates (the render anchor isn't set yet), and the ray height
            // there is garbage in the kilometers. Wait and retry.
            if (!Pad.IsBuilt)
            {
                return;
            }

            if (site.transform.position.magnitude > 1e6f)
            {
                return;
            }

            // Polygon: road points in site-local axes (x — north, z — east).
            polygon.Clear();
            List<Vector3> points = Pad.Points;
            if (points == null || points.Count < 3)
            {
                return;
            }

            float minN = float.MaxValue, maxN = float.MinValue;
            float minE = float.MaxValue, maxE = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var p = new Vector2(points[i].x, points[i].z);
                polygon.Add(p);
                minN = Mathf.Min(minN, p.x);
                maxN = Mathf.Max(maxN, p.x);
                minE = Mathf.Min(minE, p.y);
                maxE = Mathf.Max(maxE, p.y);
            }

            padCollider = GetComponent<Collider>();
            var center = new Vector2((minN + maxN) * 0.5f, (minE + maxE) * 0.5f);
            if (!TryPadTopYAt(center, out float centerY))
            {
                return;
            }

            var playerPoint = new Vector2(center.x, center.y + PlayerSpawnEastOffset);
            if (!TryPadTopYAt(playerPoint, out float playerY))
            {
                return;
            }

            CenterLocal = new Vector3(center.x, centerY, center.y);
            VesselSpawnLocal = CenterLocal;
            PlayerSpawnLocal = new Vector3(playerPoint.x, playerY, playerPoint.y);
            Ready = true;
        }

        /// <summary>
        /// Height of the site's TOP surface at a point: the topmost SITE
        /// collider (the pad OR the launch table — the table model stands on the
        /// pad and above it; spawn/pinning must land on the top or the vessel
        /// appears inside the table colliders and PhysX throws it out violently).
        /// </summary>
        private bool TryPadTopYAt(Vector2 northEast, out float y)
        {
            y = 0f;
            Vector3 origin = transform.TransformPoint(new Vector3(northEast.x, 60f, northEast.y));
            RaycastHit[] hits = Physics.RaycastAll(
                new Ray(origin, -transform.up), 200f, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                if (!IsSiteCollider(hits[i].collider))
                {
                    continue;
                }

                if (hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    best = i;
                }
            }

            if (best < 0)
            {
                return false;
            }

            y = transform.InverseTransformPoint(hits[best].point).y;
            return true;
        }

        /// <summary>The collider belongs to the site (pad, table, buildings) — placement is allowed on it.</summary>
        public static bool IsSiteCollider(Collider collider)
        {
            return collider != null && collider.GetComponentInParent<SurfaceSite>() != null;
        }

        /// <summary>Site top-surface height at a site-axes point (pad or launch table).</summary>
        public float PadTopYAt(Vector2 northEast)
        {
            if (TryPadTopYAt(northEast, out float y))
            {
                return y;
            }

            // Nothing found (point outside the structures): the honest support is the terrain under it.
            if (body != null)
            {
                Vector3 render = transform.TransformPoint(new Vector3(northEast.x, 0f, northEast.y));
                Vector3d astro = FloatingOrigin.Anchor + AstroFrame.ToAstro(render);
                body.SurfaceLatLonAt(astro, Runner != null ? Runner.TimeSeconds : 0d, out double lat, out double lon);
                double ground = body.Terrain != null
                    ? body.Terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(lat), KeplerMath.DegreesToRadians(lon))
                    : 0d;
                body.EvaluateWorldState(Runner != null ? Runner.TimeSeconds : 0d, out Vector3d bodyPos, out _);
                Vector3d up = (astro - bodyPos).Normalized;
                Vector3d groundPoint = bodyPos + (up * (body.Radius + ground));
                return transform.InverseTransformPoint(FloatingOrigin.ToRender(groundPoint)).y;
            }

            return 0f;
        }

        // ─── Coordinate conversions ──────────────────────────────────────────────

        /// <summary>Site axes → render space (for rays and visuals).</summary>
        public Vector3 SiteToRender(Vector3 local)
        {
            return transform.TransformPoint(local);
        }

        /// <summary>Render space → site axes.</summary>
        public Vector3 RenderToSite(Vector3 render)
        {
            return transform.InverseTransformPoint(render);
        }

        /// <summary>Rotation of site axes in render space.</summary>
        public Quaternion SiteRenderRotation => transform.rotation;

        /// <summary>
        /// Site axes → double world (astro frame) with the site velocity at the point.
        /// Precision: the body-fixed offset uses a small quaternion (fractions of
        /// a millimeter across the pad), on top of the exact site point from the core.
        /// </summary>
        public void SiteToAstro(Vector3 local, out Vector3d astro, out Vector3d velocity)
        {
            SiteToAstroAt(local, Runner != null ? Runner.TimeSeconds : 0d, out astro, out velocity);
        }

        /// <summary>
        /// Same, but at a given time. Matters for pinning the vessel in the builder:
        /// the time must match the player's frame time (otherwise, with a fast
        /// planet spin, the vessel lags the pad by surface_speed·dt).
        /// </summary>
        public void SiteToAstroAt(Vector3 local, double timeSeconds, out Vector3d astro, out Vector3d velocity)
        {
            double t = timeSeconds;
            double lat = site.LatitudeDegrees;
            double lon = site.LongitudeDegrees;
            double latRad = KeplerMath.DegreesToRadians(lat);
            double lonRad = KeplerMath.DegreesToRadians(lon);

            body.GetSurfaceState(lat, lon, site.AltitudeMeters, t, out Vector3d origin, out _);

            QuaternionD orientation = body.GetVisualOrientation(t);
            QuaternionD simRotation = SurfaceFrameMath.SimulationRotation(orientation, latRad, lonRad);
            Vector3 renderOffset = AstroFrame.SimulationQuaternion(simRotation) * local;
            astro = origin + AstroFrame.ToAstro(renderOffset);

            body.EvaluateWorldState(t, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d omega = body.SpinAxis * body.SpinAngularSpeed;
            velocity = bodyVel + Vector3d.Cross(omega, astro - bodyPos);
        }

        private void OnDrawGizmosSelected()
        {
            SurfaceRoad road = Pad != null ? Pad : GetComponent<SurfaceRoad>();
            if (road == null || road.Points == null || road.Points.Count < 3)
            {
                return;
            }

            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            for (int i = 0; i < road.Points.Count; i++)
            {
                Vector3 a = road.Points[i];
                Vector3 b = road.Points[(i + 1) % road.Points.Count];
                Gizmos.DrawLine(transform.TransformPoint(a), transform.TransformPoint(b));
            }
        }
    }
}
