using Galilego.Building;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Building.EditorTools
{
    /// <summary>
    /// Автосинхронизация моделей двигателей в сцене с реестром EngineSpecs:
    /// добавил двигатель одной строкой в EngineSpecs (путь к FBX — в той же
    /// строке) — массив EnginePrefabs у SpacecraftBuilder в открытых сценах
    /// обновится сам, без запуска Setup Scene. Сцена помечается изменённой:
    /// сохранить — Ctrl+S (в Play Mode работает и без сохранения — массив уже
    /// обновлён в памяти).
    ///
    /// Кнопка Galilego → Building → Setup Scene делает то же самое и сохраняет
    /// сцену сразу — ей пользуются, когда нужно настроить сцену целиком.
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

        /// <summary>Обновить EnginePrefabs у билдеров открытых сцен по реестру.</summary>
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
