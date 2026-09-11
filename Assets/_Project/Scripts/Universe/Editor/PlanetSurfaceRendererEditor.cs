using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор рендера планеты: LOD, производительность и дальний вид разнесены
    /// по секциям, dev-действия (пересборка чанков, очистка кэша) — кнопками.
    /// Кнопки работают только в Play: вне его живой HeightfieldTerrain ещё не
    /// существует, и пересобирать нечего.
    /// </summary>
    [CustomEditor(typeof(PlanetSurfaceRenderer))]
    public sealed class PlanetSurfaceRendererEditor : Editor
    {
        private static bool showLod = true;
        private static bool showPerformance;
        private static bool showFarView = true;

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

            showFarView = EditorGUILayout.BeginFoldoutHeaderGroup(showFarView, "Дальний вид (базовая сфера)");
            if (showFarView)
            {
                EditorGUI.indentLevel++;
                Field("MaxAltitudeMeters");
                Field("BaseSphereColor");
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
                    renderer.ApplyEarthLikePresetAndRebuild();
                }

                if (GUILayout.Button("Очистить кэш мешей"))
                {
                    renderer.ClearChunkCache();
                }
            }
        }

        private void Field(string path)
        {
            InspectorFields.Field(serializedObject, path);
        }
    }
}
