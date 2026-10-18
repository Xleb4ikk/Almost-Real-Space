using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// Periodic performance log written to a file so it can be inspected in a BUILT
    /// build, not only in the editor. The editor's <c>FrameTimingManager</c> does not
    /// attribute real GPU work, and the overlay shows only the current frame — it
    /// cannot answer "what happened at the moment of the hitch". This log records a
    /// series: VRAM, fps, timings and camera position.
    ///
    /// File: &lt;persistentDataPath&gt;/perf_log.txt, TAB-separated. No setup needed —
    /// the component bootstraps itself after scene load. The path is printed to the
    /// player console.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class PerfLog : MonoBehaviour
    {
        /// <summary>Row write interval, seconds.</summary>
        private const float IntervalSeconds = 2f;

        /// <summary>How many recent frame timings are kept in the buffer.</summary>
        private const int TimingBufferSize = 4;

        private static PerfLog instance;

        private readonly FrameTiming[] timings = new FrameTiming[TimingBufferSize];
        private readonly StringBuilder line = new StringBuilder(256);

        private string path;
        private float nextWriteTime;
        private float gpuMsSum;
        private float cpuMainMsSum;
        private int sampleCount;
        private bool broken;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
            {
                return;
            }

            GameObject go = new GameObject("PerfLog");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<PerfLog>();
        }

        private void Awake()
        {
            path = Path.Combine(Application.persistentDataPath, "perf_log.txt");
            var enc = new UTF8Encoding(false);

            var header = new StringBuilder(1024);
            header.AppendLine("# Galilego perf log. Columns are TAB-separated.");
            header.AppendLine("# generated\t" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            header.AppendLine("# device\t" + SystemInfo.deviceModel);
            header.AppendLine("# gpu\t" + SystemInfo.graphicsDeviceName);
            header.AppendLine("# api\t" + SystemInfo.graphicsDeviceType);
            header.AppendLine("# gfxMemMB\t" + SystemInfo.graphicsMemorySize);
            header.AppendLine("# quality\t" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            header.AppendLine("# vsync\t" + QualitySettings.vSyncCount);
            header.AppendLine("# targetFps\t" + Application.targetFrameRate);
            header.AppendLine("# columns\tsimT\tfps\tframeMs\tcpuMainMs\tgpuMs\trScaleW\trScaleH\tscreenW\tscreenH\tvramMB\treservedMB\tmonoMB\tcamFarM\tposX\tposY\tposZ"
                + SurfacePerf.PerfLogColumns + "\tliveMeshes\ttotalAllocMB\tmonoUsedMB");

            try
            {
                File.WriteAllText(path, header.ToString(), enc);
                Debug.Log("[PerfLog] writing to: " + path);
            }
            catch (System.Exception e)
            {
                broken = true;
                Debug.LogError("[PerfLog] cannot open " + path + ": " + e.Message);
            }
        }

        private void Update()
        {
            if (broken)
            {
                return;
            }

            AccumulateTimings();

            if (Time.unscaledTime < nextWriteTime)
            {
                return;
            }

            nextWriteTime = Time.unscaledTime + IntervalSeconds;
            WriteRow();
        }

        /// <summary>
        /// Averages timings over several frames. A single FrameTiming is quantized by
        /// vsync and gives garbage, and a single fps sample explains nothing in a
        /// build with a floating frame rate.
        /// </summary>
        private void AccumulateTimings()
        {
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(TimingBufferSize, timings);

            for (int i = 0; i < count; i++)
            {
                // gpuFrameTime is zero on some configurations — there is nothing to
                // average then, and 0 in the log reads as "zero".
                if (timings[i].gpuFrameTime > 0.0)
                {
                    gpuMsSum += (float)timings[i].gpuFrameTime;
                }

                if (timings[i].cpuMainThreadFrameTime > 0.0)
                {
                    cpuMainMsSum += (float)timings[i].cpuMainThreadFrameTime;
                }

                sampleCount++;
            }
        }

        private void WriteRow()
        {
            float avgGpu = sampleCount > 0 ? gpuMsSum / sampleCount : 0f;
            float avgCpu = sampleCount > 0 ? cpuMainMsSum / sampleCount : 0f;
            gpuMsSum = 0f;
            cpuMainMsSum = 0f;
            sampleCount = 0;

            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float camFar = cam != null ? cam.farClipPlane : 0f;
            var inv = CultureInfo.InvariantCulture;

            line.Length = 0;
            line.Append(Time.time.ToString("F2", inv)).Append('\t');
            line.Append((1f / Mathf.Max(1e-5f, Time.smoothDeltaTime)).ToString("F1", inv)).Append('\t');
            line.Append((Time.smoothDeltaTime * 1000f).ToString("F2", inv)).Append('\t');
            line.Append(avgCpu.ToString("F2", inv)).Append('\t');
            line.Append(avgGpu.ToString("F2", inv)).Append('\t');
            line.Append(ScalableBufferManager.widthScaleFactor.ToString("F3", inv)).Append('\t');
            line.Append(ScalableBufferManager.heightScaleFactor.ToString("F3", inv)).Append('\t');
            line.Append(Screen.width.ToString(inv)).Append('\t');
            line.Append(Screen.height.ToString(inv)).Append('\t');
            line.Append((Profiler.GetAllocatedMemoryForGraphicsDriver() / (1024 * 1024)).ToString(inv)).Append('\t');
            line.Append((Profiler.GetTotalReservedMemoryLong() / (1024 * 1024)).ToString(inv)).Append('\t');
            line.Append((Profiler.GetMonoUsedSizeLong() / (1024 * 1024)).ToString(inv)).Append('\t');
            line.Append(camFar.ToString("F0", inv)).Append('\t');
            line.Append(camPos.x.ToString("F0", inv)).Append('\t');
            line.Append(camPos.y.ToString("F0", inv)).Append('\t');
            line.Append(camPos.z.ToString("F0", inv)).Append('\t');
            // New columns go strictly AT THE END: old indices must not shift, otherwise
            // already collected perf_log.txt files stop parsing with the same script.
            SurfacePerf.AppendPerfLogColumns(line);
            SurfacePerf.AppendSlowColumns(line);

            try
            {
                File.AppendAllText(path, line + "\n", new UTF8Encoding(false));
            }
            catch (System.Exception e)
            {
                broken = true;
                Debug.LogWarning("[PerfLog] write failed, logging stopped: " + e.Message);
            }
        }
    }
}
