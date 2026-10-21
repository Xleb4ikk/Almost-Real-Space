using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Galilego.Universe
{
    /// <summary>
    /// Action → key table (two slots per action). Keys are stored as UnityEngine.KeyCode
    /// (it also names the mouse buttons Mouse0..Mouse4), so the file is readable and
    /// independent of the input backend. Reading goes through the new Input System when
    /// it is active, with the legacy Input as a fallback — like the rest of PlayerInput.
    /// </summary>
    internal static class KeyBindings
    {
        public const int Slots = 2;

        private static KeyCode[,] map;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            map = null;
            Changed = null;
        }

        // ─── Table ───────────────────────────────────────────────────────────

        private static void Ensure()
        {
            if (map == null)
            {
                // Loading settings imports the bindings into `map`.
                GameSettings.Load();
                if (map == null)
                {
                    Import(null);
                }
            }
        }

        public static KeyCode Get(GameAction action, int slot)
        {
            Ensure();
            return map[(int)action, slot];
        }

        public static void Set(GameAction action, int slot, KeyCode key)
        {
            Ensure();
            map[(int)action, slot] = key;
            Changed?.Invoke();
            GameSettings.NotifyChanged();
        }

        public static void ResetAction(GameAction action)
        {
            Ensure();
            ActionInfo info = ActionTable.Get(action);
            map[(int)action, 0] = info.Default;
            map[(int)action, 1] = info.DefaultAlt;
            Changed?.Invoke();
            GameSettings.NotifyChanged();
        }

        public static void ResetAll()
        {
            Import(null);
            Changed?.Invoke();
            GameSettings.NotifyChanged();
        }

        public static bool IsDefault(GameAction action)
        {
            Ensure();
            ActionInfo info = ActionTable.Get(action);
            return map[(int)action, 0] == info.Default && map[(int)action, 1] == info.DefaultAlt;
        }

        /// <summary>
        /// Another action that shares this key in an overlapping context
        /// (e.g. two on-foot actions on one key). Different contexts may share keys.
        /// </summary>
        public static bool FindConflict(GameAction action, KeyCode key, out GameAction other)
        {
            other = action;
            if (key == KeyCode.None)
            {
                return false;
            }

            Ensure();
            ActionContext context = ActionTable.Get(action).Context;
            for (int i = 0; i < ActionTable.Count; i++)
            {
                var candidate = (GameAction)i;
                if (candidate == action || (ActionTable.Get(candidate).Context & context) == 0)
                {
                    continue;
                }

                for (int s = 0; s < Slots; s++)
                {
                    if (map[i, s] == key)
                    {
                        other = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Remove this key from every other action it conflicts with. Returns the first one cleared.</summary>
        public static bool StealKey(GameAction action, KeyCode key, out GameAction from)
        {
            from = action;
            if (key == KeyCode.None)
            {
                return false;
            }

            Ensure();
            ActionContext context = ActionTable.Get(action).Context;
            bool any = false;
            for (int i = 0; i < ActionTable.Count; i++)
            {
                var candidate = (GameAction)i;
                if (candidate == action || (ActionTable.Get(candidate).Context & context) == 0)
                {
                    continue;
                }

                for (int s = 0; s < Slots; s++)
                {
                    if (map[i, s] == key)
                    {
                        map[i, s] = KeyCode.None;
                        if (!any)
                        {
                            from = candidate;
                        }

                        any = true;
                    }
                }
            }

            return any;
        }

        // ─── Save / load (only the differences from defaults are written) ────

        public static void Import(List<BindingEntry> entries)
        {
            int count = ActionTable.Count;
            map = new KeyCode[count, Slots];
            for (int i = 0; i < count; i++)
            {
                ActionInfo info = ActionTable.Get((GameAction)i);
                map[i, 0] = info.Default;
                map[i, 1] = info.DefaultAlt;
            }

            if (entries == null)
            {
                return;
            }

            foreach (BindingEntry e in entries)
            {
                if (e == null || !Enum.TryParse(e.action, out GameAction action)
                    || (int)action < 0 || (int)action >= count)
                {
                    continue;
                }

                map[(int)action, 0] = ParseKey(e.a, map[(int)action, 0]);
                map[(int)action, 1] = ParseKey(e.b, map[(int)action, 1]);
            }
        }

        public static List<BindingEntry> Export()
        {
            var list = new List<BindingEntry>();
            if (map == null)
            {
                return list;
            }

            for (int i = 0; i < ActionTable.Count; i++)
            {
                var action = (GameAction)i;
                if (IsDefault(action))
                {
                    continue;
                }

                list.Add(new BindingEntry
                {
                    action = action.ToString(),
                    a = map[i, 0].ToString(),
                    b = map[i, 1].ToString()
                });
            }

            return list;
        }

        private static KeyCode ParseKey(string text, KeyCode fallback)
        {
            return !string.IsNullOrEmpty(text) && Enum.TryParse(text, out KeyCode key) ? key : fallback;
        }

        // ─── Reading ─────────────────────────────────────────────────────────

        public static bool Held(GameAction action)
        {
            Ensure();
            for (int s = 0; s < Slots; s++)
            {
                KeyCode k = map[(int)action, s];
                if (k != KeyCode.None && KeyHeld(k))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Down(GameAction action)
        {
            Ensure();
            for (int s = 0; s < Slots; s++)
            {
                KeyCode k = map[(int)action, s];
                if (k != KeyCode.None && KeyDown(k))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool Up(GameAction action)
        {
            Ensure();
            for (int s = 0; s < Slots; s++)
            {
                KeyCode k = map[(int)action, s];
                if (k != KeyCode.None && KeyUp(k))
                {
                    return true;
                }
            }

            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private static Dictionary<KeyCode, Key> keyMap;

        private static void EnsureKeyMap()
        {
            if (keyMap != null)
            {
                return;
            }

            keyMap = new Dictionary<KeyCode, Key>();
            // Same names in both enums: letters and F-keys.
            for (char c = 'A'; c <= 'Z'; c++)
            {
                Pair(c.ToString(), c.ToString());
            }

            for (int i = 1; i <= 12; i++)
            {
                Pair("F" + i, "F" + i);
            }

            for (int i = 0; i <= 9; i++)
            {
                Pair("Alpha" + i, "Digit" + i);
                Pair("Keypad" + i, "Numpad" + i);
            }

            string[,] pairs =
            {
                { "Space", "Space" }, { "Return", "Enter" }, { "Tab", "Tab" }, { "Backspace", "Backspace" },
                { "Delete", "Delete" }, { "Escape", "Escape" }, { "Insert", "Insert" }, { "Home", "Home" },
                { "End", "End" }, { "PageUp", "PageUp" }, { "PageDown", "PageDown" }, { "CapsLock", "CapsLock" },
                { "LeftShift", "LeftShift" }, { "RightShift", "RightShift" },
                { "LeftControl", "LeftCtrl" }, { "RightControl", "RightCtrl" },
                { "LeftAlt", "LeftAlt" }, { "RightAlt", "RightAlt" },
                { "UpArrow", "UpArrow" }, { "DownArrow", "DownArrow" },
                { "LeftArrow", "LeftArrow" }, { "RightArrow", "RightArrow" },
                { "BackQuote", "Backquote" }, { "Minus", "Minus" }, { "Equals", "Equals" },
                { "LeftBracket", "LeftBracket" }, { "RightBracket", "RightBracket" }, { "Backslash", "Backslash" },
                { "Semicolon", "Semicolon" }, { "Quote", "Quote" }, { "Comma", "Comma" },
                { "Period", "Period" }, { "Slash", "Slash" },
                { "KeypadPlus", "NumpadPlus" }, { "KeypadMinus", "NumpadMinus" },
                { "KeypadMultiply", "NumpadMultiply" }, { "KeypadDivide", "NumpadDivide" },
                { "KeypadEnter", "NumpadEnter" }, { "KeypadPeriod", "NumpadPeriod" },
                { "Pause", "Pause" }, { "ScrollLock", "ScrollLock" }, { "Print", "PrintScreen" },
            };
            for (int i = 0; i < pairs.GetLength(0); i++)
            {
                Pair(pairs[i, 0], pairs[i, 1]);
            }
        }

        // Names are resolved by string so a missing member can never break the build.
        private static void Pair(string keyCodeName, string keyName)
        {
            if (Enum.TryParse(keyCodeName, out KeyCode code) && Enum.TryParse(keyName, out Key key))
            {
                keyMap[code] = key;
            }
        }

        private static bool IsMouse(KeyCode k)
        {
            return k >= KeyCode.Mouse0 && k <= KeyCode.Mouse4;
        }

        private static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(Mouse m, KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return m.leftButton;
                case KeyCode.Mouse1: return m.rightButton;
                case KeyCode.Mouse2: return m.middleButton;
                case KeyCode.Mouse3: return m.backButton;
                default: return m.forwardButton;
            }
        }

        private static bool TryGetControl(KeyCode k, out UnityEngine.InputSystem.Controls.ButtonControl control)
        {
            control = null;
            if (IsMouse(k))
            {
                Mouse m = Mouse.current;
                if (m == null)
                {
                    return false;
                }

                control = MouseButton(m, k);
                return control != null;
            }

            Keyboard kb = Keyboard.current;
            if (kb == null)
            {
                return false;
            }

            EnsureKeyMap();
            if (!keyMap.TryGetValue(k, out Key key))
            {
                return false;
            }

            control = kb[key];
            return control != null;
        }

        private static bool KeyHeld(KeyCode k)
        {
            return TryGetControl(k, out var c) && c.isPressed;
        }

        private static bool KeyDown(KeyCode k)
        {
            return TryGetControl(k, out var c) && c.wasPressedThisFrame;
        }

        private static bool KeyUp(KeyCode k)
        {
            return TryGetControl(k, out var c) && c.wasReleasedThisFrame;
        }
#else
        private static bool KeyHeld(KeyCode k) { return Input.GetKey(k); }
        private static bool KeyDown(KeyCode k) { return Input.GetKeyDown(k); }
        private static bool KeyUp(KeyCode k) { return Input.GetKeyUp(k); }
#endif

        /// <summary>Raw "pressed this frame" for a key, ignoring the menu gate (used by key capture).</summary>
        public static bool RawDown(KeyCode key)
        {
            return KeyDown(key);
        }

        // ─── Names for the UI ────────────────────────────────────────────────

        public static string Display(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "—";
                case KeyCode.Mouse0: return "ЛКМ";
                case KeyCode.Mouse1: return "ПКМ";
                case KeyCode.Mouse2: return "СКМ";
                case KeyCode.Mouse3: return "Мышь 4";
                case KeyCode.Mouse4: return "Мышь 5";
                case KeyCode.Space: return "Пробел";
                case KeyCode.LeftShift: return "L Shift";
                case KeyCode.RightShift: return "R Shift";
                case KeyCode.LeftControl: return "L Ctrl";
                case KeyCode.RightControl: return "R Ctrl";
                case KeyCode.LeftAlt: return "L Alt";
                case KeyCode.RightAlt: return "R Alt";
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
                case KeyCode.BackQuote: return "`";
                case KeyCode.UpArrow: return "Стрелка вверх";
                case KeyCode.DownArrow: return "Стрелка вниз";
                case KeyCode.LeftArrow: return "Стрелка влево";
                case KeyCode.RightArrow: return "Стрелка вправо";
                case KeyCode.Minus: return "-";
                case KeyCode.Equals: return "=";
                case KeyCode.Comma: return ",";
                case KeyCode.Period: return ".";
                case KeyCode.Slash: return "/";
                case KeyCode.Backslash: return "\\";
                case KeyCode.Semicolon: return ";";
                case KeyCode.Quote: return "'";
                case KeyCode.LeftBracket: return "[";
                case KeyCode.RightBracket: return "]";
            }

            string name = key.ToString();
            if (name.StartsWith("Alpha"))
            {
                return name.Substring(5);
            }

            if (name.StartsWith("Keypad"))
            {
                return "Num " + name.Substring(6);
            }

            return name;
        }

        /// <summary>Keys of an action as one string for hints: "W / Стрелка вверх".</summary>
        public static string DisplayAction(GameAction action)
        {
            Ensure();
            string a = map[(int)action, 0] != KeyCode.None ? Display(map[(int)action, 0]) : null;
            string b = map[(int)action, 1] != KeyCode.None ? Display(map[(int)action, 1]) : null;
            if (a == null && b == null)
            {
                return "не назначено";
            }

            return a != null && b != null ? a + " / " + b : (a ?? b);
        }
    }
}
