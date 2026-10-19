using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Pilot/operator seat: E to sit (the player is fixed at the anchor, walking
    /// freezes, the head is free: the mouse looks around), E to stand. The station
    /// monitor does not "stick" to the face: its buttons are pressed directly with
    /// the crosshair mouse (ShipTablet, world pointer), and RMB zooms in close (camera).
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

        [HideInInspector]
        [Tooltip("Куда смотреть сразу после посадки (направление в осях якоря). " +
            "Ставит OperatorMonitorLayout: взгляд на пару мониторов. Ноль - не менять взгляд.")]
        public Vector3 LocalLookDirection;

        [Tooltip("Имя узла модели с мешем кресла (ищется среди соседей по родителю) — его и обводим.")]
        public string HighlightNodeName = "Operator_Chair";

        public override string Prompt => "Сесть в кресло (E)";

        /// <summary>While seated, the chair is not outlined or offered.</summary>
        public override bool IsAvailableTo(PlayerController player)
        {
            return player == null || !player.Seated;
        }

        public override void CollectHighlight(List<Renderer> into)
        {
            if (HighlightRoot == null && !string.IsNullOrEmpty(HighlightNodeName))
            {
                Transform scope = transform.parent != null ? transform.parent : transform;
                foreach (Transform node in scope.GetComponentsInChildren<Transform>(true))
                {
                    if (node.name == HighlightNodeName)
                    {
                        HighlightRoot = node;
                        break;
                    }
                }
            }

            base.CollectHighlight(into);
        }

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
