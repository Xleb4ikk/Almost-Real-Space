using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Celestial body render (B2). Attach to the same GameObject as BodyAuthoring.
    /// Positions are rendered relative to the floating origin (FloatingOrigin.Anchor),
    /// computed in double and converted once on output, so Unity coordinates stay small.
    /// Once per frame: StarSystem.GetVisualFrame → position/velocity + body-to-world
    /// orientation → AstroFrame bridge → transform.localPosition/localRotation.
    /// Runs in LateUpdate AFTER SimulationRunner.Update.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-100)]
    public sealed class BodyView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        /// <summary>Body index in the system (resolved at start by name).</summary>
        public OrbitingBody Body { get; private set; }

        /// <summary>
        /// Common root of the flat body hierarchy (at the scene origin). The static
        /// hierarchy is only for AUTHORING the blueprint; at runtime all BodyViews
        /// move under this root so localPosition == worldPosition.
        /// </summary>
        private static Transform flatRoot;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            // The body is resolved by GameObject name, matching the blueprint name.
            foreach (OrbitingBody body in Runner.SystemState.AllBodies)
            {
                if (body.Name == gameObject.name)
                {
                    Body = body;
                    break;
                }
            }

            if (Body == null)
            {
                Debug.LogWarning("BodyView: тело \"" + gameObject.name + "\" не найдено в системе — вид отключён.");
                enabled = false;
                return;
            }

            if (flatRoot == null)
            {
                flatRoot = new GameObject("BodyViewsRoot").transform;
            }

            // The scene hierarchy is only organisational: a moon is authored as a child of
            // its planet, and every body's scale in the scene is its DIAMETER (the unit
            // sphere mesh × scale), not a fraction of the parent. Reparenting with
            // worldPositionStays=true keeps the WORLD scale, which multiplies the moon by
            // the planet's radius (Артемида: 824 000 × Terra: 2 286 000 ≈ 1.9e12). Its
            // SphereCollider follows that scale: an invisible sphere whose surface is a
            // billion kilometres away, so every PhysX body near the site is "deep inside"
            // it and gets depenetrated at maxDepenetrationVelocity — the vessel jumps off
            // the pad at ~10 m/s. Keep the body's OWN authored scale.
            Vector3 ownScale = transform.localScale;
            transform.SetParent(flatRoot, true);
            transform.localScale = ownScale;

            // Register in the view registry so ShipView finds the dominant body by name.
            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            Runner.SystemView.Register(Body.Name, transform);

            // Every body gets its distant representation here, not by hand in the
            // scene: a new planet/moon/gas giant must look right from another body
            // without a component-by-component setup. Authored components win; the
            // auto path only fills a missing one.
            EnsureFarProxy();
        }

        /// <summary>
        /// Creates the far view a body needs to be visible beyond the camera's far
        /// plane. A heightfield body gets a <see cref="BodyFarDiscView"/> configured
        /// from its own terrain (macro maps if baked, otherwise a colour albedo baked
        /// from the same palette/noise); a gas giant gets a <see cref="GasGiantView"/>
        /// from its preset. Idempotent: an authored component is left untouched.
        /// </summary>
        private void EnsureFarProxy()
        {
            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (authoring == null)
            {
                return;
            }

            HeightfieldTerrain terrain = Body.Terrain as HeightfieldTerrain;
            PlanetSurfaceRenderer surface = GetComponent<PlanetSurfaceRenderer>();
            if (terrain != null && surface != null && surface.enabled
                && GetComponent<BodyFarDiscView>() == null)
            {
                BodyFarDiscView disc = gameObject.AddComponent<BodyFarDiscView>();
                disc.Runner = Runner;
                disc.PlayerCamera = Camera.main;

                // The real mesh is always the better picture while it can be rasterised
                // (the gate in PlanetSurfaceRenderer decides that); the disc is the
                // far-plane fallback, and holds during the async mesh hand-over.
                disc.OnlyWhenMeshMissing = true;
                disc.MinScreenRadiusPixels = 2f;
                disc.Brightness = 1f;
                disc.NightBrightness = 24f;
                disc.LimbDarkening = Body.Atmosphere != null ? 0.1f : 0.3f;

                if (terrain.MacroAlbedo != null)
                {
                    // The lunar-style map is a scalar brightness factor (R×2), not RGB.
                    disc.SurfaceMap = terrain.MacroAlbedo;
                    disc.SurfaceMapIsColor = false;
                    disc.SurfaceMapStrength = terrain.MacroAlbedoStrength;
                }
                else if (terrain.Palette != null)
                {
                    // No baked map: the palette/noise is the only source that knows
                    // where the ocean, the beaches and the ice are. Lazy + on a worker.
                    disc.BakeTerrainAlbedo = true;
                }

                if (terrain.MacroNormal != null)
                {
                    disc.NormalMap = terrain.MacroNormal;
                    disc.NormalStrength = terrain.MacroNormalStrength;
                }

                // The palette's Snow albedo is only a last resort; the maps carry colour.
                disc.UseTerrainPalette = terrain.MacroAlbedo == null && terrain.Palette == null;
            }

            if (authoring.GasGiantPreset != null && GetComponent<GasGiantView>() == null)
            {
                GasGiantView giant = gameObject.AddComponent<GasGiantView>();
                giant.Runner = Runner;
                giant.PlayerCamera = Camera.main;
                giant.Profile = authoring.GasGiantPreset;
            }
        }

        private void LateUpdate()
        {
            if (Body == null || Runner.SystemState == null || Runner.DominantBody == null)
            {
                return;
            }

            Runner.SystemState.GetVisualFrame(
                Body, Runner.TimeSeconds,
                out Vector3d position, out Vector3d velocity, out QuaternionD orientation);

            // Floating origin: the single anchor is the player position (FloatingOrigin.Anchor).
            transform.localPosition = FloatingOrigin.ToRender(position);
            transform.localRotation = FloatingOrigin.RenderRotation(orientation);
        }
    }
}
