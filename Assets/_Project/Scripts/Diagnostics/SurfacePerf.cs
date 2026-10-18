using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Galilego.Core;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Galilego.Universe
{
    /// <summary>
    /// Planet surface render profiling: profiler markers and per-frame counters.
    ///
    /// Two independent mesh counters, both needed: leak of chunk Meshes is
    /// checked two ways (BuildChunk creates new Mesh(), while Destroy(chunk.Go)
    /// does not remove the Mesh):
    ///   * MeshesCreated / MeshesDestroyed — free and exact. Fed by the renderer
    ///     at creation and destruction points. A non-zero growing balance means
    ///     a leak, visible on any frame, without scans.
    ///   * LiveMeshes — a Resources.FindObjectsOfTypeAll&lt;Mesh&gt;() scan. It is
    ///     absolute (catches foreign leaks too) but spikes the frame at 1000+
    ///     objects, so it is flag-gated and runs once per 2 s. Do not use it to
    ///     measure hitches.
    /// </summary>
    public static class SurfacePerf
    {
        // ===== Profiler markers =====
        // PSR. prefix, so one filter finds them all in the Profiler.
        // Without a Development Build, markers compile to nothing.
        //
        // The category is passed EXPLICITLY: the single-argument constructor uses
        // its own internal category, and a ProfilerRecorder started on
        // ProfilerCategory.Scripts does not see such markers. The explicit
        // category also puts the markers in the Scripts section.

        private static readonly ProfilerCategory PhaseCategory = ProfilerCategory.Scripts;

        public static readonly ProfilerMarker TraverseMarker = new ProfilerMarker(PhaseCategory, "PSR.Traverse");
        public static readonly ProfilerMarker BuildChunkMarker = new ProfilerMarker(PhaseCategory, "PSR.BuildChunk");
        public static readonly ProfilerMarker EvictMarker = new ProfilerMarker(PhaseCategory, "PSR.Evict");
        public static readonly ProfilerMarker ChunkTransformsMarker = new ProfilerMarker(PhaseCategory, "PSR.ChunkTransforms");
        public static readonly ProfilerMarker RefreshMovingDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.RefreshMoving");
        public static readonly ProfilerMarker EnsureVisibleDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.EnsureVisible");
        public static readonly ProfilerMarker TrimDistantDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.TrimDistant");
        public static readonly ProfilerMarker StepDecorBuildsMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.StepBuilds");
        public static readonly ProfilerMarker FinalizeDecorBuildMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.Finalize");
        public static readonly ProfilerMarker UpdateDecorCollisionMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.UpdateCollision");
        public static readonly ProfilerMarker DrawDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.Draw");

        // ===== Per-frame counters (reset in BeginFrame) =====

        /// <summary>Chunks built this frame (synchronously or by finalization).</summary>
        public static int ChunksBuilt;

        /// <summary>Milliseconds spent building chunks this frame, summed over all.</summary>
        public static float BuildChunkMs;

        /// <summary>How many quadtree nodes Traverse wants to see this frame.</summary>
        public static int DesiredNodes;

        /// <summary>How many nodes are built and visible.</summary>
        public static int VisibleChunks;

        /// <summary>Chunks actually active (Visible == true). More than
        /// VisibleChunks means ancestor chunks left visible as hole plugs are
        /// drawn too: extra overdraw.</summary>
        public static int ActiveChunks;

        /// <summary>Chunks evicted on the last eviction pass.</summary>
        public static int EvictedLast;

        /// <summary>Size of the keep set on the last frame. Close to the cache
        /// size means nothing can be evicted: keep, not the limit, binds.</summary>
        public static int KeepCount;

        /// <summary>Nodes from desired that reached keep. Equals VisibleChunks of
        /// the same frame by construction; a mismatch means the cache changed
        /// between marking visible and building keep — holes on screen.</summary>
        public static int DesiredInKeep;

        /// <summary>Eviction candidates found on the last pass.</summary>
        public static int EvictCandidates;

        /// <summary>Chunk cache size, including invisible chunks.</summary>
        public static int CachedChunks;

        /// <summary>Terrain triangles in visible chunks.</summary>
        public static int TerrainTriangles;

        /// <summary>Managed bytes allocated this frame on the main thread.</summary>
        public static long GcBytes;

        /// <summary>Queued build requests (Phase 2+; 0 before that).</summary>
        public static int QueuedBuilds;

        /// <summary>Builds in flight, not yet finalized.</summary>
        public static int InFlightBuilds;

        /// <summary>Chunks uploaded into a mesh since session start.</summary>
        public static int ChunksFinalized;

        /// <summary>Nodes enqueued this frame.</summary>
        public static int ChunksEnqueued;

        /// <summary>Queue entries dropped as stale this frame: the node left
        /// desired within MaxChunkQueueAgeFrames frames.</summary>
        public static int QueueDropped;

        /// <summary>Chunks that failed to upload into a mesh (exception in
        /// FinalizeChunk). Non-zero means the async path is erroring: see the
        /// console, the reason is printed there.</summary>
        public static int FinalizeErrors;

        /// <summary>Prefetch nodes that entered the build queue.</summary>
        public static int PrefetchQueued;

        /// <summary>Nodes emitted by the prefetch traversal.</summary>
        public static int PrefetchNodes;

        // ===== Decor diagnostics =====

        /// <summary>Camera height above terrain, m. 0 = not computed.</summary>
        public static float DecorAltitude;

        /// <summary>Smoothed surface speed, m/s.</summary>
        public static float DecorSpeed;

        /// <summary>Decor pools currently drawn.</summary>
        public static int DecorPools;

        /// <summary>Instances in those pools.</summary>
        public static int DecorInstances;

        /// <summary>
        /// Decor layer rebuilds per second (RefreshMovingDecor). Counted in
        /// PerfLog because the rate is invisible to the eye: the higher it is,
        /// the smoother the carpet while walking and the more build work.
        /// </summary>
        public static int DecorRebuildsLastSample;

        /// <summary>Main-thread milliseconds per second spent rebuilding decor.</summary>
        public static float DecorRebuildMsPerSecond;

        /// <summary>
        /// Grass instances born within DecorBirthProbeMeters of the camera per
        /// second: the "bare ground underfoot" metric. While the player walks,
        /// grass near him must neither be born nor grow. Counted by the renderer
        /// at build finalization, where instances already exist.
        /// </summary>
        public static int DecorBirthsNearCamera;

        /// <summary>Radius within which births are counted (m).</summary>
        public const float DecorBirthProbeMeters = 25f;

        // ===== Speed-based quality tier (Phase 3) =====

        /// <summary>0 = Normal, 1 = Fast, 2 = Extreme.</summary>
        public static int MotionTier;

        /// <summary>True if the tier was raised by frame time rather than by speed.</summary>
        public static bool TierAutoRaised;

        // ===== Mesh counters =====

        public static int MeshesCreated;
        public static int MeshesDestroyed;

        /// <summary>Meshes created and not yet destroyed. Non-zero and
        /// growing = leak.</summary>
        public static int MeshBalance => MeshesCreated - MeshesDestroyed;

        public static void NoteMeshCreated()
        {
            MeshesCreated++;
        }

        public static void NoteMeshDestroyed()
        {
            MeshesDestroyed++;
        }

        public static void ResetMeshCounters()
        {
            MeshesCreated = 0;
            MeshesDestroyed = 0;
        }

        // ===== Slow diagnostics (once per 2 s, behind a flag) =====

        /// <summary>Absolute live Mesh count from the last scan.</summary>
        public static int LiveMeshes;

        /// <summary>Profiler.GetTotalAllocatedMemoryLong at the moment of the last scan.</summary>
        public static long TotalAllocated;

        /// <summary>Mono used size in bytes.</summary>
        public static long MonoUsedBytes;

        private static float nextSlowScanTime;

        /// <summary>
        /// Whether to run the expensive live-mesh scan. Set by the benchmark for
        /// the duration of a run; manually via the renderer's LogGeometryStats
        /// flag. Off by default: FindObjectsOfTypeAll walks all loaded objects
        /// and freezes on 1000+ meshes, spoiling exactly what we measure.
        /// </summary>
        public static bool ScanLiveMeshes;

        /// <summary>
        /// Absolute-number scan. Runs once per 2 s, and only when scanning is allowed.
        /// </summary>
        public static void MaybeScanSlowDiagnostics(bool scanLiveMeshes)
        {
            if ((!scanLiveMeshes && !ScanLiveMeshes) || Time.unscaledTime < nextSlowScanTime)
            {
                return;
            }

            nextSlowScanTime = Time.unscaledTime + 2f;
            LiveMeshes = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            TotalAllocated = Profiler.GetTotalAllocatedMemoryLong();
            MonoUsedBytes = Profiler.GetMonoUsedSizeLong();
        }

        // ===== Frame =====

        public static void BeginFrame()
        {
            ChunksBuilt = 0;
            BuildChunkMs = 0f;
            DesiredNodes = 0;
            VisibleChunks = 0;
            ActiveChunks = 0;
            EvictedLast = 0;
            CachedChunks = 0;
            TerrainTriangles = 0;
            QueuedBuilds = 0;
            InFlightBuilds = 0;
            ChunksEnqueued = 0;
            QueueDropped = 0;
            KeepCount = 0;
            DesiredInKeep = 0;
            PrefetchQueued = 0;
            PrefetchNodes = 0;
            DecorPools = 0;
            DecorInstances = 0;
            DecorRebuildsThisFrame = 0;
            DecorRebuildMsThisFrame = 0f;
            DecorBirthsNearCameraThisFrame = 0;
        }

        /// <summary>Decor rebuilds this frame (accumulated per second).</summary>
        public static int DecorRebuildsThisFrame;

        /// <summary>Main-thread milliseconds spent on rebuilds this frame.</summary>
        public static float DecorRebuildMsThisFrame;

        /// <summary>Grass instances born near the camera this frame.</summary>
        public static int DecorBirthsNearCameraThisFrame;

        private static float decorAccumTimer;
        private static int decorAccumRebuilds;
        private static float decorAccumMs;
        private static int decorAccumBirths;

        /// <summary>
        /// Accumulates the decor per-second counters. Called from the renderer:
        /// once a second the accumulated values go into the fields PerfLog reads.
        /// </summary>
        public static void TickDecorAccum(float deltaSeconds)
        {
            decorAccumTimer += deltaSeconds;
            decorAccumRebuilds += DecorRebuildsThisFrame;
            decorAccumMs += DecorRebuildMsThisFrame;
            decorAccumBirths += DecorBirthsNearCameraThisFrame;
            DecorRebuildsThisFrame = 0;
            DecorRebuildMsThisFrame = 0f;
            DecorBirthsNearCameraThisFrame = 0;
            if (decorAccumTimer < 1f)
            {
                return;
            }

            DecorRebuildsLastSample = decorAccumRebuilds;
            DecorRebuildMsPerSecond = decorAccumMs;
            DecorBirthsNearCamera = decorAccumBirths;
            decorAccumTimer = 0f;
            decorAccumRebuilds = 0;
            decorAccumMs = 0f;
            decorAccumBirths = 0;
        }

        /// <summary>
        /// Closes the frame. allocBefore is
        /// GC.GetAllocatedBytesForCurrentThread() at the start of the renderer's LateUpdate.
        /// </summary>
        public static void EndFrame(long allocBefore)
        {
            GcBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        }

        // ===== Output =====

        /// <summary>Header of the new PerfLog columns. Appended at the END so
        /// old column numbers do not shift.</summary>
        public const string PerfLogColumns =
            "\tbuiltPerFrame\tbuildMs\tvisibleChunks\tcachedChunks\tdesiredNodes\ttris\t"
            + "gcBytesPerFrame\tmeshBalance\tdecorAltM\tdecorSpeed\tchunksFinalized"
            + "\tdecorRebuildsPerSec\tdecorRebuildMsPerSec\tdecorBirthsNearCamera";

        /// <summary>Values of those columns. Order must match
        /// <see cref="PerfLogColumns"/> — the only place the two are linked.</summary>
        public static void AppendPerfLogColumns(StringBuilder line)
        {
            var inv = CultureInfo.InvariantCulture;
            line.Append(ChunksBuilt.ToString(inv)).Append('\t');
            line.Append(BuildChunkMs.ToString("F3", inv)).Append('\t');
            line.Append(VisibleChunks.ToString(inv)).Append('\t');
            line.Append(CachedChunks.ToString(inv)).Append('\t');
            line.Append(DesiredNodes.ToString(inv)).Append('\t');
            line.Append(TerrainTriangles.ToString(inv)).Append('\t');
            line.Append(GcBytes.ToString(inv)).Append('\t');
            line.Append(MeshBalance.ToString(inv)).Append('\t');
            line.Append(DecorAltitude.ToString("F0", inv)).Append('\t');
            line.Append(DecorSpeed.ToString("F0", inv)).Append('\t');

            // builtPerFrame reads ChunksBuilt, which is always zero when
            // AsyncChunkBuild = 1. chunksFinalized is the only honest build
            // counter in async mode.
            line.Append(ChunksFinalized.ToString(inv));

            // Decor rebuild per-second counters: rate and cost. Together with
            // DecorBirthsNearCamera they measure bare ground underfoot while walking.
            line.Append(DecorRebuildsLastSample.ToString(inv)).Append('\t');
            line.Append(DecorRebuildMsPerSecond.ToString("F2", inv)).Append('\t');
            line.Append(DecorBirthsNearCamera.ToString(inv));
        }

        /// <summary>Absolute (not per-frame) numbers — once per 2 s, appended at the end of the line.</summary>
        public static void AppendSlowColumns(StringBuilder line)
        {
            var inv = CultureInfo.InvariantCulture;
            line.Append('\t').Append(LiveMeshes.ToString(inv));
            line.Append('\t').Append((TotalAllocated / (1024 * 1024)).ToString(inv));
            line.Append('\t').Append((MonoUsedBytes / (1024 * 1024)).ToString(inv));
        }

        private static readonly StringBuilder hudBuilder = new StringBuilder(256);

        // ===== Marker timing (Phase 4: decide by profile) =====
        //
        // ProfilerRecorder reads a custom ProfilerMarker by name, so render
        // phases are timed from code, without the Profiler window. Works in
        // a build. Off by default: the recorder set itself costs every frame.
        private static ProfilerRecorder[] markerRecorders;
        private static string[] markerNames;

        private static readonly string[] MarkerList =
        {
            "PSR.Traverse", "PSR.BuildChunk", "PSR.Evict", "PSR.ChunkTransforms",
            "PSR.Decor.RefreshMoving", "PSR.Decor.EnsureVisible", "PSR.Decor.TrimDistant",
            "PSR.Decor.StepBuilds", "PSR.Decor.Finalize", "PSR.Decor.UpdateCollision", "PSR.Decor.Draw",
        };

        /// <summary>Enable phase timing. Cost is one counter per marker, far
        /// less than an open Profiler window.</summary>
        public static bool MarkerTimersEnabled;

        private static void EnsureMarkerRecorders()
        {
            if (markerRecorders != null || !MarkerTimersEnabled)
            {
                return;
            }

            markerNames = MarkerList;
            markerRecorders = new ProfilerRecorder[markerNames.Length];
            for (int i = 0; i < markerNames.Length; i++)
            {
                markerRecorders[i] = ProfilerRecorder.StartNew(
                    PhaseCategory, markerNames[i], 64);
            }
        }

        private static void ReleaseMarkerRecorders()
        {
            if (markerRecorders == null)
            {
                return;
            }

            for (int i = 0; i < markerRecorders.Length; i++)
            {
                if (markerRecorders[i].Valid)
                {
                    markerRecorders[i].Dispose();
                }
            }

            markerRecorders = null;
            markerNames = null;
        }

        /// <summary>Phase breakdown over the window, in milliseconds. Empty when
        /// timing is disabled.</summary>
        public static string MarkerTimings()
        {
            EnsureMarkerRecorders();
            if (markerRecorders == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder(256);
            for (int i = 0; i < markerRecorders.Length; i++)
            {
                if (!markerRecorders[i].Valid)
                {
                    sb.Append(markerNames[i]).Append(": нет данных\n");
                    continue;
                }

                // Values are in nanoseconds; average over the accumulated window,
                // otherwise single frames flicker on screen.
                long sum = 0;
                int count = 0;
                for (int s = 0; s < markerRecorders[i].Capacity && s < 64; s++)
                {
                    ProfilerRecorderSample sample = markerRecorders[i].GetSample(s);
                    long v = sample.Value;
                    if (v <= 0)
                    {
                        continue;
                    }

                    sum += v;
                    count++;
                }

                sb.Append(markerNames[i]).Append(": ")
                    .Append(count > 0 ? (sum / (double)count / 1e6).ToString("F3", CultureInfo.InvariantCulture) : "-")
                    .Append(" мс\n");
            }

            return sb.ToString();
        }

        /// <summary>One-shot breakdown to the console — for profiling every now
        /// and then rather than continuously.</summary>
        public static void LogMarkerTimings()
        {
            Debug.Log("[SurfacePerf] разбор фаз (мс на кадр):\n" + MarkerTimings());
        }

        /// <summary>
        /// Compact counters for the cheat HUD. The string is built in a cached
        /// StringBuilder and reused; the HUD allocates in IMGUI anyway, so an
        /// extra copy per frame is pointless.
        /// </summary>
        public static string HudLine()
        {
            var inv = CultureInfo.InvariantCulture;
            hudBuilder.Length = 0;
            hudBuilder.Append("чанки: ");
            hudBuilder.Append(VisibleChunks.ToString(inv));
            hudBuilder.Append(" видно / ");
            hudBuilder.Append(CachedChunks.ToString(inv));
            hudBuilder.Append(" в кэше, узлов ");
            hudBuilder.Append(DesiredNodes.ToString(inv));
            hudBuilder.Append(", активных ");
            hudBuilder.Append(ActiveChunks.ToString(inv));
            hudBuilder.Append(" (в keep ");
            hudBuilder.Append(DesiredInKeep.ToString(inv));
            hudBuilder.Append(")");
            hudBuilder.Append(", трис ");
            hudBuilder.Append((TerrainTriangles / 1000).ToString(inv));
            hudBuilder.Append("k");
            hudBuilder.Append('\n');
            hudBuilder.Append("за кадр: построено ");
            hudBuilder.Append(ChunksBuilt.ToString(inv));

            // ChunksBuilt is incremented ONLY in the synchronous branch
            // (PlanetSurfaceRenderer.cs, firstFrame || !AsyncChunkBuild), so it is
            // structurally zero when AsyncChunkBuild = 1. Print both: "built" for
            // synchronous builds and "finalized" (ChunksFinalized) for async ones.
            hudBuilder.Append(" (финализировано всего ");
            hudBuilder.Append(ChunksFinalized.ToString(inv));
            hudBuilder.Append(')');
            hudBuilder.Append(", ");
            hudBuilder.Append(BuildChunkMs.ToString("F2", inv));
            hudBuilder.Append(" мс, GC ");
            hudBuilder.Append((GcBytes / 1024).ToString(inv));
            hudBuilder.Append(" КБ, мешей ±");
            hudBuilder.Append(MeshBalance.ToString(inv));
            if (LiveMeshes > 0)
            {
                hudBuilder.Append(" (всех ");
                hudBuilder.Append(LiveMeshes.ToString(inv));
                hudBuilder.Append(')');
            }

            hudBuilder.Append('\n');
            hudBuilder.Append("декор: высота ");
            hudBuilder.Append(DecorAltitude.ToString("F0", inv));
            hudBuilder.Append(" м, скорость ");
            hudBuilder.Append(DecorSpeed.ToString("F0", inv));
            hudBuilder.Append(" м/с, пулов ");
            hudBuilder.Append(DecorPools.ToString(inv));
            hudBuilder.Append(" / инстансов ");
            hudBuilder.Append(DecorInstances.ToString(inv));
            if (QueuedBuilds > 0 || InFlightBuilds > 0)
            {
                hudBuilder.Append("\nсборка чанков: в очереди ");
                hudBuilder.Append(QueuedBuilds.ToString(inv));
                hudBuilder.Append(", в полёте ");
                hudBuilder.Append(InFlightBuilds.ToString(inv));
            }

            if (MotionTier != 0)
            {
                hudBuilder.Append("\nтир качества: ");
                hudBuilder.Append(MotionTier == 2 ? "Extreme" : "Fast");
                if (TierAutoRaised)
                {
                    hudBuilder.Append(" (по времени кадра)");
                }
            }

            return hudBuilder.ToString();
        }
    }

    /// <summary>
    /// Benchmark flight: drives the player in a large circle at a given altitude
    /// and speed, and writes a frame-time breakdown to benchmark.csv.
    ///
    /// Steering uses the stock noclip (PlayerIntent.NoclipDirection/NoclipSpeed),
    /// as in CheatMenu, so the numbers describe the same system the player flies.
    ///
    /// EXECUTION ORDER MATTERS here. PlayerController (order -100) rebuilds the
    /// intent from scratch every frame (<c>PlayerIntent intent =
    /// PlayerIntent.Idle;</c> then <c>Runner.PlayerIntent = intent;</c>), wiping
    /// anything written before it and leaving NoclipDirection at zero unless
    /// movement keys are held. SimulationRunner (order 0) reads the intent after
    /// that, so the direction can only be written in the window between -100
    /// and 0 — hence -50.
    ///
    /// Altitude is held by steering: direction = tangent + climb by altitude
    /// error, with a 2 s lookahead. Teleporting the player is impossible
    /// (PlayerPosition is read-only), so the achieved altitude is written to the
    /// CSV as its own column.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class FlightBenchmark : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся сам.")]
        public SimulationRunner Runner;

        [Tooltip("Чит-меню: его NoclipSpeed — единственный канал скорости ноуклипа.")]
        public CheatMenu Menu;

        [Header("Прогон")]
        [Tooltip("Высоты над рельефом, м — по каждой прогоняется весь ряд скоростей.")]
        public double[] Altitudes = { 2000d };

        [Tooltip("Скорости ноуклипа, м/с — по каждой прогоняется весь ряд высот.")]
        public double[] Speeds = { 200d, 500d, 1000d, 2000d };

        [Tooltip("Длительность записываемого прогона, с (разогрев идёт до него отдельно).")]
        public float StageSeconds = 30f;

        [Tooltip("Разогрев перед записью, с. Нужен, чтобы в очередь не попало то, что ещё строится с нуля.")]
        public float SettleSeconds = 6f;

        [Tooltip("Скорость набора/сброса высоты, м/с. Отдельно от измеряемой.")]
        public double ClimbSpeed = 6000d;

        [Tooltip("Допуск удержания целевой высоты, м (используется больший из него и 5 % высоты).")]
        public double AltitudeTolerance = 150d;

        [Tooltip("Предел времени на набор высоты, с. Не достигли — пишем прогон с фактической высотой.")]
        public float ClimbTimeout = 90f;

        [Tooltip("Дописать контрольные прогоны «очень высоко и медленно» (10 и 100 км на 200 м/с).")]
        public bool IncludeHighAltitudeRuns = true;

        [Tooltip("Сканировать Resources.FindObjectsOfTypeAll<Mesh> (абсолютный контроль утечки). Сам по себе даёт фриз — включать на длинных прогонах.")]
        public bool ScanLiveMeshes = true;

        private enum Phase
        {
            Idle,
            Climb,
            Settle,
            Record,
            Done,
        }

        private struct RunSpec
        {
            public double Altitude;
            public double Speed;
        }

        private static readonly string[] PhaseNames = { "ожидание", "набор высоты", "разогрев", "замер", "готово" };

        private readonly List<RunSpec> runTable = new List<RunSpec>();

        private Phase phase;
        private int runIndex;
        private float phaseTime;
        private double targetAltitude;
        private double currentAltitude;
        private bool noclipWasActive;
        private double previousNoclipSpeed = 50d;

        private float[] frameSamples;
        private int sampleCount;
        private int sampleLimit;
        private int chunksBuiltTotal;
        private int lastChunksFinalized;
        private float buildMsTotal;
        private long gcBytesTotal;
        private long decorPoolsTotal;
        private long decorInstancesTotal;
        private double altitudeSum;
        private int meshBalanceStart;
        private int liveMeshesStart;
        private long totalAllocStart;

        private readonly StringBuilder row = new StringBuilder(512);
        private string csvPath;

        public bool Running => phase != Phase.Idle && phase != Phase.Done;

        public bool Finished => phase == Phase.Done;

        public float PhaseFraction
        {
            get
            {
                float span = phase == Phase.Climb
                    ? ClimbTimeout
                    : (phase == Phase.Settle ? SettleSeconds : StageSeconds);
                return span > 0.001f ? Mathf.Clamp01(phaseTime / span) : 0f;
            }
        }

        public string StatusLine
        {
            get
            {
                if (phase == Phase.Idle)
                {
                    return "Бенчмарк не запущен";
                }

                if (phase == Phase.Done)
                {
                    return "Бенчмарк закончен: " + csvPath;
                }

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Бенчмарк {0}/{1}: {2}, высота {3:F0} м, скорость {4:F0} м/с ({5:P0})",
                    runIndex + 1,
                    runTable.Count,
                    PhaseNames[(int)phase],
                    currentAltitude,
                    CurrentSpeed,
                    PhaseFraction);
            }
        }

        private double CurrentAltitude => runTable[runIndex].Altitude;

        private double CurrentSpeed => runTable[runIndex].Speed;

        private void Start()
        {
            sampleLimit = Mathf.Max(256, Mathf.CeilToInt(StageSeconds * 240f));
            frameSamples = new float[sampleLimit];
            csvPath = Path.Combine(Application.persistentDataPath, "benchmark.csv");
        }

        private void Update()
        {
            ResolveReferences();
            if (Runner == null)
            {
                return;
            }

            if (phase != Phase.Idle && phase != Phase.Done)
            {
                Tick();
            }
        }

        private void LateUpdate()
        {
            if (phase != Phase.Record)
            {
                return;
            }

            // Renderer frame counters are final by now:
            // PlanetSurfaceRenderer.LateUpdate runs at -70, this one at 100.
            if (sampleCount < sampleLimit)
            {
                frameSamples[sampleCount++] = Time.unscaledDeltaTime * 1000f;
            }

            // ChunksFinalized, not ChunksBuilt: the latter only grows in the
            // synchronous branch and is always zero when AsyncChunkBuild = 1.
            // ChunksFinalized is monotonic since session start, so accumulate the
            // per-frame DELTA, never the value itself.
            int finalizedNow = SurfacePerf.ChunksFinalized;
            int finalizedDelta = finalizedNow - lastChunksFinalized;
            if (finalizedDelta > 0)
            {
                chunksBuiltTotal += finalizedDelta;
            }

            lastChunksFinalized = finalizedNow;
            buildMsTotal += SurfacePerf.BuildChunkMs;
            gcBytesTotal += SurfacePerf.GcBytes;
            decorPoolsTotal += SurfacePerf.DecorPools;
            decorInstancesTotal += SurfacePerf.DecorInstances;
            altitudeSum += currentAltitude;
        }

        private void OnDestroy()
        {
            if (phase != Phase.Idle && phase != Phase.Done)
            {
                RestoreState();
                phase = Phase.Done;
            }
        }

        // ===== Run control =====

        public void StartBenchmark()
        {
            if (Running)
            {
                LastStartError = "бенчмарк уже идёт";
                return;
            }

            ResolveReferences();
            if (Runner == null)
            {
                LastStartError = "не найден SimulationRunner";
                Debug.LogWarning("[Benchmark] " + LastStartError);
                return;
            }

            if (Runner.DominantBody == null)
            {
                LastStartError = "нет доминантного тела (DominantBody == null)";
                Debug.LogWarning("[Benchmark] " + LastStartError);
                return;
            }

            if (Speeds == null || Speeds.Length == 0 || Altitudes == null || Altitudes.Length == 0)
            {
                LastStartError = "пустые списки высот или скоростей";
                Debug.LogWarning("[Benchmark] " + LastStartError);
                return;
            }

            BuildRunTable();
            WriteCsvHeader();

            noclipWasActive = Runner.NoclipActive;
            SurfacePerf.ScanLiveMeshes = ScanLiveMeshes;
            if (Menu != null)
            {
                previousNoclipSpeed = Menu.NoclipSpeed;
                // The menu keeps its own copy of the intent and compares it with
                // Runner.NoclipActive every frame: without this flag it disables
                // noclip on the next frame and its slider overrides our speed.
                Menu.NoclipExternallyDriven = true;
            }

            if (!Runner.NoclipActive)
            {
                RequestNoclip(true);
            }

            runIndex = 0;
            LastStartError = string.Empty;
            BeginClimb();
        }

        /// <summary>Why the last StartBenchmark did not start a run. Empty means
        /// it started. Exposes early-exit reasons that used to be console-only.</summary>
        public string LastStartError { get; private set; } = string.Empty;

        public void StopBenchmark()
        {
            if (phase == Phase.Idle)
            {
                return;
            }

            RestoreState();
            phase = Phase.Done;
            Debug.Log("[Benchmark] остановлен. Данные: " + csvPath);
        }

        private void BuildRunTable()
        {
            runTable.Clear();
            for (int a = 0; a < Altitudes.Length; a++)
            {
                for (int s = 0; s < Speeds.Length; s++)
                {
                    runTable.Add(new RunSpec { Altitude = Altitudes[a], Speed = Speeds[s] });
                }
            }

            if (!IncludeHighAltitudeRuns)
            {
                return;
            }

            // 10 and 100 km at 200 m/s: at high altitude the planet horizon moves
            // out to hundreds of kilometres and the area kept in LOD grows
            // quadratically — a slowdown cause distinct from speed.
            runTable.Add(new RunSpec { Altitude = 10000d, Speed = 200d });
            runTable.Add(new RunSpec { Altitude = 100000d, Speed = 200d });
        }

        private void RestoreState()
        {
            if (Menu != null)
            {
                Menu.NoclipSpeed = previousNoclipSpeed;
                Menu.NoclipExternallyDriven = false;
            }

            SurfacePerf.ScanLiveMeshes = false;

            if (!noclipWasActive && Runner != null && Runner.NoclipActive)
            {
                RequestNoclip(false);
            }
        }

        /// <summary>Single entry point for toggling noclip: through the menu when
        /// present, otherwise directly. Bypassing the menu gets rolled back next frame.</summary>
        private void RequestNoclip(bool active)
        {
            if (Menu != null)
            {
                Menu.RequestNoclip(active);
                return;
            }

            Runner.SetNoclip(active, Vector3d.Zero);
        }

        // ===== Phases =====

        private void BeginClimb()
        {
            phase = Phase.Climb;
            phaseTime = 0f;
            targetAltitude = CurrentAltitude;
        }

        private void BeginSettle()
        {
            phase = Phase.Settle;
            phaseTime = 0f;
        }

        private void BeginRecord()
        {
            phase = Phase.Record;
            phaseTime = 0f;
            sampleCount = 0;
            chunksBuiltTotal = 0;

            // The counter is monotonic since session start: without this anchor
            // the first increment would count the whole session as this run's builds.
            lastChunksFinalized = SurfacePerf.ChunksFinalized;
            buildMsTotal = 0f;
            gcBytesTotal = 0;
            decorPoolsTotal = 0;
            decorInstancesTotal = 0;
            altitudeSum = 0d;
            meshBalanceStart = SurfacePerf.MeshBalance;
            totalAllocStart = SurfacePerf.TotalAllocated;
            if (ScanLiveMeshes)
            {
                // Scan once at run start, not the "last" one: the 2-second scan
                // may have landed during settle, making start and end conditions
                // differ.
                liveMeshesStart = Resources.FindObjectsOfTypeAll<Mesh>().Length;
                SurfacePerf.LiveMeshes = liveMeshesStart;
            }
            else
            {
                liveMeshesStart = 0;
            }
        }

        private void Tick()
        {
            Vector3d direction = Steer();
            if (direction.Magnitude > 1e-12d)
            {
                direction = direction.Normalized;
            }

            // Order -50: PlayerController (-100) has already wiped the intent,
            // SimulationRunner (0) has not read it yet. Write the speed both into
            // CheatMenu (the controller takes it from there next frame) and into
            // PlayerIntent (so the runner sees it this frame).
            SetNoclipSpeed(phase == Phase.Climb ? ClimbSpeed : CurrentSpeed);
            Runner.PlayerIntent.NoclipSpeed = phase == Phase.Climb ? ClimbSpeed : CurrentSpeed;
            Runner.PlayerIntent.NoclipDirection = direction;

            phaseTime += Time.unscaledDeltaTime;

            switch (phase)
            {
                case Phase.Climb:
                    if (Math.Abs(currentAltitude - targetAltitude) <= AltitudeToleranceOf(targetAltitude)
                        || phaseTime >= ClimbTimeout)
                    {
                        phase = Phase.Settle;
                        phaseTime = 0f;
                    }

                    break;

                case Phase.Settle:
                    if (phaseTime >= SettleSeconds)
                    {
                        BeginRecord();
                    }

                    break;

                case Phase.Record:
                    if (phaseTime >= StageSeconds)
                    {
                        WriteRunRow();
                        runIndex++;
                        if (runIndex >= runTable.Count)
                        {
                            RestoreState();
                            phase = Phase.Done;
                            Debug.Log("[Benchmark] записан " + csvPath);
                        }
                        else
                        {
                            BeginClimb();
                        }
                    }

                    break;
            }
        }

        private double AltitudeToleranceOf(double altitude)
        {
            return Math.Max(AltitudeTolerance, Math.Abs(altitude) * 0.05d);
        }

        private void ResolveReferences()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (Menu == null)
            {
                Menu = FindAnyObjectByType<CheatMenu>();
            }
        }

        private void SetNoclipSpeed(double speed)
        {
            if (Menu != null)
            {
                Menu.NoclipSpeed = speed;
                return;
            }

            Runner.PlayerIntent.NoclipSpeed = speed;
        }

        // ===== Altitude hold and heading =====

        /// <summary>
        /// Tangent to the sphere (which preserves altitude by itself) plus climb
        /// by altitude error. The lookahead horizon is clamped: at 2000 m/s an
        /// unclamped 100 m correction gives a slope that drifts a kilometre in
        /// two seconds.
        /// </summary>
        private Vector3d Steer()
        {
            OrbitingBody body = Runner.DominantBody;
            if (body == null)
            {
                return Vector3d.Zero;
            }

            double t = Runner.TimeSeconds;
            body.EvaluateWorldState(t, out Vector3d bodyPos, out _);
            Vector3d relative = Runner.PlayerPosition - bodyPos;
            double radius = Math.Max(1d, relative.Magnitude);
            Vector3d up = relative / radius;
            currentAltitude = AltitudeAboveTerrain(body, t, bodyPos);

            // "East" is the tangent along the body's own spin direction. If the
            // spin axis is parallel to the direction to the player, fall back to
            // another tangent to avoid dividing by zero.
            Vector3d east = Vector3d.Cross(body.SpinAxis, up);
            if (east.Magnitude < 1e-9d)
            {
                east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), up);
            }

            if (east.Magnitude < 1e-9d)
            {
                east = Vector3d.Cross(new Vector3d(1d, 0d, 0d), up);
            }

            east = east.Normalized;

            double error = targetAltitude - currentAltitude;
            double lookahead = Math.Min(20000d, Math.Max(200d, Runner.PlayerFrameRelativeSpeed(t) * 2d));
            double climb = Math.Max(-0.6d, Math.Min(0.6d, error / lookahead));
            return (east + (up * climb)).Normalized;
        }

        private double AltitudeAboveTerrain(OrbitingBody body, double t, Vector3d bodyPos)
        {
            double distance = (Runner.PlayerPosition - bodyPos).Magnitude;
            if (body.Terrain == null)
            {
                return distance - body.Radius;
            }

            body.SurfaceLatLonAt(Runner.PlayerPosition, t, out double lat, out double lon);
            double ground = body.Terrain.GetHeightMeters(body, lat * (Math.PI / 180d), lon * (Math.PI / 180d));
            return distance - body.Radius - ground;
        }

        // ===== CSV =====

        private void WriteCsvHeader()
        {
            var inv = CultureInfo.InvariantCulture;
            var header = new StringBuilder(1024);
            // Append to the file rather than overwrite: a before/after comparison
            // is two back-to-back runs in one file. The full header goes only into
            // an empty file.
            bool fresh = !File.Exists(csvPath) || new FileInfo(csvPath).Length == 0L;
            if (fresh)
            {
                header.Append("# Galilego flight benchmark. Columns are COMMA-separated, '.' decimal.\n");
            }
            else
            {
                header.Append("\n# --- new run ---");
            }

            header.Append('\n');
            header.Append("# generated\t").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append('\n');
            header.Append("# device\t").Append(SystemInfo.deviceModel).Append('\n');
            header.Append("# gpu\t").Append(SystemInfo.graphicsDeviceName).Append('\n');
            header.Append("# unity\t").Append(Application.unityVersion).Append('\n');
            header.Append("# developmentBuild\t").Append(Debug.isDebugBuild ? "true" : "false").Append('\n');
            header.Append("# stageSeconds\t").Append(StageSeconds.ToString("F0", inv)).Append('\n');
            header.Append("# settleSeconds\t").Append(SettleSeconds.ToString("F0", inv)).Append('\n');
            header.Append("run,targetAltitudeM,actualAltitudeM,speedMs,frames,fpsMean,p50Ms,p95Ms,p99Ms,maxMs,"
                + "hitchesOver50ms,chunksBuilt,chunksPerSec,buildMsTotal,buildMsPerChunk,"
                + "gcBytesPerFrameAvg,meshBalanceStart,meshBalanceEnd,liveMeshesStart,liveMeshesEnd,"
                + "totalAllocStartMB,totalAllocEndMB,decorPoolsAvg,decorInstancesAvg,motionTier\n");
            AppendToCsv(header.ToString());
        }

        private void WriteRunRow()
        {
            var inv = CultureInfo.InvariantCulture;
            if (sampleCount == 0)
            {
                return;
            }

            // Sort a COPY: the original buffer stays intact until the run ends.
            var sorted = new float[sampleCount];
            Array.Copy(frameSamples, sorted, sampleCount);
            Array.Sort(sorted);

            double mean = 0d;
            int hitches = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                mean += sorted[i];
                if (sorted[i] > 50f)
                {
                    hitches++;
                }
            }

            mean /= sampleCount;
            double seconds = Math.Max(1e-6d, StageSeconds);

            row.Length = 0;
            row.Append((runIndex + 1).ToString(inv)).Append(',');
            row.Append(targetAltitude.ToString("F0", inv)).Append(',');
            row.Append((altitudeSum / sampleCount).ToString("F0", inv)).Append(',');
            row.Append(CurrentSpeed.ToString("F0", inv)).Append(',');
            row.Append(sampleCount.ToString(inv)).Append(',');
            row.Append((1000d / Math.Max(1e-6d, mean)).ToString("F1", inv)).Append(',');
            row.Append(Percentile(sorted, 0.50).ToString("F2", inv)).Append(',');
            row.Append(Percentile(sorted, 0.95).ToString("F2", inv)).Append(',');
            row.Append(Percentile(sorted, 0.99).ToString("F2", inv)).Append(',');
            row.Append(sorted[sampleCount - 1].ToString("F2", inv)).Append(',');
            row.Append(hitches.ToString(inv)).Append(',');
            row.Append(chunksBuiltTotal.ToString(inv)).Append(',');
            row.Append((chunksBuiltTotal / seconds).ToString("F2", inv)).Append(',');
            row.Append(buildMsTotal.ToString("F1", inv)).Append(',');
            row.Append((chunksBuiltTotal > 0 ? buildMsTotal / chunksBuiltTotal : 0d).ToString("F3", inv)).Append(',');
            row.Append((gcBytesTotal / (double)sampleCount).ToString("F0", inv)).Append(',');
            row.Append(meshBalanceStart.ToString(inv)).Append(',');
            row.Append(SurfacePerf.MeshBalance.ToString(inv)).Append(',');
            row.Append(liveMeshesStart.ToString(inv)).Append(',');
            row.Append(SurfacePerf.LiveMeshes.ToString(inv)).Append(',');
            row.Append((totalAllocStart / (1024 * 1024)).ToString(inv)).Append(',');
            row.Append((SurfacePerf.TotalAllocated / (1024 * 1024)).ToString(inv)).Append(',');
            row.Append((decorPoolsTotal / (double)sampleCount).ToString("F0", inv)).Append(',');
            row.Append((decorInstancesTotal / (double)sampleCount).ToString("F0", inv)).Append(',');
            row.Append(SurfacePerf.MotionTier.ToString(inv)).Append('\n');
            AppendToCsv(row.ToString());
        }

        private void AppendToCsv(string text)
        {
            try
            {
                // Write line by line: if the run is interrupted, the rows already
                // written stay on disk.
                File.AppendAllText(csvPath, text, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError("[Benchmark] не удалось записать " + csvPath + ": " + e.Message);
                enabled = false;
            }
        }

        private static float Percentile(float[] sorted, double p)
        {
            if (sorted.Length == 0)
            {
                return 0f;
            }

            int index = (int)Math.Round(p * (sorted.Length - 1), MidpointRounding.AwayFromZero);
            return sorted[Math.Max(0, Math.Min(sorted.Length - 1, index))];
        }

        /// <summary>
        /// Self-bootstraps, like PerfLog: any scene with a SimulationRunner can
        /// run it without manual setup. Does nothing until started:
        /// Update/LateUpdate exit immediately while the phase is Idle.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<FlightBenchmark>() != null)
            {
                return;
            }

            var go = new GameObject("FlightBenchmark");
            DontDestroyOnLoad(go);
            go.AddComponent<FlightBenchmark>();
        }
    }
}
