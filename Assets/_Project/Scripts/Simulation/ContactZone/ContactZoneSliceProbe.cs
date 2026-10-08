using System.Globalization;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Проба вертикального среза (тестовая сцена): ставит плоский рельеф
    /// (SphericalTerrain, h ≡ 0) телу спавна, приподнимает корабль на DropHeightMeters
    /// (мягкое касание ~2 м/с), раз в секунду логирует состояние зоны и через
    /// DurationSeconds печатает итог — дрейф COM в кадре места. Если зона активна,
    /// корабль ведёт PhysX-прокси; вне теста компонент не нужен.
    /// Автовыход из Play — только в редакторе.
    /// </summary>
    public sealed class ContactZoneSliceProbe : MonoBehaviour
    {
        public SimulationRunner Runner;
        public ContactZoneHost Host;
        public ContactZoneProxy Proxy;

        [Tooltip("Высота старта над поверхностью (м): 0.2 м ≈ касание 2 м/с.")]
        public double DropHeightMeters = 0.2d;

        [Tooltip("Секунд держать сцену; затем итог и выход из Play.")]
        public float DurationSeconds = 12f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private float elapsed;
        private int nextLog = 1;
        private bool captured;
        private Vector3d startLocal;
        private double maxSpeed;
        private double maxDrift;

        private void Start()
        {
            if (Runner == null || Runner.DominantBody == null || Runner.Ship == null)
            {
                return;
            }

            if (Runner.DominantBody.Terrain == null)
            {
                Runner.DominantBody.Terrain = new SphericalTerrain();
            }

            if (DropHeightMeters > 0d)
            {
                Runner.DominantBody.EvaluateWorldState(Runner.TimeSeconds, out Vector3d center, out _);
                Vector3d radial = (Runner.Ship.Position - center).Normalized;
                Runner.Ship.Position += radial * DropHeightMeters;
            }

            Debug.Log("[CZProbe] старт: drop=" + DropHeightMeters.ToString("F2", Inv)
                + " м, зона=" + ContactZoneHost.Active);
        }

        private void Update()
        {
            if (Runner == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            // Дрейф меряем от момента осадки (2 с), а не от кадра входа: первые кадры —
            // контактная подстройка PhysX, она не про стоянку.
            if (ContactZoneHost.Active && !captured && elapsed >= 2f)
            {
                captured = true;
                startLocal = Proxy.CenterOfMassLocal;
                Debug.Log("[CZProbe] зона активна: деталей=" + (Runner.Parts != null ? Runner.Parts.Length : 0)
                    + ", COM на 2 с=" + Format(Proxy.CenterOfMassLocal));
            }

            if (captured)
            {
                maxDrift = System.Math.Max(maxDrift, (Proxy.CenterOfMassLocal - startLocal).Magnitude);
                maxSpeed = System.Math.Max(maxSpeed, Proxy.CenterOfMassVelocityLocal.Magnitude);
            }

            if (elapsed >= nextLog)
            {
                nextLog = Mathf.FloorToInt(elapsed) + 1;
                Debug.Log("[CZProbe] t=" + nextLog + " с, зона=" + ContactZoneHost.Active
                    + ", COM=" + Format(Proxy.CenterOfMassLocal)
                    + ", |v|=" + Proxy.CenterOfMassVelocityLocal.Magnitude.ToString("F5", Inv) + " м/с");
            }

            if (elapsed >= DurationSeconds)
            {
                Debug.Log("[CZProbe] DONE зона=" + ContactZoneHost.Active
                    + ", max дрейф=" + maxDrift.ToString("F5", Inv) + " м"
                    + ", max |v|=" + maxSpeed.ToString("F5", Inv) + " м/с"
                    + ", деталей=" + (Runner.Parts != null ? Runner.Parts.Length : 0));
                enabled = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        private static string Format(Vector3d v)
        {
            return "(" + v.X.ToString("F3", Inv) + ", " + v.Y.ToString("F3", Inv) + ", " + v.Z.ToString("F3", Inv) + ")";
        }
    }
}
