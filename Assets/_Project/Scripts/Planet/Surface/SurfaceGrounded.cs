using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Places an object ON the terrain. Without it the base cannot be built: in
    /// site local coordinates (metres) a building placed "by eye" either floats in
    /// the air or is half in the ground — and neither is visible in the scene,
    /// because the Scene view knows nothing of the terrain.
    ///
    /// Works in both the editor and Play, and refreshes itself on any change of the
    /// site position. It does three things:
    ///   • sets Y to the ground height at this point (plus HeightOffsetMeters);
    ///   • optionally tilts the object to the terrain normal (rock, ramp);
    ///   • shows in the inspector how many metres the object sags or floats —
    ///     to catch the ground seam by eye.
    ///
    /// Objects inside a site work without it too — they simply stand where placed.
    /// Snap is needed where exact ground contact matters.
    /// </summary>
    [ExecuteAlways]
    public sealed class SurfaceGrounded : MonoBehaviour
    {
        [Tooltip("Ставить объект на поверхность земли.")]
        public bool SnapToGround = true;

        [Tooltip("Доворачивать объект по нормали рельефа (для скал и наклонных площадок). Нормаль — та же, что у посадки корабля: усреднение примерно по 114 м. Поэтому мелкие детали рельефа не дёргают объект, а база садится ровно так же, как сядет шлюпка.")]
        public bool AlignToNormal;

        [Tooltip("Смещение над землёй после snap, м. Отрицательное — фундамент в землю.")]
        public double HeightOffsetMeters;

        [Tooltip("Сохранять рыскание объекта при выравнивании по нормали (иначе объект доворачивается по нормали целиком).")]
        public bool KeepYaw = true;

        [Tooltip("Пересчитывать при изменении позиции места (снять галку только для объектов, которые двигают вручную).")]
        public bool AutoUpdate = true;

        /// <summary>Site whose axes we compute in. Filled automatically.</summary>
        private SurfaceSite site;

        /// <summary>How many metres the object is above/below the ground right now, m.</summary>
        public double CurrentGapMeters { get; private set; }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            if (AutoUpdate)
            {
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (AutoUpdate && Application.isPlaying)
            {
                Apply();
            }
        }

        /// <summary>Find the site the object belongs to and place the object on the ground.</summary>
        public void Apply()
        {
            if (site == null)
            {
                site = GetComponentInParent<SurfaceSite>();
            }

            if (site == null || !site.isActiveAndEnabled)
            {
                return;
            }

            OrbitingBody body = site.BodyState;
            if (body == null)
            {
                return;
            }

            double time = Application.isPlaying && site.Runner != null && site.Runner.SystemState != null
                ? site.Runner.TimeSeconds
                : 0d;

            // Object position in SITE axes (there X north, Y zenith, Z east).
            Vector3 local = transform.localPosition;
            double east = local.z;
            double north = local.x;

            double latRad = KeplerMath.DegreesToRadians(site.LatitudeDegrees);
            double lonRad = KeplerMath.DegreesToRadians(site.LongitudeDegrees);

            Vector3d direction = SurfaceFrameMath.DirectionOffset(
                latRad, lonRad, body.Radius, east, north);
            double ground = SurfaceSceneSystem.GroundHeight(body, site.LatitudeDegrees, site.LongitudeDegrees);
            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain != null)
            {
                // Height at the object point, not the site point: on a slope the
                // difference between the base centre and its edge is tens of metres.
                // IMPORTANT: SampleHeight expects a UNIT direction on the sphere
                // (same convention as CubeSphere and the terrain). Passing a point
                // of radius R samples the noise at a different sphere point.
                ground = TerrainNoise.SampleHeight(
                    TerrainNoiseParams.FromTerrain(terrain),
                    new Unity.Mathematics.double3(direction.X, direction.Y, direction.Z)) * terrain.AmplitudeMeters;
            }

            double wanted = ground + HeightOffsetMeters - site.AltitudeMeters;
            CurrentGapMeters = local.y - wanted;

            if (SnapToGround)
            {
                Vector3 updated = local;
                updated.y = (float)wanted;
                transform.localPosition = updated;
            }

            if (AlignToNormal && terrain != null)
            {
                AlignToTerrainNormal(body, direction, time, ground);
            }
        }

        private void AlignToTerrainNormal(OrbitingBody body, Vector3d direction, double time, double ground)
        {
            // GetOutwardNormal expects a POINT of radius R (not a unit direction)
            // and averages the slope over roughly 114 m. That is exactly the normal
            // the ship lands on, so base and lander get the same tilt and the seam
            // does not diverge.
            Vector3d bodyFixed = direction * (body.Radius + ground);
            Vector3d normalAstro = SurfaceFrameMath.Up(
                KeplerMath.DegreesToRadians(site.LatitudeDegrees),
                KeplerMath.DegreesToRadians(site.LongitudeDegrees));
            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain != null)
            {
                normalAstro = terrain.GetOutwardNormal(body, bodyFixed, time);
            }

            Vector3d local = SurfaceFrameMath.ToLocal(
                KeplerMath.DegreesToRadians(site.LatitudeDegrees),
                KeplerMath.DegreesToRadians(site.LongitudeDegrees),
                normalAstro.Normalized);
            Vector3 target = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            if (target.sqrMagnitude <= 1e-9f)
            {
                return;
            }

            target.Normalize();
            // Rotation from zenith to the normal, expressed in SITE axes.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, target);
            Quaternion yaw = KeepYaw
                ? Quaternion.Euler(0f, transform.localEulerAngles.y, 0f)
                : Quaternion.identity;
            transform.localRotation = tilt * yaw;
        }

        /// <summary>Refresh all site descendants — after a point or terrain change.</summary>
        public static void RefreshAll(Transform siteRoot)
        {
            if (siteRoot == null)
            {
                return;
            }

            SurfaceGrounded[] all = siteRoot.GetComponentsInChildren<SurfaceGrounded>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].site = all[i].GetComponentInParent<SurfaceSite>();
                all[i].Apply();
            }
        }
    }
}
