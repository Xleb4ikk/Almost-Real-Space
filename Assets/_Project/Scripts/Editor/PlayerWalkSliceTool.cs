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

        [MenuItem("Galilego/Test Vessel/Build Player Walk Slice Scene")]
        public static void BuildMenu()
        {
            Debug.Log(BuildScene());
        }

        public static string BuildScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

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

            var probeGo = new GameObject("PlayerWalkProbe");
            SceneManager.MoveGameObjectToScene(probeGo, scene);
            var probe = probeGo.AddComponent<PlayerWalkProbe>();
            probe.Runner = runner;

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            return string.Format("Walk-срез: сцена \"{0}\" {1}", ScenePath, saved ? "сохранена" : "НЕ сохранена");
        }
    }
}
