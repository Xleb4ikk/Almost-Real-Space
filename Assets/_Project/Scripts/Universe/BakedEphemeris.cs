using System;
using System.IO;
using Galilego.Core;

namespace Galilego.Universe
{
    [Serializable]
    public sealed class BakedEphemeris : IDisposable
    {
        /// <summary>Магия обычного BE1-файла (legacy, без продолжения).</summary>
        private static readonly byte[] MagicPlain = { (byte)66, (byte)69, (byte)49 };

        /// <summary>
        /// Магия BE2-файла: [BE2][заголовок 27Б][сегменты float64][продолжение 48Б в хвосте].
        /// Продолжение в хвосте (вне потока сегментов): FileSink стримит сегменты
        /// в gzip последовательно и дописывает продолжение в Dispose, а OpenFile
        /// читает хвост seek'ом, не распаковывая файл целиком.
        /// </summary>
        private static readonly byte[] MagicPlain2 = { (byte)66, (byte)69, (byte)50 };

        /// <summary>Магия сжатого BG1-контейнера: [BG1][gzip(данные BE1)]. Legacy.</summary>
        private static readonly byte[] MagicGzip = { (byte)66, (byte)71, (byte)49 };

        /// <summary>
        /// Магия квантованного BG2-контейнера (legacy, без продолжения):
        /// [BG2][gzip([заголовок BE1 27Б][сегменты: 6Б (E_x,E_y,E_z,bits_x,bits_y,bits_z) + zigzag-varint кварант])].
        /// На сегмент/ось: экспонента E (масштаб 2^E по max|a| сегмента) и битность
        /// bits = E + K (K из бюджета ошибки) — файл самодостаточен посегментно,
        /// оконное чтение не требует знания амплитуд заранее. Ошибка позиции
        /// гарантированно ≤ budget/2 (Σ|T_j| ≤ 13, шаг ≤ budget/13).
        /// </summary>
        private static readonly byte[] MagicGz2 = { (byte)66, (byte)71, (byte)50 };

        /// <summary>
        /// Магия BG3: как BG2 + блок кеплерова продолжения в хвосте файла
        /// (вне gzip): [BG3][gzip([заголовок 27Б][varint-сегменты])][продолжение 48Б].
        /// </summary>
        private static readonly byte[] MagicGz3 = { (byte)66, (byte)71, (byte)51 };

        public double T0Seconds;
        public double SegmentLengthSeconds;
        public int Degree;
        public int SegmentCount;
        public double[] Coeffs;
        public string FilePath;
        public int WindowSegments = 4;

        /// <summary>Файл в контейнере BG1 (gzip): чтение окон — последовательная распаковка.</summary>
        public bool Compressed;

        /// <summary>Файл в формате BG2 (квантованные varint-сегменты внутри gzip).</summary>
        public bool Quantized;

        /// <summary>В payload файла есть блок продолжения (BE2/BG3): шапка 75 Б вместо 27.</summary>
        public bool HasContinuationBlock;

        private double[] window;
        private int windowBase = -1;
        private FileStream gzFile;
        private System.IO.Compression.GZipStream gzStream;
        private long gzPos;
        private byte[] skipBuf;
        private V2Reader v2Reader;
        private int v2Segs;
        public long WindowLoads;

        /// <summary>
        /// Буферизованный читатель varint-потока BG2 поверх gzip. Байт за байтом
        /// из GZipStream — десятки миллионов вызовов Read при скимме; буфер 4 КБ
        /// сводит это к чтению чанков.
        /// </summary>
        private sealed class V2Reader
        {
            private readonly Stream stream;
            private readonly byte[] buf = new byte[4096];
            private int pos;
            private int len;

            internal V2Reader(Stream s)
            {
                stream = s;
            }

            internal int ReadByte()
            {
                if (pos >= len)
                {
                    len = stream.Read(buf, 0, buf.Length);
                    pos = 0;
                    if (len <= 0)
                    {
                        return -1;
                    }
                }

                return buf[pos++];
            }

            internal bool ReadVar(out long value)
            {
                ulong z = 0UL;
                int shift = 0;
                while (true)
                {
                    int b = ReadByte();
                    if (b < 0)
                    {
                        value = 0L;
                        return false;
                    }

                    z |= ((ulong)(b & 0x7F)) << shift;
                    if ((b & 0x80) == 0)
                    {
                        break;
                    }

                    shift += 7;
                    if (shift > 63)
                    {
                        value = 0L;
                        return false;
                    }
                }

                // Зигзаг-декод: v = (z>>1) XOR −(z&1) — XOR со всеми единицами для
                // нечётных, НЕ арифметическое отрицание (−(z>>1) давало бы значение
                // на шаг меньше — поймано T62: коэф-разница 1.5 шага вместо ≤0.5).
                value = (long)(z >> 1);
                if ((z & 1UL) != 0UL)
                {
                    value = ~value;
                }

                return true;
            }

