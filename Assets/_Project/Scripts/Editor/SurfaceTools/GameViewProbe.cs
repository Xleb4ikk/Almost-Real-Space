#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Game View recon: at what resolution it actually renders.
    ///
    /// Render Scale lives in the Game View and is NOT saved in the project — it
    /// persists from the previous editor session, so it is not visible in Project Settings.
    ///
    /// The probe looks for a field containing "scale" on the Game View and prints its
    /// name and value: the exact Game View API is not public and changed between
    /// versions, so reflection lookup replaces a hard reference.
    /// </summary>
    public static class GameViewProbe
    {
        public static string Describe()
        {
            var sb = new StringBuilder();
            Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gvType == null)
            {
                return "GameView тип не найден";
            }

            EditorWindow window = EditorWindow.GetWindow(gvType);
            if (window == null)
            {
                return "окно Game View не найдено";
            }

            sb.Append("Game View ");
            sb.Append(window.position.ToString());

            FieldInfo[] fields = gvType.GetFields(
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].Name.IndexOf("scale", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                object value;
                try
                {
                    value = fields[i].GetValue(window);
                }
                catch (Exception)
                {
                    value = "?";
                }

                sb.Append(" | ").Append(fields[i].Name).Append('=').Append(value);
            }

            return sb.ToString();
        }
    }
}
#endif
