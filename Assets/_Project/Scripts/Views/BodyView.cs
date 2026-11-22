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
