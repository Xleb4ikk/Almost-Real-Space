using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Small helpers for custom inspectors: drawing a field by path and a slider for
    /// double fields (RangeAttribute does not work with double — Unity draws sliders
    /// for float/int only). A missing field does not break the inspector but shows a
    /// warning, so renames are visible immediately.
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

        /// <summary>Slider for a double/float/int field. The label comes from the field itself (name + tooltip).</summary>
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
