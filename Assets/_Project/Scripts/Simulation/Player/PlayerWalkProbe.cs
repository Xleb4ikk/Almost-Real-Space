using System.Globalization;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Проба ходьбы для регресса: игрок идёт на восток заданной скоростью,
    /// раз в секунду логируется его позиция в кадре места (якорь на старте).
    /// Одинаковая проба до и после интеграции PlayerSurfaceController: допуск
    /// расхождения 1 мм. Автовыход из Play — только в редакторе.
    /// </summary>
    public sealed class PlayerWalkProbe : MonoBehaviour
    {
        public SimulationRunner Runner;
        public bool Active = true;
        public double WalkSpeed = 3d;
        public float DurationSeconds = 60f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private SiteFrame frame;
        private Vector3d walkDirection;
        private float elapsed;
        private int nextLog = 1;
        private bool started;

        private void Start()
        {
            if (!Active || Runner == null || Runner.DominantBody == null)
            {
                enabled = false;
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            if (body.Terrain == null)
            {
                body.Terrain = new SphericalTerrain();
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d center, out _);
            Vector3d up = (Runner.PlayerPosition - center).Normalized;
            Vector3d east = Vector3d.Cross(body.SpinAxis, up);
            if (east.SqrMagnitude < 1e-12d)
            {
                east = Vector3d.Cross(new Vector3d(0d, 0d, 1d), up);
            }

            walkDirection = east.Normalized;
            frame = SiteFrame.Anchor(body, body.Terrain, Runner.PlayerPosition, Runner.TimeSeconds);
            started = true;
            Debug.Log("[WalkProbe] старт: " + WalkSpeed.ToString("F2", Inv) + " м/с на восток, "
                + DurationSeconds.ToString("F0", Inv) + " с");
        }

        private void Update()
        {
            if (!started)
            {
                return;
            }

            Runner.PlayerIntent.WalkDirection = walkDirection;
            Runner.PlayerIntent.WalkSpeed = WalkSpeed;
            elapsed += Time.deltaTime;

            if (elapsed >= nextLog)
            {
                nextLog = Mathf.FloorToInt(elapsed) + 1;
                SiteFrame now = frame.At(Runner.TimeSeconds);
                now.ToLocal(Runner.PlayerPosition, Runner.PlayerVelocity, out Vector3d local, out _);
                Debug.Log("[WalkProbe] t=" + nextLog
                    + " ts=" + Runner.TimeSeconds.ToString("F3", Inv)
                    + " local=(" + local.X.ToString("F6", Inv)
                    + ", " + local.Y.ToString("F6", Inv)
                    + ", " + local.Z.ToString("F6", Inv) + ")");
            }

            if (elapsed >= DurationSeconds)
            {
                Debug.Log("[WalkProbe] DONE");
                enabled = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
    }
}
