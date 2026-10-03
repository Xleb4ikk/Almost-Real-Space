using System;
using Galilego.Core;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор SurfaceFrame: широта/долгота (единственный источник истины о
    /// положении), привязка к точке спавна раннера, рамка камеры SceneView на
    /// фрейм, тумблеры превью и диагностика высоты.
    ///
    /// Кнопки «взять у раннера» и «показать здесь» — то, ради чего фрейм и
    /// нужен: без них точка спавна задаётся в инспекторе раннера, а увидеть её
    /// в сцене нечем.
    /// </summary>
    [CustomEditor(typeof(SurfaceFrame))]
    public sealed class SurfaceFrameEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var frame = (SurfaceFrame)target;

            EditorGUILayout.HelpBox(
                "Фрейм ставит объект в ноль сцены и ориентирует его локальные оси: " +
                "X — север, Y — зенит, Z — восток. Дети фрейма — это база.\n\n" +
                "Мировую позицию в сцене не храните: тело лежит на 2.6e10 м, где float32 " +
                "квантует координату до ~2 км. Положение задаётся только широтой и долготой.",
                MessageType.Info);

            DrawDefaultInspector();

            EditorGUILayout.Space(8f);
            frame.Runner = (SimulationRunner)EditorGUILayout.ObjectField(
                new GUIContent("Раннер (Play)", "SimulationRunner сцены. Нужен, чтобы фрейм ехал за якорем игрока."),
                frame.Runner, typeof(SimulationRunner), true);

            EditorGUILayout.Space(8f);
            DrawSpawnSection(frame);
            DrawPreviewSection(frame);
            DrawDiagnostics(frame);
        }

        private static void DrawSpawnSection(SurfaceFrame frame)
        {
            EditorGUILayout.LabelField("Точка спавна", EditorStyles.boldLabel);

            SimulationRunner runner = frame.Runner != null
                ? frame.Runner
                : UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();

            using (new EditorGUI.DisabledScope(runner == null))
            {
                if (GUILayout.Button("Взять lat/lon из раннера"))
                {
                    Undo.RecordObject(frame, "Surface frame: взять точку спавна");
                    frame.LatitudeDegrees = runner.SpawnLatitudeDegrees;
                    frame.LongitudeDegrees = runner.SpawnLongitudeDegrees;
                    if (runner.SpawnBodyName != null && runner.SpawnBodyName.Length > 0)
                    {
                        frame.BodyName = runner.SpawnBodyName;
                        frame.Body = null;
                    }

                    if (runner.SpawnOnSurface)
                    {
                        frame.SnapToTerrain = true;
                    }

                    EditorUtility.SetDirty(frame);
                    SurfacePreviewTool.Invalidate();
                }
            }

            if (runner == null)
            {
                EditorGUILayout.HelpBox("SimulationRunner в сцене не найден.", MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(!runner.SpawnOnSurface))
            {
                EditorGUILayout.LabelField(
                    "Раннер", $"{runner.SpawnBodyName}, lat {runner.SpawnLatitudeDegrees:F3}°, lon {runner.SpawnLongitudeDegrees:F3}°");
            }

            using (new EditorGUI.DisabledScope(!runner.SpawnOnSurface))
            {
                if (GUILayout.Button("Пересадить игрока в фрейм"))
                {
                    Undo.RecordObject(runner, "SimulationRunner: спавн в фрейм");
                    runner.SpawnBodyName = frame.Body != null ? frame.Body.gameObject.name : frame.BodyName;
                    runner.SpawnOnSurface = true;
                    runner.SpawnByCoordinates = true;
                    runner.SpawnLatitudeDegrees = frame.LatitudeDegrees;
                    runner.SpawnLongitudeDegrees = frame.LongitudeDegrees;
                    EditorUtility.SetDirty(runner);
                }
            }
        }

        private static void DrawPreviewSection(SurfaceFrame frame)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Превью рельефа", EditorStyles.boldLabel);

            bool enabled = EditorGUILayout.Toggle("Показывать превью", SurfacePreviewTool.Enabled);
            if (enabled != SurfacePreviewTool.Enabled)
            {
                SurfacePreviewTool.Enabled = enabled;
                if (!enabled)
                {
                    SurfacePreviewTool.DestroyPreview();
                }
                else
                {
                    SurfacePreviewTool.Invalidate();
                }
            }

            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (GUILayout.Button("Перестроить"))
                {
                    SurfacePreviewTool.DestroyPreview();
                    SurfacePreviewTool.Invalidate();
                }

                EditorGUI.BeginChangeCheck();
                double radius = EditorGUILayout.DoubleField(
                    new GUIContent("Радиус превью", "Размер патча вокруг фрейма, м. Кольца клипмапа идут от 1/4096 радиуса, так что 5 км дают и горизонт, и сантиметры под ногами."),
                    SurfacePreviewTool.PreviewRadiusMeters);
                if (EditorGUI.EndChangeCheck())
                {
                    SurfacePreviewTool.PreviewRadiusMeters = Mathf.Clamp((float)radius, 5f, 200000f);
                    SurfacePreviewTool.Invalidate();
                }

                if (GUILayout.Button("Камера SceneView сюда"))
                {
                    SurfacePreviewTool.FocusSceneView(frame);
                }
            }
        }

        private static void DrawDiagnostics(SurfaceFrame frame)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Диагностика", EditorStyles.boldLabel);

            if (frame.BodyState == null)
            {
                string error = SurfaceSceneSystem.LastError;
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(error)
                        ? "Тело не найдено. Проверьте Body/BodyName и наличие StarSystemAuthoring в сцене."
                        : "Система не собралась: " + error,
                    MessageType.Error);
                return;
            }

            double ground = frame.GroundHeightMeters;
            EditorGUILayout.LabelField("Тело", frame.BodyState.Name + "  R = " + frame.BodyState.Radius.ToString("F0") + " м");
            EditorGUILayout.LabelField("Высота рельефа", ground.ToString("F1") + " м");
            EditorGUILayout.LabelField("Высота фрейма", frame.AltitudeMeters.ToString("F1") + " м");
            EditorGUILayout.LabelField("Позиция в сцене",
                frame.transform.position.ToString("F2") + "   (в Play это якорь игрока, не ноль)");

            OrbitingBody body = frame.BodyState;
            double t = 0d;
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d absolute = bodyPos + body.GetVisualOrientation(t).Rotate(frame.AnchorBodyFixed);
            double distanceFromOrigin = absolute.Magnitude;
            EditorGUILayout.LabelField("Абсолютная астро-позиция", distanceFromOrigin.ToString("E3") + " м от астро-нуля");
        }

        private static void FrameSceneView(SurfaceFrame frame)
        {
            SurfacePreviewTool.FocusSceneView(frame);
        }
    }
}
