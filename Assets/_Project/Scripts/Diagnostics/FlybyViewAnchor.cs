using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Debug flyby helper. After a teleport to a distant body the regular
    /// FirstPersonCamera derives its clip planes from the dominant body, which
    /// can still be Terra for a frame (or for the whole session when the body
    /// has no solved SOI). At 1.6 AU that puts the near plane in front of the
    /// planet and the gas giant disappears. This anchor pins a sensible
    /// near/far pair for a short time and temporarily hides the home planet's
    /// atmosphere/cloud domes, which are otherwise still camera-centred at the
    /// old body.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class FlybyViewAnchor : MonoBehaviour
    {
        public Camera Camera;
        public SimulationRunner Runner;
        public OrbitingBody Body;

        // Runs at order 1000, AFTER the sky views (-35…-30) have placed their domes
        // from the camera's near. A dome that ends up at or inside this near plane is
        // clipped away (stars/atmosphere vanish for the flyby). SkyDomeLayout keeps
        // its floor at 5 km for exactly this reason — do not raise NearClip near it.
        public float NearClip = 1000f;
        public float FarClip = 4e8f;
        public float LifeSeconds = 60f;

        [Tooltip("Ниже этой высоты над целевым телом якорь отпускает клип-плоскости: " +
            "обычная камера считает near/far по телу под камерой. Иначе near = 1000 м " +
            "срезает землю прямо под ногами при посадке.")]
        public float ReleaseAltitudeMeters = 20000f;

        public GameObject[] Hide;

        private bool[] hideStates;

        private void Start()
        {
            if (Hide != null)
            {
                hideStates = new bool[Hide.Length];
                for (int i = 0; i < Hide.Length; i++)
                {
                    if (Hide[i] != null)
                    {
                        hideStates[i] = Hide[i].activeSelf;
                        Hide[i].SetActive(false);
                    }
                }
            }
        }

        private void LateUpdate()
        {
            if (Camera == null)
            {
                Destroy(gameObject);
                return;
            }

            // Keep the local frame on the target body. Without this the ship/
            // player step can fall back to the home planet, and at 1.6 AU the
            // flyby drifts out of the sky within a minute.
            if (Runner != null && Body != null)
            {
                Runner.SetDominantBodyForDebug(Body);

                // The flyby near plane (1000 m) is right for a 2 R view and
                // catastrophic for a landing: the whole foreground is inside
                // the near plane and the surface appears to vanish exactly when
                // the player touches down. Hand the clip planes back to the
                // regular camera once the eye is close to the body (or the
                // player is on the ground / in the ship).
                Body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);
                double altitude = (Runner.PlayerPosition - bodyPosition).Magnitude - Body.Radius;
                bool landed = Runner.PlayerMode == PlayerMode.OnSurface || Runner.PlayerMode == PlayerMode.InShip;
                if (altitude < ReleaseAltitudeMeters || landed)
                {
                    Destroy(gameObject);
                    return;
                }
            }

            if (Hide != null)
            {
                for (int i = 0; i < Hide.Length; i++)
                {
                    if (Hide[i] != null && Hide[i].activeSelf)
                    {
                        Hide[i].SetActive(false);
                    }
                }
            }

            Camera.nearClipPlane = NearClip;
            Camera.farClipPlane = FarClip;

            LifeSeconds -= Time.unscaledDeltaTime;
            if (LifeSeconds <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Hide == null || hideStates == null)
            {
                return;
            }

            for (int i = 0; i < Hide.Length && i < hideStates.Length; i++)
            {
                if (Hide[i] != null)
                {
                    Hide[i].SetActive(hideStates[i]);
                }
            }
        }
    }
}
