using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор тела: физика/орбита/вращение плоскими секциями, рельеф и
    /// атмосфера — ссылками на ассеты-пресеты (редактируются встроенно своими
    /// инспекторами). Плоские ~45 полей рельефа ушли в TerrainProfileAsset:
    /// один пресет можно переиспользовать между телами и сценами.
    /// </summary>
    [CustomEditor(typeof(BodyAuthoring))]
    public sealed class BodyAuthoringEditor : Editor
    {
        private static bool showBody = true;
        private static bool showOrbit = true;
        private static bool showRotation = true;
        private static bool showProfiles = true;

        private Editor terrainEditor;
        private Editor atmosphereEditor;
        private Editor cloudsEditor;
        private Editor decorEditor;
        private Object terrainEditorTarget;
        private Object atmosphereEditorTarget;
        private Object cloudsEditorTarget;
        private Object decorEditorTarget;

        private void OnDisable()
        {
            if (terrainEditor != null)
            {
                DestroyImmediate(terrainEditor);
            }

            if (atmosphereEditor != null)
            {
                DestroyImmediate(atmosphereEditor);
            }

            if (cloudsEditor != null)
            {
                DestroyImmediate(cloudsEditor);
            }

            if (decorEditor != null)
            {
                DestroyImmediate(decorEditor);
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawBody();
            DrawProfiles();
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

        private void DrawProfiles()
        {
            showProfiles = EditorGUILayout.BeginFoldoutHeaderGroup(showProfiles, "Профили-пресеты");
            if (showProfiles)
            {
                EditorGUI.indentLevel++;

                Field("TerrainPreset");
                TerrainProfileAsset terrainPreset = serializedObject.FindProperty("TerrainPreset").objectReferenceValue as TerrainProfileAsset;
                Field("TerrainSeed");
                if (terrainPreset == null)
                {
                    EditorGUILayout.HelpBox("Рельеф выключен (гладкая сфера). Пресет-пример: Assets/_Project/Profiles/Terrain/EarthLike.asset.", MessageType.Info);
                }
                else
                {
                    DrawNested(terrainPreset, ref terrainEditor, ref terrainEditorTarget);
                }

                EditorGUILayout.Space(6);
                Field("AtmospherePreset");
                AtmosphereProfileAsset atmospherePreset = serializedObject.FindProperty("AtmospherePreset").objectReferenceValue as AtmosphereProfileAsset;
                if (atmospherePreset == null)
                {
                    EditorGUILayout.HelpBox("Атмосферы нет (физика и визуал выключены). Пресет-пример: Assets/_Project/Profiles/Atmosphere/EarthAtmosphere.asset.", MessageType.Info);
                }
                else
                {
                    DrawNested(atmospherePreset, ref atmosphereEditor, ref atmosphereEditorTarget);
                }

                EditorGUILayout.Space(6);
                Field("CloudsPreset");
                CloudProfileAsset cloudsPreset = serializedObject.FindProperty("CloudsPreset").objectReferenceValue as CloudProfileAsset;
                if (cloudsPreset == null)
                {
                    EditorGUILayout.HelpBox("Облака выключены. Создайте пресет Galilego/Cloud Profile.", MessageType.Info);
                }
                else
                {
                    DrawNested(cloudsPreset, ref cloudsEditor, ref cloudsEditorTarget);
                }

                EditorGUILayout.Space(6);
                Field("GroundDecorPreset");
                GroundDecorProfileAsset decorPreset = serializedObject.FindProperty("GroundDecorPreset").objectReferenceValue as GroundDecorProfileAsset;
                if (decorPreset == null)
                {
                    EditorGUILayout.HelpBox("Декора местности нет (слой выключен).", MessageType.Info);
                }
                else
                {
                    DrawNested(decorPreset, ref decorEditor, ref decorEditorTarget);
                }

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
                const string presetPath = "Assets/_Project/Profiles/Terrain/EarthLike.asset";
                TerrainProfileAsset preset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(presetPath);
                if (preset == null)
                {
                    Debug.LogWarning("[BodyAuthoring] нет ассета " + presetPath + ".");
                    return;
                }

                Undo.RecordObject(authoring, "Assign Earth-like terrain preset");
                authoring.TerrainPreset = preset;
                EditorUtility.SetDirty(authoring);
                serializedObject.Update();

                PlanetSurfaceRenderer renderer = authoring.GetComponent<PlanetSurfaceRenderer>();
                if (renderer != null && Application.isPlaying)
                {
                    renderer.ApplyAuthoringAndRebuild();
                }
            }
        }

        private void DrawNested(Object asset, ref Editor cachedEditor, ref Object cachedTarget)
        {
            if (cachedEditor == null || cachedTarget != asset)
            {
                if (cachedEditor != null)
                {
                    DestroyImmediate(cachedEditor);
                }

                cachedEditor = CreateEditor(asset);
                cachedTarget = asset;
            }

            EditorGUILayout.Space(4);
            cachedEditor.OnInspectorGUI();
        }

        private void Field(string path)
        {
            InspectorFields.Field(serializedObject, path);
        }
    }
}
