using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// VESSEL BUILD MODE (the "building system" branch).
    ///
    /// Two states:
    ///   Build (default): the vessel is static — it stands where it was assembled,
    ///     the ship physics is not stepped (SimulationRunner.BuildMode), and the
    ///     position is pinned to the launch pad axes every frame (the planet
    ///     rotates — the vessel rides together with the slab).
    ///   Flight: holding R (1 s) turns the draft into PartDefinition[],
    ///     mass/position/velocity are handed to the simulation, and the contact
    ///     zone turns on (exact PhysX physics near the ground). Holding R again
    ///     returns the vessel and the world exactly to the build state (the
    ///     snapshot is the draft itself; zone bodies and debris are cleaned up).
    ///
    /// Placement: the part ghost follows the camera ray and snaps to free nodes
    /// of placed parts (one part per node); checks require the footprint to be
    /// fully on the pad, without intersections with other parts; placement
    /// outside the pad is forbidden. The ghost is always blue (semi-transparent),
    /// invalid — red; the natural paint appears on the placed part.
    /// </summary>
    [DefaultExecutionOrder(-110)]
    public sealed class SpacecraftBuilder : MonoBehaviour
    {
        [Header("Ссылки сцены")]
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("GO корабля (с ShipView) — под ним живут визуалы деталей.")]
        public Transform ShipTransform;

        [Tooltip("Зона стартовой площадки (объект «Площадка стартовая»).")]
        public LaunchPadZone Zone;

        [Tooltip("Зона контакта (точная физика у земли). Пусто — полёт пойдёт аналитической физикой.")]
        public ContactZoneHost ZoneHost;

        [Header("Детали")]
        [Tooltip("Модели двигателей — заполняется из реестра EngineSpecs (автосинхронизация или Galilego → Building → Setup Scene). Порядок = порядок записей реестра.")]
        public GameObject[] EnginePrefabs;

        [Tooltip("Префаб капсулы Orion (Models/Parts/Capsules/Orion/Orion_CrewModule.prefab) — экипажный модуль как деталь. Габариты и масса мерятся по нему.")]
        public GameObject CapsulePrefab;

        [Tooltip("Модель блока РСУ (Models/Parts/RCS/RCS_Block_5N.fbx) — четыре блока по кольцу вокруг каждого бака. Пусто — без блоков.")]
        public GameObject RcsBlockPrefab;

        [Tooltip("Материал боковины бака.")]
        public Material TankSideMaterial;

        [Tooltip("Материал крышек бака.")]
        public Material TankCapMaterial;

        [Tooltip("Материал отделителя.")]
        public Material SeparatorMaterial;

        [Header("Отладка")]
        [Tooltip("Собрать на старте отладочный стек двигатель+бак+отделитель (спайк).")]
        public bool DebugSpikeStack;

        [Tooltip("Отладка ступенчатой тяги: зажигать ступени автоматически, без Space (проверка расхода/выработки этапа 1).")]
        public bool DebugAutoIgniteStages;

        /// <summary>How long R must be held to toggle the physics, s.</summary>
        public const float PhysicsHoldSeconds = 1f;

        /// <summary>Maximum placement height above the slab (in-air placement), m.</summary>
        public const float MaxBuildHeight = 30f;

        // ─── Draft ────────────────────────────────────────────────────────

        private readonly System.Collections.Generic.List<PlacedPart> parts
            = new System.Collections.Generic.List<PlacedPart>();

        /// <summary>Draft parts (for UI and debugging).</summary>
        public System.Collections.Generic.IReadOnlyList<PlacedPart> Parts => parts;

        /// <summary>Physics on (flight) — R toggled the mode.</summary>
        public bool PhysicsOn { get; private set; }

        /// <summary>R hold progress (0..1) — for the indicator.</summary>
        public float RHoldProgress { get; private set; }

        /// <summary>Inventory window open: placement and R are disabled (set by BuildUI).</summary>
        public bool UiOpen { get; set; }

        /// <summary>A part is selected for placement (hotbar slot, set by BuildUI).</summary>
        public bool HasSelection { get; set; }

        /// <summary>
        /// Preset of the selected panel slot (set by BuildUI): part kind and its
        /// sizes. Each slot has its own sizes — see PartPreset.
        /// </summary>
        public PartPreset SelectedPreset { get; set; }

        // Initial part sizes: the build panel creates slot presets (PartPreset)
        // from them; the player then edits sizes per slot.
        // Tank: bottom radius, top radius and length; form — cylinder or cone.
        public float TankRadius = PartCatalog.TankDefaultRadius;
        public float TankTopRadius = PartCatalog.TankDefaultRadius;
        public float TankHeight = PartCatalog.TankDefaultHeight;

        /// <summary>Initial tank form for new slots: cylinder (0) or cone.</summary>
        public TankForm TankForm;

        public float SeparatorRadius = PartCatalog.SeparatorDefaultRadius;

        /// <summary>Initial tank load for new slots (Id from PropellantLoads).</summary>
        public PropellantLoad TankLoad = PropellantLoads.Default;

        private readonly System.Collections.Generic.List<PartCatalog.EngineTemplate> engines
            = new System.Collections.Generic.List<PartCatalog.EngineTemplate>();

        private Material ghostMaterial;
        private Material invalidMaterial;
        private Material deleteMaterial;
        private PartCatalog.PartVisual ghost;
        private PartKind ghostKind;
        private float ghostRadius;
        private float ghostTopRadius;
        private float ghostHeight;
        private string ghostEngineId;
        private int deleteTarget = -1;
        private PartCatalog.PartVisual deleteHighlighted;

        // RCS ring ghost: one block visual per ring slot, rebuilt when the count changes.
        private int ghostRcsCount;
        private Transform[] rcsGhostBlocks = System.Array.Empty<Transform>();

        // Last valid RCS ring aim: host tank and where the ring sits on its barrel.
        private bool rcsAimValid;
        private int rcsAimTank = -1;
        private float rcsAimHeight;
        private float rcsAimAngle;

        // Suppress the ghost right after placing a ring: it would otherwise sit on
        // top of the new blocks. The ghost returns when the aim moves away.
        private bool rcsPlacedAim;
        private int rcsPlacedTank = -1;
        private int rcsPlacedCount;
        private float rcsPlacedHeight;
        private float rcsPlacedAngle;

        /// <summary>Next group id for "placed together" parts (an RCS ring).</summary>
        private int nextPartGroup = 1;

        /// <summary>Aim cone samples (AimCone) — buffer to avoid per-frame allocations.</summary>
        private readonly Vector3[] aimDirections = new Vector3[AimCone.Samples];

        /// <summary>Per-part sample votes (ring coverage area) — allocation-free buffer.</summary>
        private int[] aimVotes = System.Array.Empty<int>();

        private bool spawned;
        private bool spikeDone;
        private float rHold;
        private bool rLatched;
        private bool ghostVisible;
        private bool ghostValid;
        private BuildMath.SnapHit lastSnap;
        private bool lastSnapFound;
        private Vector3 lastPivot;
        private bool pendingPhysics;
        private System.Action<double> frameHook;

        // ─── Mirror filter (solver jitter) ──────────────────────────────────

        /// <summary>Smoothed pose of the mirrored parts, in the zone root axes.</summary>
        private Vector3[] mirrorPos;

        private Quaternion[] mirrorRot;

        /// <summary>Whether the smoothed pose of a part is initialised.</summary>
        private bool[] mirrorInit;

        /// <summary>Mirrored frames since the flight start (the self-check runs once).</summary>
        private int mirrorFrames;

        /// <summary>Filter time constant, s: the solver's millimetre jitter is damped with it.</summary>
        public const float MirrorJitterSeconds = 0.04f;

        /// <summary>Deadband, m: below it the pose is damped, above it the visual snaps to the body (no lag).</summary>
        public const float MirrorJitterMeters = 0.05f;

        /// <summary>Deadband, degrees: the same for the rotation.</summary>
        public const float MirrorJitterDegrees = 0.25f;

        /// <summary>Engine catalog is built (no repeated builds).</summary>
        private bool catalogSynced;

        /// <summary>Engines/capsule already warned about — logged once, not every frame.</summary>
        private bool enginesWarned;
        private bool capsuleWarned;

        /// <summary>Earliest time to retry building the catalog (seconds).</summary>
        private float catalogRetryAt;

        private void Awake()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (ShipTransform == null)
            {
                ShipView view = FindAnyObjectByType<ShipView>();
                if (view != null)
                {
                    ShipTransform = view.transform;
                }
            }

            if (Runner != null && ShipTransform != null)
            {
                // The runner puts the ship root at Ship.Position before the player step in free flight.
                Runner.VesselRoot = ShipTransform;
            }

            if (Zone == null)
            {
                Zone = FindAnyObjectByType<LaunchPadZone>();
            }

            if (ZoneHost == null)
            {
                ZoneHost = FindAnyObjectByType<ContactZoneHost>();
            }

            SyncCatalog();

            ghostMaterial = CreateOverlayMaterial(new Color(0.25f, 0.55f, 1f, 0.38f), "GhostBlue");
            invalidMaterial = CreateOverlayMaterial(new Color(1f, 0.25f, 0.2f, 0.38f), "GhostRed");
            deleteMaterial = CreateOverlayMaterial(new Color(1f, 0.15f, 0.12f, 1f), "DeleteRed");

            if (Zone != null && Runner != null)
            {
                // Build is the default state: the ship physics step is not run
                // until the player starts flight (R). Without a zone in the scene the mode stays off.
                Runner.BuildMode = true;
                if (ZoneHost != null)
                {
                    ZoneHost.Enabled = false;
                }

                // Pinning and flight start live in the runner frame: only there is
                // the frame time advanced and consistent with player/anchor time.
                frameHook = OnBuildModeFrame;
                Runner.BuildModeFrameHook = frameHook;
            }
        }

        /// <summary>
        /// Build the engine catalog from EnginePrefabs and register the capsule.
        /// Update calls it lazily while the catalog is empty (scene objects can
        /// survive Play sessions when Scene/Domain Reload is disabled), with a
        /// retry pause between attempts.
        /// </summary>
        private void SyncCatalog()
        {
            if (catalogSynced || Time.unscaledTime < catalogRetryAt)
            {
                return;
            }

            catalogRetryAt = Time.unscaledTime + 2f;
            if (EnginePrefabs != null && EnginePrefabs.Length > 0)
            {
                for (int i = 0; i < EnginePrefabs.Length; i++)
                {
                    PartCatalog.EngineTemplate template = PartCatalog.EngineTemplate.Measure(EnginePrefabs[i]);
                    if (template != null)
                    {
                        engines.Add(template);
                    }
                }
            }

            if (CapsulePrefab != null)
            {
                PartCatalog.RegisterCapsule(CapsulePrefab);
            }

            PartCatalog.RegisterRcsBlock(RcsBlockPrefab);

            catalogSynced = engines.Count > 0;
            if (!enginesWarned && engines.Count == 0)
            {
                enginesWarned = true;
                Debug.LogError("[Build] ни одной модели двигателя: задай EnginePrefabs (Galilego → Building → Setup Scene).");
            }

            if (!capsuleWarned && CapsulePrefab == null)
            {
                capsuleWarned = true;
                Debug.LogError("[Build] не задан CapsulePrefab (капсула Orion) — карточки капсулы в интерфейсе не будет.");
            }
        }

        private void OnDisable()
        {
            if (Runner != null && ReferenceEquals(Runner.BuildModeFrameHook, frameHook))
            {
                Runner.BuildModeFrameHook = null;
            }
        }

        private void Update()
        {
            SyncCatalog();

            if (Runner == null || Zone == null)
            {
                return;
            }

            if (!Zone.Ready)
            {
                return;
            }

            if (!spawned && SpawnOnPad())
            {
                spawned = true;
            }

            if (DebugSpikeStack && spawned && !spikeDone)
            {
                spikeDone = true;
                BuildSpikeStack();
            }

            UpdateRHold();

            if (PhysicsOn)
            {
                UpdateStaging();
                return;
            }

            // Alt: delete mode — the aim target turns red, RMB deletes it;
            // the placement ghost is hidden.
            if (!UiOpen && PlayerInput.Held(GameAction.BuildDelete))
            {
                HideGhost();
                UpdateDeleteAim();
            }
            else
            {
                ClearDeleteHighlight();
                UpdatePlacement();
            }

            if (!UiOpen)
            {
                if (PlayerInput.Held(GameAction.BuildDelete))
                {
                    if (deleteTarget >= 0 && PlayerInput.Down(GameAction.BuildPlace))
                    {
                        RemoveAt(deleteTarget);
                    }
                }
                else if (ghostVisible && ghostValid && PlayerInput.Down(GameAction.BuildPlace))
                {
                    PlaceGhostedPart();
                }

                if (PlayerInput.Held(GameAction.BuildPeek) && PlayerInput.Down(GameAction.BuildUndo))
                {
                    RemoveLast();
                }
            }
        }

        // ─── Aimed deletion (Alt) ───────────────────────────────────────

        /// <summary>
        /// Highlight the part under the aim in red (Alt held). The center ray has
        /// priority; the ring is used as a fallback selection area only when the
        /// center ray misses.
        /// </summary>
        private void UpdateDeleteAim()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                ClearDeleteHighlight();
                return;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            AimCone.FillDirections(rayDirection, AimCone.Tangent(camera), aimDirections);
            if (aimVotes.Length < parts.Count)
            {
                aimVotes = new int[Mathf.Max(16, parts.Count)];
            }

            int found = -1;
            if (BuildMath.TryRaycastParts(parts, rayOrigin, aimDirections, aimVotes, BuildMath.SnapReach, out int index, out _))
            {
                found = index;
            }

            if (found == deleteTarget)
            {
                return;
            }

            ClearDeleteHighlight();
            if (found >= 0 && parts[found].Visual != null)
            {
                deleteTarget = found;
                deleteHighlighted = parts[found].Visual;
                deleteHighlighted.SetMaterial(deleteMaterial);
            }
        }

        /// <summary>Clear the red highlight of the delete target.</summary>
        private void ClearDeleteHighlight()
        {
            if (deleteHighlighted != null)
            {
                deleteHighlighted.RestoreNatural();
            }

            deleteHighlighted = null;
            deleteTarget = -1;
        }

        /// <summary>
        /// Mirror the visuals onto the contact zone bodies: in flight part poses
        /// come straight from the PhysX proxy bodies (position+rotation — falling
        /// and tipping are visible). Called by VesselMirror (order −48), after the
        /// zone has moved the frame root and before the camera.
        ///
        /// The body pose is filtered: the solver's millimetre jitter would otherwise
        /// reach the seat anchor and shake the camera (the seated player rides the
        /// anchor, not the smoothed player point). The filter works in the ZONE
        /// axes — the zone root moves with the floating origin, but the pose inside
        /// it is smooth. A deadband keeps it lag-free: the jitter is damped, a real
        /// motion (the ship's speed relative to the pad) snaps the visual to the body.
        /// </summary>
        public void MirrorVisualsToBodies()
        {
            if (Runner == null || !PhysicsOn || ZoneHost == null || !ContactZoneHost.Active)
            {
                return;
            }

            ContactZoneProxy proxy = ZoneHost.Proxy;
            if (proxy == null)
            {
                return;
            }

            if (mirrorPos == null || mirrorPos.Length != parts.Count)
            {
                mirrorPos = new Vector3[parts.Count];
                mirrorRot = new Quaternion[parts.Count];
                mirrorInit = new bool[parts.Count];
            }

            Transform zone = proxy.transform;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float k = 1f - Mathf.Exp(-dt / MirrorJitterSeconds);
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                if (part.Visual == null || part.Visual.Root == null)
                {
                    continue;
                }

                Rigidbody body = proxy.BodyAt(i);
                if (body == null)
                {
                    continue;
                }

                Vector3 targetPos = zone.InverseTransformPoint(body.transform.position);
                Quaternion targetRot = Quaternion.Inverse(zone.rotation) * body.transform.rotation;
                if (!mirrorInit[i])
                {
                    mirrorPos[i] = targetPos;
                    mirrorRot[i] = targetRot;
                    mirrorInit[i] = true;
                }
                else
                {
                    Vector3 delta = targetPos - mirrorPos[i];
                    if (delta.sqrMagnitude <= MirrorJitterMeters * MirrorJitterMeters)
                    {
                        mirrorPos[i] = Vector3.Lerp(mirrorPos[i], targetPos, k);
                    }
                    else
                    {
                        mirrorPos[i] = targetPos;
                    }

                    if (Quaternion.Angle(mirrorRot[i], targetRot) <= MirrorJitterDegrees)
                    {
                        mirrorRot[i] = Quaternion.Slerp(mirrorRot[i], targetRot, k);
                    }
                    else
                    {
                        mirrorRot[i] = targetRot;
                    }
                }

                // The proxy body sits at the CENTER of the part shape, while the
                // visual is built from the part PIVOT (tank/engine bottom,
                // separator center). Lower the visual root by Metrics.Center
                // along the body's local up.
                Quaternion rot = zone.rotation * mirrorRot[i];
                Vector3 pivot = zone.TransformPoint(mirrorPos[i])
                    - (rot * Vector3.up * (float)part.Metrics.Center);
                part.Visual.Root.transform.SetPositionAndRotation(pivot, rot);
            }

            // The capsule's exact colliders moved with the visual: the player's
            // queries run in Update, so the physics needs the new poses now.
            Physics.SyncTransforms();
            if (++mirrorFrames == 15)
            {
                CheckCapsuleQueries();
            }

            diagnosticTimer += Time.deltaTime;
            if (diagnosticTimer >= 1f)
            {
                diagnosticTimer = 0f;
                LogFlightDiagnostics();
            }
        }

        /// <summary>Flight diagnostics every second (see LogFlightDiagnostics).</summary>
        private float diagnosticTimer;

        [Tooltip("Диагностика полёта: раз в секунду писать, где камера, якорь кресла и корабль.")]
        public bool FlightDiagnostics = false;

        /// <summary>
        /// Flight diagnostics: where the camera, the seat anchor and the ship are in
        /// render space, and how much the flat floating-origin bridge and the site
        /// frame disagree at the seat. The zone and the build mode draw the vessel in
        /// the SITE axes (the ship's rotation is the site's), while the flat bridge
        /// maps a render offset as a global vector — at the seat (tens of metres above
        /// the rocket's bottom) the two interpretations differ, and the seated player's
        /// astro position (the floating origin) is converted with the flat one.
        /// A large gap is reported as a warning on its own.
        /// </summary>
        private void LogFlightDiagnostics()
        {
            Transform capsule = null;
            CockpitSeat seat = null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Kind != PartKind.Capsule || parts[i].Visual == null || parts[i].Visual.Root == null)
                {
                    continue;
                }

                capsule = parts[i].Visual.Root.transform;
                seat = capsule.GetComponentInChildren<CockpitSeat>(true);
                break;
            }

            if (seat == null || seat.Anchor == null || Runner == null)
            {
                return;
            }

            Vector3 anchor = seat.Anchor.position;
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            FirstPersonCamera fp = cam != null ? cam.GetComponent<FirstPersonCamera>() : null;
            Vector3 playerGo = fp != null && fp.Target != null ? fp.Target.position : Vector3.zero;
            Vector3 shipGo = ShipTransform != null ? ShipTransform.position : Vector3.zero;
            Vector3 zoneRoot = ZoneHost != null && ZoneHost.Proxy != null ? ZoneHost.Proxy.transform.position : Vector3.zero;
            Vector3 body = Vector3.zero;
            float bodySpeed = 0f;
            Vector3 bodyLocal = Vector3.zero;
            if (ZoneHost != null && ZoneHost.Proxy != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    if (parts[i].Kind != PartKind.Capsule)
                    {
                        continue;
                    }

                    Rigidbody rb = ZoneHost.Proxy.BodyAt(i);
                    if (rb != null)
                    {
                        body = rb.transform.position;
                        bodySpeed = rb.linearVelocity.magnitude;
                        bodyLocal = ZoneHost.Proxy.transform.InverseTransformPoint(body);
                    }

                    break;
                }
            }

            // The key number: the same point (the seat anchor) converted by the flat
            // bridge and by the site frame.
            double flatDiff = -1d;
            double latitude = 0d;
            SiteFrame zoneFrame = ZoneHost != null ? ZoneHost.ZoneFrameAt(Runner.TimeSeconds) : null;
            if (zoneFrame != null)
            {
                latitude = zoneFrame.LatitudeDegrees;
                Galilego.Core.Vector3d flatAstro = FloatingOrigin.Anchor + AstroFrame.ToAstro(anchor);

                // The site axes in render space (the same bridge the player support uses:
                // the direction of a site axis is the render offset of the world point).
                Vector3 originRender = FloatingOrigin.ToRender(zoneFrame.Origin);
                Vector3 eastRender = (FloatingOrigin.ToRender(zoneFrame.Origin + zoneFrame.East) - originRender).normalized;
                Vector3 northRender = (FloatingOrigin.ToRender(zoneFrame.Origin + zoneFrame.North) - originRender).normalized;
                Vector3 upRender = (FloatingOrigin.ToRender(zoneFrame.Origin + zoneFrame.Up) - originRender).normalized;

                Vector3 rel = anchor - zoneRoot;
                var local = new Galilego.Core.Vector3d(
                    Vector3.Dot(rel, eastRender), Vector3.Dot(rel, northRender), Vector3.Dot(rel, upRender));
                zoneFrame.ToWorld(local, Galilego.Core.Vector3d.Zero, out Galilego.Core.Vector3d frameAstro, out _);
                flatDiff = (flatAstro - frameAstro).Magnitude;
            }

            double camToAnchor = Vector3.Distance(camPos, anchor);
            double camToPlayer = Vector3.Distance(camPos, playerGo);
            double camToShip = Vector3.Distance(camPos, shipGo);
            double astroGap = (Runner.PlayerPosition - Runner.Ship.Position).Magnitude;
            string line = string.Format(
                "[Flight] anchor={0} cam={1} playerGo={2} shipGo={3} zoneRoot={4} body={5} bodyLocal={6} | cam-anchor={7:F2} cam-playerGo={8:F2} cam-shipGo={9:F2} cam-zoneRoot={10:F2} | astro player-ship={11:F2} flat-vs-frame@anchor={12:F2} lat={13:F2} bodySpeed={14:F2}",
                anchor, camPos, playerGo, shipGo, zoneRoot, body, bodyLocal,
                camToAnchor, camToPlayer, camToShip, Vector3.Distance(camPos, zoneRoot),
                astroGap, flatDiff, latitude, bodySpeed);

            if (camToAnchor > 2d
                || System.Math.Abs(camToShip - astroGap) > 3d
                || flatDiff > 5d
                || bodySpeed > 50f)
            {
                Debug.LogWarning("[Flight] РАСХОЖДЕНИЕ: " + line);
            }
            else if (FlightDiagnostics)
            {
                Debug.Log(line);
            }
        }

        /// <summary>
        /// Self-check (once per flight): the exact capsule colliders must stay
        /// visible to the player's queries (the floor rays, the E rays). excludeLayers
        /// is a CONTACT filter — if it filtered queries as well, the player would
        /// fall through the capsule floor. The ray comes from ABOVE the capsule: a
        /// ray from the seat would start inside the seat's own interaction box and
        /// always hit it, so the check would pass even with a broken filter.
        /// </summary>
        private void CheckCapsuleQueries()
        {
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                if (part.Kind != PartKind.Capsule || part.Visual == null || part.Visual.Root == null)
                {
                    continue;
                }

                Collider[] colliders = part.Visual.Root.GetComponentsInChildren<Collider>(true);
                if (colliders.Length == 0)
                {
                    return;
                }

                Bounds bounds = colliders[0].bounds;
                for (int c = 1; c < colliders.Length; c++)
                {
                    bounds.Encapsulate(colliders[c].bounds);
                }

                Transform zone = ZoneHost != null && ZoneHost.Proxy != null ? ZoneHost.Proxy.transform : null;
                Vector3 up = zone != null ? zone.up : Vector3.up;
                float span = bounds.extents.magnitude;
                Vector3 from = bounds.center + (up * (span + 2f));
                RaycastHit[] hits = Physics.RaycastAll(from, -up, (span * 2f) + 4f, ~0,
                    QueryTriggerInteraction.Ignore);
                for (int h = 0; h < hits.Length; h++)
                {
                    if (hits[h].collider.transform.IsChildOf(part.Visual.Root.transform))
                    {
                        return;
                    }
                }

                Debug.LogWarning("[Capsule] excludeLayers фильтрует и запросы: луч сверху не видит коллайдеры капсулы. "
                    + "Игрок провалится внутрь. Перейди на Physics.IgnoreCollision по парам "
                    + "(коллайдеры Orion x коллайдеры прокси).");
                return;
            }
        }

        // ─── Spawn and pinning ─────────────────────────────────────────────────

        /// <summary>
        /// Move the ship and the player to the launch pad (once).
        /// Before the move, sanity check: pad local coordinates must not be
        /// kilometres away from its origin.
        /// </summary>
        private bool SpawnOnPad()
        {
            Vector3 vesselLocal = Zone.VesselSpawnLocal;
            Vector3 playerLocal = Zone.PlayerSpawnLocal;
            bool sane = Mathf.Abs(vesselLocal.x) < 1000f && Mathf.Abs(vesselLocal.y) < 1000f && Mathf.Abs(vesselLocal.z) < 1000f
                && Mathf.Abs(playerLocal.x) < 1000f && Mathf.Abs(playerLocal.y) < 1000f && Mathf.Abs(playerLocal.z) < 1000f;
            if (!sane)
            {
                Debug.LogError("[Build] точка спавна площадки подозрительна, спавн не переношу: " + vesselLocal);
                return false;
            }

            Zone.SiteToAstro(vesselLocal, out Galilego.Core.Vector3d shipPos, out Galilego.Core.Vector3d shipVel);
            Zone.SiteToAstro(playerLocal, out Galilego.Core.Vector3d playerPos, out Galilego.Core.Vector3d playerVel);
            Runner.SetShipState(shipPos, shipVel, Galilego.Events.VesselRegime.Landed);
            Runner.SetPlayerOnSurface(playerPos, playerVel);
            Debug.Log(string.Format(
                "[Build] спавн на площадке: аппарат ({0:F1}, {1:F1}, {2:F1}), игрок ({3:F1}, {4:F1}, {5:F1})",
                vesselLocal.x, vesselLocal.y, vesselLocal.z,
                playerLocal.x, playerLocal.y, playerLocal.z));
            return true;
        }

        /// <summary>
        /// Runner frame in build mode: pins the vessel (or starts the requested
        /// flight) at the CURRENT frame time. The runner sets the render anchor
        /// later — everything is consistent within the same frame, without lag.
        /// </summary>
        private void OnBuildModeFrame(double t)
        {
            if (pendingPhysics)
            {
                pendingPhysics = false;
                EnterFlightAt(t);
                return;
            }

            PinShipAt(t);
        }

        /// <summary>Keep the ship point at the bottom of the assembly (or at the spawn point of an empty draft).</summary>
        private void PinShipAt(double t)
        {
            DebugUpdateCount++;
            Vector3 bottom = parts.Count > 0 ? BuildMath.VesselBottom(parts) : Zone.VesselSpawnLocal;
            DebugLastPinLocal = bottom;
            Zone.SiteToAstroAt(bottom, t, out Galilego.Core.Vector3d pos, out Galilego.Core.Vector3d vel);
            DebugLastPinAstro = pos;
            Runner.SetShipState(pos, vel, Galilego.Events.VesselRegime.Landed);
            if (ShipTransform != null)
            {
                ShipTransform.rotation = Zone.SiteRenderRotation;
            }
        }

        // TEMPORARY drift debugging (remove after investigation).
        public long DebugUpdateCount;
        public Vector3 DebugLastPinLocal;
        public Galilego.Core.Vector3d DebugLastPinAstro;

        // ─── R: physics/build ───────────────────────────────────────────────

        private void UpdateRHold()
        {
            bool held = !UiOpen && PlayerInput.Held(GameAction.BuildPhysics);
            if (held)
            {
                if (!rLatched)
                {
                    rHold += Time.deltaTime;
                    if (rHold >= PhysicsHoldSeconds)
                    {
                        rLatched = true;
                        rHold = 0f;
                        SetPhysics(!PhysicsOn);
                    }
                }
            }
            else
            {
                rHold = 0f;
                rLatched = false;
            }

            RHoldProgress = held && !rLatched ? Mathf.Clamp01(rHold / PhysicsHoldSeconds) : 0f;
        }

        /// <summary>
        /// Toggle the mode: true — physics (flight), false — build.
        /// Turning physics on is deferred to the runner frame (EnterFlightAt):
        /// only there is the frame time advanced and the position lands exactly on the slab.
        /// </summary>
        public void SetPhysics(bool on)
        {
            if (on)
            {
                if (parts.Count == 0)
                {
                    Debug.Log("[Build] аппарат пуст — физику включать нечего.");
                    return;
                }

                pendingPhysics = true;
            }
            else
            {
                ReturnToBuild();
            }
        }

        /// <summary>LAS/RCS parameters (placeholders until models appear) — edited in the inspector.</summary>
        public Galilego.Flight.FlightSystemsConfig FlightSystems = new Galilego.Flight.FlightSystemsConfig();

        private void EnterFlightAt(double t)
        {
            if (parts.Count == 0)
            {
                return;
            }

            // Before handing over to physics — "weld" free-standing parts:
            // a part hanging within 2 m above someone's node and centered
            // is pulled flush and rigidly attached.
            WeldLoosePartsAtLaunch();

            PartDefinition[] defs = BuildMath.ToPartDefinitions(parts);

            // Staged propulsion: cores from the draft (same "separator ends the
            // stage" grouping as the ΔV panel) + mass of parts outside the main
            // assembly — present in Ship.Mass, absent from cores, so the invariant holds.
            var cores = DeltaV.BuildStageAssembly(parts);

            // 6-DOF flight model: real engines/nozzles, RCS. The hardware (RCS blocks)
            // is added to the mass — it is "outside the cores" and lands in DetachedMassKg.
            Galilego.Flight.VesselFlightModel flight = Galilego.Flight.FlightModelFactory.Build(
                parts, defs, cores, FlightSystems);
            Runner.SetVesselParts(flight.Defs);
            double hardwareKg = flight.HardwareMassKg();
            Runner.Ship.Mass = BuildMath.TotalMass(parts) + hardwareKg;
            Runner.SetDryMass(BuildMath.EffectiveDryMass(parts, PropellantPairs.Default.Mix));

            Runner.SetVesselPropulsion(new StagePropulsion(
                cores, DetachedMassKg(parts, cores) + hardwareKg, DebugAutoIgniteStages));
            Runner.SetVesselFlight(flight);
            Runner.Ship.Attitude = Galilego.Core.QuaternionD.Identity;
            Runner.Ship.AngularVelocity = Galilego.Core.Vector3d.Zero;
            Runner.Ship.PositionIsCom = false;
            Galilego.Flight.FlightRuntime.Begin(Runner, ZoneHost, flight);

            // In flight the visual colliders are excluded from PhysX CONTACTS (otherwise the
            // proxy bodies would collide with their own visuals) but stay visible to the
            // player's queries: outside the contact zone there are no proxy bodies at all,
            // and the visuals are the only thing the player can stand on or bump into.
            // Inside the zone the support switches to the proxy bodies (VisualCovered),
            // except for the capsule, whose exact colliders are always the support.
            SetPartColliders(false);

            // The mirror filter starts fresh with the flight: the smoothed pose must
            // not be inherited from the build-mode poses.
            mirrorPos = null;
            mirrorFrames = 0;

            Vector3 bottom = BuildMath.VesselBottom(parts);
            Zone.SiteToAstroAt(bottom, t, out Galilego.Core.Vector3d pos, out Galilego.Core.Vector3d vel);
            Runner.SetShipState(pos, vel, Galilego.Events.VesselRegime.Flying);

            PhysicsOn = true;
            Runner.BuildMode = false;
            if (ZoneHost != null)
            {
                ZoneHost.Enabled = true;
            }

            HideGhost();
            Debug.Log(string.Format(
                "[Build] физика включена: деталей {0}, масса {1:F0} кг", defs.Length, Runner.Ship.Mass));
        }

        /// <summary>
        /// Mass of parts outside the main assembly (not part of any stage core):
        /// present in Ship.Mass, absent from the stage model, so the invariant
        /// "cores + detached == Ship.Mass" holds even with orphans.
        /// </summary>
        private static double DetachedMassKg(
            System.Collections.Generic.List<PlacedPart> parts,
            System.Collections.Generic.List<StageCore> cores)
        {
            double sum = 0d;
            for (int i = 0; i < parts.Count; i++)
            {
                bool inCore = false;
                for (int c = 0; c < cores.Count && !inCore; c++)
                {
                    System.Collections.Generic.List<int> indices = cores[c].PartIndices;
                    for (int k = 0; k < indices.Count; k++)
                    {
                        if (indices[k] == i)
                        {
                            inCore = true;
                            break;
                        }
                    }
                }

                if (!inCore)
                {
                    sum += parts[i].Metrics.MassKg;
                }
            }

            return sum;
        }

        /// <summary>
        /// Welding before launch: free parts (ParentIndex −1) hanging within
        /// 2 m above a free top of another part with an almost matching center
        /// are pulled flush and linked, so a rocket built with gaps becomes
        /// a single body.
        /// </summary>
        private void WeldLoosePartsAtLaunch()
        {
            bool welded = false;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.ParentIndex >= 0)
                {
                    continue;
                }

                int best = -1;
                float bestGap = float.MaxValue;
                bool bestOnTop = true;
                for (int j = 0; j < parts.Count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    PlacedPart q = parts[j];
                    float dx = p.Pivot.x - q.Pivot.x;
                    float dz = p.Pivot.z - q.Pivot.z;
                    float reach = Mathf.Max(0.1f, Mathf.Min(p.Metrics.Radius, q.Metrics.Radius));
                    if ((dx * dx) + (dz * dz) > reach * reach)
                    {
                        continue;
                    }

                    if (!q.TopOccupied)
                    {
                        float gap = (p.Pivot.y + p.Metrics.Bottom) - (q.Pivot.y + q.Metrics.Top);
                        if (gap >= -0.25f && gap <= 2f && gap < bestGap)
                        {
                            bestGap = gap;
                            best = j;
                            bestOnTop = true;
                        }
                    }

                    if (!q.BottomOccupied)
                    {
                        float gap = (q.Pivot.y + q.Metrics.Bottom) - (p.Pivot.y + p.Metrics.Top);
                        if (gap >= -0.25f && gap <= 2f && gap < bestGap)
                        {
                            bestGap = gap;
                            best = j;
                            bestOnTop = false;
                        }
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                PlacedPart parent = parts[best];
                p.Pivot = bestOnTop
                    ? new Vector3(parent.Pivot.x, parent.Pivot.y + parent.Metrics.Top - p.Metrics.Bottom, parent.Pivot.z)
                    : new Vector3(parent.Pivot.x, parent.Pivot.y + parent.Metrics.Bottom - p.Metrics.Top, parent.Pivot.z);
                p.ParentIndex = best;
                p.ParentOnTop = bestOnTop;
                if (bestOnTop)
                {
                    p.BottomOccupied = true;
                    parent.TopOccupied = true;
                }
                else
                {
                    p.TopOccupied = true;
                    parent.BottomOccupied = true;
                }

                parts[i] = p;
                parts[best] = parent;
                welded = true;
            }

            if (welded)
            {
                RefreshVisualLocals();
                Debug.Log("[Build] детали с зазорами притянуты и связаны перед полётом");
            }
        }

        private void ReturnToBuild()
        {
            PhysicsOn = false;
            Runner.BuildMode = true;
            if (ZoneHost != null)
            {
                ZoneHost.ForceExit();
                ZoneHost.Enabled = false;
            }

            // Remove debris if any appeared (hard landing of the analytic physics):
            // the rollback must return the world exactly to the moment flight started.
            if (Runner.Debris != null)
            {
                Runner.Debris.Clear();
            }

            // Staged propulsion is removed: build mode uses the legacy path.
            Galilego.Flight.FlightRuntime.End();
            Runner.ClearVesselPropulsion();
            Runner.Ship.Attitude = Galilego.Core.QuaternionD.Identity;
            Runner.Ship.AngularVelocity = Galilego.Core.Vector3d.Zero;
            Runner.Ship.PositionIsCom = false;

            SetPartColliders(true);

            RefreshVisualLocals();
            Debug.Log("[Build] возврат к стройке: состояние восстановлено");
        }

        // ─── Stages in flight ────────────────────────────────────────────────

        /// <summary>
        /// Space in the ship (KSP-style): ignite the stage, then jettison the
        /// lower attached stage and ignite the next one. Only when the player is
        /// in the ship — on foot, Space is jump and jetpack.
        /// </summary>
        private void UpdateStaging()
        {
            if (Runner == null || Runner.Paused
                || Runner.PlayerMode != PlayerMode.InShip
                || ShipTablet.AnyExpanded
                || !PlayerInput.Down(GameAction.Stage))
            {
                return;
            }

            StageJettison();
        }

        /// <summary>
        /// One "stage" command: ignite or jettison+ignite. Returns true if
        /// anything happened. The jettisoned stage mass is captured BEFORE
        /// Stage() (afterwards it can no longer be subtracted correctly);
        /// StageSeparation itself changes Ship.Mass and impulse.
        /// </summary>
        public bool StageJettison()
        {
            StagePropulsion propulsion = Runner != null ? Runner.Propulsion : null;
            if (propulsion == null)
            {
                return false;
            }

            bool inZone = ContactZoneHost.Active;
            int first = propulsion.FirstAttached;
            if (propulsion.ActiveStageIndex < 0)
            {
                if (propulsion.Stage() == StagingResult.Ignited)
                {
                    Debug.Log(string.Format("[Stage] зажигание ступени {0}", first + 1));
                    return true;
                }

                return false;
            }

            // The last stage cannot be jettisoned (no separator above it).
            if (first + 1 >= propulsion.StageCount)
            {
                return false;
            }

            double stageMass = propulsion.StageTotalKg(first);
            if (propulsion.Stage() != StagingResult.JettisonedAndIgnited)
            {
                return false;
            }

            if (inZone)
            {
                // Contact zone: joints are cut in PhysX (the jettisoned stage bodies become debris),
                // the flight model then tracks mass and inertia from the remaining parts.
                Galilego.Flight.FlightRuntime.Instance.OnStaged();
                Debug.Log(string.Format(
                    "[Stage] ступень {0} отделена на земле: {1:F0} кг; зажжена ступень {2}",
                    first + 1, stageMass, propulsion.ActiveStageIndex + 1));
                return true;
            }

            // Separation impulse — 2 m/s backward along the motion (the stage
            // decelerates relative to the ship). StageSeparation splits the pair
            // impulse and subtracts the stage mass from Ship.Mass.
            Galilego.Core.Vector3d prograde = Runner.Ship.Velocity.SqrMagnitude > 1e-6d
                ? Runner.Ship.Velocity.Normalized
                : new Galilego.Core.Vector3d(0d, 0d, 1d);
            Galilego.Events.ManeuverEvaluator.StageSeparation(
                Runner.Ship, stageMass, prograde * 2d, Runner.Debris,
                Runner.TimeSeconds, propulsion.Cores[first + 1].DryMassKg,
                Runner.DebrisLifetimeSeconds);

            // PartDefinition of the remainder — for landing (damage normalization)
            // and the contact zone: dropped stage parts by the cores' PartIndices.
            if (Galilego.Flight.FlightRuntime.Instance != null && Runner.Flight != null)
            {
                Galilego.Flight.FlightRuntime.Instance.OnStaged(); // RemainingDefs from the model (with LAS/RCS and current masses)
            }
            else
            {
                Runner.SetVesselParts(RemainingDefs(parts, propulsion));
            }

            Debug.Log(string.Format(
                "[Stage] ступень {0} отделена: {1:F0} кг; зажжена ступень {2}",
                first + 1, stageMass, propulsion.ActiveStageIndex + 1));
            return true;
        }

        /// <summary>
        /// PartDefinition[] without the jettisoned stages: which parts leave comes
        /// from the cores' PartIndices (draft indices), ParentIndex is remapped to
        /// the new numbering; a parent from a dropped stage becomes a root.
        /// </summary>
        private static PartDefinition[] RemainingDefs(
            System.Collections.Generic.List<PlacedPart> parts, StagePropulsion propulsion)
        {
            var dropped = new System.Collections.Generic.HashSet<int>();
            for (int k = 0; k < propulsion.StageCount; k++)
            {
                if (!propulsion.IsJettisoned(k))
                {
                    continue;
                }

                System.Collections.Generic.List<int> indices = propulsion.Cores[k].PartIndices;
                for (int i = 0; i < indices.Count; i++)
                {
                    dropped.Add(indices[i]);
                }
            }

            var map = new int[parts.Count];
            var remaining = new System.Collections.Generic.List<PlacedPart>(parts.Count);
            for (int i = 0; i < parts.Count; i++)
            {
                if (dropped.Contains(i))
                {
                    map[i] = -1;
                    continue;
                }

                map[i] = remaining.Count;
                remaining.Add(parts[i]);
            }

            PartDefinition[] defs = BuildMath.ToPartDefinitions(remaining);
            if (defs == null)
            {
                return null;
            }

            for (int i = 0; i < defs.Length; i++)
            {
                int parent = defs[i].ParentIndex;
                defs[i].ParentIndex = parent >= 0 && parent < map.Length ? map[parent] : -1;
            }

            return defs;
        }

        // ─── Parts ──────────────────────────────────────────────────────────

        /// <summary>Engine templates (prefabs, bounds, specs) in EnginePrefabs order.</summary>
        public System.Collections.Generic.IReadOnlyList<PartCatalog.EngineTemplate> Engines => engines;

        /// <summary>Default engine (the first model; for new slots).</summary>
        public PartCatalog.EngineTemplate DefaultEngine => engines.Count > 0 ? engines[0] : null;

        /// <summary>Engine template of a preset (by EngineId; unknown — default engine).</summary>
        public PartCatalog.EngineTemplate EngineFor(PartPreset preset)
        {
            return EngineFor(preset.EngineId);
        }

        /// <summary>Engine template by Id from EngineSpecs (null — default).</summary>
        public PartCatalog.EngineTemplate EngineFor(string engineId)
        {
            if (engines.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(engineId))
            {
                for (int i = 0; i < engines.Count; i++)
                {
                    if (engines[i].Spec != null && engines[i].Spec.Id == engineId)
                    {
                        return engines[i];
                    }
                }
            }

            return engines[0];
        }

        /// <summary>
        /// Initial preset for a part kind (the panel builds slot presets from it;
        /// the debug stack builds from it directly).
        /// </summary>
        public PartPreset DefaultPreset(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                    return new PartPreset
                    {
                        Kind = kind,
                        Form = TankForm,
                        Radius = TankRadius,
                        TopRadius = TankTopRadius,
                        Height = TankHeight,
                        Load = TankLoad,
                    };
                case PartKind.Separator:
                    return new PartPreset { Kind = kind, Radius = SeparatorRadius };
                case PartKind.Capsule:
                    // Capsule sizes are fixed by the model — the preset needs nothing.
                    return new PartPreset { Kind = kind };
                case PartKind.RcsBlock:
                    // Fixed model; the ring size is the only preset choice.
                    return new PartPreset { Kind = kind, Count = RcsBlockPlacement.DefaultCount };
                default:
                    PartCatalog.EngineTemplate engine = DefaultEngine;
                    return new PartPreset
                    {
                        Kind = kind,
                        EngineId = engine != null && engine.Spec != null ? engine.Spec.Id : null,
                    };
            }
        }

        /// <summary>Slot preset metrics: the form and sizes of this very slot.</summary>
        public PartMetrics MetricsFor(PartPreset preset)
        {
            if (preset.Kind == PartKind.FuelTank)
            {
                return PartCatalog.Measure(
                    preset.Kind, preset.Form, preset.Radius, preset.TopRadius, preset.Height,
                    preset.Load, EngineFor(preset));
            }

            if (preset.Kind == PartKind.Engine || preset.Kind == PartKind.Capsule
                || preset.Kind == PartKind.RcsBlock)
            {
                // Sizes come from the model template; preset fields are irrelevant.
                return PartCatalog.Measure(preset.Kind, 0f, 0f, EngineFor(preset));
            }

            return PartCatalog.Measure(preset.Kind, preset.Radius, 0f, DefaultEngine);
        }

        /// <summary>Metrics of a part kind with initial sizes (debug stack).</summary>
        public PartMetrics MetricsFor(PartKind kind)
        {
            return MetricsFor(DefaultPreset(kind));
        }

        /// <summary>
        /// Place a part into the draft (visual + record). parentIndex −1 — root.
        /// engineTemplate — the part's engine model (for PartKind.Engine; null — default).
        /// rotation — part frame in the ship frame (radial parts); radial — mounted on
        /// a host's side (no stack node); group — parts placed together (RCS ring).
        /// </summary>
        public PlacedPart AddPart(
            PartKind kind, PartMetrics metrics, Vector3 pivot, int parentIndex, bool parentOnTop,
            PartCatalog.EngineTemplate engineTemplate = null,
            Quaternion rotation = default, bool radial = false, int group = 0)
        {
            PartCatalog.EngineTemplate visualEngine = kind == PartKind.Engine
                ? (engineTemplate != null ? engineTemplate : DefaultEngine)
                : null;

            if (rotation.w == 0f && rotation.x == 0f && rotation.y == 0f && rotation.z == 0f)
            {
                rotation = Quaternion.identity;
            }

            var part = new PlacedPart
            {
                Kind = kind,
                Form = kind == PartKind.FuelTank ? TankForm : TankForm.Cylinder,
                SizeA = metrics.Radius,
                SizeB = metrics.Height,
                SizeTop = metrics.TopRadius,
                Metrics = metrics,
                Pivot = pivot,
                ParentIndex = parentIndex,
                ParentOnTop = parentOnTop,
                Rotation = rotation,
                Radial = radial,
                Group = group,
                PhysicsCenterLocal = radial
                    ? new Vector3(0f, 0f, RcsBlockPlacement.BodyCenterOutward)
                    : Vector3.zero,
                EngineSpec = visualEngine != null ? visualEngine.Spec : null,
            };

            part.Visual = PartCatalog.CreateVisual(
                kind, metrics, visualEngine, TankSideMaterial, TankCapMaterial, SeparatorMaterial,
                ShipTransform);

            parts.Add(part);

            if (parentIndex >= 0 && parentIndex < parts.Count - 1)
            {
                PlacedPart parent = parts[parentIndex];
                if (radial)
                {
                    // A side-mounted part is no stack node: nothing snaps to it and
                    // it does not take the host's top/bottom node.
                    part.TopOccupied = true;
                    part.BottomOccupied = true;
                }
                else if (parentOnTop)
                {
                    parent.TopOccupied = true;
                    part.BottomOccupied = true;
                }
                else
                {
                    parent.BottomOccupied = true;
                    part.TopOccupied = true;
                }
            }

            RefreshVisualLocals();
            return part;
        }

        /// <summary>Remove the last placed part (Ctrl+Z); an RCS ring goes as a whole.</summary>
        public void RemoveLast()
        {
            if (parts.Count == 0)
            {
                return;
            }

            int group = parts[parts.Count - 1].Group;
            if (group != 0)
            {
                while (parts.Count > 0 && parts[parts.Count - 1].Group == group)
                {
                    RemoveAt(parts.Count - 1);
                }

                return;
            }

            RemoveAt(parts.Count - 1);
        }

        /// <summary>
        /// Remove a part by index (Alt+RMB on the aim). Children of the removed
        /// part stay in place and become roots (the snap to the removed parent is
        /// lost — in build mode it is simply a free-standing part); parent indices
        /// are shifted.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (index < 0 || index >= parts.Count)
            {
                return;
            }

            PlacedPart part = parts[index];
            for (int i = 0; i < parts.Count; i++)
            {
                if (i == index || parts[i].ParentIndex != index)
                {
                    continue;
                }

                // Child of the removed part: unbind its node from the parent.
                PlacedPart child = parts[i];
                if (child.ParentOnTop)
                {
                    child.BottomOccupied = false;
                }
                else
                {
                    child.TopOccupied = false;
                }

                child.ParentIndex = -1;
                child.ParentOnTop = false;
                parts[i] = child;
            }

            if (part.ParentIndex >= 0 && part.ParentIndex < parts.Count)
            {
                PlacedPart parent = parts[part.ParentIndex];
                if (part.BottomOccupied)
                {
                    parent.TopOccupied = false;
                }

                if (part.TopOccupied)
                {
                    parent.BottomOccupied = false;
                }
            }

            if (part.Visual != null)
            {
                Destroy(part.Visual.Root);
            }

            parts.RemoveAt(index);
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].ParentIndex > index)
                {
                    parts[i].ParentIndex--;
                }
            }

            ClearDeleteHighlight();
            RefreshVisualLocals();
        }

        /// <summary>Remove all draft parts (developer button in the inventory).</summary>
        public void ClearAll()
        {
            ClearDeleteHighlight();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Visual != null)
                {
                    Destroy(parts[i].Visual.Root);
                }
            }

            parts.Clear();
        }

        /// <summary>
        /// Raise/lower the whole draft along the site zenith (m) — a debug lift
        /// for testing physics fall. The player does this by placing parts in the air.
        /// </summary>
        public void ShiftVessel(float upMeters)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                p.Pivot = p.Pivot + new Vector3(0f, upMeters, 0f);
            }

            RefreshVisualLocals();
        }

        /// <summary>
        /// Build mode (enabled = true): visual colliders take part in everything. Flight
        /// (enabled = false): they are excluded from PhysX CONTACTS only (excludeLayers is a
        /// contact filter, see CheckCapsuleQueries) so they do not fight the proxy bodies, but
        /// they are never switched off: outside the contact zone no proxy bodies exist, and
        /// switching them off left the tanks and engines without any collision for the player.
        /// The Collider.enabled flag is not touched: the model manages its own (the open
        /// Orion door is disabled on purpose and must stay so).
        /// </summary>
        private void SetPartColliders(bool enabled)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                if (part.Visual == null || part.Visual.Root == null)
                {
                    continue;
                }

                // All part colliders, including the MODEL's own ones (the Orion
                // capsule has dozens: shell, cabin floor, open door) — proxy bodies
                // take their place in flight.
                Collider[] colliders = part.Visual.Root.GetComponentsInChildren<Collider>(true);
                for (int c = 0; c < colliders.Length; c++)
                {
                    colliders[c].excludeLayers = enabled ? 0 : ~0;
                }

                // Inside the zone a proxy body represents every part except the capsule.
                VesselPartCollider marker = part.Visual.Root.GetComponentInParent<VesselPartCollider>();
                if (marker != null)
                {
                    marker.VisualCovered = !enabled && part.Kind != PartKind.Capsule;
                }
            }
        }

        /// <summary>Local visual poses: pivot minus the assembly bottom (site axes).</summary>
        private void RefreshVisualLocals()
        {
            if (ShipTransform == null)
            {
                return;
            }

            Vector3 bottom = parts.Count > 0 ? BuildMath.VesselBottom(parts) : Zone.VesselSpawnLocal;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                if (part.Visual != null)
                {
                    part.Visual.Root.transform.localPosition = part.Pivot - bottom;
                    part.Visual.Root.transform.localRotation = part.Rotation;
                }
            }
        }

        // ─── Ghost and placement ────────────────────────────────────────────

        private void UpdatePlacement()
        {
            if (!HasSelection || UiOpen)
            {
                HideGhost();
                return;
            }

            if (SelectedPreset.Kind == PartKind.RcsBlock)
            {
                UpdateRcsPlacement();
                return;
            }

            PartMetrics metrics = MetricsFor(SelectedPreset);
            PartCatalog.EngineTemplate selectedEngine = SelectedPreset.Kind == PartKind.Engine
                ? EngineFor(SelectedPreset)
                : null;
            EnsureGhost(SelectedPreset.Kind, metrics, selectedEngine);

            Camera camera = Camera.main;
            if (camera == null || ghost == null)
            {
                HideGhost();
                return;
            }

            // Camera ray: in site axes — for node snapping, in render space — for
            // physics rays (colliders live in render space). The aim cone uses the
            // same camera FOV that draws the ring: nodes are caught exactly under
            // the ring, not "somewhere near the ray".
            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            float coneTangent = AimCone.Tangent(camera);

            bool hasSnap = BuildMath.TryFindSnap(parts, metrics, rayOrigin, rayDirection, coneTangent, out BuildMath.SnapHit snap);
            Vector3 pivot;
            bool valid;

            if (hasSnap)
            {
                pivot = snap.Pivot;
                valid = CheckPlacement(pivot, metrics, snap.PartIndex);
            }
            else if (TryRayToSurface(camera, out Vector3 hitPoint, out bool onPad))
            {
                pivot = new Vector3(hitPoint.x, hitPoint.y - metrics.Bottom, hitPoint.z);
                valid = onPad && CheckPlacement(pivot, metrics, -1);

                // Second pass: the point is in free space but maybe a couple of
                // centimetres above a part node — snap edge to edge.
                if (onPad && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near))
                {
                    snap = near;
                    hasSnap = true;
                    pivot = near.Pivot;
                    valid = CheckPlacement(pivot, metrics, near.PartIndex);
                }
            }
            else
            {
                // The ray hit nothing: fallback point in the air in front of the player.
                Vector3 air = rayOrigin + (rayDirection * BuildMath.AirFallbackDistance);
                pivot = new Vector3(air.x, air.y - metrics.Bottom, air.z);
                float padTop = Zone.PadTopYAt(new Vector2(pivot.x, pivot.z));
                bool above = (pivot.y + metrics.Bottom) >= (padTop - 0.05f);
                bool notTooHigh = (pivot.y + metrics.Bottom) - padTop <= MaxBuildHeight;
                valid = above && notTooHigh && CheckPlacement(pivot, metrics, -1);

                if (valid && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near))
                {
                    snap = near;
                    hasSnap = true;
                    pivot = near.Pivot;
                    valid = CheckPlacement(pivot, metrics, near.PartIndex);
                }
            }

            lastPivot = pivot;
            lastSnap = snap;
            lastSnapFound = hasSnap;
            ghostVisible = true;
            ghostValid = valid;

            ghost.Root.SetActive(true);
            ghost.Root.transform.localPosition = pivot;
            ghost.Root.transform.localRotation = Quaternion.identity;
            ghost.SetMaterial(valid ? ghostMaterial : invalidMaterial);
        }

        /// <summary>
        /// RCS placement: the reticle aims at a fuel tank hull; the ghost is a full
        /// ring of SelectedPreset.Count blocks around the aimed height. The blocks
        /// follow the hull normal (cone included) and sink their round base in.
        /// </summary>
        private void UpdateRcsPlacement()
        {
            int count = RcsBlockPlacement.ClampCount(SelectedPreset.Count);
            EnsureRcsGhost(count);

            Camera camera = Camera.main;
            if (camera == null || ghost == null)
            {
                HideGhost();
                return;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);

            // Nearest tank along the reticle centre ray (analytic hull, no colliders).
            int tankIndex = -1;
            float bestT = float.MaxValue;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.FuelTank)
                {
                    continue;
                }

                Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                if (BuildMath.RayFrustum(rayOrigin, rayDirection, baseCenter,
                        p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                    && t >= 0f && t <= BuildMath.SnapReach && t < bestT)
                {
                    bestT = t;
                    tankIndex = i;
                }
            }

            if (tankIndex < 0)
            {
                rcsAimValid = false;
                HideGhost();
                return;
            }

            PlacedPart host = parts[tankIndex];
            Vector3 hit = rayOrigin + (rayDirection * bestT);
            float height = RcsBlockPlacement.ClampHeight(host.Metrics, hit.y - host.Pivot.y);

            // Ring start angle: from the aimed point; aiming at a flat cap, face the player.
            Vector3 offset = new Vector3(hit.x - host.Pivot.x, 0f, hit.z - host.Pivot.z);
            float angle = offset.sqrMagnitude > 1e-4f
                ? Mathf.Atan2(offset.z, offset.x)
                : Mathf.Atan2(-rayDirection.z, -rayDirection.x);

            // Right after placing, keep the ghost hidden while the aim stays put.
            if (rcsPlacedAim && tankIndex == rcsPlacedTank && count == rcsPlacedCount
                && Mathf.Abs(height - rcsPlacedHeight) < 0.12f
                && Mathf.Abs(Mathf.DeltaAngle(angle * Mathf.Rad2Deg, rcsPlacedAngle * Mathf.Rad2Deg)) < 4f)
            {
                rcsAimValid = false;
                HideGhost();
                return;
            }

            rcsPlacedAim = false;

            // A new block must not sit on an already mounted one (rings cannot stack).
            bool blocked = false;
            float step = (Mathf.PI * 2f) / count;
            for (int i = 0; i < count && !blocked; i++)
            {
                RcsBlockPlacement.Pose(host.Metrics, host.Pivot, height, angle + (step * i),
                    out Vector3 blockPivot, out _);
                for (int j = 0; j < parts.Count; j++)
                {
                    PlacedPart other = parts[j];
                    if (other.Kind != PartKind.RcsBlock)
                    {
                        continue;
                    }

                    if ((other.Pivot - blockPivot).sqrMagnitude < 0.9f * 0.9f)
                    {
                        blocked = true;
                        break;
                    }
                }
            }

            rcsAimValid = !blocked;
            rcsAimTank = tankIndex;
            rcsAimHeight = height;
            rcsAimAngle = angle;

            ghostVisible = true;
            ghostValid = !blocked;
            ghost.Root.SetActive(true);
            ghost.Root.transform.localPosition = host.Pivot;
            ghost.Root.transform.localRotation = Quaternion.identity;
            UpdateRcsGhostBlocks(host, count);
            ghost.SetMaterial(blocked ? invalidMaterial : ghostMaterial);
        }

        /// <summary>Rebuild the RCS ghost when the ring size changes (one block visual per slot).</summary>
        private void EnsureRcsGhost(int count)
        {
            if (ghost != null && ghostKind == PartKind.RcsBlock && ghostRcsCount == count)
            {
                return;
            }

            if (ghost != null)
            {
                Destroy(ghost.Root);
            }

            var ring = new GameObject("RCS Ring Ghost");
            ring.transform.SetParent(Zone.transform, false);

            PartMetrics metrics = MetricsFor(DefaultPreset(PartKind.RcsBlock));
            var blocks = new Transform[count];
            for (int i = 0; i < count; i++)
            {
                PartCatalog.PartVisual block = PartCatalog.CreateVisual(
                    PartKind.RcsBlock, metrics, null,
                    TankSideMaterial, TankCapMaterial, SeparatorMaterial, ring.transform,
                    withCollider: false);
                blocks[i] = block.Root.transform;
            }

            ghost = new PartCatalog.PartVisual
            {
                Root = ring,
                Renderers = ring.GetComponentsInChildren<Renderer>(true),
            };
            ghostKind = PartKind.RcsBlock;
            ghostEngineId = null;
            ghostRadius = metrics.Radius;
            ghostTopRadius = metrics.TopRadius;
            ghostHeight = metrics.Height;
            ghostRcsCount = count;
            rcsGhostBlocks = blocks;

            for (int i = 0; i < ghost.Renderers.Length; i++)
            {
                if (ghost.Renderers[i] != null)
                {
                    ghost.Renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        /// <summary>Pose the ghost blocks around the host (site axes, ghost root at the host pivot).</summary>
        private void UpdateRcsGhostBlocks(PlacedPart host, int count)
        {
            if (rcsGhostBlocks == null || rcsGhostBlocks.Length != count)
            {
                return;
            }

            float step = (Mathf.PI * 2f) / count;
            for (int i = 0; i < count; i++)
            {
                RcsBlockPlacement.Pose(
                    host.Metrics, host.Pivot, rcsAimHeight, rcsAimAngle + (step * i),
                    out Vector3 pivot, out Quaternion rotation);
                Transform block = rcsGhostBlocks[i];
                if (block == null)
                {
                    continue;
                }

                block.localPosition = pivot - host.Pivot;
                block.localRotation = rotation;
            }
        }

        /// <summary>Place the aimed RCS ring as one group of radial parts.</summary>
        private void PlaceRcsRing()
        {
            if (!rcsAimValid || rcsAimTank < 0 || rcsAimTank >= parts.Count)
            {
                return;
            }

            int count = RcsBlockPlacement.ClampCount(SelectedPreset.Count);
            PlacedPart host = parts[rcsAimTank];
            PartMetrics metrics = MetricsFor(DefaultPreset(PartKind.RcsBlock));
            int group = nextPartGroup++;
            float step = (Mathf.PI * 2f) / count;

            for (int i = 0; i < count; i++)
            {
                RcsBlockPlacement.Pose(
                    host.Metrics, host.Pivot, rcsAimHeight, rcsAimAngle + (step * i),
                    out Vector3 pivot, out Quaternion rotation);
                AddPart(PartKind.RcsBlock, metrics, pivot, rcsAimTank, false, null,
                    rotation, radial: true, group: group);
            }

            // Remember where the ring went: the ghost stays hidden until the aim moves.
            rcsPlacedAim = true;
            rcsPlacedTank = rcsAimTank;
            rcsPlacedCount = count;
            rcsPlacedHeight = rcsAimHeight;
            rcsPlacedAngle = rcsAimAngle;

            Debug.Log("[Build] РСУ: " + count + " блок(ов) на баке " + host.Kind + ", всего " + parts.Count);
        }

        /// <summary>
        /// Placement validity: the footprint fully on the pad, no intersections,
        /// within reach (pivot not above the ceiling over the slab).
        /// </summary>
        private bool CheckPlacement(Vector3 pivot, PartMetrics metrics, int ignoreIndex)
        {
            if (!BuildMath.FootprintInside(Zone.Polygon, new Vector2(pivot.x, pivot.z), metrics.Radius))
            {
                return false;
            }

            float padTop = Zone.PadTopYAt(new Vector2(pivot.x, pivot.z));
            float bottom = pivot.y + metrics.Bottom;
            if (bottom < padTop - 0.05f || bottom - padTop > MaxBuildHeight)
            {
                return false;
            }

            if (OverlapsSiteStructure(pivot, metrics))
            {
                return false;
            }

            return !BuildMath.OverlapsAny(parts, pivot, metrics, ignoreIndex);
        }

        /// <summary>
        /// Whether the trial part intersects SITE STRUCTURES (desk, trusses,
        /// parapets). The test sphere is slightly narrower than the part, pinched
        /// away from its ends and capped at half its height (so wide flat parts
        /// cannot reach below their own bottom).
        /// </summary>
        private bool OverlapsSiteStructure(Vector3 pivot, PartMetrics metrics)
        {
            if (metrics.Height < 0.5f || metrics.Radius <= 0.01f)
            {
                return false; // thin parts (separator) are not checked
            }

            float radius = (metrics.Radius * 0.92f) - 0.05f;
            radius = Mathf.Min(radius, (metrics.Height * 0.5f) - 0.10f);
            if (radius <= 0.02f)
            {
                return false;
            }

            Vector3 centerLocal = pivot + new Vector3(0f, metrics.Center, 0f);
            float inner = Mathf.Max(0f, (metrics.Height * 0.5f) - radius);
            Vector3 lower = Zone.SiteToRender(centerLocal - new Vector3(0f, inner - 0.06f, 0f));
            Vector3 upper = Zone.SiteToRender(centerLocal + new Vector3(0f, inner - 0.06f, 0f));

            Collider[] hits = Physics.OverlapCapsule(
                lower, upper, radius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                if (LaunchPadZone.IsSiteCollider(hits[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Camera ray against colliders: the closest hit. onPad — whether it hit
        /// the TOP face of a SITE collider (slab or launch desk — only those allow
        /// placement).
        /// </summary>
        private bool TryRayToSurface(Camera camera, out Vector3 hitPoint, out bool onPad)
        {
            hitPoint = default;
            onPad = false;

            RaycastHit[] hits = Physics.RaycastAll(
                new Ray(camera.transform.position, camera.transform.forward),
                BuildMath.SnapReach + 10f,
                ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0)
            {
                return false;
            }

            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hits.Length; i++)
            {
                // Vessel part colliders do not block the placement ray: node snapping
                // is analytic anyway, and the ray targets the site slab/desk.
                if (hits[i].collider.GetComponentInParent<VesselPartCollider>() != null)
                {
                    continue;
                }

                if (hits[i].distance < bestDistance)
                {
                    bestDistance = hits[i].distance;
                    best = i;
                }
            }

            if (best < 0)
            {
                // All hits are vessel part colliders: no site surface.
                return false;
            }

            RaycastHit hit = hits[best];
            hitPoint = Zone.RenderToSite(hit.point);

            // Only the top face of a site surface: sides/bottom do not accept placement.
            Vector3 normalLocal = Zone.transform.InverseTransformDirection(hit.normal);
            onPad = LaunchPadZone.IsSiteCollider(hit.collider) && normalLocal.y > 0.7f;
            return true;
        }

        private void PlaceGhostedPart()
        {
            if (SelectedPreset.Kind == PartKind.RcsBlock)
            {
                PlaceRcsRing();
                return;
            }

            PartCatalog.EngineTemplate selectedEngine = SelectedPreset.Kind == PartKind.Engine
                ? EngineFor(SelectedPreset)
                : null;
            PlacedPart part = AddPart(
                SelectedPreset.Kind,
                MetricsFor(SelectedPreset),
                lastPivot,
                lastSnapFound ? lastSnap.PartIndex : -1,
                lastSnapFound && lastSnap.OnTop,
                selectedEngine);
            if (part != null)
            {
                Debug.Log("[Build] поставлена деталь " + part.Kind + ", всего " + parts.Count);
            }
        }

        private void EnsureGhost(
            PartKind kind, PartMetrics metrics, PartCatalog.EngineTemplate engineTemplate)
        {
            string engineId = engineTemplate != null && engineTemplate.Spec != null
                ? engineTemplate.Spec.Id
                : null;
            bool same = ghost != null
                && ghostKind == kind
                && ghostEngineId == engineId
                && Mathf.Abs(ghostRadius - metrics.Radius) < 1e-4f
                && Mathf.Abs(ghostTopRadius - metrics.TopRadius) < 1e-4f
                && Mathf.Abs(ghostHeight - metrics.Height) < 1e-4f;
            if (same)
            {
                return;
            }

            if (ghost != null)
            {
                Destroy(ghost.Root);
            }

            ghost = PartCatalog.CreateVisual(
                kind, metrics, engineTemplate,
                TankSideMaterial, TankCapMaterial, SeparatorMaterial, Zone.transform,
                withCollider: false);
            ghostKind = kind;
            ghostEngineId = engineId;
            ghostRadius = metrics.Radius;
            ghostTopRadius = metrics.TopRadius;
            ghostHeight = metrics.Height;
            for (int i = 0; i < ghost.Renderers.Length; i++)
            {
                if (ghost.Renderers[i] != null)
                {
                    ghost.Renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
        }

        private void HideGhost()
        {
            ghostVisible = false;
            if (ghost != null && ghost.Root != null)
            {
                ghost.Root.SetActive(false);
            }
        }

        // ─── Debug stack ─────────────────────────────────────────────────

        /// <summary>Spike: three hard-coded parts on the pad (contact zone test).</summary>
        private void BuildSpikeStack()
        {
            PartPreset enginePreset = DefaultPreset(PartKind.Engine);
            PartMetrics engineMetrics = MetricsFor(enginePreset);
            Vector3 basePoint = Zone.VesselSpawnLocal;
            AddPart(PartKind.Engine, engineMetrics, basePoint, -1, false, EngineFor(enginePreset));

            PartMetrics tankMetrics = MetricsFor(PartKind.FuelTank);
            Vector3 tankPivot = new Vector3(basePoint.x, basePoint.y + engineMetrics.Top, basePoint.z);
            AddPart(PartKind.FuelTank, tankMetrics, tankPivot, 0, true);

            PartMetrics separatorMetrics = MetricsFor(PartKind.Separator);
            Vector3 separatorPivot = new Vector3(
                basePoint.x, tankPivot.y + tankMetrics.Height - separatorMetrics.Bottom, basePoint.z);
            AddPart(PartKind.Separator, separatorMetrics, separatorPivot, 1, true);

            Debug.Log("[Build] отладочный стек: двигатель + бак + отделитель");
        }

        // ─── Materials ───────────────────────────────────────────────────────

        /// <summary>
        /// Ghost material: OPAQUE single-color with emissive. Runtime
        /// semi-transparent HDRP materials do not render their color in this
        /// project, so the ghost is a solid "projection" whose silhouette shows
        /// the placement spot.
        /// </summary>
        private static Material CreateOverlayMaterial(Color color, string name)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                shader = Shader.Find("HDRP/Unlit");
            }

            if (shader == null)
            {
                Debug.LogError("[Build] HDRP-шейдер для призрака не найден.");
                return null;
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", new Color(color.r, color.g, color.b, 1f));
            material.SetColor("_EmissiveColor", new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.55f));
            return material;
        }
    }
}
