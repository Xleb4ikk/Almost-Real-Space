using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор тела: вместо плоской стены из ~45 полей рельефа — сворачиваемые
    /// секции (Тело / Атмосфера / Рельеф), а физически второстепенные параметры
    /// (художественные тинты, континенты, равнины, warp, цвет) — под тумблером
    /// «Дополнительно». Это ЧИСТО ВИЗУАЛЬНОЕ изменение: пути сериализации не
    /// меняются, значения сцены сохраняются. Изменение модели данных (вложенный
    /// TerrainProfile) — отдельная задача.
    /// </summary>
    [CustomEditor(typeof(BodyAuthoring))]
    public sealed class BodyAuthoringEditor : Editor
    {
        private static bool showBody = true;
        private static bool showOrbit = true;
        private static bool showRotation = true;

        private static bool showAtmosphere = true;
        private static bool showTerrain = true;
        private static bool showAtmosphereAdvanced;
        private static bool showTerrainAdvanced;

        private static bool showShape = true;
        private static bool showContinents = true;
        private static bool showPlains;
        private static bool showWarp;
        private static bool showColor = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBody();
            DrawAtmosphere();
            DrawTerrain();
            DrawActions();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawBody()
        {
            showBody = EditorGUILayout.BeginFoldoutHeaderGroup(showBody, "Тело");
            if (showBody)
            {
                EditorGUI.indentLevel++;
                InspectorFields.Label("Физика (СИ)");
                Field("StandardGravitationalParameter");
                Field("Radius");
                Field("CrashToleranceMps");
                Field("SurfaceStaticFrictionMu");
                Field("SurfaceKineticFrictionMu");

                showOrbit = EditorGUILayout.Foldout(showOrbit, "Кеплеровы элементы (относительно родителя)", true);
                if (showOrbit)
                {
                    EditorGUI.indentLevel++;
                    Field("SemiMajorAxis");
                    Field("Eccentricity");
                    Field("InclinationDegrees");
                    Field("LongitudeOfAscendingNodeDegrees");
                    Field("ArgumentOfPeriapsisDegrees");
                    Field("MeanAnomalyAtEpochDegrees");
                    Field("EpochTimeSeconds");
                    EditorGUI.indentLevel--;
                }

                showRotation = EditorGUILayout.Foldout(showRotation, "Вращение вокруг оси", true);
                if (showRotation)
                {
                    EditorGUI.indentLevel++;
                    Field("RotationPeriodSeconds");
                    Field("TidallyLocked");
                    Field("PrimeMeridianOffsetDegrees");
                    Field("NorthPoleDirection");
                    EditorGUI.indentLevel--;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawAtmosphere()
        {
            showAtmosphere = EditorGUILayout.BeginFoldoutHeaderGroup(showAtmosphere, "Атмосфера");
            if (showAtmosphere)
            {
                EditorGUI.indentLevel++;
                Field("Atmosphere.TopAltitudeMeters");
                Field("Atmosphere.SeaLevelDensityKgPerCubicMeter");
                Field("Atmosphere.ScaleHeightMeters");
                Field("Atmosphere.OzoneEnabled");
                Field("Atmosphere.VisualEnabled");

                DrawAdvanced(ref showAtmosphereAdvanced, () =>
                {
                    Field("Atmosphere.RayleighColor");
                    Field("Atmosphere.MieColor");
                    Field("Atmosphere.Intensity");
                    Field("Atmosphere.MieAnisotropy");
                    Field("Atmosphere.AerosolScale");
                    Field("Atmosphere.StepCount");
                    Field("Atmosphere.PlanetOcclusion");
                    Field("Atmosphere.GroundColor");
                    Field("Atmosphere.HorizonFade");
                });

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawTerrain()
        {
            showTerrain = EditorGUILayout.BeginFoldoutHeaderGroup(showTerrain, "Процедурный рельеф");
            if (showTerrain)
            {
                EditorGUI.indentLevel++;
                Field("TerrainEnabled");
                Field("TerrainSeed");
                Field("TerrainAmplitudeMeters");
                Field("TerrainBaseFrequency");
                Field("TerrainOctaves");
                Field("TerrainSeaLevelMeters");

                DrawAdvanced(ref showTerrainAdvanced, () =>
                {
                    showShape = EditorGUILayout.Foldout(showShape, "Форма (fBm, хребты, деталь)", true);
                    if (showShape)
                    {
                        EditorGUI.indentLevel++;
                        Field("TerrainLacunarity");
                        Field("TerrainGain");
                        Slider("TerrainRidgedMix", 0f, 1f);
                        Slider("TerrainDetailMix", 0f, 0.5f);
                        Field("TerrainDetailFrequency");
                        Field("TerrainDetailOctaves");
                        EditorGUI.indentLevel--;
                    }

                    showContinents = EditorGUILayout.Foldout(showContinents, "Континенты (океан/суша)", true);
                    if (showContinents)
                    {
                        EditorGUI.indentLevel++;
                        Field("TerrainContinentFrequency");
                        Field("TerrainContinentOctaves");
                        Field("TerrainContinentThreshold");
                        Slider("TerrainContinentSharpness", 0f, 1f);
                        Slider("TerrainContinentDepth", 0f, 1.5f);
                        EditorGUI.indentLevel--;
                    }

                    showPlains = EditorGUILayout.Foldout(showPlains, "Равнины", true);
                    if (showPlains)
                    {
                        EditorGUI.indentLevel++;
                        Slider("TerrainPlainMix", 0f, 1f);
                        Field("TerrainPlainFrequency");
                        Field("TerrainPlainOctaves");
                        Field("TerrainPlainThreshold");
                        Slider("TerrainPlainSharpness", 0f, 1f);
                        Slider("TerrainPlainElevation", 0f, 1f);
                        EditorGUI.indentLevel--;
                    }

                    showWarp = EditorGUILayout.Foldout(showWarp, "Domain warp (складки)", true);
                    if (showWarp)
                    {
                        EditorGUI.indentLevel++;
                        Slider("TerrainWarpStrength", 0f, 0.5f);
                        Field("TerrainWarpFrequency");
                        Field("TerrainWarpOctaves");
                        Field("TerrainWarpSeedOffset");
                        EditorGUI.indentLevel--;
                    }

                    showColor = EditorGUILayout.Foldout(showColor, "Цвет (скалы, снег, биомы)", true);
                    if (showColor)
                    {
                        EditorGUI.indentLevel++;
                        Slider("TerrainColorRockSlopeTan", 0f, 3f);
                        Slider("TerrainColorRockSlopeWidth", 0f, 1f);
                        Slider("TerrainColorRockHeightMin", 0f, 1f);
                        Slider("TerrainColorSnowSlopeTan", 0f, 3f);
                        Field("TerrainColorNoiseFrequency");
                        Field("TerrainColorNoiseOctaves");
                        Slider("TerrainColorNoiseStrength", 0f, 0.5f);
                        Field("TerrainColorNoiseSeedOffset");
                        Field("TerrainColorDetailFrequency");
                        Field("TerrainColorDetailOctaves");
                        Slider("TerrainColorDetailStrength", 0f, 1f);
                        Field("TerrainColorDetailSeedOffset");
                        EditorGUI.indentLevel--;
                    }
                });

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(6);

            BodyAuthoring authoring = (BodyAuthoring)target;
            if (GUILayout.Button("Пресет рельефа «Земля»"))
            {
                Undo.RecordObject(authoring, "Earth-like terrain preset");
                authoring.ApplyEarthLikeTerrainPreset();
                EditorUtility.SetDirty(authoring);
                serializedObject.Update();

                PlanetSurfaceRenderer renderer = authoring.GetComponent<PlanetSurfaceRenderer>();
                if (renderer != null && Application.isPlaying)
                {
                    renderer.ApplyAuthoringAndRebuild();
                }
            }
        }

        private void DrawAdvanced(ref bool expanded, System.Action draw)
        {
            expanded = EditorGUILayout.ToggleLeft("Дополнительно", expanded);
            if (!expanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            draw();
            EditorGUI.indentLevel--;
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
