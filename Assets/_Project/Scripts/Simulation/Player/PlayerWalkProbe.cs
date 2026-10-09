using System.Globalization;
using System.IO;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Проба ходьбы для регресса: игрок идёт на восток заданной скоростью,
    /// раз в секунду логируется его позиция в кадре места (якорь на старте).
    /// Выборка — при точных ts (интерполяция состояния между кадрами), чтобы
    /// сравнение прогонов не зависело от момента кадра: допуск 1 мм.
    /// Автовыход из Play — только в редакторе.
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
        private double nextLogTs = 1d;
        private double prevTs;
        private Vector3d prevPos;
        private bool started;
        private string logPath;

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
            prevTs = Runner.TimeSeconds;
            prevPos = Runner.PlayerPosition;
            started = true;
            logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "baseline", "player2", "walk_probe_log.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, "[WalkProbe] старт: " + WalkSpeed.ToString("F2", Inv) + " м/с на восток, "
                + DurationSeconds.ToString("F0", Inv) + " с" + "\n");
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

            double ts = Runner.TimeSeconds;
            double span = ts - prevTs;
            if (span > 1e-9d)
            {
                while (nextLogTs <= ts)
                {
                    double alpha = (nextLogTs - prevTs) / span;
                    if (alpha < 0d) { alpha = 0d; }
                    if (alpha > 1d) { alpha = 1d; }
                    Vector3d pos = prevPos + (Runner.PlayerPosition - prevPos) * alpha;
                    SiteFrame now = frame.At(nextLogTs);
                    now.ToLocal(pos, Runner.PlayerVelocity, out Vector3d local, out _);
                    string line = "[WalkProbe] ts=" + nextLogTs.ToString("F3", Inv)
                        + " local=(" + local.X.ToString("F6", Inv)
                        + ", " + local.Y.ToString("F6", Inv)
                        + ", " + local.Z.ToString("F6", Inv) + ")";
                    Debug.Log(line);
                    File.AppendAllText(logPath, line + "\n");
                    nextLogTs += 1d;
                }
            }

            prevTs = ts;
            prevPos = Runner.PlayerPosition;

            if (ts >= DurationSeconds)
            {
                Debug.Log("[WalkProbe] DONE");
                File.AppendAllText(logPath, "[WalkProbe] DONE\n");
                enabled = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }
    }
}
