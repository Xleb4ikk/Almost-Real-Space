using System.Collections.Generic;
using System.Linq;
using Galilego.Building;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Galilego.Building.EditorTools
{
    /// <summary>
    /// One-shot scene setup for the building system
    /// (menu Galilego → Building → Setup Scene):
    ///   1. Attaches LaunchPadZone to the pad object (PadObjectName) in the site PREFAB
    ///      (the source of truth for the build zone is next to the pad geometry).
    ///   2. Creates "ContactZone" in the scene (Host + Proxy + Patch + MeshCollider).
    ///   3. Creates "Building" (SpacecraftBuilder) and assigns all references:
    ///      Runner, Ship, zone, contact zone, engine models from the EngineSpecs
    ///      registry, part materials.
    ///   4. Saves the scene.
    /// The tool is idempotent: re-running updates fields without spawning objects.
    /// The engine model array also syncs with the registry by itself (EnginePrefabSync).
    /// </summary>
    public static class BuildSceneSetupTool
    {
        private const string TankSidePath = "Assets/_Project/Materials/Parts/FuelTank_Metal.mat";
        private const string TankCapPath = "Assets/_Project/Materials/Parts/FuelTank_Cap.mat";
        private const string SeparatorPath = "Assets/_Project/Materials/Parts/Separator_Metal.mat";
        private const string PadObjectName = "Площадка стартовая";

        /// <summary>
        /// Engine models from the EngineSpecs registry (the FBX path is in the
        /// spec): order = registry order. A missing file yields a warning and
        /// the engine is skipped.
        /// </summary>
        public static GameObject[] LoadEnginePrefabs()
        {
            var prefabs = new List<GameObject>();
            IReadOnlyList<EngineSpec> specs = EngineSpecs.All;
            for (int i = 0; i < specs.Count; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(specs[i].ModelPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[BuildSetup] модель двигателя " + specs[i].Id
                        + " не найдена: " + specs[i].ModelPath);
                    continue;
                }

                prefabs.Add(prefab);
            }

            return prefabs.ToArray();
        }

        [MenuItem("Galilego/Building/Setup Scene")]
        public static void SetupScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "OutdoorsScene")
            {
                Debug.LogError("[BuildSetup] Открой сцену OutdoorsScene — инструмент настраивает именно её.");
                return;
            }

            PatchSitePrefab();

            SimulationRunner runner = Object.FindObjectsByType<SimulationRunner>(FindObjectsInactive.Include)
                .FirstOrDefault();
            ShipView shipView = Object.FindObjectsByType<ShipView>(FindObjectsInactive.Include).FirstOrDefault();
            if (runner == null || shipView == null)
            {
                Debug.LogError("[BuildSetup] В сцене нет SimulationRunner или ShipView.");
                return;
            }

            ContactZoneHost host = EnsureContactZone(runner);
            SpacecraftBuilder builder = EnsureBuilder(runner, shipView.transform, host);
            builder.DebugSpikeStack = true;
            EnsureUi(builder);

            EditorUtility.SetDirty(builder);
            EditorSceneManager.MarkSceneDirty(scene);
            bool saved = EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = builder.gameObject;
            Debug.Log("[BuildSetup] Готово: " + (saved ? "сцена сохранена." : "сцена НЕ сохранена."));
        }

        /// <summary>BuilderUI and VesselMirror on the same object: references to the builder.</summary>
        private static void EnsureUi(SpacecraftBuilder builder)
        {
            BuilderUI ui = builder.GetComponent<BuilderUI>();
            if (ui == null)
            {
                ui = builder.gameObject.AddComponent<BuilderUI>();
            }

            ui.Builder = builder;
            ui.Camera = Object.FindObjectsByType<FirstPersonCamera>(FindObjectsInactive.Include)
                .FirstOrDefault();
            ui.Controller = Object.FindObjectsByType<PlayerController>(FindObjectsInactive.Include)
                .FirstOrDefault();

            VesselMirror mirror = builder.GetComponent<VesselMirror>();
            if (mirror == null)
            {
                mirror = builder.gameObject.AddComponent<VesselMirror>();
            }

            mirror.Builder = builder;
        }

        /// <summary>LaunchPadZone goes into the site prefab (onto the PadObjectName object).</summary>
        private static void PatchSitePrefab()
        {
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Sites" });
            if (guids.Length == 0)
            {
                Debug.LogError("[BuildSetup] Префаб места не найден в Assets/_Project/Sites.");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                SurfaceRoad pad = root.GetComponentsInChildren<SurfaceRoad>(true)
                    .FirstOrDefault(r => r.gameObject.name == PadObjectName);
                if (pad == null)
                {
                    Debug.LogError("[BuildSetup] В префабе места нет «" + PadObjectName + "».");
                    return;
                }

                LaunchPadZone zone = pad.GetComponent<LaunchPadZone>();
                if (zone == null)
                {
                    zone = pad.gameObject.AddComponent<LaunchPadZone>();
                }

                zone.Pad = pad;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[BuildSetup] LaunchPadZone добавлена в префаб " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>"ContactZone" GO: accurate physics near the ground (Host + Proxy + Patch).</summary>
        private static ContactZoneHost EnsureContactZone(SimulationRunner runner)
        {
            ContactZoneHost host = Object.FindObjectsByType<ContactZoneHost>(FindObjectsInactive.Include)
                .FirstOrDefault();
            GameObject go;
            if (host == null)
            {
                var existing = GameObject.Find("ContactZone");
                go = existing != null ? existing : new GameObject("ContactZone");
                host = go.AddComponent<ContactZoneHost>();
            }
            else
            {
                go = host.gameObject;
            }

            ContactZoneProxy proxy = go.GetComponent<ContactZoneProxy>();
            if (proxy == null)
            {
                proxy = go.AddComponent<ContactZoneProxy>();
            }

            TerrainPatch patch = go.GetComponent<TerrainPatch>();
            if (patch == null)
            {
                patch = go.AddComponent<TerrainPatch>();
            }

            host.Proxy = proxy;
            host.Patch = patch;
            host.Runner = runner;
            patch.HalfSize = 60f;
            patch.Resolution = 49;
            host.Enabled = false;
            host.Log = true;
            return host;
        }

        /// <summary>"Building" GO: build mode, references to parts and the zone.</summary>
        private static SpacecraftBuilder EnsureBuilder(SimulationRunner runner, Transform ship, ContactZoneHost host)
        {
            SpacecraftBuilder builder = Object.FindObjectsByType<SpacecraftBuilder>(FindObjectsInactive.Include)
                .FirstOrDefault();
            GameObject go;
            if (builder == null)
            {
                var existing = GameObject.Find("Building");
                go = existing != null ? existing : new GameObject("Building");
                builder = go.AddComponent<SpacecraftBuilder>();
            }

            builder.Runner = runner;
            builder.ShipTransform = ship;
            builder.Zone = Object.FindObjectsByType<LaunchPadZone>(FindObjectsInactive.Include).FirstOrDefault();
            builder.ZoneHost = host;
            builder.EnginePrefabs = LoadEnginePrefabs();
            builder.TankSideMaterial = AssetDatabase.LoadAssetAtPath<Material>(TankSidePath);
            builder.TankCapMaterial = AssetDatabase.LoadAssetAtPath<Material>(TankCapPath);
            builder.SeparatorMaterial = AssetDatabase.LoadAssetAtPath<Material>(SeparatorPath);

            if (builder.Zone == null)
            {
                Debug.LogWarning("[BuildSetup] LaunchPadZone в сцене не видна — возможно, Unity не "
                    + "переимпортировал префаб места. Запусти Setup Scene ещё раз.");
            }

            if (builder.EnginePrefabs.Length != EngineSpecs.All.Count || builder.TankSideMaterial == null
                || builder.TankCapMaterial == null || builder.SeparatorMaterial == null)
            {
                Debug.LogWarning("[BuildSetup] Часть ссылок на ассеты не нашлась — проверь пути.");
            }

            return builder;
        }
    }
}
