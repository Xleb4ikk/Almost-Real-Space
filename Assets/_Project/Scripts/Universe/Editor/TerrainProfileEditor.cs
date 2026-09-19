using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор ассета-профиля рельефа: сворачиваемые секции вместо плоской
    /// стены из ~40 полей. Пути свойств — от самого ассета (Profile.*).
    /// </summary>
    [CustomEditor(typeof(TerrainProfileAsset))]
    public sealed class TerrainProfileEditor : Editor
    {
        private static bool showShape = true;
        private static bool showContinents = true;
        private static bool showPlains;
        private static bool showBeach = true;
        private static bool showWarp;
        private static bool showColor = true;
        private static bool showPalette;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            showShape = EditorGUILayout.BeginFoldoutHeaderGroup(showShape, "Форма (fBm, хребты, деталь)");
            if (showShape)
            {
                EditorGUI.indentLevel++;
                Field("Profile.AmplitudeMeters");
                Field("Profile.BaseFrequency");
                Field("Profile.Octaves");
                Field("Profile.SeaLevelMeters");
                Field("Profile.Lacunarity");
                Field("Profile.Gain");
                Slider("Profile.RidgedMix", 0f, 1f);
                Slider("Profile.DetailMix", 0f, 0.5f);
                Field("Profile.DetailFrequency");
                Field("Profile.DetailOctaves");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showContinents = EditorGUILayout.BeginFoldoutHeaderGroup(showContinents, "Континенты (океан/суша)");
            if (showContinents)
            {
                EditorGUI.indentLevel++;
                Field("Profile.ContinentFrequency");
                Field("Profile.ContinentOctaves");
                Field("Profile.ContinentThreshold");
                Slider("Profile.ContinentSharpness", 0f, 1f);
                Slider("Profile.ContinentDepth", 0f, 1.5f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showPlains = EditorGUILayout.BeginFoldoutHeaderGroup(showPlains, "Равнины");
            if (showPlains)
            {
                EditorGUI.indentLevel++;
                Slider("Profile.PlainMix", 0f, 1f);
                Field("Profile.PlainFrequency");
                Field("Profile.PlainOctaves");
                Field("Profile.PlainThreshold");
                Slider("Profile.PlainSharpness", 0f, 1f);
                Slider("Profile.PlainElevation", 0f, 1f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showBeach = EditorGUILayout.BeginFoldoutHeaderGroup(showBeach, "Пляж (полоса у воды)");
            if (showBeach)
            {
                EditorGUI.indentLevel++;
                Field("Profile.BeachHeightMeters");
                Field("Profile.BeachShelfAltitudeMeters");
                Slider("Profile.BeachShelfWidth", 0f, 0.5f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showWarp = EditorGUILayout.BeginFoldoutHeaderGroup(showWarp, "Domain warp (складки)");
            if (showWarp)
            {
                EditorGUI.indentLevel++;
                Slider("Profile.WarpStrength", 0f, 0.5f);
                Field("Profile.WarpFrequency");
                Field("Profile.WarpOctaves");
                Field("Profile.WarpSeedOffset");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showColor = EditorGUILayout.BeginFoldoutHeaderGroup(showColor, "Цвет (скалы, снег, биомы)");
            if (showColor)
            {
                EditorGUI.indentLevel++;
                Slider("Profile.ColorRockSlopeTan", 0f, 3f);
                Slider("Profile.ColorRockSlopeWidth", 0f, 1f);
                Slider("Profile.ColorRockHeightMin", 0f, 1f);
                Slider("Profile.ColorSnowSlopeTan", 0f, 3f);
                Field("Profile.ColorNoiseFrequency");
                Field("Profile.ColorNoiseOctaves");
                Slider("Profile.ColorNoiseStrength", 0f, 0.5f);
                Field("Profile.ColorNoiseSeedOffset");
                Field("Profile.ColorDetailFrequency");
                Field("Profile.ColorDetailOctaves");
                Slider("Profile.ColorDetailStrength", 0f, 1f);
                Field("Profile.ColorDetailSeedOffset");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showPalette = EditorGUILayout.BeginFoldoutHeaderGroup(showPalette, "Палитра");
            if (showPalette)
            {
                EditorGUI.indentLevel++;
                Field("Profile.Palette.Sand");
                Field("Profile.Palette.Desert");
                Field("Profile.Palette.DryGrass");
                Field("Profile.Palette.Grass");
                Field("Profile.Palette.Forest");
                Field("Profile.Palette.Tundra");
                Field("Profile.Palette.Rock");
                Field("Profile.Palette.Snow");
                Field("Profile.Palette.Sea");
                Field("Profile.Palette.Soil");
                Field("Profile.Palette.Lush");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        private void Field(string path)
        {
            InspectorFields.Field(serializedObject, path);
        }

        private void Slider(string path, float min, float max)
        {
            InspectorFields.Slider(serializedObject, path, min, max);
        }
    }
}
