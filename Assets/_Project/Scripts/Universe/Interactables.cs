using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// База интерактивных объектов (кнопки, рычаги, экраны, кресла). Попадает
    /// под прицел-луч PlayerController (≤ 4 м) → подсказка Prompt → E →
    /// Interact. Наследники вешаются на GO с коллайдером внутри корабля.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>Подсказка для игрока (например, «Дать полный газ (E)»).</summary>
        public abstract string Prompt { get; }

        /// <summary>Действие по нажатию E.</summary>
        public abstract void Interact(PlayerController player);
    }

    /// <summary>
    /// Кресло пилота: садимся (камера приколачивается к якорю), встаём по E.
    /// Якорь — пустой дочерний GO кресла на высоте глаз сидящего.
    /// </summary>
    public sealed class CockpitSeat : Interactable
    {
        [Tooltip("Пустой дочерний GO — позиция глаз сидящего.")]
        public Transform Anchor;

        public override string Prompt => "Сесть в кресло (E)";

        public override void Interact(PlayerController player)
        {
            if (player != null)
            {
                player.EnterSeat(this);
            }
        }
    }

    /// <summary>
    /// Демо-рычаг газа: E — дать полный газ / убрать в ноль. Показывает паттерн
    /// «рычаг → раннер»: настоящая панель наберётся из таких интерактиблов.
    /// </summary>
    public sealed class ThrottleLever : Interactable
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Какой газ даёт рычаг (0..1).")]
        [Range(0f, 1f)]
        public float LeverThrottle = 1f;

        private bool engaged;

        public override string Prompt => engaged ? "Убрать газ (E)" : "Дать полный газ (E)";

        public override void Interact(PlayerController player)
        {
            engaged = !engaged;
            if (Runner != null)
            {
                Runner.RawThrottle = engaged ? LeverThrottle : 0f;
            }
        }
    }
}
