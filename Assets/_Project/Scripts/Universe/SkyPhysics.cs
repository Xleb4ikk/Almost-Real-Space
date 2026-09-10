using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Физика неба и звёзд — чистая математика (тестируется на стенде, T88).
    ///
    /// Разделение (важно и физически, и численно):
    ///   * AIRMASS — длина пути солнечного луча сквозь атмосферу (Kasten–Young).
    ///     У горизонта она большая, но КОНЕЧНАЯ — именно это даёт красный
    ///     тусклый закатный диск. Обнулять её «когда солнце зашло» нельзя: это
    ///     дало бы exp(0)=1 (полную прозрачность) и разрыв.
    ///   * ОККЛЮЗИЯ — отдельный геометрический множитель: закрыт ли прямой луч
    ///     телом планеты. Учитывает ВЫСОТУ наблюдателя (геометрический наклон
    ///     горизонта: чем выше, тем глубже за горизонт должна уйти звезда,
    ///     чтобы её реально закрыло) — для наземного наблюдателя ≈ sinEl<0.
    ///
    /// Оптическая толща = Airmass · ρ(h) · H; прозрачность = exp(−τ·β) (Бугер).
    /// β_rgb ∝ λ⁻⁴ (Рэлей): синий гаснет первым → красный закат.
    /// </summary>
    public static class SkyPhysics
    {
        /// <summary>Относительная рэлеевская экстинкция по каналам (λ⁻⁴, нормирована на G).</summary>
        public static readonly Vector3d RayleighExtinction = new Vector3d(0.34d, 1.0d, 3.05d);

        /// <summary>Плотность атмосферы на высоте (0..1), ноль выше topAltitude.</summary>
        public static double Density(double altitude, double scaleHeight, double topAltitude)
        {
            if (altitude >= topAltitude)
            {
                return 0d;
            }

            double h = altitude < 0d ? 0d : altitude;
            return Math.Exp(-h / Math.Max(1d, scaleHeight));
        }

        /// <summary>
        /// Относительная воздушная масса (Kasten–Young). Кламп у горизонта —
        /// только ради численной устойчивости (≈ −2.9°), не физический «заход»:
        /// у самого горизонта am конечно-большая (~38), что и нужно.
        /// </summary>
        public static double Airmass(double sinElevation)
        {
            double s = sinElevation < -0.05d ? -0.05d : sinElevation;
            return 1d / (s + (0.15d * Math.Pow(s + 3.885d, -1.253d)));
        }

        /// <summary>Наклон видимого горизонта (рад) для наблюдателя на высоте: acos(R / (R+h)).</summary>
        public static double HorizonDip(double altitude, double planetRadius)
        {
            if (altitude <= 0d || planetRadius <= 0d)
            {
                return 0d;
            }

            double ratio = planetRadius / (planetRadius + altitude);
            return Math.Acos(Clamp(ratio, -1d, 1d));
        }

        /// <summary>
        /// 1 — прямой луч до звезды не перекрыт телом; 0 — перекрыт. Граница —
        /// на угле наклона горизонта (−dip); гладкий переход шириной edge.
        /// У земли dip≈0 → граница ≈ sinEl=0. С высотой dip растёт → звезда
        /// видна даже чуть ниже местного горизонта (как из стратосферы/космоса).
        /// </summary>
        public static double SunOcclusion(double sinElevation, double altitude, double planetRadius, double edge = 0.002d)
        {
            double sinLimit = -Math.Sin(HorizonDip(altitude, planetRadius));
            return Smoothstep(sinLimit - edge, sinLimit + edge, sinElevation);
        }

        /// <summary>Прозрачность к солнцу по каналам; extinctionPerMeter — β. В космосе (ρ=0) → (1,1,1).</summary>
        public static Vector3d SunTransmittance(double sinElevation, double density, double scaleHeight, Vector3d extinctionPerMeter)
        {
            double opticalDepth = Airmass(sinElevation) * density * Math.Max(1d, scaleHeight);
            return new Vector3d(
                Math.Exp(-opticalDepth * extinctionPerMeter.X),
                Math.Exp(-opticalDepth * extinctionPerMeter.Y),
                Math.Exp(-opticalDepth * extinctionPerMeter.Z));
        }

        /// <summary>Рэлеевская экстинкция β (1/м) из плотности у поверхности (Земля ≈ 1.225 кг/м³).</summary>
        public static Vector3d ExtinctionPerMeter(double seaLevelDensityKgPerCubicMeter)
        {
            double scale = 2.4e-5d * (seaLevelDensityKgPerCubicMeter / 1.225d);
            return RayleighExtinction * scale;
        }

        /// <summary>Относительная яркость неба (день→сумерки→ночь) × плотность над наблюдателем.</summary>
        public static double SkyLuminance(double sinElevation, double density, double twilightSin = 0.15d)
        {
            return Smoothstep(-twilightSin, twilightSin, sinElevation) * density;
        }

        /// <summary>Видимость звёзд: 1 в космосе/ночью, →0 при ярком дне у поверхности.</summary>
        public static double StarVisibility(double skyLuminance, double k = 6d)
        {
            return Math.Exp(-skyLuminance * k);
        }

        /// <summary>Дневной фактор прямого света: плавно включается после восхода, 0 ночью.</summary>
        public static double DayFactor(double sinElevation)
        {
            return Smoothstep(0d, 0.1d, sinElevation);
        }

        public static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        public static double Smoothstep(double edge0, double edge1, double x)
        {
            if (edge1 <= edge0)
            {
                return x < edge0 ? 0d : 1d;
            }

            double t = Clamp((x - edge0) / (edge1 - edge0), 0d, 1d);
            return t * t * (3d - (2d * t));
        }
    }
}
