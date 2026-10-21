using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Galilego.Universe
{
    [Serializable]
    internal sealed class BindingEntry
    {
        public string action;
        public string a;
        public string b;
    }

    /// <summary>
    /// Everything the player can change. Fields marked "-1 / 0 = engine" are filled
    /// from the running engine on the first launch, so a fresh install looks exactly
    /// like the project did before the menu existed.
    /// </summary>
    [Serializable]
    internal sealed class SettingsData
    {
        public int version = 1;

        // Display
        public int resWidth;                 // 0 = current
        public int resHeight;
        public int screenMode = -1;          // 0 exclusive, 1 borderless, 2 windowed
        public int vsync = -1;               // 0 / 1
        public int frameCap = -1;            // 0 = unlimited

        // Quality
        public int quality = -1;
        public int antiAliasing = -1;        // 0 none, 1 FXAA, 2 SMAA, 3 TAA
        public int renderScale = 100;        // % (needs dynamic resolution in the HDRP asset)
        public int textureQuality = -1;      // mip limit: 0 full, 1 half, 2 quarter
        public int anisotropic = -1;         // 0 off, 1 per texture, 2 forced
        public float lodBias = -1f;
        public float shadowDistance;         // 0 = as authored in the scene
        public float terrainDetail = 1f;     // multiplies the terrain quadtree SplitFactor
        public int grassQuality = 3;         // 0 off, 1 low, 2 medium, 3 high (authored), 4 maximum

        // Effects
        public int motionBlur;               // 0 off .. 3 high
        public bool bloom = true;
        public bool filmGrain;
        public bool chromaticAberration;

        // View
        public float fov = -1f;              // vertical degrees; -1 = camera's own
        public float mouseSensitivity = 1f;
        public bool invertY;
        public bool lean = true;
        public bool crosshair = true;
        public float crosshairScale = 1f;
        public bool pauseOnMenu = true;

        // Audio
        public float volMaster = 1f;
        public float volMusic = 0.8f;
        public float volEffects = 1f;
        public float volAmbient = 1f;
        public float volInterface = 1f;
        public bool muteUnfocused = true;

        public List<BindingEntry> bindings = new List<BindingEntry>();
    }

    internal static class GameSettings
    {
        private static SettingsData data;

        // What the engine had before the player touched anything: "reset tab" returns here.
        private static int initialQuality = -1;
        private static int initialVsync = -1;
        private static int initialTexture = -1;
        private static int initialAniso = -1;
        private static float initialLod = -1f;

        /// <summary>Raised after any change (menu, reset, load). Appliers and views subscribe.</summary>
        public static event Action Changed;

        public static SettingsData Data
        {
            get
            {
                if (data == null)
                {
                    Load();
                }

                return data;
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(Application.persistentDataPath, "settings.json"); }
        }

        // The project enters Play without a domain reload: statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            data = null;
            Changed = null;
            initialQuality = -1;
            initialVsync = -1;
            initialTexture = -1;
            initialAniso = -1;
            initialLod = -1f;
        }

        public static void Load()
        {
            data = null;
            try
            {
                if (File.Exists(FilePath))
                {
                    data = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Settings] settings.json не прочитан, беру значения по умолчанию: " + e.Message);
            }

            if (data == null)
            {
                data = new SettingsData();
            }

            if (data.bindings == null)
            {
                data.bindings = new List<BindingEntry>();
            }

            // Captured before anything is applied: the reset button returns to these.
            if (initialQuality < 0)
            {
                initialQuality = QualitySettings.GetQualityLevel();
                initialVsync = QualitySettings.vSyncCount > 0 ? 1 : 0;
                initialTexture = Mathf.Clamp(QualitySettings.globalTextureMipmapLimit, 0, 2);
                initialAniso = Mathf.Clamp((int)QualitySettings.anisotropicFiltering, 0, 2);
                initialLod = QualitySettings.lodBias;
            }

            ResolveFromEngine(data);
            Sanitize(data);
            KeyBindings.Import(data.bindings);
        }

        public static void Save()
        {
            if (data == null)
            {
                return;
            }

            try
            {
                data.bindings = KeyBindings.Export();
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Settings] не удалось сохранить настройки: " + e.Message);
            }
        }

        public static void NotifyChanged()
        {
            Changed?.Invoke();
        }

        /// <summary>Back to defaults, but keeps what the engine started with (resolution, quality level).</summary>
        public static void ResetCategory(string category)
        {
            SettingsData d = Data;
            var fresh = new SettingsData();
            switch (category)
            {
                case "graphics":
                    d.antiAliasing = fresh.antiAliasing;
                    d.renderScale = 100;
                    d.shadowDistance = 0f;
                    d.terrainDetail = 1f;
                    d.grassQuality = 3;
                    d.motionBlur = 0;
                    d.bloom = true;
                    d.filmGrain = false;
                    d.chromaticAberration = false;
                    d.quality = -1;
                    d.vsync = -1;
                    d.frameCap = 0;
                    d.textureQuality = -1;
                    d.anisotropic = -1;
                    d.lodBias = -1f;
                    break;
                case "view":
                    d.fov = -1f;
                    d.mouseSensitivity = 1f;
                    d.invertY = false;
                    d.lean = true;
                    d.crosshair = true;
                    d.crosshairScale = 1f;
                    d.pauseOnMenu = true;
                    break;
                case "audio":
                    d.volMaster = 1f;
                    d.volMusic = 0.8f;
                    d.volEffects = 1f;
                    d.volAmbient = 1f;
                    d.volInterface = 1f;
                    d.muteUnfocused = true;
                    break;
                case "controls":
                    KeyBindings.ResetAll();
                    break;
            }

            ResolveFromEngine(d);
            NotifyChanged();
        }

        private static void ResolveFromEngine(SettingsData d)
        {
            if (d.screenMode < 0)
            {
                switch (Screen.fullScreenMode)
                {
                    case FullScreenMode.ExclusiveFullScreen: d.screenMode = 0; break;
                    case FullScreenMode.FullScreenWindow: d.screenMode = 1; break;
                    default: d.screenMode = 2; break;
                }
            }

            if (d.resWidth <= 0 || d.resHeight <= 0)
            {
                d.resWidth = Screen.width;
                d.resHeight = Screen.height;
            }

            if (d.vsync < 0)
            {
                d.vsync = initialVsync >= 0 ? initialVsync : (QualitySettings.vSyncCount > 0 ? 1 : 0);
            }

            if (d.frameCap < 0)
            {
                d.frameCap = Application.targetFrameRate > 0 ? Application.targetFrameRate : 0;
            }

            if (d.quality < 0 || d.quality >= QualitySettings.names.Length)
            {
                d.quality = initialQuality >= 0 ? initialQuality : QualitySettings.GetQualityLevel();
            }

            if (d.textureQuality < 0)
            {
                d.textureQuality = initialTexture >= 0
                    ? initialTexture
                    : Mathf.Clamp(QualitySettings.globalTextureMipmapLimit, 0, 2);
            }

            if (d.anisotropic < 0)
            {
                d.anisotropic = initialAniso >= 0
                    ? initialAniso
                    : Mathf.Clamp((int)QualitySettings.anisotropicFiltering, 0, 2);
            }

            if (d.lodBias < 0f)
            {
                d.lodBias = initialLod >= 0f ? initialLod : QualitySettings.lodBias;
            }
        }

        private static void Sanitize(SettingsData d)
        {
            d.renderScale = Mathf.Clamp(d.renderScale, 25, 100);
            d.antiAliasing = Mathf.Clamp(d.antiAliasing, -1, 3);
            d.motionBlur = Mathf.Clamp(d.motionBlur, 0, 3);
            d.mouseSensitivity = Mathf.Clamp(d.mouseSensitivity, 0.05f, 5f);
            d.crosshairScale = Mathf.Clamp(d.crosshairScale, 0.25f, 3f);
            d.shadowDistance = Mathf.Max(0f, d.shadowDistance);
            d.terrainDetail = Mathf.Clamp(d.terrainDetail, 0.5f, 2f);
            d.grassQuality = Mathf.Clamp(d.grassQuality, 0, 4);
            d.lodBias = Mathf.Clamp(d.lodBias, 0.25f, 4f);
            d.volMaster = Mathf.Clamp01(d.volMaster);
            d.volMusic = Mathf.Clamp01(d.volMusic);
            d.volEffects = Mathf.Clamp01(d.volEffects);
            d.volAmbient = Mathf.Clamp01(d.volAmbient);
            d.volInterface = Mathf.Clamp01(d.volInterface);
            if (d.fov > 0f)
            {
                d.fov = Mathf.Clamp(d.fov, 30f, 120f);
            }
        }
    }
}
