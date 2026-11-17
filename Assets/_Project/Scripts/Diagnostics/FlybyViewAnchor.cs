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
        public float NearClip = 1000f;
        public float FarClip = 4e8f;
        public float LifeSeconds = 60f;
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
