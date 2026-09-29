using System;
using System.Collections.Generic;
using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ровные площадки на теле — список, который автор правит руками, и
    /// помощник по их размещению.
    ///
    /// СПИСОК ЖИВЁТ НЕ ЗДЕСЬ, а в BodyAuthoring.TerrainPads: площадка — это
    /// часть РЕЛЬЕФА, и она обязана одинаково попасть и в Play-сборку
    /// (SimulationRunner), и в редакторное превью (SurfaceSceneSystem). Оба идут
    /// через один путь StarSystemAuthoring.BuildBlueprint(), поэтому правка
    /// видна в обоих, и расхождение невозможно по построению.
    ///
    /// Этот компонент — только гизмо (жёлтое кольцо = фартук, красное = ровное
    /// ядро) и операции «добавить площадку в точке» / «перезапомнить высоту».
    /// Сам список виден и правится прямо в инспекторе BodyAuthoring.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BodyAuthoring))]
    public sealed class TerrainMods : MonoBehaviour
    {
        [Header("Площадка для нового места")]
        [Tooltip("Внешний радиус новой площадки, м: в ядре ровно, дальше фартук. " +
            "Ядро шире 240 м — нормаль рельефа усредняется по 114 м, и на узком " +
            "ядре здания по краям встают с наклоном.")]
        public double NewPadInnerRadiusMeters = 150d;

        [Tooltip("Внешний радиус новой площадки, м. 0 = без фартука, только ровное ядро.")]
        public double NewPadOuterRadiusMeters = 420d;

        private BodyAuthoring Body => GetComponent<BodyAuthoring>();

        private List<TerrainModifier> Pads => Body != null ? Body.TerrainPads : null;

        /// <summary>Активных площадок (выключенные не считаются).</summary>
        public int ActiveCount
        {
            get
            {
                List<TerrainModifier> pads = Pads;
                if (pads == null)
                {
                    return 0;
                }

                int n = 0;
                for (int i = 0; i < pads.Count; i++)
                {
                    if (pads[i].IsActive)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        /// <summary>
        /// Запомнить естественную высоту рельефа в точке как целевую для
        /// площадки. Смысл: площадка должна стать РОВНОЙ на месте, где рельеф
        /// и так примерно такого уровня, — тогда она не уедет вверх/вниз и не
        /// прорежет окружающий склон.
        /// </summary>
        public void CaptureTargetHeight(int index, double latitudeDegrees, double longitudeDegrees)
        {
            List<TerrainModifier> pads = Pads;
            if (pads == null || index < 0 || index >= pads.Count)
            {
                return;
            }

            double height = SampleNaturalHeight(latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(height))
            {
                return;
            }

            // Через промежуточную переменную: список — List<TerrainModifier>, а
            // индексатор у List нельзя присваивать (struct, а не ref).
            TerrainModifier pad = pads[index];
            pad.LatitudeDegrees = latitudeDegrees;
            pad.LongitudeDegrees = longitudeDegrees;
            pad.TargetHeightMeters = height;
            pads[index] = pad;
        }

        /// <summary>
        /// Добавить площадку с центром в точке и высотой по естественному рельефу.
        /// Возвращает индекс или -1, если систему собрать не удалось.
        /// </summary>
        public int AddPad(double latitudeDegrees, double longitudeDegrees)
        {
            List<TerrainModifier> pads = Pads;
            if (pads == null)
            {
                return -1;
            }

            double height = SampleNaturalHeight(latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(height))
            {
                return -1;
            }

            pads.Add(new TerrainModifier
            {
                LatitudeDegrees = latitudeDegrees,
                LongitudeDegrees = longitudeDegrees,
                InnerRadiusMeters = NewPadInnerRadiusMeters,
                OuterRadiusMeters = System.Math.Max(NewPadOuterRadiusMeters, NewPadInnerRadiusMeters),
                TargetHeightMeters = height,
                OverridesSeaLevel = height < 0d
            });
            return pads.Count - 1;
        }

        /// <summary>Убрать площадку по индексу.</summary>
        public void RemovePad(int index)
        {
            List<TerrainModifier> pads = Pads;
            if (pads != null && index >= 0 && index < pads.Count)
            {
                pads.RemoveAt(index);
            }
        }

        /// <summary>
        /// Естественная высота рельефа в точке — БЕЗ учёта площадок. Именно она
        /// нужна как целевая: если спросить обычный GetHeightMeters, то площадка
        /// в точке вернёт уже сглаженное значение и новая площадка «замкнётся»
        /// на текущем, а не на естественном рельефе. Ошибка накапливалась бы
        /// от базы к базе.
        /// </summary>
        private double SampleNaturalHeight(double latitudeDegrees, double longitudeDegrees)
        {
            if (!SurfaceSceneSystem.TryResolve(name, out OrbitingBody body, out _))
            {
                return double.NaN;
            }

            var terrain = body.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                return double.NaN;
            }

            return terrain.GetRawHeightMeters(
                body,
                latitudeDegrees * (System.Math.PI / 180d),
                longitudeDegrees * (System.Math.PI / 180d));
        }

        private void OnDrawGizmosSelected()
        {
            var frame = UnityEngine.Object.FindAnyObjectByType<SurfaceFrame>();
            if (frame == null || !frame.IsUsable)
            {
                return;
            }

            List<TerrainModifier> pads = Pads;
            if (pads == null)
            {
                return;
            }

            for (int i = 0; i < pads.Count; i++)
            {
                TerrainModifier pad = pads[i];
                if (!pad.IsActive)
                {
                    continue;
                }

                // Радиусы в метрах поверхности, а не в градусах: автору нужны
                // метры, иначе он не сможет прикинуть размер площадки на глаз.
                double inner = pad.InnerRadiusMeters;
                double outer = pad.OuterRadiusMeters;
                if (outer <= 0d || outer > 1e6d)
                {
                    continue;
                }

                DrawCircle(frame, pad, outer, new Color(1f, 0.85f, 0.2f, 0.5f));
                if (inner > 0d)
                {
                    DrawCircle(frame, pad, inner, new Color(1f, 0.3f, 0.2f, 0.85f));
                }
            }
        }

        private static void DrawCircle(SurfaceFrame frame, TerrainModifier pad, double radius, Color color)
        {
            if (radius <= 0d)
            {
                return;
            }

            Vector3d offset = SurfaceFrameMath.SurfaceOffsetBetween(
                frame.LatitudeDegrees * (Math.PI / 180d),
                frame.LongitudeDegrees * (Math.PI / 180d),
                frame.AltitudeMeters,
                pad.LatitudeDegrees * (Math.PI / 180d),
                pad.LongitudeDegrees * (Math.PI / 180d),
                pad.TargetHeightMeters,
                frame.BodyRadius);

            // Смещение живёт в осях фрейма (x=север, y=зенит, z=восток), а гизмо
            // рисуется в мировых координатах — переносим через позу фрейма.
            var local = new Vector3((float)offset.X, (float)offset.Y, (float)offset.Z);
            Vector3 center = frame.transform.position + (frame.transform.rotation * local);

            Gizmos.color = color;
            const int segments = 48;
            Vector3 prev = center + (frame.transform.rotation * new Vector3(0f, 0f, (float)radius));
            for (int s = 1; s <= segments; s++)
            {
                double a = (s / (double)segments) * Math.PI * 2d;
                Vector3 next = center + (frame.transform.rotation * new Vector3(
                    (float)(Math.Cos(a) * radius), 0f, (float)(Math.Sin(a) * radius)));
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
