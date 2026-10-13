using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Экипажный модуль Orion: анимации люка и выдвижной руки-планшета.
    /// Аниматор стоит на корне модели (вложенный FBX), клипы — отдельные
    /// .anim: открытие/закрытие люка и выдвижение/складывание руки.
    /// Состояния аниматора держат конечную позу, поэтому методы можно
    /// вызывать в любом порядке — механизмы не мешают друг другу.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class OrionCrewModule : MonoBehaviour
    {
        private static readonly int HatchOpenId = Animator.StringToHash("OpenHatch");
        private static readonly int HatchCloseId = Animator.StringToHash("CloseHatch");
        private static readonly int ArmDeployId = Animator.StringToHash("DeployTabletArm");
        private static readonly int ArmRetractId = Animator.StringToHash("RetractTabletArm");

        [Tooltip("Аниматор модели (по умолчанию — на этом же объекте).")]
        [SerializeField] private Animator animator;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }
        }

        /// <summary>Открыть люк (шлюз наружу).</summary>
        public void OpenHatch()
        {
            SetTrigger(HatchOpenId);
        }

        /// <summary>Закрыть люк.</summary>
        public void CloseHatch()
        {
            SetTrigger(HatchCloseId);
        }

        /// <summary>Выдвинуть руку с планшетом к пилоту.</summary>
        public void DeployTabletArm()
        {
            SetTrigger(ArmDeployId);
        }

        /// <summary>Убрать руку с планшетом.</summary>
        public void RetractTabletArm()
        {
            SetTrigger(ArmRetractId);
        }

        private void SetTrigger(int id)
        {
            if (animator != null)
            {
                animator.SetTrigger(id);
            }
        }
    }
}
