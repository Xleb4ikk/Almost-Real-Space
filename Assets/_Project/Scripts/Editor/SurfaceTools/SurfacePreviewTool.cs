using System;
using Galilego.Core;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Surface preview in the scene: lives under SurfaceFrame and shows the
    /// terrain around the spawn point right in the Scene view, without Play mode.
    /// Needed to see the planet and place bases: the scene has no terrain because
    /// it is generated at runtime (PlanetSurfaceRenderer.Start), and body
    /// coordinates (2.6e10 m) do not fit the scene's float32.
    ///
    /// The preview object is transient: HideFlags.DontSave, never written to the
    /// scene and never included in builds. It samples the same noise as the game,
    /// so the patch underfoot matches in the scene and in Play.
    ///
    /// The patch size is FIXED and deliberately independent of the SceneView
    /// camera: the horizon must be visible from the surface, while the author's
    /// camera can be anywhere (35 km away, underground, orthographic) and would
    /// otherwise drag the scale along with it.
    /// </summary>
    [InitializeOnLoad]
    public static class SurfacePreviewTool
    {
        public const string PreviewName = "__SurfacePreview";

        /// <summary>Number of rings in the radial grid. 13 gives a 2^12 = 4096:1 radius range.</summary>
        private const int Rings = 13;

        /// <summary>Visible radius the preview rebuilds at before framing, m.</summary>
        private const double FocusVisibleRadius = 100d;

        /// <summary>
        /// Preview radius in the scene, m. A 5 km patch carries rings from 1.2 m
        /// at the frame up to 5 km at the horizon: the clipmap allows it, and the
        /// author needs both base-scale detail and the horizon.
        /// </summary>
        public static double PreviewRadiusMeters = 5000d;

        private static double lastBuildTime;
        private static double lastDistance = -1d;
        private static long lastSignature;
        private static Mesh previewMesh;
        private static Material previewMaterial;
        private static SurfaceFrame boundFrame;

        /// <summary>Draw the preview at all (toggle in the frame's inspector).</summary>
        public static bool Enabled = true;

        // Where the player stood in the last Play session: "take player point"
        // must work after exiting Play. Stored in EditorPrefs, not statics, because
        // exiting Play reloads the domain and resets static fields.
        private const string PlayerLatKey = "Galilego.SurfaceFrame.LastPlayerLatitude";
        private const string PlayerLonKey = "Galilego.SurfaceFrame.LastPlayerLongitude";
        private const string PlayerBodyKey = "Galilego.SurfaceFrame.LastPlayerBody";

        /// <summary>Whether a player point is remembered.</summary>
        public static bool HasRememberedPlayer => EditorPrefs.HasKey(PlayerLatKey);

        public static double RememberedLatitude => EditorPrefs.GetFloat(PlayerLatKey, 0f);

        public static double RememberedLongitude => EditorPrefs.GetFloat(PlayerLonKey, 0f);

        public static string RememberedBody => EditorPrefs.GetString(PlayerBodyKey, string.Empty);

        private static void RememberPlayerPosition(string bodyName, double lat, double lon)
        {
            EditorPrefs.SetFloat(PlayerLatKey, (float)lat);
            EditorPrefs.SetFloat(PlayerLonKey, (float)lon);
            EditorPrefs.SetString(PlayerBodyKey, bodyName ?? string.Empty);
        }

        /// <summary>
        /// Records the player position into EditorPrefs once per 0.5 s from
        /// EditorApplication.update (which also runs in Play).
        /// </summary>
        private static void RecordPlayerPosition()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now - lastRecordTime < 0.5d)
            {
                return;
            }

            lastRecordTime = now;
            var runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null || runner.SystemState == null || runner.DominantBody == null)
            {
                return;
            }

            var body = runner.DominantBody;
            body.SurfaceLatLonAt(runner.PlayerPosition, runner.TimeSeconds, out double lat, out double lon);
            RememberPlayerPosition(body.Name, lat, lon);
        }

        private static double lastRecordTime = -1d;

        static SurfacePreviewTool()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;

            // Invalidate the assembled-system cache on ANY hierarchy change:
            // the signature (hash of blueprint fields) catches value edits but not
            // when the cache was first built, so a partial first build could
            // otherwise stick forever.
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static void OnHierarchyChanged()
        {
            SurfaceSceneSystem.Invalidate();
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // No preview in Play: PlanetSurfaceRenderer draws the real terrain
            // and the service GO would be redundant.
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                DestroyPreview();
            }
        }

        /// <summary>
        /// Nothing is destroyed on domain reload: statics reset themselves and the
        /// service GO (HideFlags.DontSave) is removed by Unity; destroying it here
        /// would break an inspector that still references it.
        /// </summary>
        private static void Cleanup()
        {
            lastSignature = 0L;
            lastDistance = -1d;
            boundFrame = null;
        }

        private static void Tick()
        {
            RecordPlayerPosition();

            if (Application.isPlaying || !Enabled)
            {
                if (previewMesh != null)
                {
                    DestroyPreview();
                }

                return;
            }

            if (EditorApplication.timeSinceStartup - lastBuildTime < MinRebuildIntervalSeconds)
            {
                return;
            }

            SurfaceFrame frame = ResolveFrame();
            if (frame == null || !frame.IsUsable)
            {
                if (previewMesh != null)
                {
                    DestroyPreview();
                }

                boundFrame = null;
                lastSignature = 0L;
                lastDistance = -1d;
                return;
            }

            frame.Refresh();
            HeightfieldTerrain terrain = frame.Terrain;
            double radius = PreviewRadiusMeters;

            long signature = Signature(frame, terrain);
            bool resized = Math.Abs(radius - lastDistance) > 1e-6d;
            if (signature == lastSignature && !resized && boundFrame == frame && previewMesh != null)
            {
                return;
            }

            Rebuild(frame, terrain, radius);
            lastBuildTime = EditorApplication.timeSinceStartup;
            lastDistance = radius;
            lastSignature = signature;
            boundFrame = frame;
        }

        private const double MinRebuildIntervalSeconds = 0.2d;

        /// <summary>Wet water shoreline, defaults as in the scene.</summary>
        private const float ShoreWetMetersDefault = 3f;

        private static readonly Vector4 ShoreWetTintDefault = new Vector4(0.42f, 0.52f, 0.56f, 1f);

        private static void Rebuild(SurfaceFrame frame, HeightfieldTerrain terrain, double radius)
        {
            var grid = SurfacePreviewBuilder.GridForVisibleRadius(radius, Rings);
            Mesh mesh;
            try
            {
                mesh = SurfacePreviewBuilder.Build(
                    frame.BodyState, terrain,
                    KeplerMath.DegreesToRadians(frame.LatitudeDegrees),
                    KeplerMath.DegreesToRadians(frame.LongitudeDegrees),
                    frame.AnchorBodyFixed, frame.AltitudeMeters,
                    grid, true);
            }
            catch (Exception e)
            {
                Debug.LogError("[SurfacePreview] Сборка превью провалилась: " + e);
                return;
            }

            if (mesh == null)
            {
                return;
            }

            GameObject host = EnsureHost(frame);
            var filter = host.GetComponent<MeshFilter>();
            var renderer = host.GetComponent<MeshRenderer>();

            if (previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMesh);
            }

            previewMesh = mesh;
            filter.sharedMesh = mesh;

            // The preview uses a SIMPLIFIED shader, not the game's Galileo/PlanetSurface:
            // its globals are set by SkyEnvironment, which computes the atmosphere
            // integral and would block the editor from EditorApplication.update.
            // Base placement needs the terrain shape and object snapping
            // (SurfaceGrounded); the palette and textures come from the shared
            // PlanetSurfaceRenderer.ApplyTerrainGlobals.
            PlanetSurfaceRenderer.ApplyTerrainGlobals(terrain, ShoreWetMetersDefault, ShoreWetTintDefault);

            if (previewMaterial == null)
            {
                previewMaterial = SurfacePreviewBuilder.CreateFallbackMaterial();
                if (previewMaterial == null)
                {
                    Debug.LogError("[SurfacePreview] Шейдер Galilego/SurfacePreview не найден — превью без материала.");
                    return;
                }
            }

            ApplyGridSettings(previewMaterial, radius);
            previewMaterial.SetFloat("_PreviewSeaLevel", terrain != null ? (float)terrain.SeaLevelMeters : -1000000000f);
            renderer.sharedMaterial = previewMaterial;
            renderer.enabled = true;
        }

        /// <summary>
        /// Grid step for the scale: a 1-2-5 progression so the step changes
        /// smoothly while zooming instead of jumping 10 → 100 → 1000 m, plus a
        /// fine sub-grid inside each major cell.
        /// </summary>
        public static void ApplyGridSettings(Material material, double visibleRadius)
        {
            if (material == null)
            {
                return;
            }

            double km = Math.Max(0.005d, Math.Min(visibleRadius, 2000d) / 1000d);
            int power = (int)Math.Floor(Math.Log(km, 10d));
            double mantissa = Math.Pow(10d, Math.Log(km, 10d) - power);
            double[] steps = { 1d, 2d, 5d };
            double chosen = steps[0];
            foreach (double s in steps)
            {
                if (mantissa >= s)
                {
                    chosen = s;
                }
            }

            material.SetFloat("_PreviewGridSpacing", (float)(chosen * Math.Pow(10d, power)));
            material.SetFloat("_PreviewGridMinor", 0.2f);
            material.SetFloat("_PreviewGridStrength", 0.55f);
            material.SetFloat("_PreviewGridFade", (float)Math.Max(1.5d, visibleRadius * 2.5d / 1000d));
        }

        private static GameObject EnsureHost(SurfaceFrame frame)
        {
            Transform existing = frame.transform.Find(PreviewName);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var go = new GameObject(PreviewName);
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(frame.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>
        /// Shows the preview in the Scene view: rebuild narrow first (so pressing F
        /// does not fly past the horizon at 5 km), then frame it. Camera orientation
        /// is computed in FRAME AXES (local +Y is the point's zenith), not world
        /// axes: at the equator the world zenith is horizontal, and a world-rotated
        /// camera would look at the patch from behind, where Cull Back hides it.
        /// Selection is left alone: the service GO is DontSave; a stale selection
        /// would make the inspector throw MissingReference in OnEnable.
        /// </summary>
        public static bool FocusSceneView(SurfaceFrame frame)
        {
            if (frame == null || !frame.IsUsable)
            {
                Debug.LogWarning("Surface frame: фрейм не найден или тело не разрешилось.");
                return false;
            }

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null)
            {
                Debug.LogWarning("Surface frame: нет открытой Scene View.");
                return false;
            }

            double saved = PreviewRadiusMeters;
            PreviewRadiusMeters = FocusVisibleRadius;
            frame.Refresh();
            Rebuild(frame, frame.Terrain, FocusVisibleRadius);
            lastDistance = FocusVisibleRadius;
            lastSignature = Signature(frame, frame.Terrain);
            boundFrame = frame;
            lastBuildTime = EditorApplication.timeSinceStartup;
            PreviewRadiusMeters = saved;

            view.orthographic = false;

            // Frame INSTANTLY: Frame(bounds, false) animates, and the camera
            // settle would overwrite the rotation set below.
            Bounds bounds = new Bounds(frame.transform.position, Vector3.one * 250f);
            view.Frame(bounds, true);

            // Keep the zoom Frame picked for the patch: LookAt sets size itself and
            // would otherwise reset the framing.
            float framedSize = view.size;

            // Look at the patch from the frame's local zenith: 30° above the local
            // horizon, like standing on a slope and looking down-forward. A world
            // Euler(30, 0, 0) at the equator gives the rear view (see the method
            // comment), so the rotation is multiplied by the frame rotation.
            view.LookAt(
                bounds.center,
                frame.transform.rotation * Quaternion.Euler(30f, 0f, 0f),
                framedSize,
                false,
                true);
            view.Repaint();
            return true;
        }

        /// <summary>
        /// Signature of everything that changes the preview image. The terrain
        /// profile is not hashed in full (45 fields): shape is defined by seed,
        /// amplitude, frequency, octaves, masks and warp; there is a rebuild button
        /// for the rest.
        /// </summary>
        private static long Signature(SurfaceFrame frame, HeightfieldTerrain terrain)
        {
            unchecked
            {
                long h = 17L;
                h = (h * 31L) + (long)Math.Round(frame.LatitudeDegrees * 1e7d);
                h = (h * 31L) + (long)Math.Round(frame.LongitudeDegrees * 1e7d);
                h = (h * 31L) + (long)Math.Round(frame.AltitudeMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(frame.BodyRadius * 1e3d);
                h = (h * 31L) + frame.BodyName.GetHashCode();
                if (terrain == null)
                {
                    return h;
                }

                h = (h * 31L) + terrain.Seed;
                h = (h * 31L) + (long)Math.Round(terrain.AmplitudeMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.BaseFrequency * 1e4d);
                h = (h * 31L) + terrain.Octaves;
                h = (h * 31L) + (long)Math.Round(terrain.SeaLevelMeters * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.Lacunarity * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.Gain * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.RidgedMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ContinentFrequency * 1e4d);
                h = (h * 31L) + terrain.ContinentOctaves;
                h = (h * 31L) + (long)Math.Round(terrain.ContinentThreshold * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ContinentDepth * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.PlainMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.WarpStrength * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.DetailMix * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ColorNoiseStrength * 1e4d);
                h = (h * 31L) + (long)Math.Round(terrain.ColorDetailStrength * 1e4d);
                return h;
            }
        }

        /// <summary>Frame the preview is bound to: the selected one, otherwise the first in the scene.</summary>
        public static SurfaceFrame ResolveFrame()
        {
            if (Selection.activeGameObject != null)
            {
                var selected = Selection.activeGameObject.GetComponentInParent<SurfaceFrame>();
                if (selected != null)
                {
                    return selected;
                }
            }

            SurfaceFrame[] all = UnityEngine.Object.FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include);
            return all.Length > 0 ? all[0] : null;
        }

        public static void DestroyPreview()
        {
            if (previewMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMesh);
                previewMesh = null;
            }

            if (previewMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(previewMaterial);
                previewMaterial = null;
            }

            foreach (SurfaceFrame frame in UnityEngine.Object.FindObjectsByType<SurfaceFrame>(FindObjectsInactive.Include))
            {
                Transform child = frame.transform.Find(PreviewName);
                if (child == null)
                {
                    continue;
                }

                // If the preview is selected, the inspector holds a reference and
                // DestroyImmediate would null it, making the next OnEnable throw
                // MissingReference: clear the selection first.
                if (Selection.activeGameObject == child.gameObject)
                {
                    Selection.activeGameObject = frame.gameObject;
                }

                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            lastSignature = 0L;
            lastDistance = -1d;
            boundFrame = null;
        }

        /// <summary>Mark the preview stale (after editing the terrain profile).</summary>
        public static void Invalidate()
        {
            lastSignature = 0L;
            lastDistance = -1d;
        }
    }
}
