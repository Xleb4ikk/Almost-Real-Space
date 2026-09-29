using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор рендера планеты: LOD и производительность разнесены по
    /// секциям, dev-действия (пересборка чанков, очистка кэша) — кнопками.
    /// Кнопки работают только в Play: вне его живой HeightfieldTerrain ещё не
    /// существует, и пересобирать нечего.
    /// </summary>
    [CustomEditor(typeof(PlanetSurfaceRenderer))]
    public sealed class PlanetSurfaceRendererEditor : Editor
    {
        private static bool showLod = true;
        private static bool showPerformance;
        private static bool showAsync;
        private static bool showTiers;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorFields.Field(serializedObject, "Runner");

            showLod = EditorGUILayout.BeginFoldoutHeaderGroup(showLod, "LOD (нарезка рельефа)");
            if (showLod)
            {
                EditorGUI.indentLevel++;
                Field("TileResolution");
                Field("MaxDepth");
                Field("SplitFactor");
                Field("MergeHysteresis");
                Field("SkirtFactor");
                // [ФАЗА 2.5] Раньше в этом списке не было MinTileResolution и
                // ResolutionHalveEveryLevels, хотя оба напрямую определяют
                // разрешение дальних чанков. Поля были видны только в
                // «полном» инспекторе, то есть настраивать их приходилось
                // переключателем вверху окна — мелочь, которая стоила реального
                // времени при подборе LOD.
                Field("MinTileResolution");
                Field("ResolutionHalveEveryLevels");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showAsync = EditorGUILayout.BeginFoldoutHeaderGroup(showAsync, "Асинхронная постройка (Фаза 2)");
            if (showAsync)
            {
                EditorGUI.indentLevel++;
                Field("AsyncChunkBuild");
                Field("MaxChunkJobsInFlight");
                Field("ChunkFinalizeBudgetMs");
                Field("MaxChunkQueueAgeFrames");
                EditorGUILayout.HelpBox(
                    "AsyncChunkBuild выключен по умолчанию осознанно: код написан "
                    + "и замеры сняты (постройка 42.75 -> 0.3-1.4 мс/кадр, очередь "
                    + "дренируется), но дефолтом включать рельеф, который можно "
                    + "не увидеть, нельзя. Включайте и смотрите глазами.",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showTiers = EditorGUILayout.BeginFoldoutHeaderGroup(showTiers, "Тиры качества по скорости (Фаза 3)");
            if (showTiers)
            {
                EditorGUI.indentLevel++;
                Field("EnableQualityTiers");
                Field("TierFastSpeed");
                Field("TierExtremeSpeed");
                Field("TierDownDelaySeconds");
                Field("TierDepthDropFast");
                Field("TierDepthDropExtreme");
                Field("TierSplitScaleFast");
                Field("TierSplitScaleExtreme");
                Field("TierAutoRaise");
                Field("TierTargetFrameMs");
                Field("PrefetchEnabled");
                Field("PrefetchSeconds");
                Field("PrefetchVelSmoothSeconds");
                Field("PrefetchMinLeadMeters");
                Field("MaxPrefetchNodes");
                Field("MaxPrefetchQueued");
                Field("TierHalveResolution");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            showPerformance = EditorGUILayout.BeginFoldoutHeaderGroup(showPerformance, "Производительность");
            if (showPerformance)
            {
                EditorGUI.indentLevel++;
                Field("BuildsPerFrame");
                Field("MaxNodes");
                Field("MaxCachedChunks");
                Field("MaxEvictionsPerFrame");
                Field("CullBeyondHorizon");
                Field("HorizonMargin");
                Field("LogGeometryStats");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();

            DrawActions();
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(6);
            PlanetSurfaceRenderer renderer = (PlanetSurfaceRenderer)target;

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Пересборка и очистка кэша доступны в Play-режиме.", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Применить параметры рельефа + пересобрать"))
                {
                    renderer.ApplyAuthoringAndRebuild();
                }

                if (GUILayout.Button("Пресет «Земля» + пересобрать"))
                {
                    AssignEarthLikePreset(renderer);
                }

                if (GUILayout.Button("Очистить кэш мешей"))
                {
                    renderer.ClearChunkCache();
                }
            }
        }

        private static void AssignEarthLikePreset(PlanetSurfaceRenderer renderer)
        {
            const string presetPath = "Assets/_Project/Profiles/Terrain/EarthLike.asset";
            BodyAuthoring authoring = renderer.GetComponent<BodyAuthoring>();
            TerrainProfileAsset preset = AssetDatabase.LoadAssetAtPath<TerrainProfileAsset>(presetPath);
            if (authoring == null || preset == null)
            {
                Debug.LogWarning("[PlanetSurfaceRenderer] нет BodyAuthoring или ассета " + presetPath + ".");
                return;
            }

            Undo.RecordObject(authoring, "Assign Earth-like terrain preset");
            authoring.TerrainPreset = preset;
            EditorUtility.SetDirty(authoring);
            renderer.ApplyAuthoringAndRebuild();
        }

        private void Field(string path)
        {
            InspectorFields.Field(serializedObject, path);
        }
    }
}
