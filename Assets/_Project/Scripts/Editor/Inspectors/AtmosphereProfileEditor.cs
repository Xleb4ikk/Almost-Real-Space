using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор ассета-профиля атмосферы: физика, визуал и художественные
    /// тинты по секциям (раньше жили inline на BodyAuthoring).
    /// </summary>
    [CustomEditor(typeof(AtmosphereProfileAsset))]
    public sealed class AtmosphereProfileEditor : Editor
    {
        private static bool showPhysics = true;
        private static bool showVisual = true;
        private static bool showTints;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            showPhysics = EditorGUILayout.BeginFoldoutHeaderGroup(showPhysics, "Физика (высотная модель плотности)");
            if (showPhysics)
            {
                EditorGUI.indentLevel++;
                Field("Profile.TopAltitudeMeters");
                Field("Profile.SeaLevelDensityKgPerCubicMeter");
                Field("Profile.ScaleHeightMeters");
                Field("Profile.OzoneEnabled");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showVisual = EditorGUILayout.BeginFoldoutHeaderGroup(showVisual, "Визуал (raymarch-атмосфера)");
            if (showVisual)
            {
                EditorGUI.indentLevel++;
                Field("Profile.VisualEnabled");
                Field("Profile.Intensity");
                Field("Profile.MieAnisotropy");
                Field("Profile.AerosolScale");
                Field("Profile.StepCount");
                Field("Profile.PlanetOcclusion");
                Field("Profile.GroundColor");
                Field("Profile.HorizonFade");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showTints = EditorGUILayout.BeginFoldoutHeaderGroup(showTints, "Тинты (множители поверх физики)");
            if (showTints)
            {
                EditorGUI.indentLevel++;
                Field("Profile.RayleighColor");
                Field("Profile.MieColor");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void Field(string path)
        {
            InspectorFields.Field(serializedObject, path);
        }
    }
}