            /// <summary>Декодирует один сегмент BG2 в dest начиная с destOff.</summary>
            internal bool DecodeSegment(int stride, double[] dest, int destOff)
            {
                int perAxis = stride / 3;
                int[] e = new int[3];
                int[] bits = new int[3];
                double[] mult = new double[3];
                for (int ax = 0; ax < 3; ax++)
                {
                    int eb = ReadByte();
                    int bb = ReadByte();
                    if (eb < 0 || bb < 0)
                    {
                        return false;
                    }

                    e[ax] = eb;
                    bits[ax] = bb;
                    mult[ax] = Math.Pow(2d, eb - bb);
                }

                for (int i = 0; i < stride; i++)
                {
                    if (!ReadVar(out long q))
                    {
                        return false;
                    }

                    dest[destOff + i] = q * mult[i / perAxis];
                }

                return true;
            }

            /// <summary>Пропускает один сегмент (скимм вперёд без декодирования коэффициентов).</summary>
            internal bool SkipSegment(int stride)
            {
                for (int i = 0; i < 6; i++)
                {
                    if (ReadByte() < 0)
                    {
                        return false;
                    }
                }

                for (int i = 0; i < stride; i++)
                {
                    if (!ReadVar(out _))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// K квантования из бюджета ошибки: шаг кванта 2^(E−bits) = 2^−K ≤ budget/13
        /// гарантирует ошибку позиции ≤ budget/2 (худший случай Σ|T_j(x)| ≤ 13).
        /// </summary>
        internal static int KFromBudget(double budget)
        {
            double k = Math.Ceiling(Math.Log(13d / budget, 2d));
            if (k < 1d)
            {
                k = 1d;
            }

            return (int)k;
        }

        /// <summary>
        /// Кодирует один сегмент в формат BG2: 6 байт (E и bits по осям) +
        /// zigzag-varint квантованные коэффициенты. Возвращает число записанных
        /// байт. Громкое исключение при амплитуде вне диапазона формата.
        /// </summary>
        internal static int EncodeSegmentV2(double[] coeffs, int stride, double budget, byte[] dest)
        {
            int perAxis = stride / 3;
            int k = KFromBudget(budget);
            int p = 0;
            for (int ax = 0; ax < 3; ax++)
            {
                double max = 0d;
                int baseOff = ax * perAxis;
                for (int j = 0; j < perAxis; j++)
                {
                    double v = Math.Abs(coeffs[baseOff + j]);
                    if (v > max)
                    {
                        max = v;
                    }
                }

                int e = max > 0d ? (int)Math.Ceiling(Math.Log(max, 2d)) : 0;
                if (e < 0)
                {
                    e = 0;
                }

                if (e > 63 || e + k > 45)
                {
                    throw new InvalidOperationException(
                        "Амплитуда рельсы " + max.ToString("E2") + " (E=" + e + ") вне диапазона BG2 при бюджете " +
                        budget.ToString("F1") + " м — уменьшите масштаб системы или увеличьте бюджет квантования.");
                }

                dest[p++] = (byte)e;
                dest[p++] = (byte)(e + k);
            }

            for (int ax = 0; ax < 3; ax++)
            {
                double mult = Math.Pow(2d, dest[(ax * 2) + 1] - dest[ax * 2]);
                int baseOff = ax * perAxis;
                for (int j = 0; j < perAxis; j++)
                {
                    long q = (long)Math.Round(coeffs[baseOff + j] * mult);
                    ulong z = (ulong)((q << 1) ^ (q >> 63));
                    while (z >= 0x80UL)
                    {
                        dest[p++] = (byte)(z | 0x80UL);
                        z >>= 7;
                    }

                    dest[p++] = (byte)z;
                }
            }

            return p;
        }

        public static int AxisStride(int degree)
        {
            return 3 * (degree + 1);
        }

        public double EndSeconds
        {
            get { return T0Seconds + SegmentCount * SegmentLengthSeconds; }
        }

        /// <summary>
        /// Кеплерово продолжение за концом рельсы: [a, e, inc°, Ω°, ω°, M0°]
        /// относительно родителя, снятые с рельсы в EndSeconds (эпоха продолжения).
        /// null = нет продолжения (старые форматы BE1/BG1/BG2 или невалидный фиттинг).
        /// </summary>
        public double[] Continuation;

        /// <summary>Размер блока продолжения в файле (6 doubles).</summary>
        internal const int ContinuationBytes = 48;

        /// <summary>Причина отказа TryEvaluate — для диагностики вместо лжи «вне интервала».</summary>
        public enum FailReason
        {
            /// <summary>TryEvaluate должен был succeed — причин нет.</summary>
            None,
            /// <summary>Время вне испечённого интервала.</summary>
            OutOfRange,
            /// <summary>Данные отсутствуют (не заполнены Coeffs/структура).</summary>
            NoData,
            /// <summary>Структура повреждена (Degree/сегменты вне диапазонов).</summary>
            Corrupt
        }

        /// <summary>
        /// Причина, по которой TryEvaluate вернула/вернула бы false для
        /// timeSeconds. Дешёвая (окна не читаются). Нужна, чтобы
        /// EphemerisRangeException не лгал «вне интервала» при битых данных
        /// внутри спана (аудит S2/4.3).
        /// </summary>
        public FailReason GetFailReason(double timeSeconds)
        {
            if (Degree < 1 || SegmentCount <= 0 || SegmentLengthSeconds <= 0d)
            {
                return FailReason.Corrupt;
            }

            if (FilePath == null && Coeffs == null)
            {
                return FailReason.NoData;
            }

            double dt = timeSeconds - T0Seconds;
            double boundaryTol = 1e-6d + Math.Abs(EndSeconds) * 1e-12d;
            if (dt < -boundaryTol || dt > SegmentCount * SegmentLengthSeconds + boundaryTol)
            {
                return FailReason.OutOfRange;
            }

            return FailReason.None;
        }

        /// <summary>
        /// Кеплерово продолжение для timeSeconds ЗА концом рельсы (спрос вызывается
        /// только после того, как TryEvaluate вернул false). Бесшовность: элементы
        /// сняты с интегрированного состояния в EndSeconds, поэтому на стыке позиция
        /// непрерывна по построению. mu — тот же μ_local, что у кеплеровой рельсы
        /// (μ родителя + μ поддерева тела). Формула и порядок операций — один в один
        /// с кеплеровой рельсой EvaluateLocalOffset.
        /// </summary>
        public bool TryEvaluateContinuation(double timeSeconds, double mu, out Vector3d position, out Vector3d velocity)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            if (Continuation == null || Continuation.Length != 6)
            {
                return false;
            }

            double boundaryTol = 1e-6d + Math.Abs(EndSeconds) * 1e-12d;
            if (timeSeconds <= EndSeconds + boundaryTol)
            {
                // Внутри рельсы TryEvaluate обязан был ответить; сюда попадаем
                // только из фолбэка за концом.
                return false;
            }

            double a = Continuation[0];
            double e = Continuation[1];
            if (!(a > 0d) || !(e >= 0d) || !(e < 1d))
            {
                // Невалидный фиттинг (гипербола/убегание/мусор) — громко.
                return false;
            }

            double inc = KeplerMath.DegreesToRadians(Continuation[2]);
            double raan = KeplerMath.DegreesToRadians(Continuation[3]);
            double argp = KeplerMath.DegreesToRadians(Continuation[4]);
            double m0 = KeplerMath.DegreesToRadians(Continuation[5]);
            double meanMotion = Math.Sqrt(mu / (a * a * a));
            double meanAnomaly = KeplerMath.NormalizeAngle(m0 + (meanMotion * (timeSeconds - EndSeconds)));
            double eccentricAnomaly = KeplerMath.SolveEccentricAnomaly(meanAnomaly, e);

            double cosE = Math.Cos(eccentricAnomaly);
            double sinE = Math.Sin(eccentricAnomaly);
            double radius = a * (1d - (e * cosE));
            double orbitalYScale = Math.Sqrt(1d - (e * e));

            Vector3d orbitalPosition = new Vector3d(
                a * (cosE - e),
                a * orbitalYScale * sinE,
                0d);
            double velocityFactor = Math.Sqrt(mu * a) / radius;
            Vector3d orbitalVelocity = new Vector3d(
                -velocityFactor * sinE,
                velocityFactor * orbitalYScale * cosE,
                0d);

            position = KeplerMath.RotateOrbitalToWorld(orbitalPosition, raan, inc, argp);
            velocity = KeplerMath.RotateOrbitalToWorld(orbitalVelocity, raan, inc, argp);
            return true;
        }

        /// <summary>
        /// Гейт оконного курсора. TryEvaluate мутирует общее состояние (окно,
        /// gzip-курсор, v2-ридер), поэтому без гейта два читателя разъедают окно
        /// и стрим: gzip forward-only — вторая рука на нём даёт мусор или IOException,
        /// а ResetCompressedStream может Dispose стрим, из которого другой поток
        /// ещё читает. Секция покрывает ВЕСЬ TryEvaluate (Clenshaw вкл.): окно
        /// переиспользуемый буфер, ссылку наружу не отдаём. Контракт:
        /// — TryEvaluate потокобезопасен между инстансами и с гейтом внутри
        ///   одного (критическая секция редкая — раз в WindowSegments сегментов);
        /// — TryEvaluateContinuation чистая математика, безопасна всегда;
        /// — LoadRangeToMemory открывает СВОИ стримы и возвращает новый инстанс —
        ///   immutable-snapshot контракт для параллельных планировщиков
        ///   (porkchop, Lambert, предикторы) с их ограниченными окнами;
        /// — attach (body.Baked = ...) и мутации публичных полей — только до
        ///   входа системы в работу, с главного потока.
        /// </summary>
        public bool TryEvaluate(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            if (FilePath == null)
            {
                // Инстанс «всё в памяти»: состояния нет, гонка невозможна —
                // гейт не нужен (горячий путь бейка/тестов без lock-накладки).
                return TryEvaluateCore(timeSeconds, out position, out velocity);
            }

            lock (windowGate)
            {
                return TryEvaluateCore(timeSeconds, out position, out velocity);
            }
        }

        private readonly object windowGate = new object();

        private bool TryEvaluateCore(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            position = Vector3d.Zero;
            velocity = Vector3d.Zero;
            if (Degree < 1 || SegmentCount <= 0 || SegmentLengthSeconds <= 0d)
            {
                return false;
            }

            double dt = timeSeconds - T0Seconds;
            // Допуск на границе — ОТНОСИТЕЛЬНЫЙ: EndSeconds пересчитывается с
            // fp-округлением и на спанах ~3e10 с расходится со спаном на
            // единицы ulp (ulp(3.2e10)≈3.7мкс) — запрос ровно в конец спана
            // не должен падать (поймано T60). Вне нескольких ulp — по-прежнему
            // громкий отказ.
            double boundaryTol = 1e-6d + Math.Abs(T0Seconds + SegmentCount * SegmentLengthSeconds) * 1e-12d;
            if (dt < -boundaryTol || dt > SegmentCount * SegmentLengthSeconds + boundaryTol)
            {
                return false;
            }

            if (dt < 0d)
            {
                dt = 0d;
            }

            int seg = (int)(dt / SegmentLengthSeconds);
            if (seg >= SegmentCount)
            {
                seg = SegmentCount - 1;
            }

            double[] src;
            int basis;
            if (FilePath == null)
            {
                if (Coeffs == null)
                {
                    return false;
                }

                src = Coeffs;
                basis = seg * AxisStride(Degree);
            }
            else
            {
                if (!EnsureWindow(seg))
                {
                    return false;
                }

                src = window;
                basis = (seg - windowBase) * AxisStride(Degree);
            }

            double x = 2d * ((timeSeconds - (T0Seconds + seg * SegmentLengthSeconds)) / SegmentLengthSeconds) - 1d;
            if (x < -1d)
            {
                x = -1d;
            }
            else if (x > 1d)
            {
                x = 1d;
            }

            int width = Degree + 1;
            position = new Vector3d(
                Clenshaw(src, basis, Degree, x),
                Clenshaw(src, basis + width, Degree, x),
                Clenshaw(src, basis + 2 * width, Degree, x));
            double scale = 2d / SegmentLengthSeconds;
            velocity = new Vector3d(
                DerivSum(src, basis, Degree, x) * scale,
                DerivSum(src, basis + width, Degree, x) * scale,
                DerivSum(src, basis + 2 * width, Degree, x) * scale);
            return position.IsFinite && velocity.IsFinite;
        }

        private bool EnsureWindow(int seg)
        {
            int w = WindowSegments;
            if (w < 1)
            {
                w = 1;
            }

            if (window != null && seg >= windowBase && seg < windowBase + w && seg < SegmentCount)
            {
                return true;
            }

            int stride = AxisStride(Degree);
            int count = w;
            if (seg + count > SegmentCount)
            {
                count = SegmentCount - seg;
            }

            if (count <= 0)
            {
                return false;
            }

            try
            {
                if (window == null || window.Length < w * stride)
                {
                    window = new double[w * stride];
                }

                if (Quantized)
                {
                    ReadWindowV2(seg, count, stride);
                }
                else if (Compressed)
                {
                    ReadWindowCompressed(seg, count, stride);
                }
                else
                {
                    ReadWindowPlain(seg, count, stride);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                // Файл отсутствует/повреждён/недоступен — это ошибка окружения,
                // а не «вне диапазона»: молча вернуть false значило бы выдать
                // мусорную позицию за легитимный вердикт TryEvaluate (поймано
                // T45 «битый файл громко»). InvalidDataException — битый gzip,
                // тоже окружение, тоже громко и с именем файла.
                throw new InvalidOperationException("Не удалось прочитать файл эфемериды: " + FilePath, ex);
            }

            windowBase = seg;
            WindowLoads++;
            return true;
        }

        private void ReadWindowPlain(int seg, int count, int stride)
        {
            // Шапка = 27 Б (включая магию) для всех форматов: продолжение лежит
            // в хвосте файла, вне потока сегментов.
            using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(27L + ((long)seg * stride * 8L), SeekOrigin.Begin);
                byte[] buf = new byte[count * stride * 8];
                if (!ReadFully(fs, buf))
                {
                    throw new IOException("Неожиданный конец файла эфемериды.");
                }

                for (int i = 0; i < count * stride; i++)
                {
                    window[i] = ReadFloat64(buf, i * 8);
                }
            }
        }

        /// <summary>
        /// Оконное чтение BG1: gzip не умеет seek, но время движется вперёд —
        /// читаем последовательно, лишнее распакованное выбрасываем чанками.
        /// Прыжок назад (варп в прошлое) — переоткрытие файла. Скимминг в
        /// worst-case — десятки мс на десятки МБ, в прямом времени — ноль.
        /// </summary>
        private void ReadWindowCompressed(int seg, int count, int stride)
        {
            long target = 27L + (long)seg * stride * 8L;
            if (gzFile == null || gzPos > target)
            {
                ResetCompressedStream();
                gzFile = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                gzFile.Seek(3, SeekOrigin.Begin); // магия BG1
                gzStream = new System.IO.Compression.GZipStream(gzFile, System.IO.Compression.CompressionMode.Decompress);
                gzPos = 0;
            }

            if (skipBuf == null)
            {
                skipBuf = new byte[65536];
            }

            while (gzPos < target)
            {
                int chunk = (int)Math.Min(skipBuf.Length, target - gzPos);
                if (!ReadFully(gzStream, skipBuf, chunk))
                {
                    throw new IOException("Неожиданный конец сжатого файла эфемериды.");
                }

                gzPos += chunk;
            }

            byte[] data = new byte[count * stride * 8];
            if (!ReadFully(gzStream, data, data.Length))
            {
                throw new IOException("Неожиданный конец сжатого файла эфемериды.");
            }

            gzPos += data.Length;
            for (int i = 0; i < count * stride; i++)
            {
                window[i] = ReadFloat64(data, i * 8);
            }
        }

        /// <summary>
        /// Оконное чтение BG2: поток varint-сегментов декодируется последовательно;
        /// прыжок назад — переоткрытие файла (как у BG1). Каждый сегмент
        /// самодостаточен (E/bits внутри), поэтому скимм — дешёвое чтение varint
        /// без декодирования коэффициентов.
        /// </summary>
        private void ReadWindowV2(int seg, int count, int stride)
        {
            if (gzFile == null || v2Segs > seg)
            {
                ResetCompressedStream();
                gzFile = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                gzFile.Seek(3, SeekOrigin.Begin); // магия BG2
                gzStream = new System.IO.Compression.GZipStream(gzFile, System.IO.Compression.CompressionMode.Decompress);
                gzPos = 0;
                v2Segs = 0;
                v2Reader = new V2Reader(gzStream);
                // Шапка payload = 27 Б для BG2/BG3: продолжение лежит в хвосте
                // файла, вне gzip-потока.
                for (int i = 0; i < 27; i++)
                {
                    if (v2Reader.ReadByte() < 0)
                    {
                        throw new IOException("Неожиданный конец сжатого файла на заголовке.");
                    }
                }
            }

            while (v2Segs < seg)
            {
                if (!v2Reader.SkipSegment(stride))
                {
                    throw new IOException("Неожиданный конец BG2 при скимме к сегменту " + seg + ".");
                }

                v2Segs++;
            }

            for (int s = 0; s < count; s++)
            {
                if (!v2Reader.DecodeSegment(stride, window, s * stride))
                {
                    throw new IOException("Неожиданный конец BG2 в окне у сегмента " + (seg + s) + ".");
                }

                v2Segs++;
            }
        }

        private void ResetCompressedStream()
        {
            if (gzStream != null)
            {
                gzStream.Dispose();
                gzStream = null;
            }

            if (gzFile != null)
            {
                gzFile.Dispose();
                gzFile = null;
            }

            v2Reader = null;
            v2Segs = 0;
            gzPos = 0;
        }

        /// <summary>
        /// Явное освобождение файлового дескриптора. Без Dispose держатель
        /// FileStream жил до GC-финализатора: переттач эфемериды (attach нового
        /// инстанса на то же тело) блокировал перезапись/удаление файла на
        /// некоторых платформах и капал хэндлами при частых переттачах.
        /// Гейт обязателен: ResetCompressedStream мутует то же состояние, что
        /// TryEvaluateCore. После Dispose TryEvaluate честно вернёт false
        /// (EnsureWindow не сможет прочитать окно) — переоткрытия не делаем:
        /// dispose-после-use — ошибка вызывающего кода.
        /// </summary>
        public void Dispose()
        {
            lock (windowGate)
            {
                ResetCompressedStream();
            }

            windowBase = -1;
        }

        private static bool ReadFully(Stream stream, byte[] buf)
        {
            return ReadFully(stream, buf, buf.Length);
        }

        private static bool ReadFully(Stream stream, byte[] buf, int length)
        {
            int got = 0;
            while (got < length)
            {
                int r = stream.Read(buf, got, length - got);
                if (r <= 0)
                {
                    return false;
                }

                got += r;
            }

            return true;
        }

        /// <summary>Полная распаковка BG1 в память (для LoadRangeToMemory).</summary>
        private byte[] DecompressWhole()
        {
            using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.Seek(3, SeekOrigin.Begin);
                using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// Обычный BE2-файл: [BE2][заголовок 27Б][сегменты float64][продолжение 48Б].
        /// Блок продолжения присутствует всегда; при отсутствии фиттинга — NaN
        /// (читатель трактует как «нет продолжения» — громко за концом).
        /// </summary>
        public void WriteToFile(string path)
        {
            WritePayload(path, MagicPlain2, null);
        }

        /// <summary>
        /// Сжатый BG3-файл: [BG3][gzip([заголовок 27Б][сегменты varint])][продолжение 48Б].
        /// Квантование с бюджетом ошибки позиции: гарантия ≤ budget/2 на сегмент
        /// (см. KFromBudget). Продолжение в хвосте, вне gzip — стриминг сегментов
        /// сохраняется, OpenFile читает хвост seek'ом.
        /// </summary>
        public void WriteToFileCompressed(string path, double quantBudgetMeters = 2d)
        {
            int stride = AxisStride(Degree);
            int kCap = 6 + (stride * 10);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(MagicGz3[0]);
                fs.WriteByte(MagicGz3[1]);
                fs.WriteByte(MagicGz3[2]);
                using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal, true))
                {
                    byte[] head = new byte[27];
                    head[0] = MagicPlain[0];
                    head[1] = MagicPlain[1];
                    head[2] = MagicPlain[2];
                    WriteInt32(head, 3, Degree);
                    WriteInt32(head, 7, SegmentCount);
                    WriteFloat64(head, 11, T0Seconds);
                    WriteFloat64(head, 19, SegmentLengthSeconds);
                    gz.Write(head, 0, head.Length);

                    byte[] seg = new byte[kCap];
                    double[] one = new double[stride];
                    for (int s = 0; s < SegmentCount; s++)
                    {
                        Array.Copy(Coeffs, s * stride, one, 0, stride);
                        int n = EncodeSegmentV2(one, stride, quantBudgetMeters, seg);
                        gz.Write(seg, 0, n);
                    }
                }

                // Продолжение — в хвост, после закрытия gzip (хвост должен быть дописан).
                byte[] tail = new byte[ContinuationBytes];
                for (int i = 0; i < 6; i++)
                {
                    WriteFloat64(tail, i * 8, Continuation != null && Continuation.Length == 6 ? Continuation[i] : double.NaN);
                }

                fs.Write(tail, 0, tail.Length);
            }
        }

