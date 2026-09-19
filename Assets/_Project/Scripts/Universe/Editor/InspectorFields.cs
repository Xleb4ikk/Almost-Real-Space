using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Мелкие помощники кастомных инспекторов: рисование поля по пути и слайдер
    /// для double-полей (RangeAttribute с double не работает — Unity рисует
    /// слайдер только для float/int). Отсутствующее поле не роняет инспектор,
    /// а показывает предупреждение — при переименовании данных видно сразу.
    /// </summary>
    internal static class InspectorFields
    {
        public static void Field(SerializedObject serializedObject, string path)
        {
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null)
            {
                EditorGUILayout.HelpBox("Не найдено поле: " + path, MessageType.Warning);
                return;
            }

            EditorGUILayout.PropertyField(property, true);
        }

        /// <summary>Слайдер для double/float/int-поля. Label берётся из самого поля (имя + тултип).</summary>
        public static void Slider(SerializedObject serializedObject, string path, float min, float max)
        {
            SerializedProperty property = serializedObject.FindProperty(path);
            if (property == null)
            {
                EditorGUILayout.HelpBox("Не найдено поле: " + path, MessageType.Warning);
                return;
            }

            GUIContent label = new GUIContent(property.displayName, property.tooltip);
            if (property.propertyType == SerializedPropertyType.Integer)
            {
                property.intValue = EditorGUILayout.IntSlider(label, property.intValue, Mathf.RoundToInt(min), Mathf.RoundToInt(max));
            }
            else
            {
                property.doubleValue = EditorGUILayout.Slider(label, (float)property.doubleValue, min, max);
            }
        }

        public static void Label(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }
    }
}
