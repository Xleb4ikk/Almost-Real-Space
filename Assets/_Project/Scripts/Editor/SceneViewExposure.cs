using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// The HDRP Scene view camera computes exposure separately from the game camera
    /// and does not repeat the volume's auto-exposure result ("Sky and Fog Volume"):
    /// each orbit resets the history and the frame comes out nearly unexposed —
    /// buildings and ground look washed out. Here the Scene view camera gets a fixed
    /// exposure (EV100): the same as the "Override Exposure" checkbox in the Scene
    /// View Camera window, but automatic and persistent. Editor only — neither Game
    /// view nor play mode is affected.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneViewExposure
    {
        private const string EnabledKey = "Galilego.SceneViewExposure.Enabled";
        private const string Ev100Key = "Galilego.SceneViewExposure.EV100";

        /// <summary>
        /// Default exposure when the tool is switched on manually.
        /// </summary>
        public const float DefaultEv100 = 11.5f;
        public const float MinEv100 = -13f;
        public const float MaxEv100 = 16f;

        // The fields are internal in HDAdditionalCameraData; reachable via reflection only.
        private static readonly FieldInfo OverrideField = typeof(HDAdditionalCameraData)
            .GetField("doesSceneViewOverrideExposure", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ExposureField = typeof(HDAdditionalCameraData)
            .GetField("sceneViewOverrideExposureValue", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Available => OverrideField != null && ExposureField != null;

        public static bool Enabled
        {
            // Default is OFF: the tool overrides the Volume exposure
            // (SkyandFogSettingsProfile, Exposure fixedExposure), which is calibrated
            // to the actual Sun.intensity written by SkyEnvironment. Enable manually
            // only if the Volume stops coping.
            get => EditorPrefs.GetInt(EnabledKey, 0) != 0;
            set
            {
                EditorPrefs.SetInt(EnabledKey, value ? 1 : 0);
                ApplyToAll();
            }
        }

        /// <summary>
        /// EV100 for the Scene view camera. Manual: the value lives in EditorPrefs,
        /// default is DefaultEv100.
        ///
        /// EV100 is NOT recomputed from Sun intensity: scene exposure is set by the
        /// Volume in SkyandFogSettingsProfile (Exposure, mode = Fixed,
        /// fixedExposure ≈ 8.58), already calibrated to the actual Sun.intensity
        /// written by SkyEnvironment.
        /// </summary>
        public static float Ev100
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(Ev100Key, DefaultEv100), MinEv100, MaxEv100);
            set
            {
                EditorPrefs.SetFloat(Ev100Key, Mathf.Clamp(value, MinEv100, MaxEv100));
                ApplyToAll();
            }
        }

        static SceneViewExposure()
        {
            if (!Available)
            {
                Debug.LogWarning("SceneViewExposure: в установленной версии HDRP нет полей override-экспозиции Scene view — инструмент не работает.");
                return;
            }

            SceneView.onCameraCreated -= OnCameraCreated;
            SceneView.onCameraCreated += OnCameraCreated;
            EditorApplication.update -= Enforce;
            EditorApplication.update += Enforce;
        }

        private static void OnCameraCreated(SceneView view)
        {
            // HDRP adds HDAdditionalCameraData in its own onCameraCreated handler; call
            // order is not guaranteed, so apply on the next editor tick.
            EditorApplication.delayCall += () => ApplyTo(view);
        }

        /// <summary>
        /// Keeps the value on Scene view cameras: after a domain reload and view
        /// recreation HDRP sets its own false/10 again, and onCameraCreated for a
        /// restored layout does not always arrive in time.
        /// </summary>
        private static void Enforce()
        {
            foreach (SceneView view in SceneView.sceneViews)
                ApplyTo(view, false);
        }

        public static void ApplyToAll()
        {
            if (!Available)
                return;

            foreach (SceneView view in SceneView.sceneViews)
                ApplyTo(view);

            SceneView.RepaintAll();
        }

        /// <summary>Diagnostics: what is currently set on Scene view cameras.</summary>
        [MenuItem("Tools/Galilego/Scene view exposure/Log state", false, 21)]
        public static void LogState()
        {
            if (!Available)
            {
                Debug.LogWarning("SceneViewExposure: HDRP-поля override-экспозиции не найдены, инструмент не работает.");
                return;
            }

            string sun = string.Format("инструмент {0}, его EV100 = {1:F2}",
                Enabled ? "ВКЛ" : "выкл", Ev100);

            foreach (SceneView view in SceneView.sceneViews)
            {
                Camera camera = view != null ? view.camera : null;
                HDAdditionalCameraData data = camera != null ? camera.GetComponent<HDAdditionalCameraData>() : null;
                string state = data == null
                    ? "нет HDAdditionalCameraData"
                    : "override=" + OverrideField.GetValue(data) + ", EV100=" + ExposureField.GetValue(data);
                Debug.Log("SceneViewExposure: \"" + (view != null ? view.name : "null") + "\": " + state + " | " + sun);
            }
        }

        public static void ApplyTo(SceneView view, bool repaint = true)
        {
            if (view == null || !Available)
                return;

            Camera camera = view.camera;
            if (camera == null)
                return;

            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null)
                data = camera.gameObject.AddComponent<HDAdditionalCameraData>();

            bool changed = false;

            if ((bool)OverrideField.GetValue(data) != Enabled)
            {
                OverrideField.SetValue(data, Enabled);
                changed = true;
            }

            if ((float)ExposureField.GetValue(data) != Ev100)
            {
                ExposureField.SetValue(data, Ev100);
                changed = true;
            }

            if (changed && repaint)
                SceneView.RepaintAll();
        }
    }

    /// <summary>
    /// Scene view exposure settings. Tune to match what the Game view shows: raise
    /// EV100 when the frame is too bright, lower it when too dark.
    /// </summary>
    public class SceneViewExposureWindow : EditorWindow
    {
        [MenuItem("Tools/Galilego/Scene view exposure", false, 20)]
        private static void Open()
        {
            GetWindow<SceneViewExposureWindow>("Scene Exposure");
        }

        private void OnGUI()
        {
            if (!SceneViewExposure.Available)
            {
                EditorGUILayout.HelpBox(
                    "HDRP не найден или в нём нет полей override-экспозиции Scene view. Обнови пакет HDRP.",
                    MessageType.Error);
                return;
            }

            EditorGUI.BeginChangeCheck();

            bool enabled = EditorGUILayout.Toggle(
                new GUIContent("Фиксировать экспозицию", "Эквивалент галочки Override Exposure в окне Scene View Camera."),
                SceneViewExposure.Enabled);

            if (!SceneViewExposure.Enabled)
            {
                EditorGUILayout.HelpBox(
                    "Выключено — экспозицию камеры Scene view задаёт Volume (Sky and Fog Volume). "
                    + "Это штатный режим: Volume откалиброван под фактический Sun.intensity, который пишет SkyEnvironment.",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!SceneViewExposure.Enabled))
            {
                EditorGUILayout.Slider(
                    new GUIContent("Scene exposure (EV100)", "Больше — темнее. Работает, только когда инструмент включён."),
                    SceneViewExposure.Ev100, SceneViewExposure.MinEv100, SceneViewExposure.MaxEv100);
            }

            if (EditorGUI.EndChangeCheck())
            {
                SceneViewExposure.Ev100 = SceneViewExposure.Ev100;
                SceneViewExposure.Enabled = enabled;
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Инструмент перекрывает Volume-экспозицию камерой Scene view. Включайте его только если Volume перестал справляться: "
                + "например, при каждой орбите HDRP сбрасывает историю авто-экспозиции и кадр выходит выбеленным. "
                + "Пока инструмент выключен, здания и рельеф освещаются как в Game view.",
                MessageType.Info);
        }
    }
}