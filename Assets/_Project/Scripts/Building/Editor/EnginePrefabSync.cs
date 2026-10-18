using Galilego.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Building.EditorTools
{
    /// <summary>
    /// Auto-syncs engine models in the scene with the EngineSpecs registry: add
    /// an engine with one line in EngineSpecs (the FBX path is on the same line)
    /// and the EnginePrefabs array on SpacecraftBuilder in open scenes updates
    /// by itself, without running Setup Scene. The scene is marked dirty: save
    /// with Ctrl+S (in Play Mode it works without saving too — the array is
    /// already updated in memory).
    ///
    /// The Galilego → Building → Setup Scene button does the same and saves the
    /// scene at once — use it when setting up the whole scene.
    /// </summary>
    [InitializeOnLoad]
    internal static class EnginePrefabSync
    {
        static EnginePrefabSync()
        {
            EditorApplication.delayCall += Sync;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            Sync();
        }

        /// <summary>Update EnginePrefabs on builders of open scenes from the registry.</summary>
        private static void Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            GameObject[] wanted = BuildSceneSetupTool.LoadEnginePrefabs();
            if (wanted.Length == 0)
            {
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    SpacecraftBuilder builder = roots[r].GetComponentInChildren<SpacecraftBuilder>(true);
                    if (builder != null)
                    {
                        Apply(builder, wanted, scene);
                    }
                }
            }
        }

        private static void Apply(SpacecraftBuilder builder, GameObject[] wanted, Scene scene)
        {
            if (Same(builder.EnginePrefabs, wanted))
            {
                return;
            }

            builder.EnginePrefabs = wanted;
            EditorUtility.SetDirty(builder);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Build] EnginePrefabs синхронизированы с EngineSpecs ("
                + wanted.Length + " движк.) — сохрани сцену (Ctrl+S).");
        }

        private static bool Same(GameObject[] current, GameObject[] wanted)
        {
            if (current == null || current.Length != wanted.Length)
            {
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != wanted[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
