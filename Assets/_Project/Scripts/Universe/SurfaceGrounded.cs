using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Ставит объект НА рельеф. Это то, без чего базу не построить: в
    /// локальных координатах места (метры) здание, поставленное «на глаз»,
    /// либо висит в воздухе, либо наполовину в земле — и ни то, ни другое не
    /// видно в сцене, потому что Scene view не знает про рельеф.
    ///
    /// Компонент работает и в редакторе, и в Play, и пересчитывается сам при
    /// любой смене позиции места. Чинит три вещи:
    ///   • ставит Y на высоту земли в этой точке (плюс HeightOffsetMeters);
    ///   • по желанию доворачивает объект по нормали рельефа (скала, пандус);
    ///   • показывает в инспекторе, на сколько метров объект провисает или
    ///     висит — чтобы «поймать» стык с землёй глазами.
    ///
    /// Объекты внутри места работают и без него — просто стоят там, куда их
    /// положили. Snap нужен там, где важно точное касание земли.
    /// </summary>
    [ExecuteAlways]
    public sealed class SurfaceGrounded : MonoBehaviour
    {
        [Tooltip("Ставить объект на поверхность земли.")]
        public bool SnapToGround = true;

        [Tooltip("Доворачивать объект по нормали рельефа (для скал и наклонных площадок). Нормаль — та же, что у посадки корабля: усреднение примерно по 114 м. Поэтому мелкие детали рельефа не дёргают объект, а база садится ровно так же, как сядет шлюпка.")]
        public bool AlignToNormal;

        [Tooltip("Смещение над землёй после snap, м. Отрицательное — фундамент в землю.")]
        public double HeightOffsetMeters;

        [Tooltip("Сохранять рыскание объекта при выравнивании по нормали (иначе объект доворачивается по нормали целиком).")]
        public bool KeepYaw = true;

        [Tooltip("Пересчитывать при изменении позиции места (снять галку только для объектов, которые двигают вручную).")]
        public bool AutoUpdate = true;

        /// <summary>Позиция места, в чьих осях считаем. Заполняется автоматически.</summary>
        private SurfaceSite site;

        /// <summary>На сколько метров объект выше/ниже земли прямо сейчас, м.</summary>
        public double CurrentGapMeters { get; private set; }

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        private void Update()
        {
            if (AutoUpdate)
            {
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (AutoUpdate && Application.isPlaying)
            {
                Apply();
            }
        }

        /// <summary>Найти место, которому принадлежит объект, и поставить объект на землю.</summary>
        public void Apply()
        {
            if (site == null)
            {
                site = GetComponentInParent<SurfaceSite>();
            }

            if (site == null || !site.isActiveAndEnabled)
            {
                return;
            }

            OrbitingBody body = site.BodyState;
            if (body == null)
            {
                return;
            }

            double time = Application.isPlaying && site.Runner != null && site.Runner.SystemState != null
                ? site.Runner.TimeSeconds
                : 0d;

            // Позиция объекта в осях МЕСТА (там X — север, Y — зенит, Z — восток).
            Vector3 local = transform.localPosition;
            double east = local.z;
            double north = local.x;

            double latRad = KeplerMath.DegreesToRadians(site.LatitudeDegrees);
            double lonRad = KeplerMath.DegreesToRadians(site.LongitudeDegrees);

            Vector3d direction = SurfaceFrameMath.DirectionOffset(
                latRad, lonRad, body.Radius, east, north);
            double ground = SurfaceSceneSystem.GroundHeight(body, site.LatitudeDegrees, site.LongitudeDegrees);
            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain != null)
            {
                // Высота в точке объекта, а не в точке места: на склоне разница
                // между центром базы и её краем — десятки метров.
                // ВАЖНО: SampleHeight ждёт ЕДИНИЧНОЕ направление по поверхности
                // шара (та же конвенция, что у CubeSphere и у рельефа). Если
                // передать точку радиуса R (~1.14e6), шум опросится в другой
                // точке сферы и вернёт чушь — здание встанет на километры в
                // сторону и вверх (проверено: 3807 м и −7153 м).
                ground = TerrainNoise.SampleHeight(
                    TerrainNoiseParams.FromTerrain(terrain),
                    new Unity.Mathematics.double3(direction.X, direction.Y, direction.Z)) * terrain.AmplitudeMeters;
            }

            double wanted = ground + HeightOffsetMeters - site.AltitudeMeters;
            CurrentGapMeters = local.y - wanted;

            if (SnapToGround)
            {
                Vector3 updated = local;
                updated.y = (float)wanted;
                transform.localPosition = updated;
            }

            if (AlignToNormal && terrain != null)
            {
                AlignToTerrainNormal(body, direction, time, ground);
            }
        }

        private void AlignToTerrainNormal(OrbitingBody body, Vector3d direction, double time, double ground)
        {
            // GetOutwardNormal ждёт ТОЧКУ радиуса R (не единичное направление)
            // и усредняет уклон примерно по 114 м. Это ровно та нормаль, по
            // которой садится корабль, — база и шлюпка получают одинаковый
            // наклон, и стык не расходится.
            Vector3d bodyFixed = direction * (body.Radius + ground);
            Vector3d normalAstro = SurfaceFrameMath.Up(
                KeplerMath.DegreesToRadians(site.LatitudeDegrees),
                KeplerMath.DegreesToRadians(site.LongitudeDegrees));
            HeightfieldTerrain terrain = body.Terrain as HeightfieldTerrain;
            if (terrain != null)
            {
                normalAstro = terrain.GetOutwardNormal(body, bodyFixed, time);
            }

            Vector3d local = SurfaceFrameMath.ToLocal(
                KeplerMath.DegreesToRadians(site.LatitudeDegrees),
                KeplerMath.DegreesToRadians(site.LongitudeDegrees),
                normalAstro.Normalized);
            Vector3 target = new Vector3((float)local.X, (float)local.Y, (float)local.Z);
            if (target.sqrMagnitude <= 1e-9f)
            {
                return;
            }

            target.Normalize();
            // Поворот от зенита к нормали, выраженный в осях МЕСТА.
            Quaternion tilt = Quaternion.FromToRotation(Vector3.up, target);
            Quaternion yaw = KeepYaw
                ? Quaternion.Euler(0f, transform.localEulerAngles.y, 0f)
                : Quaternion.identity;
            transform.localRotation = tilt * yaw;
        }

        /// <summary>Пересчитать всех потомков места — после смены точки или рельефа.</summary>
        public static void RefreshAll(Transform siteRoot)
        {
            if (siteRoot == null)
            {
                return;
            }

            SurfaceGrounded[] all = siteRoot.GetComponentsInChildren<SurfaceGrounded>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].site = all[i].GetComponentInParent<SurfaceSite>();
                all[i].Apply();
            }
        }
    }
}