        private void WritePayload(string path, byte[] magic, System.IO.Compression.CompressionLevel? compress)
        {
            int stride = AxisStride(Degree);
            byte[] buf = new byte[27 + (SegmentCount * stride * 8) + ContinuationBytes];
            buf[0] = magic[0];
            buf[1] = magic[1];
            buf[2] = magic[2];
            WriteInt32(buf, 3, Degree);
            WriteInt32(buf, 7, SegmentCount);
            WriteFloat64(buf, 11, T0Seconds);
            WriteFloat64(buf, 19, SegmentLengthSeconds);
            for (int i = 0; i < Coeffs.Length; i++)
            {
                WriteFloat64(buf, 27 + (i * 8), Coeffs[i]);
            }

            for (int i = 0; i < 6; i++)
            {
                WriteFloat64(buf, 27 + (Coeffs.Length * 8) + (i * 8), Continuation != null && Continuation.Length == 6 ? Continuation[i] : double.NaN);
            }

            if (compress.HasValue)
            {
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gz = new System.IO.Compression.GZipStream(fs, compress.Value))
                {
                    gz.Write(buf, 0, buf.Length);
                }
            }
            else
            {
                File.WriteAllBytes(path, buf);
            }
        }

        public static BakedEphemeris OpenFile(string path)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length < 3L)
            {
                return null;
            }

            byte[] magic = new byte[3];
            using (FileStream fs0 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (!ReadFully(fs0, magic))
                {
                    return null;
                }
            }

            bool quantized = magic[0] == MagicGz2[0] && magic[1] == MagicGz2[1] && magic[2] == MagicGz2[2];
            bool quantized3 = magic[0] == MagicGz3[0] && magic[1] == MagicGz3[1] && magic[2] == MagicGz3[2];
            bool plain2 = magic[0] == MagicPlain2[0] && magic[1] == MagicPlain2[1] && magic[2] == MagicPlain2[2];
            bool compressed = quantized || quantized3 || (magic[0] == MagicGzip[0] && magic[1] == MagicGzip[1] && magic[2] == MagicGzip[2]);
            bool hasContinuation = quantized3 || plain2;
            if (!compressed && !plain2 && (magic[0] != MagicPlain[0] || magic[1] != MagicPlain[1] || magic[2] != MagicPlain[2]))
            {
                return null;
            }

            // BE2/BG3: продолжение — 48 Б в хвосте файла, вне сжатия: OpenFile
            // читает его seek'ом, не распаковывая сегменты.
            int payloadHeader = 27;
            byte[] head = new byte[payloadHeader];
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (compressed)
                {
                    fs.Seek(3, SeekOrigin.Begin);
                    using (var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
                    {
                        if (!ReadFully(gz, head))
                        {
                            return null;
                        }
                    }
                }
                else
                {
                    if (info.Length < payloadHeader || !ReadFully(fs, head))
                    {
                        return null;
                    }
                }
            }

            int degree = ReadInt32(head, 3);
            int segCount = ReadInt32(head, 7);
            if (degree < 1 || degree > 32 || segCount <= 0)
            {
                return null;
            }

            if (!compressed && info.Length != (long)payloadHeader + ((long)segCount * AxisStride(degree) * 8L) + (hasContinuation ? ContinuationBytes : 0L))
            {
                return null;
            }

            double[] continuation = null;
            if (hasContinuation)
            {
                if (info.Length < ContinuationBytes)
                {
                    return null;
                }

                continuation = new double[6];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fs.Seek(-ContinuationBytes, SeekOrigin.End);
                    byte[] tail = new byte[ContinuationBytes];
                    if (!ReadFully(fs, tail))
                    {
                        return null;
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        continuation[i] = ReadFloat64(tail, i * 8);
                    }
                }

                if (double.IsNaN(continuation[0]))
                {
                    continuation = null; // фиттинг не удался — громкий фолбэк за концом
                }
            }

            BakedEphemeris e = new BakedEphemeris
            {
                T0Seconds = ReadFloat64(head, 11),
                SegmentLengthSeconds = ReadFloat64(head, 19),
                Degree = degree,
                SegmentCount = segCount,
                FilePath = path,
                Compressed = compressed,
                Quantized = quantized || quantized3,
                HasContinuationBlock = hasContinuation,
                Continuation = continuation
            };
            return e;
        }

        /// <summary>
        /// Загрузка диапазона в память (планировочные окна). Жёсткий кап числа
        /// сегментов: вызов с гигантским диапазоном — громкое исключение, а не
        /// молчаливое съедание ОЗУ (C-план).
        /// </summary>
        public BakedEphemeris LoadRangeToMemory(double fromSeconds, double toSeconds, int maxSegments = 1000000)
        {
            if (fromSeconds < T0Seconds)
            {
                fromSeconds = T0Seconds;
            }

            if (toSeconds > EndSeconds)
            {
                toSeconds = EndSeconds;
            }

            int segA = (int)((fromSeconds - T0Seconds) / SegmentLengthSeconds);
            int segB = (int)((toSeconds - T0Seconds) / SegmentLengthSeconds);
            if (segA < 0)
            {
                segA = 0;
            }

            if (segB >= SegmentCount)
            {
                segB = SegmentCount - 1;
            }

            if (segB < segA)
            {
                return null;
            }

            if (segB - segA + 1 > maxSegments)
            {
                throw new ArgumentOutOfRangeException(nameof(toSeconds),
                    "Диапазон [" + fromSeconds + "; " + toSeconds + "] = " + (segB - segA + 1) +
                    " сегментов превышает кап " + maxSegments + " — используйте оконное чтение по FilePath.");
            }

            int stride = AxisStride(Degree);
            double[] slice = new double[(segB - segA + 1) * stride];
            if (FilePath == null)
            {
                Array.Copy(Coeffs, segA * stride, slice, 0, slice.Length);
            }
            else if (Quantized)
            {
                byte[] whole = DecompressWhole();
                using (var ms = new MemoryStream(whole))
                {
                    var rd = new V2Reader(ms);
                    for (int i = 0; i < 27; i++)
                    {
                        if (rd.ReadByte() < 0)
                        {
                            throw new IOException("Неожиданный конец сжатого файла на заголовке.");
                        }
                    }

                    for (int i = 0; i < segA; i++)
                    {
                        if (!rd.SkipSegment(stride))
                        {
                            throw new IOException("Неожиданный конец сжатого файла при пропуске до сегмента " + segA + ".");
                        }
                    }

                    for (int s = 0; s <= segB - segA; s++)
                    {
                        if (!rd.DecodeSegment(stride, slice, s * stride))
                        {
                            throw new IOException("Неожиданный конец сжатого файла при декодировании сегмента " + (segA + s) + ".");
                        }
                    }
                }
            }
            else if (Compressed)
            {
                byte[] whole = DecompressWhole();
                for (int i = 0; i < slice.Length; i++)
                {
                    slice[i] = ReadFloat64(whole, (int)(27L + ((long)segA * stride + i) * 8));
                }
            }
            else
            {
                // Тот же fail-loudly контракт, что и EnsureWindow (урок T45):
                // IOException/UnauthorizedAccess — ошибка окружения, а не
                // «вне диапазона»; молча вернуть null — выдать мусорный вердикт
                // (аудит S2/4.1: внутри одного класса были два противоположных
                // контракта на одну ошибку).
                try
                {
                    using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        fs.Seek(27L + ((long)segA * stride * 8L), SeekOrigin.Begin);
                        byte[] buf = new byte[slice.Length * 8];
                        if (!ReadFully(fs, buf))
                        {
                            throw new IOException("Неожиданный конец файла эфемериды.");
                        }

                        for (int i = 0; i < slice.Length; i++)
                        {
                            slice[i] = ReadFloat64(buf, i * 8);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    throw new InvalidOperationException("Не удалось прочитать файл эфемериды: " + FilePath, ex);
                }
            }

            BakedEphemeris e = new BakedEphemeris();
            e.T0Seconds = T0Seconds + segA * SegmentLengthSeconds;
            e.SegmentLengthSeconds = SegmentLengthSeconds;
            e.Degree = Degree;
            e.SegmentCount = segB - segA + 1;
            e.Coeffs = slice;
            // Слайс не делит массив продолжения с родителем: поля публично
            // мутируемы — мутация одного не должна менять другой (аудит S2).
            e.Continuation = Continuation != null ? (double[])Continuation.Clone() : null;
            return e;
        }

        public static double Clenshaw(double[] c, int offset, int degree, double x)
        {
            double b2 = 0d;
            double b1 = 0d;
            for (int k = degree; k >= 1; k--)
            {
                double b0 = c[offset + k] + 2d * x * b1 - b2;
                b2 = b1;
                b1 = b0;
            }

            return c[offset] + x * b1 - b2;
        }

        public static double DerivSum(double[] c, int offset, int degree, double x)
        {
            if (degree < 1)
            {
                return 0d;
            }

            double t0 = 1d;
            double t1 = x;
            double d0 = 0d;
            double d1 = 1d;
            double s = c[offset + 1];
            for (int k = 1; k < degree; k++)
            {
                double t2 = 2d * x * t1 - t0;
                double d2 = 2d * t1 + 2d * x * d1 - d0;
                s += c[offset + k + 1] * d2;
                t0 = t1;
                t1 = t2;
                d0 = d1;
                d1 = d2;
            }

            return s;
        }

        public string ToPortableString()
        {
            int stride = AxisStride(Degree);
            byte[] buf = new byte[3 + 4 + 4 + 8 + 8 + Coeffs.Length * 8];
            buf[0] = (byte)66;
            buf[1] = (byte)69;
            buf[2] = (byte)49;
            WriteInt32(buf, 3, Degree);
            WriteInt32(buf, 7, SegmentCount);
            WriteFloat64(buf, 11, T0Seconds);
            WriteFloat64(buf, 19, SegmentLengthSeconds);
            for (int i = 0; i < Coeffs.Length; i++)
            {
                WriteFloat64(buf, 27 + i * 8, Coeffs[i]);
            }

            return Convert.ToBase64String(buf);
        }

        public static BakedEphemeris FromPortableString(string s)
        {
            byte[] buf = Convert.FromBase64String(s);
            if (buf.Length < 27 || buf[0] != 66 || buf[1] != 69 || buf[2] != 49)
            {
                return null;
            }

            int degree = ReadInt32(buf, 3);
            int segCount = ReadInt32(buf, 7);
            if (degree < 1 || degree > 32 || segCount <= 0)
            {
                return null;
            }

            int stride = AxisStride(degree);
            if (buf.Length != 27 + segCount * stride * 8)
            {
                return null;
            }

            BakedEphemeris e = new BakedEphemeris();
            e.T0Seconds = ReadFloat64(buf, 11);
            e.SegmentLengthSeconds = ReadFloat64(buf, 19);
            e.Degree = degree;
            e.SegmentCount = segCount;
            e.Coeffs = new double[segCount * stride];
            for (int i = 0; i < e.Coeffs.Length; i++)
            {
                e.Coeffs[i] = ReadFloat64(buf, 27 + i * 8);
            }

            return e;
        }

        internal static void WriteInt32(byte[] buf, int off, int v)
        {
            buf[off] = (byte)(v & 255);
            buf[off + 1] = (byte)((v >> 8) & 255);
            buf[off + 2] = (byte)((v >> 16) & 255);
            buf[off + 3] = (byte)((v >> 24) & 255);
        }

        private static int ReadInt32(byte[] buf, int off)
        {
            return buf[off] | (buf[off + 1] << 8) | (buf[off + 2] << 16) | (buf[off + 3] << 24);
        }

        internal static void WriteFloat64(byte[] buf, int off, double v)
        {
            long bits = BitConverter.DoubleToInt64Bits(v);
            for (int i = 0; i < 8; i++)
            {
                buf[off + i] = (byte)((bits >> (8 * i)) & 255L);
            }
        }

        private static double ReadFloat64(byte[] buf, int off)
        {
            long bits = 0L;
            for (int i = 0; i < 8; i++)
            {
                bits |= ((long)buf[off + i]) << (8 * i);
            }

            return BitConverter.Int64BitsToDouble(bits);
        }
    }
}
