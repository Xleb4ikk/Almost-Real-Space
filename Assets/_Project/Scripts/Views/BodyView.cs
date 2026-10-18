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

            transform.SetParent(flatRoot, true);

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
