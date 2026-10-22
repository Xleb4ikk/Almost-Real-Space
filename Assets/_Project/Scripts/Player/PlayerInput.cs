using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Galilego.Universe
{
    /// <summary>
    /// Game input. Keys are asked by ACTION (GameAction), not by physical key: the
    /// action → key table lives in KeyBindings and is edited in the settings menu.
    /// While the settings menu is open every gameplay query returns "nothing pressed",
    /// so no system (flight, builder, throttle, warp) has to know about the menu.
    /// The new Input System is used when active (ENABLE_INPUT_SYSTEM), with a legacy
    /// fallback. Needed because with Active Input Handling = Both the old mouse
    /// module spams "Screen position out of view frustum" while the cursor is locked.
    /// </summary>
    internal static class PlayerInput
    {
        private const float DoubleTapWindowSeconds = 0.3f;

        private static bool[] tapIsDown;
        private static float[] tapLastPress;

        /// <summary>The settings menu consumed Esc on this frame (opened or closed itself).</summary>
        public static int EscapeConsumedFrame = -1;

        private static bool Blocked
        {
            get { return SettingsMenu.IsOpen; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            tapIsDown = null;
            tapLastPress = null;
            EscapeConsumedFrame = -1;
        }

        public static bool Held(GameAction action)
        {
            return !Blocked && KeyBindings.Held(action);
        }

        public static bool Down(GameAction action)
        {
            return !Blocked && KeyBindings.Down(action);
        }

        public static bool Up(GameAction action)
        {
            return !Blocked && KeyBindings.Up(action);
        }

        /// <summary>Double tap on an action's key: Jump — jetpack, Sprint — hover.</summary>
        public static bool DoubleDown(GameAction action)
        {
            if (tapIsDown == null)
            {
                tapIsDown = new bool[ActionTable.Count];
                tapLastPress = new float[ActionTable.Count];
                for (int i = 0; i < tapLastPress.Length; i++)
                {
                    tapLastPress[i] = float.NegativeInfinity;
                }
            }

            int index = (int)action;
            return TrackDoubleTap(ref tapIsDown[index], ref tapLastPress[index],
                Held(action), Down(action), Up(action));
        }

        /// <summary>Esc for gameplay (leave a screen, close a window). Not while the menu is open or just used it.</summary>
        public static bool EscapeDown()
        {
            return !Blocked && EscapeConsumedFrame != Time.frameCount && RawEscapeDown();
        }

        /// <summary>Esc with no filtering — only the settings menu itself uses it.</summary>
        public static bool RawEscapeDown()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        /// <summary>
        /// Raw physical key press for UI shortcuts that are not rebindable actions
        /// (the RCS ring size on X/Z). Works with either input backend.
        /// </summary>
        public static bool RawKeyDown(KeyCode key)
        {
            if (Blocked)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (key)
            {
                case KeyCode.X: return keyboard.xKey.wasPressedThisFrame;
                case KeyCode.Z: return keyboard.zKey.wasPressedThisFrame;
                default: return false;
            }
#else
            return Input.GetKeyDown(key);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        /// <summary>Mouse delta for this frame in axis units (like the old Mouse X/Y).</summary>
        public static Vector2 MouseDelta()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.delta.ReadValue() * 0.1f : Vector2.zero;
        }

        /// <summary>Cursor position in screen pixels.</summary>
        public static Vector2 MousePosition()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : Vector2.zero;
        }

        public static bool MouseLeftDown()
        {
            Mouse mouse = Mouse.current;
            return !Blocked && mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        public static bool MouseLeftHeld()
        {
            Mouse mouse = Mouse.current;
            return !Blocked && mouse != null && mouse.leftButton.isPressed;
        }

        /// <summary>Wheel scroll for this frame (positive is away from the user).</summary>
        public static float MouseScroll()
        {
            Mouse mouse = Mouse.current;
            return !Blocked && mouse != null ? mouse.scroll.ReadValue().y * 0.01f : 0f;
        }
#else
        public static Vector2 MouseDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        public static Vector2 MousePosition() => Input.mousePosition;

        public static bool MouseLeftDown() => !Blocked && Input.GetMouseButtonDown(0);

        public static bool MouseLeftHeld() => !Blocked && Input.GetMouseButton(0);

        public static float MouseScroll() => Blocked ? 0f : Input.mouseScrollDelta.y;
#endif

        /// <summary>
        /// Cheat menu: F10, and with requireAlt only together with Alt. Alt is
        /// checked as held rather than pressed: the combo is pressed in one
        /// motion, so Alt may be held well before F10.
        /// </summary>
        public static bool CheatMenuDown(bool requireAlt)
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            if (!keyboard.f10Key.wasPressedThisFrame)
            {
                return false;
            }

            return !requireAlt || keyboard.leftAltKey.isPressed;
#else
            if (!Input.GetKeyDown(KeyCode.F10))
            {
                return false;
            }

            return !requireAlt || Input.GetKey(KeyCode.LeftAlt);
#endif
        }

        private static bool TrackDoubleTap(ref bool isDown, ref float lastPressTime,
            bool isPressed, bool wasPressedThisFrame, bool wasReleasedThisFrame)
        {
            bool doubleDown = false;
            if (wasPressedThisFrame && !isDown)
            {
                doubleDown = Time.unscaledTime - lastPressTime <= DoubleTapWindowSeconds;
                lastPressTime = Time.unscaledTime;
                isDown = true;
            }

            if (wasReleasedThisFrame || (!isPressed && isDown))
            {
                isDown = false;
            }

            return doubleDown;
        }
    }
}
