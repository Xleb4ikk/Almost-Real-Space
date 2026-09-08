using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер небесного тела (B2). Вешается на тот же GameObject, что
    /// BodyAuthoring; иерархия Transform'ов = орбитальная иерархия, поэтому
    /// локальная позиция относительно родительского BodyView решает float-origin:
    /// мир ядра ~1e11 м живёт в double, Unity-трансформы — в локальных дельтах
    /// (луна ~4e8 м от планеты, корабль — относительно доминантного тела).
    /// Раз в кадр: StarSystem.GetVisualFrame → позиция/скорость + ориентация
    /// body→world → мост AstroFrame → transform.localPosition/localRotation.
    /// Исполнение в LateUpdate ПОСЛЕ SimulationRunner.Update (порядок скриптов
    /// или гарантия: runner обновляется в Update, views — в LateUpdate).
    /// </summary>
    public sealed class BodyView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        /// <summary>Индекс тела в системе (заполняется при старте по имени).</summary>
        public OrbitingBody Body { get; private set; }

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            // Тело определяется по имени GameObject'а — совпадает с именем в блюпринте.
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

            // Регистрация в реестре вида системы: ShipView находит доминантное
            // тело по имени без перебора иерархии каждый кадр.
            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            Runner.SystemView.Register(Body.Name, transform);
        }

        private void LateUpdate()
        {
            if (Body == null || Runner.SystemState == null)
            {
                return;
            }

            Runner.SystemState.GetVisualFrame(
                Body, Runner.TimeSeconds,
                out Vector3d position, out Vector3d velocity, out QuaternionD orientation);

            // Локальная позиция относительно орбитального родителя: дельта в
            // double (точность ядра), конверсия в float — последняя операция.
            Vector3d local;
            if (Body.Parent != null)
            {
                Runner.SystemState.EvaluateBodyState(Body.Parent, Runner.TimeSeconds, out Vector3d parentP, out _);
                local = position - parentP;
            }
            else
            {
                // Корень (звезда): локальная позиция относительно origin сцены.
                local = position;
            }

            transform.localPosition = AstroFrame.ToSimulation(local);
            transform.localRotation = AstroFrame.ToSimulation(orientation);
        }
    }
}
