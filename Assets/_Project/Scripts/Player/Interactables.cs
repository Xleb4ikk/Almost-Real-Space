using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Base class for interactable objects (buttons, levers, screens, seats).
    /// Caught by the PlayerController aim cone (≤ 4 m) → Prompt hint → E →
    /// Interact. Subclasses are attached to a GO with a collider inside the ship.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Tooltip("Что обводить при наведении. Пусто — все меши этого объекта и его детей.")]
        public Transform HighlightRoot;

        /// <summary>Prompt for the player (e.g. "Full throttle (E)").</summary>
        public abstract string Prompt { get; }

        /// <summary>Action performed on pressing E.</summary>
        public abstract void Interact(PlayerController player);

        /// <summary>Whether it can be used right now. No — no outline, no prompt, no E.</summary>
        public virtual bool IsAvailableTo(PlayerController player)
        {
            return true;
        }

        /// <summary>Outline color (an emergency lever may be red).</summary>
        public virtual Color HighlightColor
        {
            get { return Color.white; }
        }

        /// <summary>Which renderers to outline. By default — HighlightRoot (or this object) with children.</summary>
        public virtual void CollectHighlight(List<Renderer> into)
        {
            InteractionHighlighter.Gather(HighlightRoot != null ? HighlightRoot : transform, into);
        }
    }

    /// <summary>
    /// Demo throttle lever: E — full throttle / back to zero. Shows the
    /// "lever → runner" pattern: the real panel will be built from such interactables.
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
