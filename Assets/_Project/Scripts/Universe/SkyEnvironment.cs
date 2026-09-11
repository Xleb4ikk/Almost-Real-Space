using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Физика неба в рантайме: считает прозрачность к солнцу, окклюзию телом,
    /// дневной фактор и видимость звёзд (SkyPhysics) для наблюдателя и кладёт
    /// их в глобалы шейдеров, плюс гасит/краснит Directional Light (день→ночь).
    ///
    /// Airmass (длина пути) и окклюзия (закрыт ли луч телом, С УЧЁТОМ ВЫСОТЫ
    /// наблюдателя) — раздельные множители: у горизонта солнце красное и
    /// тусклое, но исчезает только когда его реально закрывает планета.
    /// Вызов — LateUpdate; потребители (SunBillboard, StarField) читают
    /// вычисленные значения того же/предыдущего кадра.
    /// </summary>
    public sealed class SkyEnvironment : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Directional Light солнца (гасится/краснится по физике).")]
        public Light SunLight;

        [Tooltip("Базовая интенсивность света днём в зените.")]
        public float SunLightIntensity = 1.6f;

        [Tooltip("Множитель видимости звёзд (k в exp(−L·k)): больше — звёзды прячутся раньше.")]
        public float StarVisibilityK = 6f;

        [Tooltip("Сила солнечного члена террейна. Шейдер мягко сжимает пересвет (Reinhard), поэтому диапазон ~1..9 даёт насыщенный цвет без выгорания.")]
        public float TerrainSunIntensity = 3f;

        [Tooltip("Ночная засветка террейна (звёздный свет); почти 0 = кромешная тьма.")]
        public float NightAmbient = 0.005f;

        [Tooltip("Дневная засветка террейна небом.")]
        public float SkyAmbient = 0.1f;

        /// <summary>Прозрачность к солнцу по каналам (обновляется каждый кадр).</summary>
        public Vector3 Transmittance { get; private set; } = Vector3.one;

        /// <summary>Дневной фактор прямого света 0..1.</summary>
        public float DayFactor { get; private set; }

        /// <summary>Окклюзия солнца телом (0/1, сглажено).</summary>
        public float SunOcclusion { get; private set; }

        /// <summary>Видимость звёзд 0..1.</summary>
        public float StarVisibility { get; private set; } = 1f;

        private void LateUpdate()
        {
            if (Runner == null || Runner.SystemState == null || Runner.DominantBody == null
                || Runner.SystemState.Root == null || Runner.Ship == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            AtmosphereProfile atmosphere = body.Atmosphere;

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d relative = Runner.PlayerPosition - bodyPos;
            double distance = relative.Magnitude;
            double altitude = System.Math.Max(0d, distance - body.Radius);
            Vector3d up = distance > 0d ? relative / distance : new Vector3d(0d, 0d, 1d);

            Runner.SystemState.Root.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
            Vector3d toStar = starPos - Runner.PlayerPosition;
            double starDistance = toStar.Magnitude;
            Vector3d sunDir = starDistance > 0d ? toStar / starDistance : up;

            double sinEl = Vector3d.Dot(sunDir, up);

            // Единый источник β: те же коэффициенты, что у GPU-купола
            // (AtmosphereOptics). Раньше здесь был эмпирический ExtinctionPerMeter —
            // небо и свет Солнца считались по разным моделям и расходились.
            AtmosphereOptics.Coefficients coeff = atmosphere != null
                ? atmosphere.ToOptics()
                : AtmosphereOptics.FromProfile(0d, 0d, 0d, 0d, false);
            double scaleHeight = coeff.RayleighScaleHeight;
            double topAltitude = atmosphere != null ? atmosphere.TopAltitudeMeters : 0d;
            double density = SkyPhysics.Density(altitude, scaleHeight, topAltitude);

            Vector3d transmittance = SkyPhysics.SunTransmittance(sinEl, density, coeff);
            double occlusion = SkyPhysics.SunOcclusion(sinEl, altitude, body.Radius);
            double day = SkyPhysics.DayFactor(sinEl);
            double skyLuminance = SkyPhysics.SkyLuminance(sinEl, density);
            double starVisibility = SkyPhysics.StarVisibility(skyLuminance, StarVisibilityK);

            Transmittance = new Vector3((float)transmittance.X, (float)transmittance.Y, (float)transmittance.Z);
            DayFactor = (float)day;
            SunOcclusion = (float)occlusion;
            StarVisibility = (float)starVisibility;

            Shader.SetGlobalVector("_SunTransmittance", Transmittance);
            Shader.SetGlobalFloat("_StarVisibility", StarVisibility);
            Shader.SetGlobalVector("_SkySunDir", AstroFrame.ToSimulation(sunDir));
            Shader.SetGlobalVector("_SkyUp", AstroFrame.ToSimulation(up));

            // Ambient террейна — пофрагментный (в шейдере): ночь → NightAmbient
            // (≈0), день → + SkyAmbient; солнце добавляет _TerrainSun. От позиции
            // наблюдателя не зависит — ночная сторона темна всегда.
            Shader.SetGlobalFloat("_NightAmbient", NightAmbient);
            Shader.SetGlobalFloat("_SkyAmbient", SkyAmbient);
            Shader.SetGlobalFloat("_TerrainSun", TerrainSunIntensity);

            if (SunLight != null)
            {
                // Глобальный свет: постоянен, ночь — геометрически (N·L). Краснение
                // диска/неба делается отдельно (T_sun), свет им не пересчитываем.
                Vector3d baseLinear = StarColorUtil.FromTemperature(5772d);
                SunLight.color = new Color((float)baseLinear.X, (float)baseLinear.Y, (float)baseLinear.Z, 1f);
                SunLight.intensity = SunLightIntensity;
            }
        }
    }
}
