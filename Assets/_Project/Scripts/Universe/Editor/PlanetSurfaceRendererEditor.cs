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
