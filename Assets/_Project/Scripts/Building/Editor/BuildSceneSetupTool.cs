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
    /// Одноразовая автонастройка сцены для системы строительства
    /// (меню Galilego → Building → Setup Scene):
    ///   1. Вешает LaunchPadZone на «Площадку стартовую» в ПРЕФАБЕ места
    ///      (источник истины о зоне строительства — там же, где геометрия плиты).
    ///   2. Создаёт в сцене «ContactZone» (Host + Proxy + Patch + MeshCollider).
    ///   3. Создаёт «Building» (SpacecraftBuilder) и назначает все ссылки:
    ///      Runner, Ship, зона, зона контакта, модель RS-25, материалы деталей.
    ///   4. Сохраняет сцену.
    /// Инструмент идемпотентен: повторный запуск обновляет поля, не плодит объекты.
    /// </summary>
    public static class BuildSceneSetupTool
    {
        private const string EnginePath = "Assets/_Project/Models/Parts/Engines/RS-25/RS25_Engine.fbx";
        private const string TankSidePath = "Assets/_Project/Materials/Parts/FuelTank_Metal.mat";
        private const string TankCapPath = "Assets/_Project/Materials/Parts/FuelTank_Cap.mat";
        private const string SeparatorPath = "Assets/_Project/Materials/Parts/Separator_Metal.mat";
        private const string PadObjectName = "Площадка стартовая";

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

        /// <summary>BuilderUI и VesselMirror на том же объекте: ссылки на билдер.</summary>
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

        /// <summary>LaunchPadZone — в префаб места (на объект «Площадка стартовая»).</summary>
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

        /// <summary>GO «ContactZone»: точная физика у земли (Host + Proxy + Patch).</summary>
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

        /// <summary>GO «Building»: режим строительства, ссылки на детали и зону.</summary>
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
            builder.EnginePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnginePath);
            builder.TankSideMaterial = AssetDatabase.LoadAssetAtPath<Material>(TankSidePath);
            builder.TankCapMaterial = AssetDatabase.LoadAssetAtPath<Material>(TankCapPath);
            builder.SeparatorMaterial = AssetDatabase.LoadAssetAtPath<Material>(SeparatorPath);

            if (builder.Zone == null)
            {
                Debug.LogWarning("[BuildSetup] LaunchPadZone в сцене не видна — возможно, Unity не "
                    + "переимпортировал префаб места. Запусти Setup Scene ещё раз.");
            }

            if (builder.EnginePrefab == null || builder.TankSideMaterial == null
                || builder.TankCapMaterial == null || builder.SeparatorMaterial == null)
            {
                Debug.LogWarning("[BuildSetup] Часть ссылок на ассеты не нашлась — проверь пути.");
            }

            return builder;
        }
    }
}
