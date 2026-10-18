using System.Globalization;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Vertical-slice probe (test scene). Scenarios:
    ///   stand    — flat terrain (SphericalTerrain, h ≡ 0), starts at DropHeightMeters,
    ///              logs the ship point (Runner.Ship.Position) and TimeSeconds once
    ///              per second; DONE — max |ΔPosition| between adjacent seconds after 2 s.
    ///   breakLeg — after settling, lifts the assembly by 2 m (no contact), lateral force
    ///              2×JointStrength on leg 3 until it breaks; then a 0.5 s window with no
    ///              external forces except gravity: the root island's momentum is checked
    ///              against M·g·Δt (internal joint impulses do not change momentum).
    ///   drop100  — 100 landings at 2 m/s onto a flat pad (timeScale ×3), counts false
    ///              joint breaks.
    /// Auto-exit from Play only in the editor.
    /// </summary>
    public sealed class ContactZoneSliceProbe : MonoBehaviour
    {
        public SimulationRunner Runner;
        public ContactZoneHost Host;
        public ContactZoneProxy Proxy;

        [Tooltip("Сценарий: stand | breakLeg | drop100.")]
        public string Case = "stand";

        [Tooltip("Высота старта над поверхностью (м): 0.2 м ≈ касание 2 м/с.")]
        public double DropHeightMeters = 0.2d;

        [Tooltip("breakLeg: латеральная сила на ногу 3 (Н). Предел ноги T1 — 400 кН.")]
        public float BreakForceNewtons = 450000f;

        [Tooltip("Секунд держать сцену (stand); затем итог и выход из Play.")]
        public float DurationSeconds = 12f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private float elapsed;
        private int nextLog = 1;
        private bool captured;
        private double maxPosDelta;
        private SiteFrame standFrame;
        private Vector3d previousLocalPosition;
        private bool havePreviousLocal;
        private bool exitTestStarted;
        private Vector3d localBeforeExit;
        private float exitTestTimer;

        // breakLeg
        private int breakPhase;
        private double phaseTimer;
        private Vector3d momentum0;
        private float momentumMass;
        private float momentumFixedTime0;
        private Vector3 gAtWindow;
        private double momentumError;
        private double momentumScale;

        // drop100
        private bool dropPrepared;
        private int dropIteration;
        private float dropIterationStartFixed;
        private int falseBreaks;
        private Vector3[] dropHome;
        private float savedTimeScale = 1f;

        private void Start()
        {
            if (Runner == null || Runner.DominantBody == null || Runner.Ship == null)
            {
                Debug.LogError("[CZProbe] не назначены ссылки сцены.");
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

            Debug.Log("[CZProbe] старт: case=" + Case + ", drop=" + DropHeightMeters.ToString("F2", Inv)
                + " м, зона=" + ContactZoneHost.Active);
        }

        private void Update()
        {
            if (Runner == null)
            {
                return;
            }

            elapsed += Time.deltaTime;

            if (Case == "drop100")
            {
                UpdateDrop100();
                return;
            }

            if (Case == "breakLeg")
            {
                UpdateBreakLeg();
            }

            // stand: sampling happens in LateUpdate, AFTER ContactZoneHost (order −50),
            // so Ship.Position and TimeSeconds come from the same instant.
        }

        private void LateUpdate()
        {
            if (Runner == null || Case != "stand")
            {
                return;
            }

            UpdateStand();
        }

        // ─── stand ───────────────────────────────────────────────────────────

        private void UpdateStand()
        {
            if (ContactZoneHost.Active && !captured && elapsed >= 2f)
            {
                captured = true;
                // Site-frame anchor at settle time: drift is measured RELATIVE to the
                // surface. Absolute Ship.Position moves with the planet (~30 km/s) and
                // is meaningless for parking.
                standFrame = SiteFrame.Anchor(Runner.DominantBody, Runner.DominantBody.Terrain,
                    Runner.Ship.Position, Runner.TimeSeconds);
                Debug.Log("[CZProbe] зона активна: деталей=" + (Runner.Parts != null ? Runner.Parts.Length : 0)
                    + ", COM на 2 с=" + Format(Proxy.CenterOfMassLocal));
            }

            if (elapsed >= nextLog)
            {
                nextLog = Mathf.FloorToInt(elapsed) + 1;
                Vector3d localPosition = Vector3d.Zero;
                if (captured)
                {
                    standFrame.At(Runner.TimeSeconds).ToLocal(Runner.Ship.Position, Runner.Ship.Velocity,
                        out localPosition, out _);
                    if (havePreviousLocal && !exitTestStarted)
                    {
                        double delta = (localPosition - previousLocalPosition).Magnitude;
                        if (delta > maxPosDelta)
                        {
                            maxPosDelta = delta;
                        }
                    }

                    previousLocalPosition = localPosition;
                    havePreviousLocal = true;
                }

                Debug.Log("[CZProbe] t=" + nextLog + " с, зона=" + ContactZoneHost.Active
                    + ", TimeSeconds=" + Runner.TimeSeconds.ToString("F3", Inv)
                    + ", точка места=" + Format(localPosition)
                    + ", |v|=" + Runner.Ship.Velocity.Magnitude.ToString("F5", Inv) + " м/с");
            }

            if (elapsed >= DurationSeconds && !exitTestStarted)
            {
                // Task-1 criterion: leaving the zone at 400 m altitude without a position
                // jump. Physically push the assembly up (a velocity impulse, not a teleport):
                // the gate exits by altitude and Exit writes the ship point through the same anchor.
                exitTestStarted = true;
                exitTestTimer = 0f;
                localBeforeExit = previousLocalPosition;
                for (int i = 0; i < Proxy.BodyCount; i++)
                {
                    Rigidbody b = Proxy.BodyAt(i);
                    if (b != null && Proxy.IsRootBody(i))
                    {
                        b.linearVelocity += new Vector3(0f, 95f, 0f);
                    }
                }

                Debug.Log("[CZProbe] exit-тест: подъём +95 м/с, ждём выход по высоте 400 м");
                return;
            }

            if (exitTestStarted && ContactZoneHost.Active)
            {
                // Transition continuity: keep the last in-zone frame point (frame by
                // frame) to compare it with the first state after exit.
                standFrame.At(Runner.TimeSeconds).ToLocal(Runner.Ship.Position, Runner.Ship.Velocity,
                    out localBeforeExit, out _);
                exitTestTimer += Time.deltaTime;
                if (exitTestTimer > 10f)
                {
                    Debug.Log("[CZProbe] DONE-exit ФЕЙЛ: зона не вышла за 10 с (altitude?)");
                    Finish();
                }

                return;
            }

            if (exitTestStarted && !ContactZoneHost.Active)
            {
                standFrame.At(Runner.TimeSeconds).ToLocal(Runner.Ship.Position, Runner.Ship.Velocity,
                    out Vector3d localAfter, out _);
                Debug.Log("[CZProbe] DONE-exit: последний кадр в зоне " + Format(localBeforeExit)
                    + ", первый вне " + Format(localAfter)
                    + ", |Δ|=" + (localAfter - localBeforeExit).Magnitude.ToString("F4", Inv)
                    + " м (ожидание — ход за кадр; скачок формулы дал бы ~6 м ЦМ)");
                Debug.Log("[CZProbe] DONE-stand зона=False, max |ΔPosition| (кадр места) после 2 с="
                    + maxPosDelta.ToString("F5", Inv) + " м"
                    + ", TimeSeconds=" + Runner.TimeSeconds.ToString("F3", Inv)
                    + ", деталей=" + (Runner.Parts != null ? Runner.Parts.Length : 0));
                Finish();
                return;
            }
        }

        // ─── breakLeg ────────────────────────────────────────────────────────

        private void UpdateBreakLeg()
        {
            if (breakPhase == 0)
            {
                if (elapsed < 2f || !ContactZoneHost.Active)
                {
                    return;
                }

                // Lift the assembly by 5 m and zero velocities: the break and the momentum
                // window must run without contact (gravity only), with margin for the fall.
                for (int i = 0; i < Proxy.BodyCount; i++)
                {
                    Rigidbody b = Proxy.BodyAt(i);
                    if (b == null)
                    {
                        continue;
                    }

                    b.position += new Vector3(0f, 5f, 0f);
                    b.linearVelocity = Vector3.zero;
                    b.angularVelocity = Vector3.zero;
                }

                breakPhase = 1;
                phaseTimer = 0d;
                Debug.Log("[CZProbe] breakLeg: сборка поднята на 5 м, подаю силу на ногу 3");
                return;
            }

            if (breakPhase == 1)
            {
                phaseTimer += Time.deltaTime;
                Rigidbody leg = Proxy.BodyAt(3);
                if (leg != null && Proxy.BrokenChildren.Count == 0)
                {
                    // Ramp the force over 0.2 s to avoid transient breaks of
                    // neighbouring joints. The amplitude is slightly above leg T1's
                    // joint limit (400 kN).
                    float ramp = Mathf.Min(1f, (float)(phaseTimer / 0.2d));
                    leg.AddForce(new Vector3(BreakForceNewtons * ramp, 0f, 0f), ForceMode.Force);
                }

                if (Proxy.BrokenChildren.Count > 0)
                {
                    Debug.Log("[CZProbe] breakLeg: разрыв после " + phaseTimer.ToString("F3", Inv)
                        + " с; разорвано=" + Proxy.BrokenChildren.Count
                        + " (деталь " + string.Join(",", Proxy.BrokenChildren) + ")"
                        + ", корневых деталей=" + CountRootBodies());
                    breakPhase = 2;
                    phaseTimer = 0d;
                    RootMomentum(out momentum0, out momentumMass);
                    momentumFixedTime0 = Time.fixedTime;
                    gAtWindow = Physics.gravity;
                    Debug.Log("[CZProbe] breakLeg: P0=" + Format(momentum0));
                    return;
                }

                if (phaseTimer > 1d)
                {
                    Debug.Log("[CZProbe] DONE-break ФЕЙЛ: стык не разорвался за 1 с");
                    Finish();
                }

                return;
            }

            if (breakPhase == 2)
            {
                phaseTimer += Time.deltaTime;
                if (phaseTimer < 0.3d)
                {
                    return;
                }

                RootMomentum(out Vector3d p1, out float mass);
                double dt = Time.fixedTime - momentumFixedTime0;
                Vector3d predicted = momentum0 + (ToVector3d(gAtWindow) * (mass * dt));
                momentumError = (p1 - predicted).Magnitude;
                momentumScale = Mathf.Max(1e-3f, SumMassSpeed());
                breakPhase = 3;
                Debug.Log("[CZProbe] DONE-momentum: Δt=" + dt.ToString("F4", Inv)
                    + " с, |ΔP − M·g·Δt|=" + momentumError.ToString("E3", Inv)
                    + " Н·с, Σm|v|=" + momentumScale.ToString("F4", Inv)
                    + ", отн.=" + (momentumError / momentumScale).ToString("E3", Inv)
                    + " (ТЗ ≤1e-6), корневая масса=" + mass.ToString("F0", Inv) + " кг");
                Debug.Log("[CZProbe] DONE-break нога отделилась: разорвано=" + Proxy.BrokenChildren.Count
                    + " (деталь " + string.Join(",", Proxy.BrokenChildren) + "), корневых=" + CountRootBodies());
                Finish();
            }
        }

        private void RootMomentum(out Vector3d momentum, out float mass)
        {
            Vector3 p = Vector3.zero;
            float m = 0f;
            for (int i = 0; i < Proxy.BodyCount; i++)
            {
                if (!Proxy.IsRootBody(i))
                {
                    continue;
                }

                Rigidbody b = Proxy.BodyAt(i);
                if (b == null)
                {
                    continue;
                }

                p += b.linearVelocity * b.mass;
                m += b.mass;
            }

            momentum = new Vector3d(p.x, p.y, p.z);
            mass = m;
        }

        private float SumMassSpeed()
        {
            float sum = 0f;
            for (int i = 0; i < Proxy.BodyCount; i++)
            {
                Rigidbody b = Proxy.BodyAt(i);
                if (b != null)
                {
                    sum += b.mass * b.linearVelocity.magnitude;
                }
            }

            return sum;
        }

        private int CountRootBodies()
        {
            int count = 0;
            for (int i = 0; i < Proxy.BodyCount; i++)
            {
                if (Proxy.IsRootBody(i))
                {
                    count++;
                }
            }

            return count;
        }

        // ─── drop100 ─────────────────────────────────────────────────────────

        private void UpdateDrop100()
        {
            if (!dropPrepared)
            {
                if (elapsed < 2f || !ContactZoneHost.Active)
                {
                    return;
                }

                dropHome = new Vector3[Proxy.BodyCount];
                for (int i = 0; i < Proxy.BodyCount; i++)
                {
                    Rigidbody b = Proxy.BodyAt(i);
                    dropHome[i] = b != null ? b.position : Vector3.zero;
                }

                savedTimeScale = Time.timeScale;
                Time.timeScale = 3f;
                dropPrepared = true;
                dropIteration = 0;
                dropIterationStartFixed = Time.fixedTime;
                falseBreaks = 0;
                Debug.Log("[CZProbe] drop100: старт 100 посадок на 2 м/с (timeScale ×3), база=" + Proxy.BrokenChildren.Count + " разрывов");
                return;
            }

            if (dropIteration >= 100)
            {
                Time.timeScale = savedTimeScale;
                Debug.Log("[CZProbe] DONE-drop100: посадок=100, ложных разрывов=" + falseBreaks
                    + " (всего разорвано=" + Proxy.BrokenChildren.Count + ")");
                Finish();
                return;
            }

            float elapsedFixed = Time.fixedTime - dropIterationStartFixed;
            if (elapsedFixed < 0.45f)
            {
                return;
            }

            // End of iteration: lift +0.2 m from the home pose, zero velocities.
            if (Proxy.BrokenChildren.Count > 0)
            {
                falseBreaks++;
                Debug.Log("[CZProbe] drop100: разрыв на посадке " + (dropIteration + 1)
                    + " (деталь " + string.Join(",", Proxy.BrokenChildren) + ")");
            }

            dropIteration++;
            for (int i = 0; i < Proxy.BodyCount; i++)
            {
                Rigidbody b = Proxy.BodyAt(i);
                if (b == null)
                {
                    continue;
                }

                b.position = dropHome[i] + new Vector3(0f, 0.2f, 0f);
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                b.WakeUp();
            }

            dropIterationStartFixed = Time.fixedTime;
        }

        // ─── common ──────────────────────────────────────────────────────────

        private void Finish()
        {
            enabled = false;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private static Vector3d ToVector3d(Vector3 v)
        {
            return new Vector3d(v.x, v.y, v.z);
        }

        private static string Format(Vector3d v)
        {
            return "(" + v.X.ToString("F3", Inv) + ", " + v.Y.ToString("F3", Inv) + ", " + v.Z.ToString("F3", Inv) + ")";
        }

        private static string Format(Vector3 v)
        {
            return "(" + v.x.ToString("F3", Inv) + ", " + v.y.ToString("F3", Inv) + ", " + v.z.ToString("F3", Inv) + ")";
        }
    }
}
