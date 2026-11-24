using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Terrain profile asset inspector: collapsible sections instead of a flat wall
    /// of ~40 fields. Property paths are relative to the asset itself (Profile.*).
    /// </summary>
    [CustomEditor(typeof(TerrainProfileAsset))]
    public sealed class TerrainProfileEditor : Editor
    {
        private static bool showShape = true;
        private static bool showContinents = true;
        private static bool showContinentShape;
        private static bool showOcean;
        private static bool showOrogeny;
        private static bool showPlains;
        private static bool showBeach = true;
        private static bool showPolarIce = true;
        private static bool showWarp;
        private static bool showColor = true;
        private static bool showPalette;
        private static bool showLunar;
        private static bool showMartian;

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

            showContinentShape = EditorGUILayout.BeginFoldoutHeaderGroup(
                showContinentShape, "Континенты: отдельный gain, warp и форма");
            if (showContinentShape)
            {
                EditorGUI.indentLevel++;
                Slider("Profile.ContinentGain", 0f, 0.8f);
                Slider("Profile.ContinentWarpStrength", 0f, 0.6f);
                Field("Profile.ContinentWarpFrequency");
                Field("Profile.ContinentWarpOctaves");
                Slider("Profile.ContinentRidgeMix", 0f, 0.4f);
                Field("Profile.ContinentRidgeFrequency");
                Field("Profile.ContinentRidgeOctaves");
                Slider("Profile.ContinentLatitudeBias", 0f, 0.2f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showOcean = EditorGUILayout.BeginFoldoutHeaderGroup(showOcean, "Океан и внутренность суши");
            if (showOcean)
            {
                EditorGUI.indentLevel++;
                Slider("Profile.OceanFloorDepth", 0f, 1.5f);
                Slider("Profile.OceanShelfDepth", 0f, 0.2f);
                Slider("Profile.InteriorFloor", 0f, 0.2f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showOrogeny = EditorGUILayout.BeginFoldoutHeaderGroup(showOrogeny, "Горные пояса");
            if (showOrogeny)
            {
                EditorGUI.indentLevel++;
                Field("Profile.OrogenyFrequency");
                Field("Profile.OrogenyOctaves");
                Field("Profile.OrogenyThreshold");
                Slider("Profile.OrogenySharpness", 0f, 1f);
                Slider("Profile.OrogenyFloor", 0f, 1f);
                Slider("Profile.OrogenyGain", 0f, 1f);
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
                Slider("Profile.BeachShelfWidthMaxScale", 1f, 20f);
                Field("Profile.BeachShelfWidthNoiseFrequency");
                Field("Profile.BeachShelfWidthNoiseOctaves");
                Field("Profile.BeachShelfSeawardWidth");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showPolarIce = EditorGUILayout.BeginFoldoutHeaderGroup(showPolarIce, "Полярные льды");
            if (showPolarIce)
            {
                EditorGUI.indentLevel++;
                Field("Profile.PolarIceLatitudeDeg");
                Field("Profile.PolarIceFadeDeg");
                Field("Profile.PolarIceThicknessMeters");
                Field("Profile.PolarIceEdgeAmplitudeDeg");
                Field("Profile.PolarIceEdgeFrequency");
                Field("Profile.PolarIceEdgeOctaves");
                EditorGUILayout.HelpBox(
                    "Широта края шапки: 66.5° — земные ~8 % поверхности (площадь выше φ = (1 − sin φ)/2 сферы). "
                    + "Толщина — это плоский морской лёд на высоте уровня моря + N м; суша выше него сохраняет рельеф.",
                    MessageType.Info);
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

            showMartian = EditorGUILayout.BeginFoldoutHeaderGroup(showMartian, "Марсианский рельеф (заменяет форму выше)");
            if (showMartian)
            {
                EditorGUI.indentLevel++;
                var asset = (TerrainProfileAsset)target;
                if (asset.Profile != null && asset.Profile.Martian.Enabled == 0)
                {
                    EditorGUILayout.HelpBox("Выключен (Enabled = 0): работает обычная форма. Включите кнопкой ниже или создайте планету через Tools/Galilego/Planets.", MessageType.Info);
                }

                SerializedProperty martian = serializedObject.FindProperty("Profile.Martian");
                if (martian != null)
                {
                    EditorGUILayout.PropertyField(martian, true);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Заполнить: Марс-подобный"))
                {
                    Undo.RecordObject(asset, "Mars-like relief preset");
                    asset.Profile.Martian = MartianReliefParams.CreateMarsLike();
                    EditorUtility.SetDirty(asset);
                    serializedObject.Update();
                }

                if (GUILayout.Button("Вариант по сиду…"))
                {
                    Undo.RecordObject(asset, "Mars-like relief variant");
                    asset.Profile.Martian = MartianReliefParams.CreateVariant(Random.Range(1, 100000));
                    EditorUtility.SetDirty(asset);
                    serializedObject.Update();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.HelpBox("AmplitudeMeters профиля должна равняться HeightUnitMeters × (R / 3389.5 км), SeaLevelMeters — глубокий «нуль» (≈ −17000 м × то же), ColorRockSlopeTan = 0. Это делает Tools/Galilego/Planets.", MessageType.None);
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

            showLunar = EditorGUILayout.BeginFoldoutHeaderGroup(showLunar, "Лунный рельеф (кратеры, моря, бассейны)");
            if (showLunar)
            {
                EditorGUI.indentLevel++;
                SerializedProperty lunar = serializedObject.FindProperty("Profile.Lunar");
                if (lunar != null)
                {
                    EditorGUILayout.PropertyField(lunar, true);
                }

                Field("Profile.NormalEpsilonMeters");
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
