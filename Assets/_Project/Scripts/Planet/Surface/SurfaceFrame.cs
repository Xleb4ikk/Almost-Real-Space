using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Local map frame on a body surface: a container for authoring the base.
    /// Places its GameObject at the scene ORIGIN and orients it along local ENU
    /// axes (+X north, +Y zenith, +Z east), so inside the frame ordinary Unity
    /// coordinates are kilometre-scale with millimetre precision.
    ///
    /// Why: the body sits at a semi-major axis of 2.6e10 m, where float32 ulp is
    /// about 2048 m: neither the spawn point nor the base object can be stored in
    /// scene world coordinates. At runtime a floating origin works around this
    /// (anchor = player position); here the same trick applies, with the frame as
    /// the anchor.
    ///
    /// Latitude/longitude are the ONLY source of truth for the frame position.
    /// The transform is not serialized by meaning: it is derived each time from
    /// lat/lon + altitude, and may be anything in the scene (on entering Play the
    /// frame jumps by ~4 m — that is the player anchor, not an error).
    ///
    /// The frame's children are the base: ordinary GOs/prefabs with ordinary local
    /// transforms, assembled with regular scene tools. They need no update of
    /// their own: the frame moves itself and Unity multiplies the hierarchy.
    /// SurfacePlaced is only needed for objects OUTSIDE the frame.
    ///
    /// [ExecuteAlways] with the transform written only when the pose changes:
    /// otherwise Update in the editor would mark the scene dirty every frame.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-90)]
    [ExecuteAlways]
    public sealed class SurfaceFrame : MonoBehaviour
    {
        [Header("Тело")]
        [Tooltip("BodyAuthoring тела-поверхности. null = искать по BodyName.")]
        public BodyAuthoring Body;

        [Tooltip("Имя GameObject'а тела, если Body не назначен (ключ связи, как в BodyView).")]
        public string BodyName = "Terra";

        [Header("Точка на поверхности (body-fixed)")]
        [Tooltip("Широта, градусы. Единственный источник истины о положении фрейма.")]
        public double LatitudeDegrees;

        [Tooltip("Долгота от нулевого меридиана тела, градусы (body-fixed, как в GetSurfaceState).")]
        public double LongitudeDegrees;

        [Header("Высота")]
        [Tooltip("Привязать к поверхности: высота фрейма = рельеф в точке + смещение.")]
        public bool SnapToTerrain = true;

        [Tooltip("Высота фрейма над радиусом тела, м (к рельефу при SnapToTerrain, иначе — к опорной сфере).")]
        public double HeightOffsetMeters;

        [Header("Рантайм")]
        [Tooltip("SimulationRunner сцены. Нужен, чтобы фрейм ехал за якорем игрока; без него в Play стоит на месте.")]
        public SimulationRunner Runner;

        [Header("Отладка")]
        [Tooltip("Рисовать оси ENU и масштабные кольца в сцене.")]
        public bool DrawGizmos = true;

        [Tooltip("Радиусы колец-рулетки, м.")]
        public double[] ScaleRingsMeters = { 10d, 100d, 1000d, 10000d };

        /// <summary>Absolute astro position of the frame (double, astro frame) at the last refresh.</summary>
        public Vector3d AnchorAstro { get; private set; }

        /// <summary>Body-fixed frame position (without orbit and rotation) — for the terrain preview.</summary>
        public Vector3d AnchorBodyFixed { get; private set; }

        /// <summary>Live body the frame stands on; null if the system failed to build.</summary>
        public OrbitingBody BodyState { get; private set; }

        /// <summary>Live terrain of the frame's body; null = smooth sphere.</summary>
        public HeightfieldTerrain Terrain => BodyState?.Terrain as HeightfieldTerrain;

        /// <summary>Radius of the frame's body, m.</summary>
        public double BodyRadius => BodyState != null ? BodyState.Radius : 0d;

        /// <summary>Terrain height at the frame point, m (without offset).</summary>
        public double GroundHeightMeters => SurfaceSceneSystem.GroundHeight(BodyState, LatitudeDegrees, LongitudeDegrees);

        /// <summary>Frame altitude above the body radius, m — what goes into GetSurfaceState.</summary>
        public double AltitudeMeters =>
            SnapToTerrain ? (GroundHeightMeters + HeightOffsetMeters) : HeightOffsetMeters;

        private bool poseValid;
        private readonly int[] gizmoRingSegments = new int[12];

        /// <summary>
        /// Absolute astro position taken as the ZERO of editor local space. The
        /// active frame sets it; everything else (SurfacePlaced, preview) subtracts
        /// it from its own absolute position and gets kilometre coordinates near
        /// the frame instead of 2.6e10. Zero means "no frame" — then objects stay
        /// in astro coordinates, which is honestly visible in the inspector.
        /// </summary>
        public static Vector3d EditAnchorAstro { get; set; }

        /// <summary>
        /// "Editor world" rotation: the inverse of the active frame's rotation in
        /// astro orientation. In the editor it is applied to EVERYTHING near the
        /// frame (the frame itself, SurfacePlaced), so the active frame's axes
        /// (+X north, +Y zenith, +Z east) coincide with Unity world axes and the
        /// ground lies horizontally in the Scene view, as the player sees it.
        /// Without it the frame would get the planet's "honest" orientation in
        /// space (axis tilt, longitude, latitude), and the surface patch would
        /// stand on its side in the Scene view. Unused in Play: there the player
        /// camera is already aligned with the local zenith.
        /// </summary>
        public static Quaternion EditRotation { get; private set; } = Quaternion.identity;

        /// <summary>
        /// Frame that defines the editor zero: the first SurfaceFrame in the scene.
        /// Resolved with a 0.5 s cache — otherwise FindAnyObjectByType would fire
        /// on every Update of every SurfacePlaced in the scene.
        /// </summary>
        public static SurfaceFrame ActiveEditFrame()
        {
            if (Application.isPlaying)
            {
                return null;
            }

            if (cachedFrame != null
                && cachedFrame.IsUsable
                && Time.realtimeSinceStartup - cachedFrameTime < 0.5d)
            {
                return cachedFrame;
            }

            SurfaceFrame[] all = FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include);
            cachedFrame = all.Length > 0 ? all[0] : null;
            cachedFrameTime = Time.realtimeSinceStartup;
            if (cachedFrame == null)
            {
                EditAnchorAstro = Vector3d.Zero;
                EditRotation = Quaternion.identity;
            }

            return cachedFrame;
        }

        private static SurfaceFrame cachedFrame;
        private static float cachedFrameTime;

        /// <summary>Frame is usable for the preview and children: active and body resolved.</summary>
        public bool IsUsable => isActiveAndEnabled && BodyState != null;

        private void OnEnable()
        {
            poseValid = false;
            Refresh();
        }

        private void OnValidate()
        {
            poseValid = false;
        }

        private void Update()
        {
            // In Play the pose is taken in LateUpdate: the player anchor and time
            // are set by SimulationRunner.Update (order 0), while the frame is
            // declared with -90, so in Update it would read the PREVIOUS frame's
            // data. At low speed that is centimetres, under time warp tens of
            // metres. Same as in SurfacePlaced.
            if (!Application.isPlaying)
            {
                Refresh();
            }
        }

        private void LateUpdate()
        {
            if (Application.isPlaying)
            {
                Refresh();
            }
        }

        /// <summary>Force a pose refresh (after editing the terrain profile in the inspector).</summary>
        [ContextMenu("Refresh frame")]
        public void Refresh()
        {
            if (!TryResolve(out OrbitingBody body))
            {
                BodyState = null;
                return;
            }

            BodyState = body;

            // Time: in Play the system time (the planet rotates, the frame must
            // move with it), in the editor zero — otherwise Update would move the
            // frame every frame by 1.9 m/s of Terra's own rotation.
            double time = Application.isPlaying && Runner != null && Runner.SystemState != null
                ? Runner.TimeSeconds
                : 0d;

            double lat = KeplerMath.DegreesToRadians(LatitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(LongitudeDegrees);
            Vector3d bodyFixed = SurfaceFrameMath.Up(lat, lon) * (body.Radius + AltitudeMeters);
            AnchorBodyFixed = bodyFixed;

            QuaternionD orientation = body.GetVisualOrientation(time);

            // The frame orientation is built directly in sim axes: every image of a
            // local axis gets the astro→sim permutation (SimulationRotation), else
            // the AstroFrame bridge conjugation swapped zenith into east.
            QuaternionD renderRotation = SurfaceFrameMath.SimulationRotation(orientation, lat, lon);

            if (Application.isPlaying)
            {
                // Runtime: the point in the absolute astro world (floating origin),
                // the frame stands at ToRender from it — near the player, not at 2.6e10.
                body.GetSurfaceState(LatitudeDegrees, LongitudeDegrees, AltitudeMeters, time,
                    out Vector3d surfacePos, out _);
                AnchorAstro = surfacePos;
                ApplyPose(
                    FloatingOrigin.ToRender(surfacePos),
                    AstroFrame.SimulationQuaternion(renderRotation));
                return;
            }

            // Editor: the first frame in the scene becomes the zero of local space.
            // Any other frames stand not at zero but at their true offset from it —
            // so local coordinates are shared across the scene and nothing jumps
            // when selecting in the hierarchy.
            body.EvaluateWorldState(time, out Vector3d bodyPos, out _);
            AnchorAstro = bodyPos + orientation.Rotate(bodyFixed);
            Quaternion simRotation = AstroFrame.SimulationQuaternion(renderRotation);
            if (ReferenceEquals(ActiveEditFrame(), this))
            {
                EditAnchorAstro = AnchorAstro;

                // Active frame = scene world axes: position zero, rotation identity
                // (exactly, not q⁻¹·q ≈ 1: otherwise float noise would jitter the
                // transform and mark the scene dirty).
                EditRotation = Quaternion.Inverse(simRotation);
                ApplyPose(Vector3.zero, Quaternion.identity);
                return;
            }

            ApplyPose(
                EditRotation * AstroFrame.ToSimulation(AnchorAstro - EditAnchorAstro),
                EditRotation * simRotation);
        }

        /// <summary>
        /// Surface point position in render space. The SINGLE point for the frame,
        /// SurfacePlaced and the preview: one math means one discrepancy is
        /// impossible. In Play from the floating origin (near the player), in the
        /// editor from the frame (scene zero), so coordinates are kilometre-scale.
        /// </summary>
        public static Vector3 RenderPosition(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees,
            double altitudeMeters, double timeSeconds, out Vector3d surfaceAstro)
        {
            body.GetSurfaceState(latitudeDegrees, longitudeDegrees, altitudeMeters, timeSeconds,
                out surfaceAstro, out _);
            return Application.isPlaying
                ? FloatingOrigin.ToRender(surfaceAstro)
                : EditRotation * AstroFrame.ToSimulation(surfaceAstro - EditAnchorAstro);
        }

        /// <summary>
        /// Pose of a surface point, EXPLICITLY anchored to the scene zero — unlike
        /// RenderPosition, which in the editor computes from the active SurfaceFrame.
        ///
        /// Needed by sites (SurfaceSite). A site is an editor working copy of one
        /// point: it must always stand at zero, otherwise lat/lon far from the scene
        /// frame give kilometre coordinates. At R = 1143 km the chord between points
        /// reaches 2.29e6 m, and float32 at that scale has a step of 0.25 m — at a
        /// distance of thousands of kilometres the step exceeds metres, and the base
        /// "falls apart" in the Scene view. The scene zero removes the issue: site
        /// local coordinates are always metres.
        ///
        /// In Play it behaves like RenderPosition — it follows the player anchor.
        /// </summary>
        public static void OriginAnchoredPose(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees,
            double altitudeMeters, double timeSeconds, out Vector3 position, out Quaternion rotation)
        {
            body.GetSurfaceState(latitudeDegrees, longitudeDegrees, altitudeMeters, timeSeconds,
                out Vector3d surfaceAstro, out _);

            // In the editor the site stands at zero in world axes (+X north, +Y
            // zenith, +Z east): it is a working copy of one point, and the base is
            // only convenient to build on horizontal ground. In Play the true
            // orientation, as the player has.
            rotation = Application.isPlaying
                ? RenderRotation(body, latitudeDegrees, longitudeDegrees, timeSeconds)
                : Quaternion.identity;
            position = Application.isPlaying
                ? FloatingOrigin.ToRender(surfaceAstro)
                : Vector3.zero;
        }

        /// <summary>Orientation of the surface point's local ENU axes in render space.</summary>
        public static Quaternion RenderRotation(
            OrbitingBody body, double latitudeDegrees, double longitudeDegrees, double timeSeconds)
        {
            QuaternionD orientation = SurfaceFrameMath.SimulationRotation(
                body.GetVisualOrientation(timeSeconds),
                KeplerMath.DegreesToRadians(latitudeDegrees),
                KeplerMath.DegreesToRadians(longitudeDegrees));
            Quaternion simRotation = AstroFrame.SimulationQuaternion(orientation);
            return Application.isPlaying ? simRotation : EditRotation * simRotation;
        }

        /// <summary>Body-fixed position of the surface point (without orbit and rotation) — preview input.</summary>
        public static Vector3d BodyFixedPoint(OrbitingBody body, double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
        {
            return SurfaceFrameMath.Up(
                KeplerMath.DegreesToRadians(latitudeDegrees),
                KeplerMath.DegreesToRadians(longitudeDegrees)) * (body.Radius + altitudeMeters);
        }

        private bool TryResolve(out OrbitingBody body)
        {
            HeightfieldTerrain ignored;

            // In Play the body comes from the runner's LIVE system: it is already
            // built and exact. The SurfaceSceneSystem editor cache is not needed
            // there and only gets in the way (in Play it is built from a scene that
            // is still loading, and the cache may stay incomplete).
            if (Application.isPlaying && Runner != null && Runner.SystemState != null)
            {
                string wanted = Body != null ? Body.gameObject.name : BodyName;
                foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
                {
                    if (candidate.Name == wanted)
                    {
                        body = candidate;
                        return true;
                    }
                }

                body = null;
                return false;
            }

            if (Body != null)
            {
                BodyName = Body.gameObject.name;
                return SurfaceSceneSystem.TryResolve(Body, out body, out ignored);
            }

            return SurfaceSceneSystem.TryResolve(BodyName, out body, out ignored);
        }

        private void ApplyPose(Vector3 position, Quaternion rotation)
        {
            if (Application.isPlaying)
            {
                // Every frame: the player anchor moves, so the frame moves too.
                transform.SetPositionAndRotation(position, rotation);
                poseValid = true;
                return;
            }

            if (poseValid
                && (transform.position - position).sqrMagnitude <= 1e-12f
                && Quaternion.Angle(transform.rotation, rotation) <= 1e-3f)
            {
                return;
            }

            transform.SetPositionAndRotation(position, rotation);
            poseValid = true;
        }

        private void OnDrawGizmos()
        {
            if (!DrawGizmos || (!Application.isPlaying && BodyState == null))
            {
                return;
            }

            // Local axes = frame ENU, drawn in its space.
            const float axis = 12f;
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(axis, 0f, 0f));
            Gizmos.color = new Color(0.4f, 0.6f, 1f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, axis, 0f));
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            Gizmos.DrawLine(Vector3.zero, new Vector3(0f, 0f, axis));
            if (ScaleRingsMeters == null)
            {
                return;
            }

            // Tape-measure rings: "centimetre, metre, hundred, kilometre" in local
            // units. They read as scale and serve as a fallback: if the preview did
            // not build or the camera is away, the rings still show where the frame
            // is and how big it is.
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            for (int i = 0; i < ScaleRingsMeters.Length; i++)
            {
                float r = (float)ScaleRingsMeters[i];
                if (r <= 0f || r > 1e7f)
                {
                    continue;
                }

                int segments = RingSegments(r);
                Vector3 prev = new Vector3(r, 0f, 0f);
                for (int s = 1; s <= segments; s++)
                {
                    float a = (s / (float)segments) * Mathf.PI * 2f;
                    Vector3 next = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    Gizmos.DrawLine(prev, next);
                    prev = next;
                }
            }
        }

        private int RingSegments(float radius)
        {
            int slot = Mathf.Clamp(Mathf.FloorToInt(Mathf.Log(radius) / Mathf.Log(10f)) + 11, 0, gizmoRingSegments.Length - 1);
            if (gizmoRingSegments[slot] == 0)
            {
                // ~4 m per segment, but no less than 64 and no more than 512.
                gizmoRingSegments[slot] = Mathf.Clamp(Mathf.CeilToInt(radius / 4f), 64, 512);
            }

            return gizmoRingSegments[slot];
        }
    }
}
