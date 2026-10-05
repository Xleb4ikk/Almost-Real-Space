using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Камера Scene view в HDRP считает экспозицию отдельно от игровой и не
    /// повторяет результат авто-экспозиции тома ("Sky and Fog Volume"): при каждом
    /// орбите история сбрасывается, и кадр выходит почти без экспозиции — здания
    /// и земля выглядят выбеленными. Здесь камере Scene view выставляется
    /// фиксированная экспозиция (EV100): тот же приём, что и галочка
    /// "Override Exposure" в окне Scene View Camera, только автоматически и
    /// persistent. Затрагивается только редактор — ни Game view, ни play mode.
    /// </summary>
    [InitializeOnLoad]
    public static class SceneViewExposure
    {
        private const string EnabledKey = "Galilego.SceneViewExposure.Enabled";
        private const string Ev100Key = "Galilego.SceneViewExposure.EV100";

        /// <summary>
        /// Подобрано под сцену: солнце 130000 лк и белая кровля здания.
        /// Ниже ~11 EV100 кровля уходит в клип и зажигает bloom (threshold 1.2),
        /// выше — земля уходит в черноту.
        /// </summary>
        public const float DefaultEv100 = 11.5f;
        public const float MinEv100 = -11f;
        public const float MaxEv100 = 16f;

        // Поля internal в HDAdditionalCameraData, доступны только через рефлексию.
        private static readonly FieldInfo OverrideField = typeof(HDAdditionalCameraData)
            .GetField("doesSceneViewOverrideExposure", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ExposureField = typeof(HDAdditionalCameraData)
            .GetField("sceneViewOverrideExposureValue", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool Available => OverrideField != null && ExposureField != null;

        public static bool Enabled
        {
            get => EditorPrefs.GetInt(EnabledKey, 1) != 0;
            set
            {
                EditorPrefs.SetInt(EnabledKey, value ? 1 : 0);
                ApplyToAll();
            }
        }

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
            // HDRP добавляет HDAdditionalCameraData в своём обработчике onCameraCreated,
            // порядок вызовов не гарантирован — применяем на следующем цикле редактора.
            EditorApplication.delayCall += () => ApplyTo(view);
        }

        /// <summary>
        /// Держит значение на камерах Scene view: после перезагрузки домена и
        /// пересоздания вьюхи HDRP снова ставит свои false/10, а onCameraCreated
        /// на восстановленную раскладку не всегда приходит вовремя.
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

        /// <summary>Диагностика: что сейчас выставлено на камерах Scene view.</summary>
        [MenuItem("Tools/Galilego/Scene view exposure/Log state", false, 21)]
        public static void LogState()
        {
            if (!Available)
            {
                Debug.LogWarning("SceneViewExposure: HDRP-поля override-экспозиции не найдены, инструмент не работает.");
                return;
            }

            foreach (SceneView view in SceneView.sceneViews)
            {
                Camera camera = view != null ? view.camera : null;
                HDAdditionalCameraData data = camera != null ? camera.GetComponent<HDAdditionalCameraData>() : null;
                string state = data == null
                    ? "нет HDAdditionalCameraData"
                    : "override=" + OverrideField.GetValue(data) + ", EV100=" + ExposureField.GetValue(data);
                Debug.Log("SceneViewExposure: \"" + (view != null ? view.name : "null") + "\": " + state);
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
    /// Настройка экспозиции Scene view. Подбирается под то, что видно в Game view:
    /// при слишком ярком кадре увеличить EV100, при слишком тёмном — уменьшить.
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
            float ev100 = EditorGUILayout.Slider(
                new GUIContent("Scene exposure (EV100)", "Тот же EV100, что у Fixed Exposure в томе. Больше — темнее."),
                SceneViewExposure.Ev100, SceneViewExposure.MinEv100, SceneViewExposure.MaxEv100);

            if (EditorGUI.EndChangeCheck())
            {
                SceneViewExposure.Ev100 = ev100;
                SceneViewExposure.Enabled = enabled;
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Меняется только камера Scene view. Если в окне Scene View Camera галочка Override Exposure выглядит снятой — это другое окно HDRP, оно не мешает; значение здесь главнее.",
                MessageType.Info);
        }
    }
}