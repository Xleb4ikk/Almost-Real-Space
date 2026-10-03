#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Galilego.Universe.EditorTools
{
    /// <summary>
    /// Автозапуск съёмки по файлу-запросу: без клика по меню.
    ///
    /// Зачем: пункт «Galilego/Capture/...» можно запустить только руками в
    /// открытом редакторе, а сравнение «до/после» нужно гонять много раз, и
    /// каждый запуск кликом стоит отдельного присутствия автора у машины.
    /// Поэтому рядом с инструментом лежит файл-запрос — обычный редакторский
    /// код без побочных эффектов: он существует только в редакторе, смотрит в
    /// Temp/ и начинает съёмку, когда запрос там появился.
    ///
    /// Протокол:
    ///   Temp/surface-capture.request      — текст: underwater-quick |
    ///                                     underwater-full | water-skirt | damp |
    ///                                     benchmark. Пустой файл = underwater-quick.
    ///   Temp/surface-capture.status       — ответ: что произошло (пишет триггер).
    ///
    /// ВАЖНО, и это не формальность: проект входит в Play с
    /// runInBackground = 0 (ProjectSettings.asset:90), и при потере фокуса
    /// редактора игровой цикл ОСТАНАВЛИВАЕТСЯ — не «замедляется», а именно
    /// стоит: не тикают LateUpdate, не качаются джобы сборки чанков, не
    /// перерисовывается HUD. Все счётчики при этом замирают на последних
    /// значениях, и картина «очередь 305, в полёте 6, ничего не
    /// финализируется три съёмки подряд» получается без всякого зависания.
    /// Поэтому SurfaceCaptureTool на время съёмки сам включает
    /// Application.runInBackground и возвращает настройку при выходе из Play.
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

            // Запрос НЕ удаляем до старта: если старт не удался, файл остаётся и
            // съёмка попробует снова — молча проглоченный запрос невозможно
            // отличить от неработающего триггера.
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
                    // Выйти из Play. Нужен после съёмки, умершей от
                    // перекомпиляции: домен перезагрузился, статики
                    // SurfaceCaptureTool обнулились, и выйти из Play больше
                    // некому.
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
