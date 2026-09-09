using Galilego.Core;
using Galilego.Debris;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер обломков (маркеры-сферы). Позиция — относительно доминантного тела
    /// корабля (floating origin, тот же мост, что в ShipView). Осознанное
    /// упрощение MVP: обломок у ДРУГОГО тела рисуется от доминантного тела
    /// корабля — дельта уедет в float. Обломки живут секунды рядом с точкой
    /// удара, для них это не страшно; честный per-body якорь — когда понадобится.
    /// Вызов — в LateUpdate ПОСЛЕ BodyView (Script Execution Order).
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
            if (Runner == null || Runner.Debris == null || Runner.SystemState == null || Runner.DominantBody == null)
            {
                return;
            }

            if (Runner.SystemView == null
                || !Runner.SystemView.TryGetBodyTransform(Runner.DominantBody.Name, out Transform bodyTransform))
            {
                return;
            }

            if (markers == null || markers.Length != DebrisPool.Capacity)
            {
                CreateMarkers();
            }

            Runner.SystemState.EvaluateBodyState(Runner.DominantBody, Runner.TimeSeconds, out Vector3d bodyPosition, out _);
            for (int i = 0; i < DebrisPool.Capacity; i++)
            {
                DebrisSlot slot = Runner.Debris.SlotAt(i);
                if (slot.Active)
                {
                    Vector3d delta = slot.Body.Position - bodyPosition;
                    markers[i].position = bodyTransform.position + AstroFrame.ToSimulation(delta);
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
