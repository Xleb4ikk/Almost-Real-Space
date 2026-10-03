#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Разведка Game View: каким реально разрешением он рендерит.
    ///
    /// Зачем: снимки съёмки приходили 4x блочными — 3D-сцена рисовалась в
    /// 640x360 и апскейлилась в 2560x1440, при этом HUD оставался чётким
    /// (IMGUI рисуется после апскейла). На таких кадрах нельзя судить ни о
    /// ряби на потолке, ни об алиасинге, то есть весь смысл сравнения
    /// «до/после» терялся. Render Scale живёт в Game View и НЕ сохраняется в
    /// проекте — он остался от прошлой сессии редактора, поэтому и не виден
    /// в Project Settings.
    ///
    /// Проба ищет у Game View поле со словом «scale» и печатает его имя и
    /// значение: точный API у Game View не публичный и менялся между версиями,
    /// поэтому вместо жёсткой ссылки — поиск по отражению с понятной выдачей.
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
