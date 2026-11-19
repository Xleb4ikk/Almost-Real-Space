using Galilego.Core;
using Galilego.Universe;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Galilego.EditorTools
{
    /// <summary>
    /// One-click setup for the physically based star:
    ///   — creates/loads the scene's solar StarPreset (mass = 1.0 M☉, a yellow
    ///     dwarf like the Sun; the 0.2 M☉ reference preset remains separate);
    ///   — creates/loads the Volume profile (ACES, bloom, auto exposure);
    ///   — adds StarRenderer to the scene's star visual and wires Runner,
    ///     camera, Directional Light, SkyEnvironment and the Volume.
    /// All values remain in StarPreset/StarPhysics, not in this helper.
    /// </summary>
    public static class StarSceneSetup
    {
        private const string PresetFolder = "Assets/_Project/Profiles/Stars";
        private const string PresetPath = PresetFolder + "/StarPreset_Default.asset";
        private const string SolarPresetPath = PresetFolder + "/StarPreset_Sol.asset";
        private const string VolumeProfilePath = PresetFolder + "/StarVolumeProfile.asset";

        [MenuItem("Tools/Galilego/Stars/Create Or Update Default Preset")]
        public static void CreateDefaultPreset()
        {
            StarPreset preset = CreateOrLoadPreset();
            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);
        }

        [MenuItem("Tools/Galilego/Stars/Create Or Update Solar Preset")]
        public static void CreateSolarPreset()
        {
            StarPreset preset = CreateOrLoadSolarPreset();
            Selection.activeObject = preset;
            EditorGUIUtility.PingObject(preset);
        }

        [MenuItem("Tools/Galilego/Stars/Setup Star Renderer In Open Scene")]
        public static void SetupOpenScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("[StarSceneSetup] Нет открытой сцены.");
                return;
            }

            StarRenderer renderer = Object.FindAnyObjectByType<StarRenderer>(FindObjectsInactive.Include);
            if (renderer == null)
            {
                GameObject host = FindStarHost();
                if (host == null)
                {
                    Debug.LogError(
                        "[StarSceneSetup] Не найден объект звезды. Ожидался GameObject 'SunVisual' " +
                        "или корневой BodyAuthoring без родителя-тела.");
                    return;
                }

                renderer = Undo.AddComponent<StarRenderer>(host);
                Debug.Log("[StarSceneSetup] StarRenderer добавлен на '" + host.name + "'.");
            }

            SimulationRunner runner = Object.FindAnyObjectByType<SimulationRunner>(FindObjectsInactive.Include);
            SkyEnvironment sky = Object.FindAnyObjectByType<SkyEnvironment>(FindObjectsInactive.Include);
            Camera camera = Camera.main;
            Light sun = FindDirectionalLight();

            renderer.Runner = runner;
            renderer.PlayerCamera = camera;
            renderer.Sky = sky;
            renderer.SunLight = sun;
            renderer.Preset = CreateOrLoadSolarPreset();
            renderer.StarVolume = CreateOrLoadVolume(scene);
            if (sky != null)
            {
                sky.Star = renderer;
                EditorUtility.SetDirty(sky);
            }

            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = renderer;
            Debug.Log(
                "[StarSceneSetup] Готово: StarRenderer на '" + renderer.gameObject.name + "', пресет '" +
                renderer.Preset.name + "'. Сцена помечена изменённой — сохрани её (Ctrl+S).");
        }

        [MenuItem("Tools/Galilego/Stars/Validate Star Physics")]
        public static void ValidateStarPhysics()
        {
            foreach (double mass in new[] { 0.1d, 0.2d, 0.5d, 1d, 2d, 10d })
            {
                StarState state = StarPhysics.Derive(new StarDefinition(mass, adaptation01: 0d));
                Debug.Log(string.Format(
                    "[StarPhysics] M={0,5:F2} M☉ | L={1,10:E3} L☉ | R={2,9:E3} R☉ ({3,10:N0} км) | " +
                    "Teff={4,6:F0} K | g={5,7:F1} | cell={6,7:F1} км | N={7,6:F0} | " +
                    "spots={8,5:P1} | V={9,9:E2} → V'={10,6:F2} | m(1 а.е.)={11,6:F2}",
                    mass, state.LuminositySolar, state.RadiusSolar, state.RadiusKm,
                    state.EffectiveTemperatureK, state.SurfaceGravityMps2, state.GranulationCellKm,
                    state.GranulationCells, state.SpotCoverage, state.DiscBrightness,
                    state.DiscBrightnessCompressed, state.ApparentMagnitude(StarPhysics.AuKm)));
            }
        }

        private static StarPreset CreateOrLoadPreset()
        {
            EnsureFolder(PresetFolder);
            StarPreset preset = AssetDatabase.LoadAssetAtPath<StarPreset>(PresetPath);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<StarPreset>();
                preset.MassSolar = 0.2d;
                preset.name = "StarPreset_Default";
                AssetDatabase.CreateAsset(preset, PresetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[StarSceneSetup] Создан пресет " + PresetPath + " (M = 0.2 M☉).");
            }

            return preset;
        }

        /// <summary>
        /// The scene's star is deliberately a solar analogue: mass = 1 M☉, so
        /// StarPhysics derives the Sun's R, L, Teff, g and colour. This is the
        /// explicit exception to the 0.2 M☉ reference preset used by the checks.
        /// </summary>
        private static StarPreset CreateOrLoadSolarPreset()
        {
            EnsureFolder(PresetFolder);
            StarPreset preset = AssetDatabase.LoadAssetAtPath<StarPreset>(SolarPresetPath);
            if (preset == null)
            {
                preset = ScriptableObject.CreateInstance<StarPreset>();
                preset.MassSolar = 1d;
                preset.name = "StarPreset_Sol";
                preset.ApplyDerivedRadiusToStarBody = true;
                AssetDatabase.CreateAsset(preset, SolarPresetPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[StarSceneSetup] Создан солнечный пресет " + SolarPresetPath + " (M = 1 M☉).");
            }

            return preset;
        }

        private static Volume CreateOrLoadVolume(Scene scene)
        {
            EnsureFolder(PresetFolder);
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                profile.name = "StarVolumeProfile";
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            ConfigureProfile(profile);
            AssetDatabase.SaveAssets();

            foreach (Volume existing in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            {
                if (existing.name == "Star Volume" && existing.sharedProfile == profile)
                {
                    return existing;
                }
            }

            GameObject volumeObject = new GameObject("Star Volume");
            Volume volume = Undo.AddComponent<Volume>(volumeObject);
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            Undo.RegisterCreatedObjectUndo(volumeObject, "Create Star Volume");
            return volume;
        }

        private static void ConfigureProfile(VolumeProfile profile)
        {
            Bloom bloom = GetOrAdd<Bloom>(profile);
            bloom.active = true;
            bloom.threshold.Override(1f);
            bloom.intensity.Override(1f);
            bloom.scatter.Override(0.7f);
            bloom.tint.Override(Color.white);

            Tonemapping tonemapping = GetOrAdd<Tonemapping>(profile);
            tonemapping.active = true;
            tonemapping.mode.Override(TonemappingMode.ACES);
            tonemapping.useFullACES.Override(false);

            Exposure exposure = GetOrAdd<Exposure>(profile);
            exposure.active = true;
            exposure.mode.Override(ExposureMode.Automatic);
            exposure.meteringMode.Override(MeteringMode.CenterWeighted);
            exposure.compensation.Override(0f);
            exposure.limitMin.Override(-1f);
            exposure.limitMax.Override(14f);
            exposure.adaptationMode.Override(AdaptationMode.Progressive);
            exposure.adaptationSpeedLightToDark.Override(3f);
            exposure.adaptationSpeedDarkToLight.Override(0.7f);

            EditorUtility.SetDirty(profile);
        }

        private static T GetOrAdd<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (profile.TryGet(out T component))
            {
                return component;
            }

            component = profile.Add<T>(true);
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        private static GameObject FindStarHost()
        {
            GameObject sunVisual = GameObject.Find("SunVisual ") ?? GameObject.Find("SunVisual");
            if (sunVisual != null)
            {
                return sunVisual;
            }

            BodyAuthoring[] bodies = Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Include);
            foreach (BodyAuthoring body in bodies)
            {
                bool hasParentBody = false;
                Transform parent = body.transform.parent;
                while (parent != null)
                {
                    if (parent.GetComponent<BodyAuthoring>() != null)
                    {
                        hasParentBody = true;
                        break;
                    }

                    parent = parent.parent;
                }

                if (!hasParentBody)
                {
                    return body.gameObject;
                }
            }

            return null;
        }

        private static Light FindDirectionalLight()
        {
            // The scene has a dedicated Sun light and a shadowless SkyFill light;
            // both are directional. Never take the first one blindly: the star must
            // drive the Sun.
            GameObject sunObject = GameObject.Find("Sun");
            if (sunObject != null
                && sunObject.TryGetComponent(out Light namedSun)
                && namedSun.type == LightType.Directional)
            {
                return namedSun;
            }

            SkyEnvironment sky = Object.FindAnyObjectByType<SkyEnvironment>(FindObjectsInactive.Include);
            if (sky != null && sky.SunLight != null)
            {
                return sky.SunLight;
            }

            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                if (light.type == LightType.Directional && light.name != "SkyFill")
                {
                    return light;
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
