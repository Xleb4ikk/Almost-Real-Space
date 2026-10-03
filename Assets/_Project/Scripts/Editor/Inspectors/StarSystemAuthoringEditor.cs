using System;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Инспектор StarSystemAuthoring: кнопка «Сгенерировать эфемериды» запускает
    /// выпечку в фоновом потоке (RunBake уже потокобезопасен: читает чистую
    /// математику тел, Unity API не трогает; система на главном потоке во время
    /// бейка не мутируется — кнопки задизейблены). Прогресс — через polling
    /// EditorApplication.update: Unity API вызывается только из главного потока.
    /// Отмена — через cancelled() пекаря. Ошибки (ValidateBakeable, Hill, файлы)
    /// — громкий диалог, пекарь намеренно отказывается молчать.
    /// </summary>
    [CustomEditor(typeof(StarSystemAuthoring))]
    public sealed class StarSystemAuthoringEditor : Editor
    {
        private static volatile bool baking;
        private static double progress;
        private static volatile bool cancelRequested;
        private static Thread worker;
        private static Exception workerError;
        private static bool cancelledByUser;
        private static string doneSummary;

        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(baking))
            {
                DrawDefaultInspector();
            }

            EditorGUILayout.Space(8);
            StarSystemAuthoring auth = (StarSystemAuthoring)target;

            using (new EditorGUI.DisabledScope(baking))
            {
                if (GUILayout.Button("Сгенерировать эфемериды (" + auth.SpanYears + " лет)"))
                {
                    StartBake(auth);
                }
            }

            if (baking)
            {
                if (GUILayout.Button("Отмена"))
                {
                    cancelRequested = true;
                }

                Rect barRect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(barRect, (float)Volatile.Read(ref progress), "Выпечка: " + (Volatile.Read(ref progress) * 100d).ToString("F1") + "%");
            }

            if (workerError != null)
            {
                EditorGUILayout.HelpBox(workerError.Message, MessageType.Error);
            }

            if (doneSummary != null)
            {
                EditorGUILayout.HelpBox(doneSummary, MessageType.Info);
            }
        }

        private void StartBake(StarSystemAuthoring auth)
        {
            // Сборка системы и все валидации контента (Hill, bakeable) — на
            // главном потоке: исключения показываем диалогом, до бейка дело
            // не доходит.
            SystemBlueprint blueprint;
            try
            {
                blueprint = auth.BuildBlueprint();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Система не собрана", ex.Message, "OK");
                return;
            }

            StarSystem sys;
            try
            {
                sys = blueprint.Build();
            }
            catch (InvalidOperationException ex)
            {
                EditorUtility.DisplayDialog("Система нестабильна/некорректна", ex.Message, "OK");
                return;
            }

            var config = new BakeConfig
            {
                Degree = auth.Degree,
                MaxStepSeconds = auth.MaxStepSeconds,
                QuantBudgetMeters = auth.QuantBudgetMeters
            };

            string folder = auth.OutputFolder ?? "Assets/StreamingAssets/Ephemerides";
            string absolute = ResolveProjectPath(folder);
            int spanYears = auth.SpanYears;
            bool compress = auth.CompressFiles;

            progress = 0d;
            cancelRequested = false;
            workerError = null;
            cancelledByUser = false;
            doneSummary = null;
            baking = true;

            worker = new Thread(() =>
            {
                try
                {
                    var files = EphemerisBaker.BakeToFiles(
                        sys, absolute, spanYears, config,
                        null,
                        f => Volatile.Write(ref progress, f),
                        () => cancelRequested,
                        compress);
                    long total = 0L;
                    foreach (var kv in files)
                    {
                        total += new FileInfo(kv.Value.FilePath).Length;
                    }

                    doneSummary = "Готово: " + files.Count + " файлов, " +
                        (total / 1048576.0).ToString("F1") + " МБ (" + (compress ? "BG3" : "BE2") +
                        ") за " + spanYears + " лет → " + folder;
                }
                catch (OperationCanceledException)
                {
                    cancelledByUser = true;
                }
                catch (Exception ex)
                {
                    workerError = ex;
                }
            });
            worker.IsBackground = true;
            worker.Start();
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (baking)
            {
                EditorUtility.DisplayProgressBar(
                    "Выпечка эфемерид",
                    (Volatile.Read(ref progress) * 100d).ToString("F1") + "%",
                    (float)Volatile.Read(ref progress));
                RepaintAll();
                return;
            }

            EditorUtility.ClearProgressBar();
            EditorApplication.update -= Poll;

            if (workerError != null)
            {
                Debug.LogError("Выпечка эфемерид упала: " + workerError);
                EditorUtility.DisplayDialog("Выпечка эфемерид упала", workerError.Message, "OK");
            }
            else if (cancelledByUser)
            {
                EditorUtility.DisplayDialog("Выпечка отменена", "Файлы могут быть неполными — перегенерируйте.", "OK");
            }
            else if (doneSummary != null)
            {
                AssetDatabase.Refresh();
                EditorUtility.DisplayDialog("Выпечка эфемерид", doneSummary, "OK");
            }

            RepaintAll();
        }

        private static void RepaintAll()
        {
            foreach (StarSystemAuthoring a in UnityEngine.Object.FindObjectsByType<StarSystemAuthoring>(FindObjectsSortMode.None))
            {
                EditorUtility.SetDirty(a);
            }
        }

        /// <summary>Путь в проекте: "Assets/..." — от корня проекта, иначе как есть.</summary>
        private static string ResolveProjectPath(string folder)
        {
            if (Path.IsPathRooted(folder))
            {
                return folder;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, folder));
        }
    }
}
