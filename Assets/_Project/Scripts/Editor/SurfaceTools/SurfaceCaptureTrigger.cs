#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Starts a capture from a request file, without a menu click.
    ///
    /// Protocol:
    ///   Temp/surface-capture.request      — text: underwater-quick |
    ///                                     underwater-full | water-skirt | damp |
    ///                                     benchmark. Empty file = underwater-quick.
    ///   Temp/surface-capture.status       — reply: what happened (written by the trigger).
    ///
    /// The project enters Play with runInBackground = 0, so losing editor focus
    /// stops the game loop and freezes all counters; SurfaceCaptureTool forces
    /// Application.runInBackground on for the duration of a capture.
    /// </summary>
    [InitializeOnLoad]
    public static class SurfaceCaptureTrigger
    {
        private const string RequestPath = "Temp/surface-capture.request";
        private const string StatusPath = "Temp/surface-capture.status";

        static SurfaceCaptureTrigger()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!File.Exists(RequestPath))
            {
                return;
            }

            string action = (File.ReadAllText(RequestPath) ?? string.Empty).Trim();
            if (action.Length == 0)
            {
                action = "underwater-quick";
            }

            File.Delete(RequestPath);

            switch (action)
            {
                case "underwater-quick":
                    SurfaceCaptureTool.CaptureUnderwaterCeilingQuick();
                    break;
                case "underwater-full":
                    SurfaceCaptureTool.CaptureUnderwaterCeilingFull();
                    break;
                case "water-skirt":
                    SurfaceCaptureTool.CaptureWaterSkirt();
                    break;
                case "damp":
                    SurfaceCaptureTool.CaptureDampModes();
                    break;
                case "benchmark":
                    SurfaceCaptureTool.FlightBenchmark();
                    break;
                case "stop":
                    // Leave Play mode. Needed when a capture dies from a
                    // recompilation: the domain reloads and the tool's statics
                    // reset, so nothing else can exit Play.
                    WriteStatus("выходим из Play");
                    EditorApplication.isPlaying = false;
                    return;
                case "diag-gv":
                    WriteStatus(GameViewProbe.Describe());
                    return;
                default:
                    WriteStatus("неизвестное действие: " + action);
                    return;
            }

            WriteStatus("запущено: " + action
                + (EditorApplication.isFocused ? "" : " (редактор в фоне; runInBackground принудительно включён)"));
        }

        private static void WriteStatus(string text)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatusPath) ?? ".");
                File.WriteAllText(
                    StatusPath,
                    "[" + System.DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + text);
            }
            catch (IOException)
            {
                Debug.LogWarning("[SurfaceCaptureTrigger] не удалось записать статус");
            }
        }
    }
}
#endif
