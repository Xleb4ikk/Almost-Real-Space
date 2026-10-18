using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Inspector for the visual atmosphere: dome parameters and debug mode.
    /// Physics/optics live in the body's AtmosphereProfile — this is visual only.
    /// </summary>
    [CustomEditor(typeof(PlanetAtmosphereView))]
    public sealed class PlanetAtmosphereViewEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorFields.Field(serializedObject, "Runner");

            InspectorFields.Label("Купол");
            InspectorFields.Field(serializedObject, "LatitudeSegments");
            InspectorFields.Field(serializedObject, "LongitudeSegments");
            InspectorFields.Field(serializedObject, "DebugMode");

            serializedObject.ApplyModifiedProperties();

            PlanetAtmosphereView view = (PlanetAtmosphereView)target;
            if (view.DebugMode != PlanetAtmosphereView.AtmosphereDebugMode.Final)
            {
                EditorGUILayout.HelpBox(
                    "Включён отладочный режим: вместо финального кадра рисуется отладочное поле (для разработки).",
                    MessageType.Warning);
            }
        }
    }
}
