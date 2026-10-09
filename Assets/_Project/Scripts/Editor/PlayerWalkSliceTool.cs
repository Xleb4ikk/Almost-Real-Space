using Galilego.Simulation.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сцена регресса ходьбы: Terra со сферическим рельефом (плоская площадка,
    /// h ≡ 0), раннер и PlayerWalkProbe (60 с ходьбы на восток). Используется
    /// дважды: до и после интеграции PlayerSurfaceController, сравнение 1 мм.
    /// </summary>
    public static class PlayerWalkSliceTool
    {
        private const string ScenePath = "Assets/PlayerWalkSlice.unity";
        private const string RoofScenePath = "Assets/PlayerRoofSlice.unity";
        private const string SitePrefabPath = "Assets/_Project/Sites/Место 1Site 1.prefab";

        [MenuItem("Galilego/Test Vessel/Build Player Walk Slice Scene")]
        public static void BuildMenu()
        {
            Debug.Log(BuildScene());
        }

        [MenuItem("Galilego/Test Vessel/Build Player Roof Slice Scene")]
        public static void BuildRoofMenu()
        {
            Debug.Log(BuildRoofScene());
        }

        public static string BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SimulationRunner runner = BuildRunner(scene);

            var probeGo = new GameObject("PlayerWalkProbe");
            SceneManager.MoveGameObjectToScene(probeGo, scene);
            var probe = probeGo.AddComponent<PlayerWalkProbe>();
            probe.Runner = runner;

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            return string.Format("Walk-срез: сцена \"{0}\" {1}", ScenePath, saved ? "сохранена" : "НЕ сохранена");
        }

        /// <summary>Сцена пробы крыши: тот же мир + место (SurfaceSite) в точке спавна (0,0).</summary>
        public static string BuildRoofScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SimulationRunner runner = BuildRunner(scene);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SitePrefabPath);
            if (prefab == null)
            {
                EditorSceneManager.CloseScene(scene, true);
                return "Roof-срез: префаб места не найден: " + SitePrefabPath;
            }

            var siteGo = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var site = siteGo.GetComponentInChildren<SurfaceSite>(true);
            if (site != null)
            {
                site.LatitudeDegrees = 0d;
                site.LongitudeDegrees = 0d;
            }

            var probeGo = new GameObject("PlayerRoofProbe");
            SceneManager.MoveGameObjectToScene(probeGo, scene);
            var probe = probeGo.AddComponent<PlayerRoofProbe>();
            probe.Runner = runner;

            bool saved = EditorSceneManager.SaveScene(scene, RoofScenePath);
            EditorSceneManager.CloseScene(scene, true);
            return string.Format("Roof-срез: сцена \"{0}\" {1}", RoofScenePath, saved ? "сохранена" : "НЕ сохранена");
        }

        private static SimulationRunner BuildRunner(Scene scene)
        {
            var systemGo = new GameObject("System");
            SceneManager.MoveGameObjectToScene(systemGo, scene);
            var authoring = systemGo.AddComponent<StarSystemAuthoring>();

            var starGo = new GameObject("Star");
            starGo.transform.SetParent(systemGo.transform, false);
            var star = starGo.AddComponent<BodyAuthoring>();
            star.StandardGravitationalParameter = 1.327e20d;
            star.Radius = 6.96e8d;

            var terraGo = new GameObject("Terra");
            terraGo.transform.SetParent(starGo.transform, false);
            var terra = terraGo.AddComponent<BodyAuthoring>();
            terra.StandardGravitationalParameter = 1.28e13d;
            terra.Radius = 1143000d;
            terra.RotationPeriodSeconds = 86400d;
            terra.SemiMajorAxis = 1.5e11d;
            terra.Eccentricity = 0.017d;

            var runnerGo = new GameObject("Runner");
            SceneManager.MoveGameObjectToScene(runnerGo, scene);
            var runner = runnerGo.AddComponent<SimulationRunner>();
            runner.System = authoring;
            runner.SpawnBodyName = "Terra";
            runner.SpawnOnSurface = true;
            runner.SpawnMassKg = 5000d;
            return runner;
        }
    }
}
