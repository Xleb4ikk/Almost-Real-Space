using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Математика локального поверхностного фрейма (ENU) — чистые double, без
    /// UnityEngine. Основа авторинга на планете: без неё сцену не на чем строить.
    ///
    /// Зачем фрейм. Абсолютные координаты тела лежат на полуоси 2.6e10 м, где
    /// float32 даёт ulp ~2048 м: ни показать в сцене точку поверхности, ни
    /// поставить туда объект с точностью метра невозможно. Рантайм это обходит
    /// floating origin (FloatingOrigin.Anchor = позиция игрока, вершины чанков
    /// пересобираются в render-пространстве). Здесь то же самое для редактора:
    /// фрейм ставится в НОЛЬ сцены, и внутри него координаты — километры.
    ///
    /// Базис фрейма: +X — север, +Y — зенит, +Z — восток. Правый: N × U = E
    /// (проверено T108). Совпадает с привычкой Unity (forward=X, up=Y, right=Z):
    /// «лицом на север» — right смотрит на восток.
    ///
    /// Все направления — body-fixed (долгота отсчитывается от нулевого меридиана
    /// тела), ровно как в OrbitingBody.GetSurfaceState / SurfaceLatLonAt и в
    /// HeightfieldTerrain.GetRawHeightMeters. Смешивать с инерциальными
    /// долготами нельзя: промах на угол собственного вращения.
    /// </summary>
    public static class SurfaceFrameMath
    {
        /// <summary>Зенит (body-fixed направление на опорную точку), радианы.</summary>
        public static Vector3d Up(double latitudeRadians, double longitudeRadians)
        {
            double cosLat = Math.Cos(latitudeRadians);
            return new Vector3d(
                cosLat * Math.Cos(longitudeRadians),
                cosLat * Math.Sin(longitudeRadians),
                Math.Sin(latitudeRadians));
        }

        /// <summary>Касательная на север в опорной точке, единичная.</summary>
        public static Vector3d North(double latitudeRadians, double longitudeRadians)
        {
            double sinLat = Math.Sin(latitudeRadians);
            double cosLat = Math.Cos(latitudeRadians);
            return new Vector3d(
                -sinLat * Math.Cos(longitudeRadians),
                -sinLat * Math.Sin(longitudeRadians),
                cosLat);
        }

        /// <summary>Касательная на восток в опорной точке, единичная.</summary>
        public static Vector3d East(double latitudeRadians, double longitudeRadians)
        {
            return new Vector3d(-Math.Sin(longitudeRadians), Math.Cos(longitudeRadians), 0d);
        }

        /// <summary>
        /// Поворот локальных осей фрейма в оси тела: q · x̂ = север, q · ŷ = зенит,
        /// q · ẑ = восток. Собирается из аналитического базиса матрицей (не
        /// композицией из axis-angle: на полюсах и в меридиане распадки нет,
        /// одна формула на весь шар).
        /// </summary>
        public static QuaternionD Rotation(double latitudeRadians, double longitudeRadians)
        {
            return FromBasis(
                North(latitudeRadians, longitudeRadians),
                Up(latitudeRadians, longitudeRadians),
                East(latitudeRadians, longitudeRadians));
        }

        /// <summary>
        /// Кватернион из ортонормированного ПРАВОГО базиса: колонки матрицы —
        /// образы локальных осей. Стандартная трассировочная конверсия с четырьмя
        /// ветвями (как внутри Unity Quaternion.LookRotation) — на границах ветвей
        /// (trace ≈ 0) матрица вырождается, и однозначная формула даёт NaN.
        /// </summary>
        public static QuaternionD FromBasis(Vector3d xAxis, Vector3d yAxis, Vector3d zAxis)
        {
            double m00 = xAxis.X, m01 = yAxis.X, m02 = zAxis.X;
            double m10 = xAxis.Y, m11 = yAxis.Y, m12 = zAxis.Y;
            double m20 = xAxis.Z, m21 = yAxis.Z, m22 = zAxis.Z;

            double trace = m00 + m11 + m22;
            double qx, qy, qz, qw;
            if (trace > 0d)
            {
                double s = Math.Sqrt(trace + 1d) * 2d;
                qw = 0.25d * s;
                qx = (m21 - m12) / s;
                qy = (m02 - m20) / s;
                qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1d + m00 - m11 - m22) * 2d;
                qw = (m21 - m12) / s;
                qx = 0.25d * s;
                qy = (m01 + m10) / s;
                qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = Math.Sqrt(1d + m11 - m00 - m22) * 2d;
                qw = (m02 - m20) / s;
                qx = (m01 + m10) / s;
                qy = 0.25d * s;
                qz = (m12 + m21) / s;
            }
            else
            {
                double s = Math.Sqrt(1d + m22 - m00 - m11) * 2d;
                qw = (m10 - m01) / s;
                qx = (m02 + m20) / s;
                qy = (m12 + m21) / s;
                qz = 0.25d * s;
            }

            return new QuaternionD(qx, qy, qz, qw).Normalized;
        }

        /// <summary>
        /// Body-fixed направление точки, смещённой от опорной на (east, north)
        /// МЕТРОВ по касательной. Смещение кладётся на сферу радиуса R: реальная
        /// поверхность лежит на R + h, но h неизвестна до выборки направления, а
        /// ошибка от подмены радиуса — второго порядка по h/R (на 50 км и h=9 км
        /// это сантиметры, для превью авторинга неразличимо).
        /// Возвращает нормализованное направление в осях тела.
        /// </summary>
        public static Vector3d DirectionOffset(
            double latitudeRadians, double longitudeRadians, double radiusMeters,
            double eastMeters, double northMeters)
        {
            Vector3d p = (Up(latitudeRadians, longitudeRadians) * radiusMeters)
                        + (East(latitudeRadians, longitudeRadians) * eastMeters)
                        + (North(latitudeRadians, longitudeRadians) * northMeters);
            double len = p.Magnitude;
            return len > 1e-12d ? (p / len) : Up(latitudeRadians, longitudeRadians);
        }

        /// <summary>
        /// ENU-смещение (восток, север, вверх — метры) в локальные оси фрейма
        /// (x=север, y=зенит, z=восток). Тригонометрии here нет и не должно
        /// быть: оси фрейма УЖЕ переставлены, всё, что нужно — переставить
        /// компоненты.
        ///
        /// Отдельно от DirectionOffset: то тащит смещение в осях ТЕЛА (для
        /// выборки высоты рельефа), это — в осях фрейма (для локальных
        /// трансформов объектов). Путаница между ними даёт повёрнутый на угол
        /// фрейма объект — ровно то, что поймал T108.
        /// </summary>
        public static Vector3d EnuToFrameLocal(double eastMeters, double northMeters, double upMeters)
        {
            return new Vector3d(northMeters, upMeters, eastMeters);
        }

        /// <summary>
        /// Обратная к DirectionOffset задача: смещение ОТ одной точки поверхности
        /// К ДРУГОЙ, разложенное по осям фрейма (x=север, y=зенит, z=восток).
        ///
        /// Нужна всем, кто рисует или ставит объект в соседней точке планеты:
        /// гизмо площадки, кольцо-рулетка, «показать, где это место относительно
        /// меня». Считается по ХОРДЕ между точками поверхности, а не по дуге:
        /// редакторская касательная плоскость и так прямолинейна, и на дистанциях
        /// в километры разница между хордой и дугой — миллиметры. Зато хорда не
        /// требует обратной задачи сферической тригонометрии, которая сама по
        /// себе склонна к провалу точности на lat/lon.
        /// </summary>
        public static Vector3d SurfaceOffsetBetween(
            double fromLatRadians, double fromLonRadians, double fromAltitudeMeters,
            double toLatRadians, double toLonRadians, double toAltitudeMeters,
            double radiusMeters)
        {
            Vector3d from = Up(fromLatRadians, fromLonRadians) * (radiusMeters + fromAltitudeMeters);
            Vector3d to = Up(toLatRadians, toLonRadians) * (radiusMeters + toAltitudeMeters);
            Vector3d delta = to - from;

            // Раскладываем по базису ИСХОДНОЙ точки: ровно та касательная
            // плоскость, в которой автор и видит сцену.
            return EnuToFrameLocal(
                Vector3d.Dot(delta, East(fromLatRadians, fromLonRadians)),
                Vector3d.Dot(delta, North(fromLatRadians, fromLonRadians)),
                Vector3d.Dot(delta, Up(fromLatRadians, fromLonRadians)));
        }

        /// <summary>Проекция body-fixed вектора на локальные оси фрейма (x=север, y=зенит, z=восток).</summary>
        public static Vector3d ToLocal(double latitudeRadians, double longitudeRadians, Vector3d bodyFixed)
        {
            return new Vector3d(
                Vector3d.Dot(bodyFixed, North(latitudeRadians, longitudeRadians)),
                Vector3d.Dot(bodyFixed, Up(latitudeRadians, longitudeRadians)),
                Vector3d.Dot(bodyFixed, East(latitudeRadians, longitudeRadians)));
        }
    }
}
