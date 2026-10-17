using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Кресло пилота/оператора: E — сесть (игрок фиксируется у якоря, ходьба
    /// замирает, голова свободна: мышь смотрит вокруг), E — встать. Монитор
    /// станции не «прилипает» к лицу: кнопки на нём жмутся прямо мышью-прицелом
    /// (ShipTablet, мировой указатель), а ПКМ приближает вплотную (камера).
    /// </summary>
    public sealed class CockpitSeat : Interactable
    {
        [Tooltip("Пустой дочерний GO — позиция ГЛАЗ сидящего. Местный поворот " +
            "якоря держите нулевым: местная +Z — это «вверх» кресла (ноги вниз).")]
        public Transform Anchor;

        [Tooltip("Высота глаз сидящего над точкой опоры (м): ноги — под якорем.")]
        public float EyeHeightMeters = 0.85f;

        [Tooltip("Куда встать: смещение от ног под якорем, м, в местных осях кресла " +
            "(+X — от консоли в проход).")]
        public Vector3 StandOffsetMeters = new Vector3(1.3f, 0f, 0f);

        public override string Prompt => "Сесть в кресло (E)";

        public override void Interact(PlayerController player)
        {
            if (player == null)
            {
                return;
            }

            if (player.Seated)
            {
                player.StandUp();
                return;
            }

            player.EnterSeat(this);
        }
    }
}
