using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Galilego.Universe
{
    /// <summary>Абстракция клавиш ввода (не зависит от системы ввода).</summary>
    internal enum GameKey
    {
        W, A, S, D, Space, LeftShift, LeftControl, E, P, Escape,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7
    }

    /// <summary>
    /// Ввод через новый Input System, когда он активен (ENABLE_INPUT_SYSTEM), с
    /// legacy-запаской для тестового стенда. Нужно, потому что при Active Input
    /// Handling = Both старый mouse-модуль сыпет «Screen position out of view
    /// frustum» при захваченном курсоре (это и было причиной спама в консоли).
    /// Проект переводится на «Input System Package (New)» — legacy-модуль
    /// выключается, предупреждение исчезает.
    /// </summary>
    internal static class PlayerInput
    {
        private const float DoubleTapWindowSeconds = 0.3f;
        private static bool spaceIsDown;
        private static float lastSpacePressTime = float.NegativeInfinity;

        public static bool DoubleDown(GameKey key)
        {
            if (key != GameKey.Space)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                spaceIsDown = false;
                return false;
            }

            var space = keyboard[Key.Space];
            return TrackDoubleTap(space.isPressed, space.wasPressedThisFrame, space.wasReleasedThisFrame);
#else
            return TrackDoubleTap(
                Input.GetKey(KeyCode.Space),
                Input.GetKeyDown(KeyCode.Space),
                Input.GetKeyUp(KeyCode.Space));
#endif
        }

#if ENABLE_INPUT_SYSTEM
        public static bool Held(GameKey key)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[ToKey(key)].isPressed;
        }

        public static bool Down(GameKey key)
        {
            Keyboard keyboard = Keyboard.current;
            return keyboard != null && keyboard[ToKey(key)].wasPressedThisFrame;
        }

        /// <summary>Смещение мыши за кадр в «осевых» единицах (как старый Mouse X/Y).</summary>
        public static Vector2 MouseDelta()
        {
            Mouse mouse = Mouse.current;
            return mouse != null ? mouse.delta.ReadValue() * 0.1f : Vector2.zero;
        }

        public static bool MouseLeftDown()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private static Key ToKey(GameKey key)
        {
            switch (key)
            {
                case GameKey.W: return Key.W;
                case GameKey.A: return Key.A;
                case GameKey.S: return Key.S;
                case GameKey.D: return Key.D;
                case GameKey.Space: return Key.Space;
                case GameKey.LeftShift: return Key.LeftShift;
                case GameKey.LeftControl: return Key.LeftCtrl;
                case GameKey.E: return Key.E;
                case GameKey.P: return Key.P;
                case GameKey.Escape: return Key.Escape;
                case GameKey.Digit1: return Key.Digit1;
                case GameKey.Digit2: return Key.Digit2;
                case GameKey.Digit3: return Key.Digit3;
                case GameKey.Digit4: return Key.Digit4;
                case GameKey.Digit5: return Key.Digit5;
                case GameKey.Digit6: return Key.Digit6;
                default: return Key.Digit7;
            }
        }
#else
        public static bool Held(GameKey key) => Input.GetKey(ToKeyCode(key));

        public static bool Down(GameKey key) => Input.GetKeyDown(ToKeyCode(key));

        public static Vector2 MouseDelta() => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

        public static bool MouseLeftDown() => Input.GetMouseButtonDown(0);

        private static KeyCode ToKeyCode(GameKey key)
        {
            switch (key)
            {
                case GameKey.W: return KeyCode.W;
                case GameKey.A: return KeyCode.A;
                case GameKey.S: return KeyCode.S;
                case GameKey.D: return KeyCode.D;
                case GameKey.Space: return KeyCode.Space;
                case GameKey.LeftShift: return KeyCode.LeftShift;
                case GameKey.LeftControl: return KeyCode.LeftControl;
                case GameKey.E: return KeyCode.E;
                case GameKey.P: return KeyCode.P;
                case GameKey.Escape: return KeyCode.Escape;
                case GameKey.Digit1: return KeyCode.Alpha1;
                case GameKey.Digit2: return KeyCode.Alpha2;
                case GameKey.Digit3: return KeyCode.Alpha3;
                case GameKey.Digit4: return KeyCode.Alpha4;
                case GameKey.Digit5: return KeyCode.Alpha5;
                case GameKey.Digit6: return KeyCode.Alpha6;
                default: return KeyCode.Alpha7;
            }
        }
#endif

        private static bool TrackDoubleTap(bool isPressed, bool wasPressedThisFrame, bool wasReleasedThisFrame)
        {
            bool doubleDown = false;
            if (wasPressedThisFrame && !spaceIsDown)
            {
                doubleDown = Time.unscaledTime - lastSpacePressTime <= DoubleTapWindowSeconds;
                lastSpacePressTime = Time.unscaledTime;
                spaceIsDown = true;
            }

            if (wasReleasedThisFrame || (!isPressed && spaceIsDown))
            {
                spaceIsDown = false;
            }

            return doubleDown;
        }
    }
}
