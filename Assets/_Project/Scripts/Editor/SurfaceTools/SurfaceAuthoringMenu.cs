using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Меню авторинга поверхности: создать фрейм, показать/скрыть превью,
    /// развернуть префаб в базу. Здесь то, что иначе пришлось бы делать
    /// руками каждый раз при новой точке спавна.
    /// </summary>
    public static class SurfaceAuthoringMenu
    {
        private const string Root = "Tools/Galilego/Surface authoring/";
        private const string FrameName = "SurfaceFrame";

        [MenuItem(Root + "Create surface frame at spawn", false, 100)]
        private static void CreateFrameAtSpawn()
        {
            SimulationRunner runner = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
            if (runner == null)
            {
                Debug.LogError("Surface authoring: в сцене нет SimulationRunner — негде взять точку спавна.");
                return;
            }

            BodyAuthoring body = null;
            foreach (BodyAuthoring candidate in SurfaceSceneSystem.AllAuthorings())
            {
                if (candidate.gameObject.name == runner.SpawnBodyName)
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null)
            {
                Debug.LogError("Surface authoring: тело спавна \"" + runner.SpawnBodyName + "\" не найдено среди BodyAuthoring.");
                return;
            }

            var existing = UnityEngine.Object.FindAnyObjectByType<SurfaceFrame>();
            if (existing != null)
            {
                Debug.Log("Surface authoring: фрейм уже есть (" + existing.name + "), обновляю точку спавна.");
                Undo.RecordObject(existing, "Surface frame: точка спавна");
                existing.Body = body;
                existing.LatitudeDegrees = runner.SpawnLatitudeDegrees;
                existing.LongitudeDegrees = runner.SpawnLongitudeDegrees;
                existing.SnapToTerrain = runner.SpawnOnSurface;
                EditorUtility.SetDirty(existing);
                SurfacePreviewTool.Invalidate();
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject(FrameName);
            Undo.RegisterCreatedObjectUndo(go, "Create surface frame");
            var frame = Undo.AddComponent<SurfaceFrame>(go);
            frame.Body = body;
            frame.Runner = runner;
            frame.LatitudeDegrees = runner.SpawnLatitudeDegrees;
            frame.LongitudeDegrees = runner.SpawnLongitudeDegrees;
            frame.SnapToTerrain = runner.SpawnOnSurface;
            frame.HeightOffsetMeters = 0d;

            Selection.activeGameObject = go;
            SurfacePreviewTool.FocusSceneView(frame);
            SurfacePreviewTool.Invalidate();
            Debug.Log("Surface authoring: фрейм создан в точке спавна " +
                frame.LatitudeDegrees.ToString("F3") + "°, " + frame.LongitudeDegrees.ToString("F3") + "°.");
        }

        [MenuItem(Root + "Focus surface frame in Scene view", false, 103)]
        private static void FocusFrame()
        {
            SurfaceFrame frame = SurfacePreviewTool.ResolveFrame();
            if (frame == null)
            {
                Debug.LogError("Surface authoring: в сцене нет SurfaceFrame.");
                return;
            }

            SurfacePreviewTool.FocusSceneView(frame);
        }

        [MenuItem(Root + "Rebuild surface preview", false, 101)]
        private static void RebuildPreview()
        {
            SurfacePreviewTool.DestroyPreview();
            SurfacePreviewTool.Invalidate();
            SceneView.RepaintAll();
        }

        [MenuItem(Root + "Hide surface preview", false, 102)]
        private static void HidePreview()
        {
            SurfacePreviewTool.Enabled = false;
            SurfacePreviewTool.DestroyPreview();
            SceneView.RepaintAll();
        }

        [MenuItem(Root + "Drop prefab into surface frame", false, 120)]
        private static void DropPrefab()
        {
            Object prefab = Selection.activeObject;
            if (prefab == null || !EditorUtility.IsPersistent(prefab))
            {
                Debug.LogError("Surface authoring: выберите в Project ассет-префаб, который хотите поставить.");
                return;
            }

            SurfaceFrame frame = SurfacePreviewTool.ResolveFrame();
            if (frame == null)
            {
                Debug.LogError("Surface authoring: в сцене нет SurfaceFrame.");
                return;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, frame.transform);
            Undo.RegisterCreatedObjectUndo(go, "Drop prefab into surface frame");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            Selection.activeGameObject = go;
        }

        [MenuItem(Root + "Drop prefab into surface frame", true)]
        private static bool DropPrefabValidate()
        {
            return Selection.activeObject != null && EditorUtility.IsPersistent(Selection.activeObject);
        }
    }
}
