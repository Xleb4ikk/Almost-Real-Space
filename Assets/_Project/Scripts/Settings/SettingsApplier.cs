using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Galilego.Universe
{
    /// <summary>
    /// Pushes GameSettings into the engine: display, QualitySettings, HDRP camera
    /// (anti-aliasing, render scale), a runtime override Volume (shadows, motion blur,
    /// bloom, grain, chromatic aberration) and the audio listener. Created together
    /// with the menu after the first scene loads and kept across scenes.
    /// </summary>
    public sealed class SettingsApplier : MonoBehaviour
    {
        private const float RevertSeconds = 15f;
        private static readonly float[] MotionBlurLevels = { 0f, 0.15f, 0.3f, 0.6f };

        public static SettingsApplier Instance { get; private set; }

        private FirstPersonCamera cam;
        private Volume volume;
        private VolumeProfile profile;
        private MotionBlur motionBlur;
        private Bloom bloom;
        private FilmGrain filmGrain;
        private ChromaticAberration chromatic;
        private HDShadowSettings shadows;

        private float nextCameraSearch;
        private float scalePercent = 100f;
        private bool scalerSet;

        private float revertDeadline = -1f;
        private int prevWidth, prevHeight;
        private FullScreenMode prevMode;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("GameSettings");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsApplier>();
            go.AddComponent<SettingsMenu>();
        }

        // ─── State for the menu ──────────────────────────────────────────────

        public bool RevertPending { get { return revertDeadline > 0f; } }

        public float RevertSecondsLeft { get { return Mathf.Max(0f, revertDeadline - Time.unscaledTime); } }

        /// <summary>True when the active HDRP asset has Dynamic Resolution enabled (render scale works).</summary>
        public static bool DynamicResolutionAvailable
        {
            get
            {
                // HDRenderPipeline.currentAsset is internal to HDRP: the active asset is the
                // render pipeline of the current quality level.
                HDRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
                return asset != null && asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings.enabled;
            }
        }

        /// <summary>Distinct width×height pairs the display offers (ascending), plus the current one.</summary>
        public static List<Vector2Int> GetResolutions()
        {
            var set = new HashSet<Vector2Int>();
            foreach (Resolution r in Screen.resolutions)
            {
                if (r.width >= 800 && r.height >= 600)
                {
                    set.Add(new Vector2Int(r.width, r.height));
                }
            }

            set.Add(new Vector2Int(Screen.width, Screen.height));
            var list = new List<Vector2Int>(set);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SettingsData d = GameSettings.Data;
            GameSettings.Changed += ApplyAll;
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (d.resWidth != Screen.width || d.resHeight != Screen.height || d.screenMode != ModeIndex(Screen.fullScreenMode))
            {
                SetScreen(d.resWidth, d.resHeight, ModeFromIndex(d.screenMode));
            }

            ApplyAll();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            GameSettings.Changed -= ApplyAll;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (profile != null)
            {
                Destroy(profile);
            }
        }

        private void OnApplicationQuit()
        {
            GameSettings.Save();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            cam = null;
            ApplyCamera();
        }

        private void Update()
        {
            SettingsData d = GameSettings.Data;
            float target = d.muteUnfocused && !Application.isFocused ? 0f : d.volMaster;
            if (!Mathf.Approximately(AudioListener.volume, target))
            {
                AudioListener.volume = target;
            }

            if (revertDeadline > 0f && Time.unscaledTime >= revertDeadline)
            {
                RevertDisplay();
            }

            if (cam == null && Time.unscaledTime >= nextCameraSearch)
            {
                // The camera may appear after a scene change: pick up AA / render scale for it.
                nextCameraSearch = Time.unscaledTime + 1f;
                cam = FindAnyObjectByType<FirstPersonCamera>();
                if (cam != null)
                {
                    ApplyCamera();
                }
            }
        }

        // ─── Display (staged by the menu, confirmed or reverted) ─────────────

        public void ApplyDisplay(int width, int height, int modeIndex)
        {
            prevWidth = Screen.width;
            prevHeight = Screen.height;
            prevMode = Screen.fullScreenMode;
            SetScreen(width, height, ModeFromIndex(modeIndex));

            SettingsData d = GameSettings.Data;
            d.resWidth = width;
            d.resHeight = height;
            d.screenMode = modeIndex;
            revertDeadline = Time.unscaledTime + RevertSeconds;
        }

        public void ConfirmDisplay()
        {
            revertDeadline = -1f;
            GameSettings.Save();
        }

        public void RevertDisplay()
        {
            revertDeadline = -1f;
            SetScreen(prevWidth, prevHeight, prevMode);
            SettingsData d = GameSettings.Data;
            d.resWidth = prevWidth;
            d.resHeight = prevHeight;
            d.screenMode = ModeIndex(prevMode);
            GameSettings.NotifyChanged();
        }

        private static void SetScreen(int width, int height, FullScreenMode mode)
        {
            if (width < 640 || height < 480)
            {
                return;
            }

            RefreshRate refresh = Screen.currentResolution.refreshRateRatio;
            foreach (Resolution r in Screen.resolutions)
            {
                if (r.width == width && r.height == height
                    && r.refreshRateRatio.value > refresh.value)
                {
                    refresh = r.refreshRateRatio;
                }
            }

            Screen.SetResolution(width, height, mode, refresh);
        }

        public static int ModeIndex(FullScreenMode mode)
        {
            switch (mode)
            {
                case FullScreenMode.ExclusiveFullScreen: return 0;
                case FullScreenMode.FullScreenWindow: return 1;
                default: return 2;
            }
        }

        private static FullScreenMode ModeFromIndex(int index)
        {
            switch (index)
            {
                case 0: return FullScreenMode.ExclusiveFullScreen;
                case 1: return FullScreenMode.FullScreenWindow;
                default: return FullScreenMode.Windowed;
            }
        }

        // ─── Quality preset ──────────────────────────────────────────────────

        /// <summary>
        /// Switch the Unity quality level (it selects the HDRP asset). The level resets
        /// v-sync, texture, anisotropic and LOD values, so they are read back into the
        /// settings — the preset then behaves as a template the sliders start from.
        /// </summary>
        public void ChangePreset(int index)
        {
            if (index < 0 || index >= QualitySettings.names.Length)
            {
                return;
            }

            SettingsData d = GameSettings.Data;
            d.quality = index;
            QualitySettings.SetQualityLevel(index);
            d.vsync = QualitySettings.vSyncCount > 0 ? 1 : 0;
            d.textureQuality = Mathf.Clamp(QualitySettings.globalTextureMipmapLimit, 0, 2);
            d.anisotropic = Mathf.Clamp((int)QualitySettings.anisotropicFiltering, 0, 2);
            d.lodBias = QualitySettings.lodBias;
            cam = null;
            GameSettings.NotifyChanged();
        }

        // ─── Everything else ─────────────────────────────────────────────────

        private void ApplyAll()
        {
            ApplyQuality();
            ApplyCamera();
            ApplyVolume();
        }

        private static void ApplyQuality()
        {
            SettingsData d = GameSettings.Data;
            if (d.quality >= 0 && d.quality < QualitySettings.names.Length
                && QualitySettings.GetQualityLevel() != d.quality)
            {
                QualitySettings.SetQualityLevel(d.quality);
            }

            QualitySettings.vSyncCount = d.vsync > 0 ? 1 : 0;
            Application.targetFrameRate = d.frameCap > 0 ? d.frameCap : -1;
            QualitySettings.globalTextureMipmapLimit = Mathf.Clamp(d.textureQuality, 0, 2);
            QualitySettings.anisotropicFiltering = (AnisotropicFiltering)Mathf.Clamp(d.anisotropic, 0, 2);
            QualitySettings.lodBias = d.lodBias;
        }

        private void ApplyCamera()
        {
            if (cam == null)
            {
                cam = FindAnyObjectByType<FirstPersonCamera>();
            }

            if (cam == null)
            {
                return;
            }

            cam.ApplyAntiAliasing();

            SettingsData d = GameSettings.Data;
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null)
            {
                return;
            }

            // Render scale: HDRP dynamic resolution with a fixed percentage. Off at 100 %
            // so the default path stays byte-for-byte what the project rendered before.
            bool scaled = d.renderScale < 100 && DynamicResolutionAvailable;
            hd.allowDynamicResolution = scaled;
            scalePercent = d.renderScale;
            if (!scalerSet)
            {
                DynamicResolutionHandler.SetDynamicResScaler(() => scalePercent, DynamicResScalePolicyType.ReturnsPercentage);
                scalerSet = true;
            }
        }

        private void EnsureVolume()
        {
            if (volume != null)
            {
                return;
            }

            var go = new GameObject("GameSettingsVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f;
            volume.weight = 1f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "GameSettingsProfile";
            volume.sharedProfile = profile;

            motionBlur = profile.Add<MotionBlur>(false);
            bloom = profile.Add<Bloom>(false);
            filmGrain = profile.Add<FilmGrain>(false);
            chromatic = profile.Add<ChromaticAberration>(false);
            shadows = profile.Add<HDShadowSettings>(false);
        }

        /// <summary>
        /// Only the effects the player changed are overridden; every component stays
        /// inactive (= the scene's own look) until then.
        /// </summary>
        private void ApplyVolume()
        {
            EnsureVolume();
            SettingsData d = GameSettings.Data;

            motionBlur.active = d.motionBlur > 0;
            if (d.motionBlur > 0)
            {
                motionBlur.intensity.Override(MotionBlurLevels[Mathf.Clamp(d.motionBlur, 0, 3)]);
            }

            bloom.active = !d.bloom;
            if (!d.bloom)
            {
                bloom.intensity.Override(0f);
            }

            filmGrain.active = d.filmGrain;
            if (d.filmGrain)
            {
                filmGrain.intensity.Override(0.5f);
            }

            chromatic.active = d.chromaticAberration;
            if (d.chromaticAberration)
            {
                chromatic.intensity.Override(0.2f);
            }

            shadows.active = d.shadowDistance > 0f;
            if (d.shadowDistance > 0f)
            {
                shadows.maxShadowDistance.Override(d.shadowDistance);
            }
        }
    }
}
