using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Конфигурация выпечки эфемерид. Числа по умолчанию — из практики T40/T46:
    /// сегмент = минимальный период поддерева / 4 (кламп [2; 64] сут), степень 12,
    /// подшаг RK4 из бюджета фазовой ошибки быстрейшей орбиты.
    /// </summary>
    public sealed class BakeConfig
    {
        /// <summary>Степень чебышёвского интерполянта на сегмент (узлов degree+1).</summary>
        public int Degree = 12;

        /// <summary>
        /// Бюджет фазовой ошибки на подшаг RK4: n·h ≤ PhasePerStep, где n — среднее
        /// движение быстрейшей орбиты системы. Локальная ошибка RK4 ~ (n·h)⁵/120:
        /// при 0.02 это ~2.7e-9 рад/шаг — за 1024 года накопление остаётся в
        /// метрах. Меньше значение — точнее и дольше.
        /// </summary>
        public double PhasePerStep = 0.02d;

        /// <summary>Абсолютный кап подшага RK4 (с): старый параметр stepSeconds.</summary>
        public double MaxStepSeconds = 900d;

        /// <summary>
        /// Бюджет ошибки позиции квантования BG2 (м) на сегмент: шаг кванта
        /// подбирается так, чтобы худший случай Σ|T_j|·(шаг/2) ≤ budget/2.
        /// 2 м — на 25 раз меньше гейта стыков луны (50 м) и в 25 раз меньше
        /// интерполяционной погрешности T40. Меньше — точнее и тяжелее файл.
        /// </summary>
        public double QuantBudgetMeters = 2d;

        /// <summary>Сегмент = минимальный период поддерева / SegmentPeriodDivisor.</summary>
        public double SegmentPeriodDivisor = 4d;

        public double MinSegmentSeconds = 2d * 86400d;

        public double MaxSegmentSeconds = 64d * 86400d;

        /// <summary>Валидация диапазонов; громкое исключение при мусоре.</summary>
        public void Validate()
        {
            if (Degree < 1 || Degree > 32)
            {
                throw new ArgumentOutOfRangeException(nameof(Degree), Degree, "Степень обязана быть в [1, 32].");
            }

            if (PhasePerStep <= 0d || PhasePerStep > 0.2d)
            {
                throw new ArgumentOutOfRangeException(nameof(PhasePerStep), PhasePerStep, "n·h обязан быть в (0, 0.2].");
            }

            if (MaxStepSeconds <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxStepSeconds), MaxStepSeconds, "Кап подшага обязан быть положительным.");
            }

            if (QuantBudgetMeters <= 0d || QuantBudgetMeters > 1000d)
            {
                throw new ArgumentOutOfRangeException(nameof(QuantBudgetMeters), QuantBudgetMeters, "Бюджет квантования обязан быть в (0, 1000] м.");
            }

            if (SegmentPeriodDivisor <= 0d || MinSegmentSeconds <= 0d || MaxSegmentSeconds < MinSegmentSeconds)
            {
                throw new ArgumentOutOfRangeException(nameof(SegmentPeriodDivisor), "Неконсистентное правило длины сегмента.");
            }
        }
    }

    public static class EphemerisBaker
    {
        public static void ExportFiles(StarSystem sys, string directory)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            Directory.CreateDirectory(directory);
            StringBuilder manifest = new StringBuilder();
            var used = new HashSet<string>();
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Baked == null || body.Baked.Coeffs == null)
                {
                    continue;
                }

                string file = Sanitize(body.Name, used) + ".bin";
                body.Baked.WriteToFile(Path.Combine(directory, file));
                manifest.Append(body.Name);
                manifest.Append('\t');
                manifest.Append(file);
                manifest.Append('\n');
            }

            File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString());
        }

        /// <summary>
        /// Прицепляет испечённые эфемериды из папки (по manifest.txt). Громкий
        /// контракт (аудит S2/2.1): КАЖДОЕ не-корневое тело обязано получить
        /// эфемериду — тело вне манифеста или с нечитаемым файлом оставалось бы
        /// на кеплеровых рельсах/старом Baked молча, давая гетерогенный мир.
        /// Полярность громкости: ValidateBakeable запрещает частичную выпечку,
        /// поэтому полный набор — единственная легитимная конфигурация.
        /// </summary>
        public static void AttachFiles(StarSystem sys, string directory)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            string[] lines = File.ReadAllLines(Path.Combine(directory, "manifest.txt"));
            var table = new Dictionary<string, string>();
            for (int i = 0; i < lines.Length; i++)
            {
                int tab = lines[i].IndexOf('\t');
                if (tab <= 0)
                {
                    continue;
                }

                string name = lines[i].Substring(0, tab);
                string file = lines[i].Substring(tab + 1).Trim();
                if (!table.ContainsKey(name))
                {
                    table[name] = file;
                }
            }

            var attached = new List<OrbitingBody>();
            var failures = new List<string>();
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Parent == null)
                {
                    continue; // звезда не выпекается и не прицепляется
                }

                if (!table.TryGetValue(body.Name, out string file))
                {
                    failures.Add(body.Name + ": нет записи в манифесте");
                    continue;
                }

                BakedEphemeris e = BakedEphemeris.OpenFile(Path.Combine(directory, file));
                if (e == null)
                {
                    failures.Add(body.Name + ": файл \"" + file + "\" не читается");
                    continue;
                }

                BakedEphemeris previous = body.Baked;
                body.Baked = e;
                if (previous != null && !ReferenceEquals(previous, e) && previous.FilePath != null)
                {
                    previous.Dispose();
                }

                attached.Add(body);
            }

            if (failures.Count > 0)
            {
                // Откат частичного attach: уже прицепленные снимаем (файловые —
                // с Dispose) — система остаётся консистентно «не прицепленной»,
                // а не наполовину.
                foreach (OrbitingBody body in attached)
                {
                    BakedEphemeris e = body.Baked;
                    body.Baked = null;
                    if (e != null && e.FilePath != null)
                    {
                        e.Dispose();
                    }
                }

                throw new InvalidOperationException(
                    "AttachFiles: " + failures.Count + " тел(а) без эфемериды — " + string.Join("; ", failures) +
                    ". Испечённый набор обязан покрывать все тела системы.");
            }

            sys.InvalidatePositionCache();
        }

        private static string Sanitize(string name, HashSet<string> used)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder(name.Length);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool bad = char.IsControl(c);
                for (int k = 0; k < invalid.Length && !bad; k++)
                {
                    bad = c == invalid[k];
                }

                sb.Append(bad ? '_' : c);
            }

            string file = sb.ToString().Trim();
            if (file.Length == 0)
            {
                file = "body";
            }

            string candidate = file;
            int suffix = 2;
            while (used.Contains(candidate))
            {
                candidate = file + "_" + suffix;
                suffix++;
            }

            used.Add(candidate);
            return candidate;
        }

        /// <summary>
        /// Длина сегмента тела. Правило: минимальный орбитальный период ПОДДЕРЕВА
        /// (своего и всех потомков), делённый на 4, кламп [2; 64] сут.
        /// Поддерево — не артефакт формулы: у планеты с луной гелиоцентрическая
        /// орбита несёт «рябь» от луны — смещение планеты от барицентра
        /// a_луны·μ_луны/(μ_луны+μ_планеты). Для пресета это 3.84e8·4.9e12/4.035e14
        /// ≈ 4700 км с периодом луны (4.34 сут) — сегмент обязан быть короче
        /// периода ряби, иначе чебышёвская аппроксимация её теряет.
        /// </summary>
        public static double DefaultSegmentLengthSeconds(OrbitingBody body)
        {
            double best = double.PositiveInfinity;
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }

            if (double.IsPositiveInfinity(best))
            {
                return 32d * 86400d;
            }

            double seg = best / 4d;
            if (seg < 2d * 86400d)
            {
                seg = 2d * 86400d;
            }

            if (seg > 64d * 86400d)
            {
                seg = 64d * 86400d;
            }

            return seg;
        }

        private static void AccumulateSubtreePeriods(OrbitingBody body, ref double best)
        {
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }
        }

        private static void AccumulatePeriod(OrbitingBody body, ref double best)
        {
            if (body.Parent == null)
            {
                return;
            }

            double a = body.SemiMajorAxis;
            if (a <= 0d)
            {
                return;
            }

            double mu = body.Parent.ResolveStandardGravitationalParameter() + body.ResolveSubtreeStandardGravitationalParameter();
            if (mu <= 0d)
            {
                return;
            }

            double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);
            if (period > 0d && period < best)
            {
                best = period;
            }
        }

        public static void BakeAndAttach(
            StarSystem sys,
            double spanYears,
            double stepSeconds,
            int degree,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            var baked = Bake(sys, spanYears, stepSeconds, degree, segmentLengthFor);
            foreach (var kv in baked)
            {
                kv.Key.Baked = kv.Value;
            }

            sys.InvalidatePositionCache();
        }

        /// <summary>Legacy-сигнатура: шаг и степень через BakeConfig.</summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> Bake(
            StarSystem sys,
            double spanYears,
            double stepSeconds,
            int degree,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            var config = new BakeConfig { Degree = degree, MaxStepSeconds = stepSeconds };
            return Bake(sys, spanYears, config, segmentLengthFor, null, null);
        }

        /// <summary>
        /// Выпечка в память (все коэффициенты в ОЗУ — для тестов и малых спанов).
        /// Для 1024-летних спанов использовать BakeToFiles: тот же движок, но
        /// сегменты пишутся в файл потоком, пик ОЗУ O(1) на тело.
        /// </summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> Bake(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null,
            Action<double> progress = null,
            Func<bool> cancelled = null)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            var memory = new MemorySink();
            RunBake(sys, spanYears, config, segmentLengthFor, memory, progress, cancelled);
            return memory.Result;
        }

        /// <summary>
        /// B1: потоковая выпечка в файлы (по телу — файл + manifest).
        /// Сегмент пишется в FileStream сразу по готовности — в ОЗУ только буфер
        /// узлов текущего сегмента. Возвращённые BakedEphemeris ссылаются на
        /// файлы (FilePath) без Coeffs — рантайм читает окнами, ОЗУ ограничено
        /// WindowSegments независимо от длины спана.
        /// compress=true — файлы в контейнере BG2: квантование коэффициентов с
        /// бюджетом ошибки config.QuantBudgetMeters (гарантия ≤ budget/2 на
        /// позицию), затем zigzag-varint + gzip. Типичное сжатие 2-4× к BE1.
        /// </summary>
        public static Dictionary<OrbitingBody, BakedEphemeris> BakeToFiles(
            StarSystem sys,
            string directory,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null,
            Action<double> progress = null,
            Func<bool> cancelled = null,
            bool compress = false)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            Directory.CreateDirectory(directory);
            using (var sink = new FileSink(sys, directory, compress, config.QuantBudgetMeters))
            {
                RunBake(sys, spanYears, config, segmentLengthFor, sink, progress, cancelled);
                return sink.Result;
            }
        }

        /// <summary>
        /// B4: оценка размера файлов выпечки без интегрирования (мгновенно).
        /// Ключ — тело, значение — байты его файла (заголовок + сегменты).
        /// Для несжатых BE1-файлов оценка точна (формат детерминирован).
        /// Для BG2 (compress=true в BakeToFiles) это верхняя граница по BE1 —
        /// фактический размер меньше примерно в 2.5-4× (квантование+varint+gzip,
        /// измерено 3.18× на 12-тельной системе, T62/probecmp2), т.к. длина
        /// varint-сегментов зависит от данных и до выпечки неизвестна.
        /// </summary>
        public static Dictionary<OrbitingBody, long> EstimateBakeBytes(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor = null)
        {
            if (segmentLengthFor == null)
            {
                segmentLengthFor = DefaultSegmentLengthSeconds;
            }

            config.Validate();
            double spanSeconds = spanYears * YearSeconds;
            ValidateBakeable(sys, spanSeconds, config, segmentLengthFor);
            var result = new Dictionary<OrbitingBody, long>();
            int stride = BakedEphemeris.AxisStride(config.Degree);
            foreach (OrbitingBody body in sys.AllBodies)
            {
                if (body.Parent == null)
                {
                    continue;
                }

                double spanSecondsLocal = spanSeconds;
                double segLen = AdjustedSegmentLength(spanSecondsLocal, segmentLengthFor(body));
                int segCount = (int)Math.Ceiling(spanSecondsLocal / segLen);
                result[body] = 27L + ContinuationBytesTotal + ((long)segCount * stride * 8L);
            }

            return result;
        }

        private const double YearSeconds = 365d * 86400d;

        /// <summary>Хвост BE2/BG3: 6 doubles кеплерова продолжения.</summary>
        private const long ContinuationBytesTotal = 48L;

        // ════════════════════════════════════════════════════════════════════
        // Ядро: глобальный n-body RK4, сэмплы в чебышёвских узлах Лобатто
        // каждого сегмента, коэффициенты прямым преобразованием (DCT-III).
        // LSQ через Грам-матрицы удалён: у степени 12 узлы Лобатто дают тот же
        // полином, что и LSQ на сотнях равномерных сэмплов, но без решателя и
        // без буферов сэмплов (JPL DE-style).
        // ════════════════════════════════════════════════════════════════════

        private interface ISegmentSink
        {
            void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree);

            void WriteSegment(int bodyIndex, int segIndex, double[] coeffs);

            /// <summary>Кеплерово продолжение тела: [a, e, i°, Ω°, ω°, M0°] — углы в ГРАДУСАХ (как у OrbitalElements), не радианах.</summary>
            void WriteContinuation(int bodyIndex, double[] elements);
        }

        private sealed class MemorySink : ISegmentSink
        {
            internal double[][] Coeffs;
            private readonly List<OrbitingBody> bakedBodies = new List<OrbitingBody>();
            private readonly List<int> segCounts = new List<int>();
            private readonly List<double> segLens = new List<double>();
            private readonly List<int> degrees = new List<int>();
            private int stride;

            public void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree)
            {
                stride = BakedEphemeris.AxisStride(degree);
                bakedBodies.Add(body);
                segCounts.Add(segCount);
                segLens.Add(segLen);
                degrees.Add(degree);
                continuations = new double[bakedBodies.Count][];
                Coeffs = new double[bakedBodies.Count][];
                for (int i = 0; i < bakedBodies.Count; i++)
                {
                    Coeffs[i] = new double[segCounts[i] * stride];
                }
            }

            public void WriteSegment(int bodyIndex, int segIndex, double[] coeffs)
            {
                Array.Copy(coeffs, 0, Coeffs[bodyIndex], segIndex * stride, stride);
            }

            public void WriteContinuation(int bodyIndex, double[] elements)
            {
                continuations[bodyIndex] = (double[])elements.Clone();
            }

            private double[][] continuations;

            public Dictionary<OrbitingBody, BakedEphemeris> Result
            {
                get
                {
                    var result = new Dictionary<OrbitingBody, BakedEphemeris>();
                    for (int i = 0; i < bakedBodies.Count; i++)
                    {
                        result[bakedBodies[i]] = new BakedEphemeris
                        {
                            T0Seconds = 0d,
                            SegmentLengthSeconds = segLens[i],
                            Degree = degrees[i],
                            SegmentCount = segCounts[i],
                            Coeffs = Coeffs[i],
                            Continuation = continuations != null ? continuations[i] : null
                        };
                    }

                    return result;
                }
            }
        }

        private sealed class FileSink : ISegmentSink, IDisposable
        {
            private readonly List<OrbitingBody> bakedBodies = new List<OrbitingBody>();
            private readonly List<Stream> streams = new List<Stream>();
            private readonly List<FileStream> files = new List<FileStream>();
            private readonly List<string> paths = new List<string>();
            private readonly Dictionary<OrbitingBody, BakedEphemeris> result = new Dictionary<OrbitingBody, BakedEphemeris>();
            private byte[] writeBuf;
            private readonly bool compress;
            private double quantBudget;
            private int v2Stride;
            private double[][] continuations;

            internal FileSink(StarSystem sys, string directory, bool compress, double quantBudgetMeters)
            {
                this.compress = compress;
                quantBudget = quantBudgetMeters;
                var used = new HashSet<string>();
                var manifest = new StringBuilder();
                foreach (OrbitingBody body in sys.AllBodies)
                {
                    if (body.Parent == null)
                    {
                        continue;
                    }

                    // Имя файла стабильно: тот же Sanitize, что и ExportFiles —
                    // испечённые файлы читаются AttachFiles без перенастройки.
                    // Формат: BE2 (plain) / BG3 (gzip+квантование); продолжение —
                    // 48 Б в хвосте, дописывается в Dispose после закрытия gzip.
                    string file = Sanitize(body.Name, used) + ".bin";
                    string path = Path.Combine(directory, file);
                    FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                    Stream stream = fs;
                    if (compress)
                    {
                        // BG3: магия вне gzip, payload — [заголовок 27Б][varint-сегменты].
                        // leaveOpen: gzip при Dispose закрывает подлежащий FileStream —
                        // а хвост (продолжение) дописывается в Dispose ПОСЛЕ него.
                        fs.WriteByte(66);
                        fs.WriteByte(71);
                        fs.WriteByte(51);
                        stream = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal, true);
                    }
                    // Plain (BE2): внешней магии нет — магия внутри 27-байтового
                    // заголовка (RegisterLayout пишет "BE2"), как у WritePayload.

                    files.Add(fs);
                    streams.Add(stream);
                    paths.Add(path);
                    bakedBodies.Add(body);
                    manifest.Append(body.Name);
                    manifest.Append('\t');
                    manifest.Append(file);
                    manifest.Append('\n');
                }

                File.WriteAllText(Path.Combine(directory, "manifest.txt"), manifest.ToString());
            }

            public void RegisterLayout(int bodyIndex, OrbitingBody body, double segLen, int segCount, int degree)
            {
                int stride = BakedEphemeris.AxisStride(degree);
                v2Stride = stride;
                writeBuf = compress ? new byte[6 + (stride * 10)] : new byte[stride * 8];
                continuations = new double[bakedBodies.Count][];
                byte[] head = new byte[27];
                head[0] = (byte)66;
                // Внутренняя магия заголовка: BG3/BG2 несут "BE1"-заголовок payload'а
                // (легаси-конвенция BG1), plain BE2 — "BE2" (внешней магии нет).
                head[1] = (byte)69;
                head[2] = compress ? (byte)49 : (byte)50;
                BakedEphemeris.WriteInt32(head, 3, degree);
                BakedEphemeris.WriteInt32(head, 7, segCount);
                BakedEphemeris.WriteFloat64(head, 11, 0d);
                BakedEphemeris.WriteFloat64(head, 19, segLen);
                streams[bodyIndex].Write(head, 0, head.Length);

                result[bakedBodies[bodyIndex]] = new BakedEphemeris
                {
                    T0Seconds = 0d,
                    SegmentLengthSeconds = segLen,
                    Degree = degree,
                    SegmentCount = segCount,
                    FilePath = paths[bodyIndex],
                    Compressed = compress,
                    Quantized = compress,
                    HasContinuationBlock = true
                };
            }

            public void WriteContinuation(int bodyIndex, double[] elements)
            {
                continuations[bodyIndex] = (double[])elements.Clone();
                // Возвращённые BakedEphemeris ссылаются на файл: продолжение уже
                // записано в хвост, и в памяти оно тоже нужно (пока файл не открыт).
                result[bakedBodies[bodyIndex]].Continuation = continuations[bodyIndex];
            }

            public void WriteSegment(int bodyIndex, int segIndex, double[] coeffs)
            {
                if (compress)
                {
                    // BG2: квантование с бюджетом ошибки + zigzag-varint.
                    int n = BakedEphemeris.EncodeSegmentV2(coeffs, v2Stride, quantBudget, writeBuf);
                    streams[bodyIndex].Write(writeBuf, 0, n);
                    return;
                }

                for (int i = 0; i < coeffs.Length; i++)
                {
                    BakedEphemeris.WriteFloat64(writeBuf, i * 8, coeffs[i]);
                }

                streams[bodyIndex].Write(writeBuf, 0, writeBuf.Length);
            }

            public Dictionary<OrbitingBody, BakedEphemeris> Result => result;

            public void Dispose()
            {
                for (int i = 0; i < streams.Count; i++)
                {
                    Stream s = streams[i];
                    if (s is System.IO.Compression.GZipStream)
                    {
                        s.Dispose(); // gzip обязан закрыться, чтобы дописать хвост
                    }
                }

                // Блок продолжения — в хвост каждого файла (вне сжатия): OpenFile
                // читает его seek'ом от конца, не распаковывая сегменты.
                for (int i = 0; i < files.Count; i++)
                {
                    byte[] tail = new byte[BakedEphemeris.ContinuationBytes];
                    for (int j = 0; j < 6; j++)
                    {
                        BakedEphemeris.WriteFloat64(tail, j * 8,
                            continuations != null && continuations[i] != null ? continuations[i][j] : double.NaN);
                    }

                    files[i].Write(tail, 0, tail.Length);
                    files[i].Dispose();
                }

                streams.Clear();
                files.Clear();
            }
        }

        private static double AdjustedSegmentLength(double spanSeconds, double requested)
        {
            int segCount = (int)Math.Ceiling(spanSeconds / requested);
            return spanSeconds / segCount;
        }

        /// <summary>
        /// Валидация bakeable-ности: доставленный сегмент обязан быть короче
        /// периода самой быстрой орбиты поддерева тела. Иначе кривая наматывается
        /// на орбиту многократно и чебышёвский интерполянт по 13 узлам алиасирует
        /// (5.4-часовая луна на 2-сут сегментах — 9 витков на сегмент) — ТИХИЙ
        /// мусор. Дроби орбиты на сегмент валидны: T40 работает с 0.29 орбиты
        /// (8-сут сегменты при 27.25-сут луне, 0.577 м) — граница именно «не
        /// более одного витка». Каскад вверх: рябь луны сидит в рельсе планеты
        /// (смещение планеты от барицентра подсистемы), поэтому предки
        /// unbakeable-тела тоже unbakeable. Громкое исключение со списком —
        /// решение (раздвинуть луну, поднять разрешение, оставить тело на
        /// кеплеровых рельсах) принимает вызывающий, не пекарь.
        /// </summary>
        private static void ValidateBakeable(
            StarSystem sys,
            double spanSeconds,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor)
        {
            var bodies = sys.AllBodies;
            int n = bodies.Count;
            var index = new Dictionary<OrbitingBody, int>(n);
            for (int i = 0; i < n; i++)
            {
                index[bodies[i]] = i;
            }

            bool[] unbakeable = new bool[n];
            var names = new List<string>();
            for (int i = n - 1; i >= 0; i--) // дети-вперёд: каскад вверх
            {
                OrbitingBody body = bodies[i];
                if (body.Parent == null)
                {
                    continue;
                }

                double subtreeMin = SubtreeMinPeriodSeconds(body);
                double delivered = AdjustedSegmentLength(spanSeconds, segmentLengthFor(body));
                bool own = !double.IsPositiveInfinity(subtreeMin) && delivered > subtreeMin * (1d + 1e-9d);
                bool childBad = false;
                foreach (OrbitingBody child in body.Children)
                {
                    if (index.TryGetValue(child, out int c) && unbakeable[c])
                    {
                        childBad = true;
                        break;
                    }
                }

                if (own || childBad)
                {
                    unbakeable[i] = true;
                    names.Add(body.Name + (own
                        ? " (период поддерева " + (subtreeMin / 3600.0).ToString("F1") + "ч, сегмент " + (delivered / 3600.0).ToString("F1") + "ч — " + (delivered / subtreeMin).ToString("F1") + " витков орбиты на сегмент)"
                        : " (несёт рябь быстрого потомка)"));
                }
            }

            if (names.Count > 0)
            {
                throw new InvalidOperationException(
                    "Выпечка невозможна: " + names.Count + " тел(а) с более чем одним витком орбиты на сегмент — " +
                    string.Join("; ", names) +
                    ". Решения: раздвинуть быструю луну, уменьшить минимум сегмента в правиле, или оставить тело на кеплеровых рельсах без выпечки.");
            }
        }

        /// <summary>Минимальный орбитальный период тела и всех потомков (сек).</summary>
        private static double SubtreeMinPeriodSeconds(OrbitingBody body)
        {
            double best = double.PositiveInfinity;
            AccumulatePeriod(body, ref best);
            for (int i = 0; i < body.Children.Count; i++)
            {
                AccumulateSubtreePeriods(body.Children[i], ref best);
            }

            return best;
        }

        /// <summary>
        /// Движок выпечки. Тела интегрируются общим n-body RK4; подшаг капится
        /// бюджетом фазовой ошибки быстрейшей орбиты; посадка ровно в чебышёвские
        /// узлы Лобатто каждого сегмента каждого тела; по заполнению узлов —
        /// DCT в коэффициенты и запись в sink. Аллокаций в горячем цикле нет.
        /// </summary>
        private static void RunBake(
            StarSystem sys,
            double spanYears,
            BakeConfig config,
            Func<OrbitingBody, double> segmentLengthFor,
            ISegmentSink sink,
            Action<double> progress,
            Func<bool> cancelled)
        {
            if (sys == null)
            {
                throw new ArgumentNullException("sys");
            }

            if (segmentLengthFor == null)
            {
                throw new ArgumentNullException("segmentLengthFor");
            }

            config.Validate();

            double span = spanYears * YearSeconds;
            ValidateBakeable(sys, span, config, segmentLengthFor);

            var bodies = sys.AllBodies;
            int n = bodies.Count;
            double g = PhysicsSolver.GravitationalConstant;
            double[] m = new double[n];
            double[] p = new double[3 * n];
            double[] v = new double[3 * n];
            for (int i = 0; i < n; i++)
            {
                bodies[i].EvaluateWorldState(0d, out Vector3d bp, out Vector3d bv);
                m[i] = bodies[i].ResolveStandardGravitationalParameter() / g;
                p[3 * i] = bp.X;
                p[3 * i + 1] = bp.Y;
                p[3 * i + 2] = bp.Z;
                v[3 * i] = bv.X;
                v[3 * i + 1] = bv.Y;
                v[3 * i + 2] = bv.Z;
            }

            // Барицентрическая коррекция иерархии (лист-первым): позиция/скорость
            // родителя сдвигаются против суммарного момента детей — иначе рельсы
            // детей считались бы вокруг неподвижного родителя.
            for (int i = n - 1; i >= 0; i--)
            {
                var b = bodies[i];
                if (b.Children.Count == 0 || m[i] <= 0d)
                {
                    continue;
                }

                double sx = 0d;
                double sy = 0d;
                double sz = 0d;
                double svx = 0d;
                double svy = 0d;
                double svz = 0d;
                for (int j = 0; j < b.Children.Count; j++)
                {
                    int c = IndexOf(bodies, n, b.Children[j]);
                    if (c < 0)
                    {
                        continue;
                    }

                    double mc = SubtreeMass(bodies, m, n, c);
                    sx += mc * (p[3 * c] - p[3 * i]);
                    sy += mc * (p[3 * c + 1] - p[3 * i + 1]);
                    sz += mc * (p[3 * c + 2] - p[3 * i + 2]);
                    svx += mc * (v[3 * c] - v[3 * i]);
                    svy += mc * (v[3 * c + 1] - v[3 * i + 1]);
                    svz += mc * (v[3 * c + 2] - v[3 * i + 2]);
                }

                p[3 * i] -= sx / m[i];
                p[3 * i + 1] -= sy / m[i];
                p[3 * i + 2] -= sz / m[i];
                v[3 * i] -= svx / m[i];
                v[3 * i + 1] -= svy / m[i];
                v[3 * i + 2] -= svz / m[i];
            }

            int[] parentIdx = new int[n];
            for (int i = 0; i < n; i++)
            {
                parentIdx[i] = -1;
                if (bodies[i].Parent != null)
                {
                    parentIdx[i] = IndexOf(bodies, n, bodies[i].Parent);
                }
            }

            // A1: кап подшага из быстрейшего периода (n·h ≤ PhasePerStep),
            // не из жёсткой константы: системы без быстрых лун пекутся кратно быстрее.
            double dtMax = config.MaxStepSeconds;
            for (int i = 0; i < n; i++)
            {
                if (parentIdx[i] < 0)
                {
                    continue;
                }

                double a = bodies[i].SemiMajorAxis;
                if (a <= 0d)
                {
                    continue;
                }

                double muLocal = bodies[parentIdx[i]].ResolveStandardGravitationalParameter()
                    + bodies[i].ResolveSubtreeStandardGravitationalParameter();
                if (muLocal <= 0d)
                {
                    continue;
                }

                double period = 2d * Math.PI * Math.Sqrt(a * a * a / muLocal);
                if (period > 0d)
                {
                    double phaseStep = period * config.PhasePerStep / (2d * Math.PI);
                    if (phaseStep < dtMax)
                    {
                        dtMax = phaseStep;
                    }
                }
            }

            // Сегменты/узлы на тело.
            int bakedCount = 0;
            int[] bodySlot = new int[n]; // индекс в массивах пекаря (без корня)
            var bakedBodies = new List<OrbitingBody>();
            var segLen = new List<double>();
            var segCount = new List<int>();
            var nodeOffsets = new List<double[]>(); // L·(1−cos(kπ/N))/2, k=0..N
            var sampleBuf = new List<double[]>();   // 3·(N+1)
            var fill = new List<int>();
            var curSeg = new List<int>();
            var nextNode = new List<int>();
            for (int i = 0; i < n; i++)
            {
                if (parentIdx[i] < 0)
                {
                    continue;
                }

                double requested = segmentLengthFor(bodies[i]);
                double len = AdjustedSegmentLength(span, requested);
                int count = (int)Math.Ceiling(span / len);
                int degree = config.Degree;
                var offs = new double[degree + 1];
                for (int k = 0; k <= degree; k++)
                {
                    offs[k] = len * (1d - Math.Cos(k * Math.PI / degree)) * 0.5d;
                }

                bodySlot[i] = bakedCount;
                bakedBodies.Add(bodies[i]);
                segLen.Add(len);
                segCount.Add(count);
                nodeOffsets.Add(offs);
                sampleBuf.Add(new double[3 * (degree + 1)]);
                fill.Add(0);
                curSeg.Add(0);
                nextNode.Add(0);
                bakedCount++;
            }

            for (int s = 0; s < bakedCount; s++)
            {
                sink.RegisterLayout(s, bakedBodies[s], segLen[s], segCount[s], config.Degree);
            }

            double maxEnd = 0d;
            for (int s = 0; s < bakedCount; s++)
            {
                double end = segCount[s] * segLen[s];
                if (end > maxEnd)
                {
                    maxEnd = end;
                }
            }

            // Таблица косинусов DCT (один раз на степень).
            int degreeN = config.Degree;
            var cosTable = new double[degreeN + 1, degreeN + 1];
            for (int j = 0; j <= degreeN; j++)
            {
                for (int k = 0; k <= degreeN; k++)
                {
                    cosTable[j, k] = Math.Cos(j * k * Math.PI / degreeN);
                }
            }

        // RK4-массивы (одна аллокация на всю выпечку).
        var kp1 = new double[3 * n];
        var kv1 = new double[3 * n];
        var kp2 = new double[3 * n];
        var kv2 = new double[3 * n];
        var kp3 = new double[3 * n];
        var kv3 = new double[3 * n];
        var kp4 = new double[3 * n];
        var kv4 = new double[3 * n];
        var tp = new double[3 * n];
        var ta = new double[3 * n];
        var coeffs = new double[BakedEphemeris.AxisStride(degreeN)];

        // Параллельный Accel окупается только при большом n: при 12 телах это
        // 66 пар на вызов — накладные расходы планировщика дороже самой работы,
        // поэтому порог 64 (небольшие системы идут строго последовательно, тот
        // же порядок суммирования, что и раньше). Буферы — одна аллокация.
        double[][] accelPartials = null;
        if (n >= ParallelAccelBodies)
        {
            int workers = Math.Min(Environment.ProcessorCount, n);
            accelPartials = new double[workers][];
            for (int w = 0; w < workers; w++)
            {
                accelPartials[w] = new double[3 * n];
            }
        }

            double t = 0d;
            double nextProgress = 0d;
            while (t < maxEnd - 1e-9d)
            {
                if (cancelled != null && cancelled())
                {
                    throw new OperationCanceledException("Выпечка эфемерид отменена.");
                }

                // Ближайший узел среди всех тел.
                double nextT = double.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (parentIdx[i] < 0)
                    {
                        continue;
                    }

                    int slot = bodySlot[i];
                    if (curSeg[slot] < segCount[slot])
                    {
                        double nodeT = curSeg[slot] * segLen[slot] + nodeOffsets[slot][nextNode[slot]];
                        if (nodeT < nextT)
                        {
                            nextT = nodeT;
                        }
                    }
                }

                // Завершение — ВСЕ ТЕЛА ЗАКОНЧИЛИСЬ, а не «t дошло до maxEnd»:
                // последний узел из-за округлений может лежать на пару ulp ниже
                // maxEnd−1e-9 (ulp(5e8)≈6e-8 ≫ 1e-9), и тогда внутренний цикл
                // с nextT=+Inf марширует вечно (T46 проходил только по удаче
                // округления — поймано T60 на 16 годах).
                if (nextT == double.PositiveInfinity)
                {
                    break;
                }

                // Подшаги до узла: последний садится точно в nextT (без дрейфа).
                // Интегратор — RK4: на возмущаемых орбитах лун его константа
                // ошибки в ~17 раз лучше Yoshida-4 при том же числе вызовов
                // Accel (5e6 против 2.7e5 на 16 лет для луны III-1, поймано
                // T60/probechaos2); секулярный дрейф энергии RK4 при h=900
                // ~1e-6 относительных за 1024 года — несущественен.
                while (t < nextT - 1e-9d)
                {
                    double h = Math.Min(dtMax, nextT - t);
                    Accel(p, ta, m, g, n, accelPartials);
                    for (int i = 0; i < 3 * n; i++)
                    {
                        kp1[i] = v[i];
                        kv1[i] = ta[i];
                        tp[i] = p[i] + (0.5d * h * kp1[i]);
                    }

                    Accel(tp, ta, m, g, n, accelPartials);
                    for (int i = 0; i < 3 * n; i++)
                    {
                        kp2[i] = v[i] + (0.5d * h * kv1[i]);
                        kv2[i] = ta[i];
                        tp[i] = p[i] + (0.5d * h * kp2[i]);
                    }

                    Accel(tp, ta, m, g, n, accelPartials);
                    for (int i = 0; i < 3 * n; i++)
                    {
                        kp3[i] = v[i] + (0.5d * h * kv2[i]);
                        kv3[i] = ta[i];
                        tp[i] = p[i] + (h * kp3[i]);
                    }

                    Accel(tp, ta, m, g, n, accelPartials);
                    for (int i = 0; i < 3 * n; i++)
                    {
                        kp4[i] = v[i] + (h * kv3[i]);
                        kv4[i] = ta[i];
                    }

                    for (int i = 0; i < 3 * n; i++)
                    {
                        p[i] += (h / 6d) * ((kp1[i] + kp4[i]) + (2d * (kp2[i] + kp3[i])));
                        v[i] += (h / 6d) * ((kv1[i] + kv4[i]) + (2d * (kv2[i] + kv3[i])));
                    }

                    t += h;
                }

                t = nextT; // точная посадка: сэмплы считаются на узлах, а не «почти на узлах»

                // Сэмплируем все тела, чей узел наступил.
                for (int i = 0; i < n; i++)
                {
                    if (parentIdx[i] < 0)
                    {
                        continue;
                    }

                    int slot = bodySlot[i];
                    if (curSeg[slot] >= segCount[slot])
                    {
                        continue;
                    }

                    double nodeT = curSeg[slot] * segLen[slot] + nodeOffsets[slot][nextNode[slot]];
                    if (Math.Abs(nodeT - t) > 1e-6d + 1e-12d * t)
                    {
                        continue;
                    }

                    double[] buf = sampleBuf[slot];
                    int o = 3 * nextNode[slot];
                    buf[o] = p[3 * i] - p[3 * parentIdx[i]];
                    buf[o + 1] = p[3 * i + 1] - p[3 * parentIdx[i] + 1];
                    buf[o + 2] = p[3 * i + 2] - p[3 * parentIdx[i] + 2];
                    nextNode[slot]++;

                    if (nextNode[slot] > degreeN)
                    {
                        FitSegmentChebyshev(buf, degreeN, cosTable, coeffs);
                        sink.WriteSegment(slot, curSeg[slot], coeffs);
                        curSeg[slot]++;
                        nextNode[slot] = 0;
                    }
                }

                if (progress != null && t >= nextProgress)
                {
                    progress(Math.Min(1d, t / maxEnd));
                    nextProgress = t + maxEnd * 0.01d;
                }
            }

            // Кеплерово продолжение: финальные состояния интегрирования (все тела
            // дошли до своих EndSeconds в рамках fp) снимаются как элементы
            // относительно родителя — та же рамка, что у рельсок. За концом рельсы
            // тело летит по этой кеплеровой орбите; стык бесшовный по построению.
            var finalRel = new double[6];
            for (int i = 0; i < n; i++)
            {
                int parent = parentIdx[i];
                if (parent < 0)
                {
                    continue;
                }

                double muLocal = g * (m[parent] + SubtreeMass(bodies, m, n, i));
                finalRel[0] = p[3 * i] - p[3 * parent];
                finalRel[1] = p[(3 * i) + 1] - p[(3 * parent) + 1];
                finalRel[2] = p[(3 * i) + 2] - p[(3 * parent) + 2];
                finalRel[3] = v[3 * i] - v[3 * parent];
                finalRel[4] = v[(3 * i) + 1] - v[(3 * parent) + 1];
                finalRel[5] = v[(3 * i) + 2] - v[(3 * parent) + 2];
                OrbitalElements el = OrbitalElements.FromState(
                    new Vector3d(finalRel[0], finalRel[1], finalRel[2]),
                    new Vector3d(finalRel[3], finalRel[4], finalRel[5]),
                    muLocal);
                var cont = new double[6];
                cont[0] = el.SemiMajorAxis;
                cont[1] = el.Eccentricity;
                cont[2] = el.InclinationDegrees;
                cont[3] = el.LongitudeOfAscendingNodeDegrees;
                cont[4] = el.ArgumentOfPeriapsisDegrees;
                cont[5] = el.MeanAnomalyDegrees;
                sink.WriteContinuation(bodySlot[i], cont);
            }
        }

        /// <summary>
        /// Прямое чебышёвское преобразование узлов Лобатто в коэффициенты.
        /// Узлы сэмплировались как t_k = T0 + L·(1−cos(kπ/N))/2, т.е. x_k =
        /// −cos(kπ/N) — порядок, ОБРАТНЫЙ стандартному, что даёт множитель
        /// (−1)^j в коэффициентах: a_j = (−1)^j·(2/(N·c_j))·Σ_k (1/c_k)·y_k·cos(jkπ/N),
        /// c_0 = c_N = 2. Проверено на f=t (a_0=1/2, a_1=1/2) и константе.
        /// Скорость ридер получает как dP/dx·(2/L) — формат BE1 не меняется.
        /// </summary>
        private static void FitSegmentChebyshev(double[] samples, int degree, double[,] cosTable, double[] dest)
        {
            int stride = 3 * (degree + 1);
            for (int axis = 0; axis < 3; axis++)
            {
                for (int j = 0; j <= degree; j++)
                {
                    double cj = (j == 0 || j == degree) ? 2d : 1d;
                    double sum = 0d;
                    for (int k = 0; k <= degree; k++)
                    {
                        double ck = (k == 0 || k == degree) ? 2d : 1d;
                        sum += samples[3 * k + axis] * cosTable[j, k] / ck;
                    }

                    double a = 2d * sum / (degree * cj);
                    if ((j & 1) != 0)
                    {
                        a = -a;
                    }

                    dest[axis * (degree + 1) + j] = a;
                }
            }
        }

        private static int IndexOf(System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, int n, OrbitingBody b)
        {
            for (int k = 0; k < n; k++)
            {
                if (ReferenceEquals(bodies[k], b))
                {
                    return k;
                }
            }

            return -1;
        }

        private static double SubtreeMass(System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies, double[] m, int n, int idx)
        {
            double total = m[idx];
            var b = bodies[idx];
            for (int j = 0; j < b.Children.Count; j++)
            {
                int c = IndexOf(bodies, n, b.Children[j]);
                if (c >= 0)
                {
                    total += SubtreeMass(bodies, m, n, c);
                }
            }

            return total;
        }

        /// <summary>
        /// Порог тел для параллельного Accel: ниже — последовательный путь
        /// (при n=12 это 66 пар, планировщик потоков дороже работы; порядок
        /// суммирования маленьких систем не меняется).
        /// </summary>
        private const int ParallelAccelBodies = 64;

        private static void Accel(double[] p, double[] a, double[] m, double g, int n, double[][] partials = null)
        {
            if (partials != null)
            {
                AccelParallel(p, a, m, g, n, partials);
                return;
            }

            for (int i = 0; i < 3 * n; i++)
            {
                a[i] = 0d;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double dx = p[3 * j] - p[3 * i];
                    double dy = p[3 * j + 1] - p[3 * i + 1];
                    double dz = p[3 * j + 2] - p[3 * i + 2];
                    double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                    double r = Math.Sqrt(r2);
                    double s = g / (r2 * r);
                    double fi = s * m[j];
                    double fj = s * m[i];
                    a[3 * i] += fi * dx;
                    a[3 * i + 1] += fi * dy;
                    a[3 * i + 2] += fi * dz;
                    a[3 * j] -= fj * dx;
                    a[3 * j + 1] -= fj * dy;
                    a[3 * j + 2] -= fj * dz;
                }
            }
        }

        /// <summary>
        /// Параллельные полные строки: каждый работник считает свои тела i по
        /// ВСЕМ j≠i (пары считаются дважды — цена векторизуемой декомпозиции),
        /// в свой буфер без блокировок; слияние после барьера. Работники
        /// закреплены стридом (i = w, w+W, …) — буферы предвыделены, ноль
        /// аллокаций на шаг.
        /// </summary>
        private static void AccelParallel(double[] p, double[] a, double[] m, double g, int n, double[][] partials)
        {
            int workers = partials.Length;
            System.Threading.Tasks.Parallel.For(0, workers, w =>
            {
                double[] buf = partials[w];
                for (int i = w; i < n; i += workers)
                {
                    double pxi = p[3 * i];
                    double pyi = p[3 * i + 1];
                    double pzi = p[3 * i + 2];
                    double ax = 0d;
                    double ay = 0d;
                    double az = 0d;
                    for (int j = 0; j < n; j++)
                    {
                        if (j == i)
                        {
                            continue;
                        }

                        double dx = p[3 * j] - pxi;
                        double dy = p[3 * j + 1] - pyi;
                        double dz = p[3 * j + 2] - pzi;
                        double r2 = (dx * dx) + (dy * dy) + (dz * dz);
                        double r = Math.Sqrt(r2);
                        double f = (g / (r2 * r)) * m[j];
                        ax += f * dx;
                        ay += f * dy;
                        az += f * dz;
                    }

                    buf[3 * i] = ax;
                    buf[3 * i + 1] = ay;
                    buf[3 * i + 2] = az;
                }
            });

            for (int w = 0; w < workers; w++)
            {
                double[] buf = partials[w];
                for (int i = w; i < n; i += workers)
                {
                    a[3 * i] += buf[3 * i];
                    a[3 * i + 1] += buf[3 * i + 1];
                    a[3 * i + 2] += buf[3 * i + 2];
                }
            }
        }
    }
}
