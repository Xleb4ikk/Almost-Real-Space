using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Сборка тестовой сцены вертикального среза: центральное тело Terra со сферическим
    /// рельефом (плоская площадка, h ≡ 0 — Terrain ставит ContactZoneSliceProbe),
    /// T1 из JSON, раннер и узел зоны контакта (область ±150 м, 129×129 вершин).
    /// Сцена сохраняется отдельным ассетом, текущая сцена пользователя не трогается.
    /// </summary>
    public static class ContactZoneSliceTool
    {
        private const string ScenePath = "Assets/ContactZoneSlice.unity";

        [MenuItem("Galilego/Test Vessel/Build Contact Zone Slice Scene")]
        public static void BuildMenu()
        {
            Debug.Log(BuildScene());
        }

        /// <summary>Собрать сцену среза; вернуть отчёт.</summary>
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
            runner.MainThrustNewtons = 100000d;
            runner.MainIspSeconds = 320d;
            runner.DryMassKg = 3000d;

            System.Collections.Generic.List<PartDefinition> t1 = TestVesselSceneTool.LoadT1Parts();
            runner.Parts = t1 != null ? t1.ToArray() : new PartDefinition[0];

            var zoneGo = new GameObject("ContactZone");
            SceneManager.MoveGameObjectToScene(zoneGo, scene);
            zoneGo.AddComponent<MeshCollider>();
            var patch = zoneGo.AddComponent<TerrainPatch>();
            var proxy = zoneGo.AddComponent<ContactZoneProxy>();
            var host = zoneGo.AddComponent<ContactZoneHost>();
            var probe = zoneGo.AddComponent<ContactZoneSliceProbe>();
            host.Runner = runner;
            host.Proxy = proxy;
            host.Patch = patch;
            probe.Runner = runner;
            probe.Host = host;
            probe.Proxy = proxy;

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            return string.Format(
                "Срез: деталей T1={0}, сцена \"{1}\" {2}",
                runner.Parts.Length, ScenePath, saved ? "сохранена" : "НЕ сохранена");
        }
    }
}
