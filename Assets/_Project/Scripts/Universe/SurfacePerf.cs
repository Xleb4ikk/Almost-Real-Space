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
    /// Диагностика рендера планетной поверхности: маркеры профилировщика и
    /// счётчики за кадр.
    ///
    /// Живёт отдельным файлом намеренно. PlanetSurfaceRenderer — на 5000+
    /// строк, в нём часть старых комментариев в кракозябрах (двойная
    /// перекодировка), и любая правка его целиком рискует их затереть. Здесь
    /// файл чистый, и в главный класс добавляются только вызовы.
    ///
    /// Два независимых счётчика мешей, потому что утечка чанковых Mesh
    /// (BuildChunk создаёт new Mesh(), а Destroy(chunk.Go) меш не убирает)
    /// проверяется двумя разными способами, и нужны оба:
    ///   * MeshesCreated / MeshesDestroyed — бесплатные и точные. Их ведёт сам
    ///     рендерер в точках создания и уничтожения. Ненулевой растущий баланс
    ///     = утечка, и видно это на любом кадре, без сканов.
    ///   * LiveMeshes — скан Resources.FindObjectsOfTypeAll&lt;Mesh&gt;(). Он
    ///     абсолютный (ловит и чужую утечку), но сам по себе даёт скачок кадра
    ///     на 1000+ объектах, поэтому включается флагом и идёт раз в 2 с.
    ///     Мерить фризы им самим нельзя.
    /// </summary>
    public static class SurfacePerf
    {
        // ===== Маркеры профилировщика =====
        // Префикс PSR. — чтобы находить в Profiler одним фильтром.
        // В сборке без Development Build маркеры компилируются в ничто.
        //
        // Категория задана ЯВНО. У одноаргументного конструктора она своя
        // внутренняя, и ProfilerRecorder, стартованный на ProfilerCategory.
        // Scripts, такой маркер не увидит — рекордер вернёт пустоту, и выглядит
        // это как «фаза ничего не стоит», а не как «я ищу не там». Явная
        // категория заодно кладёт маркеры в раздел Scripts.

        private static readonly ProfilerCategory PhaseCategory = ProfilerCategory.Scripts;

        public static readonly ProfilerMarker TraverseMarker = new ProfilerMarker(PhaseCategory, "PSR.Traverse");
        public static readonly ProfilerMarker BuildChunkMarker = new ProfilerMarker(PhaseCategory, "PSR.BuildChunk");
        public static readonly ProfilerMarker EvictMarker = new ProfilerMarker(PhaseCategory, "PSR.Evict");
        public static readonly ProfilerMarker ChunkTransformsMarker = new ProfilerMarker(PhaseCategory, "PSR.ChunkTransforms");
        public static readonly ProfilerMarker RefreshMovingDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.RefreshMoving");
        public static readonly ProfilerMarker EnsureVisibleDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.EnsureVisible");
        public static readonly ProfilerMarker TrimDistantDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.TrimDistant");
        public static readonly ProfilerMarker StepDecorBuildsMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.StepBuilds");
        public static readonly ProfilerMarker UpdateDecorCollisionMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.UpdateCollision");
        public static readonly ProfilerMarker DrawDecorMarker = new ProfilerMarker(PhaseCategory, "PSR.Decor.Draw");

        // ===== Счётчики за кадр (обнуляются в BeginFrame) =====

        /// <summary>Чанков построено в этом кадре (синхронно или финализацией).</summary>
        public static int ChunksBuilt;

        /// <summary>Мс на постройку чанков в этом кадре, суммарно по всем.</summary>
        public static float BuildChunkMs;

        /// <summary>Сколько узлов quadtree хочет видеть Traverse на этом кадре.</summary>
        public static int DesiredNodes;

        /// <summary>Сколько узлов построено и видимо.</summary>
        public static int VisibleChunks;

        /// <summary>Сколько чанков реально активны (Visible == true). Больше
        /// VisibleChunks — значит в кадр рисуются предки, оставленные
        /// видимыми как затычки дыр; это лишний overdraw.</summary>
        public static int ActiveChunks;

        /// <summary>Сколько чанков выгружено на последнем проходе выгрузки.</summary>
        public static int EvictedLast;

        /// <summary>Размер множества keep на последнем кадре. Если он близок к
        /// размеру кэша, вычистить нечего — кэш упирается в keep, а не в лимит.</summary>
        public static int KeepCount;

        /// <summary>Сколько узлов из desired попало в keep. По построению равно
        /// VisibleChunks того же кадра. Расхождение означает, что кэш изменился
        /// между постановкой в visible и сборкой keep, и на экране есть дыры.</summary>
        public static int DesiredInKeep;

        /// <summary>Сколько кандидатов на выгрузку нашлось на последнем проходе.</summary>
        public static int EvictCandidates;

        /// <summary>Размер кэша чанков, включая невидимые.</summary>
        public static int CachedChunks;

        /// <summary>Треугольников рельефа в видимых чанках.</summary>
        public static int TerrainTriangles;

        /// <summary>Managed-аллокаций байт за кадр на главном потоке.</summary>
        public static long GcBytes;

        /// <summary>Запросов на постройку в очереди (Фаза 2+; 0 до неё).</summary>
        public static int QueuedBuilds;

        /// <summary>Построек в полёте, не дождавшихся финализации.</summary>
        public static int InFlightBuilds;

        /// <summary>Чанков, перелитых в меш с начала сессии.</summary>
        public static int ChunksFinalized;

        /// <summary>Сколько узлов поставлено в очередь на этом кадре.</summary>
        public static int ChunksEnqueued;

        /// <summary>Сколько записей очереди выброшено на этом кадре как
        /// устаревшие: узел ушёл из desired за MaxChunkQueueAgeFrames кадров.</summary>
        public static int QueueDropped;

        /// <summary>Сколько чанков не удалось залить в меш (исключение в
        /// FinalizeChunk). Ненулевое значение означает, что async-путь сыпет
        /// ошибками: смотрите консоль, там же печатается причина.</summary>
        public static int FinalizeErrors;

        /// <summary>Сколько префетч-узлов попало в очередь на постройку.</summary>
        public static int PrefetchQueued;

        /// <summary>Сколько узлов выдал префетч-обход.</summary>
        public static int PrefetchNodes;

        // ===== Диагностика декора =====

        /// <summary>Высота камеры над рельефом, м. 0 = не вычислена.</summary>
        public static float DecorAltitude;

        /// <summary>Скорость по поверхности, сглаженная, м/с.</summary>
        public static float DecorSpeed;

        /// <summary>Сколько пулов декора сейчас рисуется.</summary>
        public static int DecorPools;

        /// <summary>Сколько инстансов в этих пулах.</summary>
        public static int DecorInstances;

        // ===== Тир качества по скорости (Фаза 3) =====

        /// <summary>0 = Normal, 1 = Fast, 2 = Extreme.</summary>
        public static int MotionTier;

        /// <summary>True, если тир поднят по времени кадра, а не по скорости.</summary>
        public static bool TierAutoRaised;

        // ===== Счётчики мешей =====

        public static int MeshesCreated;
        public static int MeshesDestroyed;

        /// <summary>Сколько мешей создано и ещё не уничтожено. Ненулевое и
        /// растущее = утечка.</summary>
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

        // ===== Медленная диагностика (раз в 2 с, по флагу) =====

        /// <summary>Абсолютное число живых Mesh по последнему скану.</summary>
        public static int LiveMeshes;

        /// <summary>Profiler.GetTotalAllocatedMemoryLong на момент последнего скана.</summary>
        public static long TotalAllocated;

        /// <summary>mono used size в байтах.</summary>
        public static long MonoUsedBytes;

        private static float nextSlowScanTime;

        /// <summary>
        /// Включать ли дорогой скан живых мешей. Ставится бенчмарком на время
        /// прогона; вручную — флагом LogGeometryStats у рендерера. По умолчанию
        /// выключено: FindObjectsOfTypeAll обходит все загруженные объекты и на
        /// 1000+ мешей сам даёт фриз, то есть испортил бы ровно то, что мы меряем.
        /// </summary>
        public static bool ScanLiveMeshes;

        /// <summary>
        /// Скан абсолютных чисел. Идёт раз в 2 с и только когда скан разрешён.
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

        // ===== Кадр =====

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
        }

        /// <summary>
        /// Закрывает кадр. allocBefore — значение
        /// GC.GetAllocatedBytesForCurrentThread() в начале LateUpdate рендерера.
        /// </summary>
        public static void EndFrame(long allocBefore)
        {
            GcBytes = GC.GetAllocatedBytesForCurrentThread() - allocBefore;
        }

        // ===== Вывод =====

        /// <summary>Заголовок новых колонок PerfLog. Дописывается В КОНЕЦ, чтобы
        /// номера старых колонок не поехали.</summary>
        public const string PerfLogColumns =
            "\tbuiltPerFrame\tbuildMs\tvisibleChunks\tcachedChunks\tdesiredNodes\ttris\t"
            + "gcBytesPerFrame\tmeshBalance\tdecorAltM\tdecorSpeed";

        /// <summary>Значения тех же колонок. Порядок обязан совпадать с
        /// <see cref="PerfLogColumns"/> — это единственное место, где они связаны.</summary>
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
            line.Append(DecorSpeed.ToString("F0", inv));
        }

        /// <summary>Абсолютные (не покадровые) числа — раз в 2 с, в конец строки.</summary>
        public static void AppendSlowColumns(StringBuilder line)
        {
            var inv = CultureInfo.InvariantCulture;
            line.Append('\t').Append(LiveMeshes.ToString(inv));
            line.Append('\t').Append((TotalAllocated / (1024 * 1024)).ToString(inv));
            line.Append('\t').Append((MonoUsedBytes / (1024 * 1024)).ToString(inv));
        }

        private static readonly StringBuilder hudBuilder = new StringBuilder(256);

        // ===== Замеры самих маркеров (Фаза 4: решать по профилю) =====
        //
        // ProfilerRecorder умеет читать пользовательский ProfilerMarker по
        // имени, поэтому фазы рендера меряются из кода, без Profiler и без
        // ручного разбора. Работает и в сборке. Выключено по умолчанию:
        // набор рекордеров сам стоит денег на каждом кадре.
        private static ProfilerRecorder[] markerRecorders;
        private static string[] markerNames;

        private static readonly string[] MarkerList =
        {
            "PSR.Traverse", "PSR.BuildChunk", "PSR.Evict", "PSR.ChunkTransforms",
            "PSR.Decor.RefreshMoving", "PSR.Decor.EnsureVisible", "PSR.Decor.TrimDistant",
            "PSR.Decor.StepBuilds", "PSR.Decor.UpdateCollision", "PSR.Decor.Draw",
        };

        /// <summary>Включить замеры фаз. Расход — по одному счётчику на
        /// маркер, это не то же самое, что открытый Profiler.</summary>
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

        /// <summary>Разбор фаз за окно в миллисекундах. Пусто, если замеры
        /// выключены.</summary>
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

                // LastValue в наносекундах; усредняем по накопленному окну,
                // иначе на экране мелькают одиночные кадры.
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

        /// <summary>Однократный разбор в консоль — для профиля, который
        /// снимают раз в полчаса, а не постоянно.</summary>
        public static void LogMarkerTimings()
        {
            Debug.Log("[SurfacePerf] разбор фаз (мс на кадр):\n" + MarkerTimings());
        }

        /// <summary>
        /// Компактные счётчики для HUD читов. Строка собирается в кэшированный
        /// StringBuilder и переиспользуется. Сам HUD в IMGUI и так аллоцирует,
        /// лишнюю копию в кадр добавлять незачем; когда HUD выключен, строка
        /// вообще не собирается.
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
    /// Бенчмарк-полёт: гоняет игрока по большому кругу на заданной высоте с
    /// заданной скоростью и пишёт разбор времени кадра в benchmark.csv.
    ///
    /// Управление — штатным ноуклипом (PlayerIntent.NoclipDirection/NoclipSpeed),
    /// как в CheatMenu: отдельная физика для замеров давала бы числа не той
    /// системы, которую мы чиним.
    ///
    /// ПОРЯДОК ВЫПОЛНЕНИЯ ЗДЕСЬ НЕ КОСМЕТИКА, а условие работоспособности.
    /// PlayerController (порядок -100) каждый кадр собирает намерение с нуля:
    /// <c>PlayerIntent intent = PlayerIntent.Idle;</c> и потом
    /// <c>Runner.PlayerIntent = intent;</c>. То есть НЕ ТОЛЬКО пишет своё, но и
    /// затирает всё, что поставили до него, а NoclipDirection оставляет нулём,
    /// если клавиши движения не зажаты. SimulationRunner (порядок 0) читает
    /// интент уже после этого. Значит записать направление можно только в окне
    /// между -100 и 0 — отсюда -50. Записать позже (например в Update порядка
    /// 100, «после всего») можно, и ничего не сломается, но игрок за этот кадр
    /// уже ничего не услышит: он шагается по интенту прошлого кадра.
    ///
    /// Высота держится подруливанием: направление = касательная + подъём по
    /// ошибке высоты, горизонт упреждения — 2 с полёта. Телепортировать игрока
    /// без правок раннера нельзя (PlayerPosition — read-only), а фактическая
    /// достигнутая высота пишется в CSV отдельной колонкой, так что результат
    /// сам себя описывает.
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

            // Счётчики кадра от рендерера к этому моменту уже итоговые:
            // PlanetSurfaceRenderer.LateUpdate имеет порядок -70, наш — 100.
            if (sampleCount < sampleLimit)
            {
                frameSamples[sampleCount++] = Time.unscaledDeltaTime * 1000f;
            }

            chunksBuiltTotal += SurfacePerf.ChunksBuilt;
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

        // ===== Управление прогоном =====

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
                // Меню держит свою копию намерения и каждый кадр сверяет её с
                // Runner.NoclipActive: без этой флажка оно выключит ноуклик на
                // следующем же кадре, а ползунок в окне затрёт нашу скорость.
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

        /// <summary>Почему последний StartBenchmark не начал прогон. Пусто —
        /// начал. Раньше причины ранних выходов были только в консоли, и из
        /// командной строки (eval, автоматический запуск) было видно лишь
        /// «Бенчмарк не запущен» без причины.</summary>
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

            // 10 и 100 км на 200 м/с: на большой высоте горизонт планеты уезжает
            // за сотни километров, и площадь, которую надо держать в LOD, растёт
            // квадратично. Это отдельная от скорости причина просадки, и без её
            // замера её легко спутать со скоростной.
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

        /// <summary>Единая точка включения ноуклипа: через меню, если оно есть,
        /// иначе напрямую. Иначе меню на следующем кадре откатит состояние.</summary>
        private void RequestNoclip(bool active)
        {
            if (Menu != null)
            {
                Menu.RequestNoclip(active);
                return;
            }

            Runner.SetNoclip(active, Vector3d.Zero);
        }

        // ===== Фазы =====

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
            buildMsTotal = 0f;
            gcBytesTotal = 0;
            decorPoolsTotal = 0;
            decorInstancesTotal = 0;
            altitudeSum = 0d;
            meshBalanceStart = SurfacePerf.MeshBalance;
            totalAllocStart = SurfacePerf.TotalAllocated;
            if (ScanLiveMeshes)
            {
                // Один скан на старте замера, а не «последний»: двухсекундный
                // скан мог прийтись на разогрев, и тогда start был бы с других
                // условий, чем end.
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

            // Порядок -50: PlayerController (его -100) уже отработал и затирает
            // интент, SimulationRunner (0) ещё не читал. Пишем скорость и в
            // CheatMenu (оттуда её возьмёт контроллер в следующем кадре) и в сам
            // PlayerIntent (чтобы раннер увидел её уже в этом).
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

        // ===== Удержание высоты и направление =====

        /// <summary>
        /// Касательная к сфере (высоту сохраняет сама по себе) плюс подъём по
        /// ошибке высоты. Горизонт упреждения ограничен: на 2000 м/с коррекция
        /// в 100 м без ограничения даёт наклон, который за два секунды уводит
        /// на километр.
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

            // «Восток» — касательная в сторону собственного вращения тела.
            // Если ось вращения случайно параллельна направлению на игрока,
            // берём любую другую касательную, чтобы не делить на ноль.
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
            // Файл дописываем, а не перезаписываем: сравнение «до/после» — это
            // два запуска подряд в одном файле. Полную шапку печатаем только в
            // пустой файл, иначе перед каждым запуском шла бы ещё и шапка, и
            // разбирать CSV пришлось бы вручную.
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

            // Сортируем КОПИЮ: исходный буфер остаётся целым до конца прогона.
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
                // Пишем построчно: если прогон прервут (закрыли игру, упал кадр),
                // уже записанные строки останутся на диске.
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
        /// Поднимаемся сами, как PerfLog. Бенчмарк нужен на любой сцене с
        /// SimulationRunner, и класть его в сцену вручную — лишний шаг, который
        /// придётся повторять в каждой тестовой сцене. Сам он ничего не делает
        /// до нажатия кнопки: Update/LateUpdate выходят сразу, пока фаза Idle.
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
