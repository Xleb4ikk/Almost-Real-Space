using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рендер небесного тела (B2). Вешается на тот же GameObject, что
    /// BodyAuthoring. ЯКОРЬ РЕНДЕРА — доминантное тело корабля (не орбитальный
    /// родитель): позиция тела рисуется дельтой bodyPos − dominantPos,
    /// вычисленной в double и сконвертированной один раз на выходе. У корня
    /// сцены (звезда, astro-ноль) якорь всегда мал, поэтому Unity-координаты
    /// НЕ улетают за 1e9 — на 2.6e10 ulp float это километры, сцена
    /// «рассыпается» и камера слепнет (поймано в юнити-тесте сцены).
    /// Далёкие тела (звезда при полёте у планеты) получают большие дельты —
    /// их трансформы неточны, но они и не рендерятся: их визуал заменяет
    /// billboard (SunBillboard). В рантайме все BodyView переезжают под общий
    /// корень в origin (плоская иерархия): родитель-цепочка авторинга тащила бы
    /// huge-координаты якоря в детей. Раз в кадр: StarSystem.GetVisualFrame →
    /// позиция/скорость + ориентация body→world → мост AstroFrame →
    /// transform.localPosition/localRotation. Исполнение в LateUpdate ПОСЛЕ
    /// SimulationRunner.Update (порядок скриптов или гарантия: runner
    /// обновляется в Update, views — в LateUpdate).
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-100)]
    public sealed class BodyView : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        /// <summary>Индекс тела в системе (заполняется при старте по имени).</summary>
        public OrbitingBody Body { get; private set; }

        /// <summary>
        /// Общий корень плоской иерархии тел (в origin сцены). Статическая
        /// иерархия (Terra — ребёнок Sol) нужна только для АВТОРИНГА блюпринта;
        /// в рантайме якорь рендера динамический (доминантное тело), и
        /// родитель-цепочка тащила бы huge-координаты якоря в детей
        /// (Sol в −2.6e10 → Terra наследует ±2048 м float-джиттер). Поэтому
        /// при старте все BodyView переезжают под этот корень: parent в
        /// origin → localPosition == worldPosition.
        /// </summary>
        private static Transform flatRoot;

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

            if (flatRoot == null)
            {
                flatRoot = new GameObject("BodyViewsRoot").transform;
            }

            transform.SetParent(flatRoot, true);

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
            if (Body == null || Runner.SystemState == null || Runner.DominantBody == null)
            {
                return;
            }

            Runner.SystemState.GetVisualFrame(
                Body, Runner.TimeSeconds,
                out Vector3d position, out Vector3d velocity, out QuaternionD orientation);

            // Floating origin: единственный якорь — позиция игрока (FloatingOrigin.Anchor),
            // а не центр доминантного тела. Тогда у поверхности координаты малы
            // (центр тела давал ~радиус планеты → float-дрожь).
            transform.localPosition = FloatingOrigin.ToRender(position);
            transform.localRotation = FloatingOrigin.RenderRotation(orientation);
        }
    }
}
