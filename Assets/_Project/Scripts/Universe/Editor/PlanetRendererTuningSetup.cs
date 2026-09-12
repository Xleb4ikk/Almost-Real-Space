using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Разово поднимает бюджеты LOD/кэша у всех PlanetSurfaceRenderer в сценах
    /// (сцена хранит собственные сериализованные значения): быстрее строится
    /// ближний LOD, меньше «мигания» чанков и декора при движении.
    /// </summary>
    public static class PlanetRendererTuningSetup
    {
        internal const string Marker = "Temp/renderer-tuning-v1.done";

        [MenuItem("Tools/Galilego/Tune planet LOD budgets")]
        public static void Run()
        {
            int count = 0;
            bool sceneChanged = false;
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    PlanetSurfaceRenderer[] renderers = roots[r].GetComponentsInChildren<PlanetSurfaceRenderer>(true);
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        PlanetSurfaceRenderer renderer = renderers[i];
                        renderer.BuildsPerFrame = Mathf.Max(renderer.BuildsPerFrame, 64);
                        renderer.MaxCachedChunks = Mathf.Max(renderer.MaxCachedChunks, 8192);
                        renderer.MaxNodes = Mathf.Max(renderer.MaxNodes, 12000);
                        EditorUtility.SetDirty(renderer);
                        count++;
                        sceneChanged = true;
                    }
                }

                if (sceneChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }

            if (sceneChanged)
            {
                EditorSceneManager.SaveOpenScenes();
            }

            Debug.Log("[PlanetRendererTuningSetup] обновлено рендереров: " + count);
        }
    }

    /// <summary>Первичный автозапуск по маркеру.</summary>
    [InitializeOnLoad]
    internal static class PlanetRendererTuningSetupAuto
    {
        static PlanetRendererTuningSetupAuto()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            if (File.Exists(PlanetRendererTuningSetup.Marker))
            {
                return;
            }

            try
            {
                PlanetRendererTuningSetup.Run();
                File.WriteAllText(PlanetRendererTuningSetup.Marker, System.DateTime.Now.ToString("O"));
            }
            catch (System.Exception exception)
            {
                Debug.LogError("[PlanetRendererTuningSetup] ошибка: " + exception);
            }
        }
    }
}
