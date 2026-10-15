using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Экипажный модуль Orion: анимации люка и выдвижной руки-планшета.
    /// Аниматор стоит на корне модели (вложенный FBX), клипы — отдельные
    /// .anim: открытие/закрытие люка и выдвижение/складывание руки.
    /// Состояния аниматора держат конечную позу, поэтому методы можно
    /// вызывать в любом порядке — механизмы не мешают друг другу.
    ///
    /// Поза открытой створки: клип открытия крутит дверь вокруг её ЦЕНТРА
    /// (шарнир модели — центр двери, не кромка) и в конечной позе створка
    /// висит ровно над проёмом, запирая вход внутрь. Поэтому в состоянии
    /// Hatch_Open створка плавно поднимается вдоль вертикали капсулы
    /// (OpenLiftMeters) — проём полностью свободен, игрок входит пригнувшись.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class OrionCrewModule : MonoBehaviour
    {
        private static readonly int HatchOpenId = Animator.StringToHash("OpenHatch");
        private static readonly int HatchCloseId = Animator.StringToHash("CloseHatch");
        private static readonly int ArmDeployId = Animator.StringToHash("DeployTabletArm");
        private static readonly int ArmRetractId = Animator.StringToHash("RetractTabletArm");

        /// <summary>Состояние аниматора «люк открыт» (держит конечную позу клипа).</summary>
        private const string HatchOpenState = "Hatch_Open";

        [Tooltip("Аниматор модели (по умолчанию — на этом же объекте).")]
        [SerializeField] private Animator animator;

        [Tooltip("Подъём открытой створки над проёмом (м): клип оставляет её «в проёме», " +
            "запирая вход; поднимаем — проход свободен.")]
        [SerializeField] private float openLiftMeters = 1.3f;

        [Tooltip("Скорость подъёма створки (м/с).")]
        [SerializeField] private float openLiftSpeed = 1.2f;

        private Transform door;
        private Vector3 openBaseLocal;
        private Vector3 liftLocal;
        private float liftLocalSpeed;
        private bool openPoseActive;

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

            // Створка (по имени узла модели).
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "Hatch_Door")
                {
                    door = all[i];
                }
            }
        }

        private void Update()
        {
            if (animator == null || door == null)
            {
                return;
            }

            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            bool open = state.IsName(HatchOpenState);
            if (open && !openPoseActive)
            {
                openPoseActive = true;
                openBaseLocal = door.localPosition;
                // Смещение — в локальных единицах РОДИТЕЛЯ, но задано МЕТРАМИ:
                // InverseTransformVector учитывает масштаб (у модели Orion на
                // внутренних узлах он стократный — «1.3 м» в лоб давало 130 м).
                liftLocal = door.parent.InverseTransformVector(transform.root.up * openLiftMeters);
                liftLocalSpeed = liftLocal.magnitude * Mathf.Max(0.01f, openLiftSpeed / Mathf.Max(0.01f, openLiftMeters));
            }
            else if (!open)
            {
                openPoseActive = false;
            }

            if (openPoseActive)
            {
                // Состояние без клипа: аниматор позу больше не пишет — поднимаем сами.
                Vector3 targetLocal = openBaseLocal + liftLocal;
                door.localPosition = Vector3.MoveTowards(
                    door.localPosition, targetLocal, liftLocalSpeed * Time.deltaTime);
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
