using System.Globalization;
using System.IO;
using Galilego.Core;
using Galilego.Simulation.ContactZone;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Simulation.Player
{
    /// <summary>
    /// Проба крыши постройки: луч сверху в render-пространстве находит крышу
    /// (коллайдер SiteBox), игрок ставится на неё и идёт на восток; лог — в файл
    /// (baseline/player2/roof_probe_log.txt). Проверяет, что опора-постройка держит
    /// (sourceId ≥ 100), высота стабильна и нет проваливаний — в том числе от
    /// устаревших поз коллайдеров при выключенном Physics.autoSyncTransforms.
    /// Автовыход из Play — только в редакторе.
    /// </summary>
    public sealed class PlayerRoofProbe : MonoBehaviour
    {
        public SimulationRunner Runner;
        public bool Active = true;
        public double WalkSpeed = 3d;
        public float SettleSeconds = 1.5f;
        public float WalkSeconds = 20f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly System.Reflection.PropertyInfo PositionProperty =
            typeof(SimulationRunner).GetProperty("PlayerPosition",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        private static readonly System.Reflection.PropertyInfo VelocityProperty =
            typeof(SimulationRunner).GetProperty("PlayerVelocity",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

        private SurfaceSite site;
        private SiteFrame frame;
        private Vector3d walkDirection;
        private double startTs;
        private double phaseTs;
        private double nextLogTs;
        private string logPath;
        private int phase;
        private bool started;
        private Vector3 lastSitePosition;

        private void Start()
        {
            if (!Active || Runner == null || Runner.DominantBody == null)
            {
                enabled = false;
                return;
            }

            site = UnityEngine.Object.FindAnyObjectByType<SurfaceSite>();
            if (site == null)
            {
                Debug.LogWarning("[RoofProbe] В сцене нет SurfaceSite — проба выключена.");
                enabled = false;
                return;
            }

            logPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "baseline", "player2", "roof_probe_log.txt"));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, "[RoofProbe] старт: место lat=" + site.LatitudeDegrees.ToString("F5", Inv)
                + " lon=" + site.LongitudeDegrees.ToString("F5", Inv)
                + ", ходьба " + WalkSeconds.ToString("F0", Inv) + " с\n");
            Debug.Log("[RoofProbe] старт");
            startTs = Runner.TimeSeconds;
            started = true;
        }

        private void Update()
        {
            if (!started)
            {
                return;
            }

            double ts = Runner.TimeSeconds;

            if (phase == 0)
            {
                if (ts - startTs >= SettleSeconds)
                {
                    PlaceOnRoof(ts);
                }

                return;
            }

            if (phase == 1)
            {
                if (ts >= nextLogTs)
                {
                    nextLogTs = ts + 0.25d;
                    Vector3 feet = FloatingOrigin.ToRender(Runner.PlayerPosition);
                    Vector3 up = site.transform.up;
                    RaycastHit[] hits = Physics.RaycastAll(feet + (up * 0.5f), -up, 1.5f, ~0, QueryTriggerInteraction.Ignore);
                    int siteHits = 0;
                    float nearest = float.PositiveInfinity;
                    string nearestName = "-";
                    for (int i = 0; i < hits.Length; i++)
                    {
                        SiteBox box = hits[i].collider != null
                            ? hits[i].collider.GetComponentInParent<SiteBox>()
                            : null;
                        if (box == null)
                        {
                            continue;
                        }

                        siteHits++;
                        if (hits[i].distance < nearest)
                        {
                            nearest = hits[i].distance;
                            nearestName = hits[i].collider.name;
                        }
                    }

                    Vector3 siteNow = site.transform.position;
                    Vector3 siteDelta = siteNow - lastSitePosition;
                    lastSitePosition = siteNow;
                    Log("оседание ts=" + ts.ToString("F3", Inv)
                        + " grounded=" + Runner.PlayerGrounded
                        + " src=" + Runner.PlayerGroundSourceId
                        + " ручной-луч: hits=" + siteHits
                        + " ближайший=" + (float.IsPositiveInfinity(nearest) ? "-" : nearest.ToString("F3", Inv))
                        + " '" + nearestName + "'"
                        + " site=" + siteNow.ToString("F2")
                        + " dsite=" + siteDelta.ToString("F4"));
                }

                if (ts - phaseTs >= 2d)
                {
                    BeginWalk(ts);
                }

                return;
            }

            Runner.PlayerIntent.WalkDirection = walkDirection;
            Runner.PlayerIntent.WalkSpeed = WalkSpeed;

            if (ts >= nextLogTs)
            {
                nextLogTs = ts + 0.5d;
                SiteFrame now = frame.At(ts);
                now.ToLocal(Runner.PlayerPosition, Runner.PlayerVelocity, out Vector3d local, out Vector3d lv);
                Log("ts=" + ts.ToString("F3", Inv)
                    + " local=(" + local.X.ToString("F3", Inv)
                    + ", " + local.Y.ToString("F3", Inv)
                    + ", " + local.Z.ToString("F3", Inv) + ")"
                    + " grounded=" + Runner.PlayerGrounded
                    + " src=" + Runner.PlayerGroundSourceId
                    + " vz=" + lv.Z.ToString("F3", Inv));
            }

            if (ts - phaseTs >= WalkSeconds)
            {
                Log("DONE");
                enabled = false;
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        /// <summary>Луч сверху в render-пространстве: найти верхнюю опору-SiteBox и встать на неё.</summary>
        private void PlaceOnRoof(double ts)
        {
            SiteBox[] boxes = UnityEngine.Object.FindObjectsByType<SiteBox>(FindObjectsSortMode.None);
            SiteBox largest = null;
            float largestVolume = 0f;
            for (int i = 0; i < boxes.Length; i++)
            {
                float volume = boxes[i].Size.x * boxes[i].Size.y * boxes[i].Size.z;
                if (volume > largestVolume)
                {
                    largestVolume = volume;
                    largest = boxes[i];
                }
            }

            if (largest == null)
            {
                Log("НЕТ SiteBox в сцене — проба остановлена");
                enabled = false;
                return;
            }

            Vector3 up = site.transform.up;
            Vector3 origin = largest.transform.position + (up * 400f);
            RaycastHit[] hits = Physics.RaycastAll(origin, -up, 800f, ~0, QueryTriggerInteraction.Ignore);
            float bestDistance = float.PositiveInfinity;
            RaycastHit best = default;
            int siteHits = 0;
            for (int i = 0; i < hits.Length; i++)
            {
                SiteBox box = hits[i].collider != null
                    ? hits[i].collider.GetComponentInParent<SiteBox>()
                    : null;
                if (box == null)
                {
                    continue;
                }

                siteHits++;
                if (hits[i].distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = hits[i].distance;
                best = hits[i];
            }

            if (siteHits == 0)
            {
                Log("НЕТ коллайдера SiteBox под лучом над '" + largest.name + "' ("
                    + largest.transform.position + ") — проба остановлена");
                enabled = false;
                return;
            }

            double roofHeight = Vector3.Dot(best.point - site.transform.position, up);
            Vector3 placeRender = best.point + (up * 0.05f);
            Vector3d place = FloatingOrigin.Anchor + AstroFrame.ToAstro(placeRender);
            PositionProperty.SetValue(Runner, place);
            VelocityProperty.SetValue(Runner, Vector3d.Zero);

            Log("постановка на крышу: высота над местом=" + roofHeight.ToString("F2", Inv)
                + " м, коллайдер='" + best.collider.name + "', попаданий SiteBox=" + siteHits
                + ", бокс='" + largest.name + "'");

            phase = 1;
            phaseTs = ts;
        }

        private void BeginWalk(double ts)
        {
            phase = 2;
            phaseTs = ts;
            nextLogTs = ts;
            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(ts, out Vector3d center, out _);
            Vector3d up = (Runner.PlayerPosition - center).Normalized;
            Vector3d east = Vector3d.Cross(body.SpinAxis, up);
            walkDirection = east.SqrMagnitude > 1e-12d
                ? east.Normalized
                : Vector3d.Cross(new Vector3d(0d, 0d, 1d), up).Normalized;
            frame = SiteFrame.Anchor(body, body.Terrain ?? new SphericalTerrain(), Runner.PlayerPosition, ts);
            Log("ходьба: старт src=" + Runner.PlayerGroundSourceId + " grounded=" + Runner.PlayerGrounded);
        }

        private void Log(string message)
        {
            Debug.Log("[RoofProbe] " + message);
            File.AppendAllText(logPath, "[RoofProbe] " + message + "\n");
        }
    }
}
