using Galilego.Core;
using Galilego.Debris;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Debris render (sphere markers). Position is relative to the floating origin
    /// (same bridge as ShipView). MVP simplification: debris at ANOTHER body is drawn
    /// from the ship's anchor, so the delta may lose float precision; debris lives for
    /// seconds near the impact point, so this is acceptable. Runs in LateUpdate AFTER BodyView.
    /// </summary>
    public sealed class DebrisView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Радиус маркера обломка (м, визуальный; физика его не видит).")]
        public float MarkerScale = 0.5f;

        private Transform[] markers;

        private void LateUpdate()
        {
            if (Runner == null || Runner.Debris == null || Runner.SystemState == null)
            {
                return;
            }

            if (markers == null || markers.Length != DebrisPool.Capacity)
            {
                CreateMarkers();
            }

            for (int i = 0; i < DebrisPool.Capacity; i++)
            {
                DebrisSlot slot = Runner.Debris.SlotAt(i);
                if (slot.Active)
                {
                    markers[i].position = FloatingOrigin.ToRender(slot.Body.Position);
                    markers[i].localScale = Vector3.one * MarkerScale;
                    if (!markers[i].gameObject.activeSelf)
                    {
                        markers[i].gameObject.SetActive(true);
                    }
                }
                else if (markers[i].gameObject.activeSelf)
                {
                    markers[i].gameObject.SetActive(false);
                }
            }
        }

        private void CreateMarkers()
        {
            markers = new Transform[DebrisPool.Capacity];
            for (int i = 0; i < DebrisPool.Capacity; i++)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(sphere.GetComponent<Collider>());
                sphere.name = "DebrisMarker" + i;
                sphere.SetActive(false);
                markers[i] = sphere.transform;
            }
        }
    }
}
