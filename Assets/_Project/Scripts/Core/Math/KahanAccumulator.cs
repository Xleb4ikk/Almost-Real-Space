namespace Galilego.Core
{
    /// <summary>
    /// Компенсированное суммирование (Кэхен–Бабушка) для длительных аккумуляторов
    /// времени: EventDrivenPropagator (тысячи чанков) и burn-цикл ManeuverEvaluator.
    /// На годах варпа наивное t += chunk накапливает ошибку, сопоставимую с
    /// допуском корня 1e-6с; компенсация существенно её уменьшает. Конкретная
    /// величина ошибки зависит от последовательности чисел и IEEE-754 и здесь
    /// НЕ гарантируется — гарантируется только существенно меньший рост ошибки
    /// с числом сложений (доказано T21a числами, не формулировкой).
    /// Sealed struct, ноль аллокаций. Неявных преобразований нет специально:
    /// должно быть видно, где идёт компенсация, а где голый double.
    /// Не применяется: короткие внутренние циклы степперов (ошибка ничтожна,
    /// побитовый churn даром), единичные присваивания (LongWarpDriver,
    /// WarpController — там нечего компенсировать).
    /// </summary>
    public struct KahanAccumulator
    {
        private double sum;
        private double compensation;

        public double Sum => sum;

        public KahanAccumulator(double initialValue)
        {
            sum = initialValue;
            compensation = 0d;
        }

        public void Add(double value)
        {
            double y = value - compensation;
            double t = sum + y;
            compensation = (t - sum) - y;
            sum = t;
        }

        public void Reset(double value)
        {
            sum = value;
            compensation = 0d;
        }
    }
}
