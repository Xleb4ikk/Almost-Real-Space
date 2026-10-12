using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// РЕЖИМ СТРОИТЕЛЬСТВА аппарата (ветка «система строительства»).
    ///
    /// Два состояния:
    ///   Стройка (по умолчанию): аппарат статичен — стоит там, где его собрали,
    ///     физика корабля не шагается (SimulationRunner.BuildMode), позиция
    ///     каждый кадр пинится к осям стартовой площадки (планета вращается —
    ///     аппарат едет вместе с плитой).
    ///   Полёт: на удержании R (1 с) черновик превращается в PartDefinition[],
    ///     массы/позиция/скорость передаются в симуляцию, включается зона
    ///     контакта (точная PhysX-физика у земли). Ещё удержание R — аппарат
    ///     и мир возвращаются ровно к состоянию стройки (снимок = сам черновик;
    ///     тела зоны и обломки чистятся).
    ///
    /// Размещение: призрак детали ходит по лучу камеры, магнитится к свободным
    /// узлам поставленных деталей (одна деталь на узел), проверки — пятно
    /// целиком на площадке, без пересечений с другими деталями; за пределами
    /// площадки постановка запрещена. Призрак всегда синий (полупрозрачный),
    /// невалидный — красный; естественный раскрас появляется у поставленной
    /// детали.
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
        [Tooltip("Модель двигателя (RS-25 FBX).")]
        public GameObject EnginePrefab;

        [Tooltip("Материал боковины бака.")]
        public Material TankSideMaterial;

        [Tooltip("Материал крышек бака.")]
        public Material TankCapMaterial;

        [Tooltip("Материал отделителя.")]
        public Material SeparatorMaterial;

        [Header("Отладка")]
        [Tooltip("Собрать на старте отладочный стек двигатель+бак+отделитель (спайк).")]
        public bool DebugSpikeStack;

        /// <summary>Длительность удержания R для переключения физики, с.</summary>
        public const float PhysicsHoldSeconds = 1f;

        /// <summary>Максимальная высота постановки над плитой (полёт в воздухе), м.</summary>
        public const float MaxBuildHeight = 30f;

        // ─── Черновик ────────────────────────────────────────────────────────

        private readonly System.Collections.Generic.List<PlacedPart> parts
            = new System.Collections.Generic.List<PlacedPart>();

        /// <summary>Детали черновика (для UI и отладки).</summary>
        public System.Collections.Generic.IReadOnlyList<PlacedPart> Parts => parts;

        /// <summary>Физика включена (полёт) — R переключил режим.</summary>
        public bool PhysicsOn { get; private set; }

        /// <summary>Прогресс удержания R (0..1) — для индикатора.</summary>
        public float RHoldProgress { get; private set; }

        /// <summary>Окно инвентаря открыто: размещение и R не работают (ставит BuildUI).</summary>
        public bool UiOpen { get; set; }

        /// <summary>Есть выбранная деталь для постановки (слот хотбара, ставит BuildUI).</summary>
        public bool HasSelection { get; set; }

        public PartKind SelectedKind { get; set; } = PartKind.FuelTank;

        // Размеры деталей, задаваемые игроком (UI правит, ставит через PartCatalog.ClampSizes)
        public float TankRadius = PartCatalog.TankDefaultRadius;
        public float TankHeight = PartCatalog.TankDefaultHeight;
        public float SeparatorRadius = PartCatalog.SeparatorDefaultRadius;

        private PartCatalog.EngineTemplate engine;
        private Material ghostMaterial;
        private Material invalidMaterial;
        private Material deleteMaterial;
        private PartCatalog.PartVisual ghost;
        private PartKind ghostKind;
        private float ghostRadius;
        private float ghostHeight;
        private int deleteTarget = -1;
        private PartCatalog.PartVisual deleteHighlighted;

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

            if (Zone == null)
            {
                Zone = FindAnyObjectByType<LaunchPadZone>();
            }

            if (ZoneHost == null)
            {
                ZoneHost = FindAnyObjectByType<ContactZoneHost>();
            }

            engine = PartCatalog.EngineTemplate.Measure(EnginePrefab);
            ghostMaterial = CreateOverlayMaterial(new Color(0.25f, 0.55f, 1f, 0.38f), "GhostBlue");
            invalidMaterial = CreateOverlayMaterial(new Color(1f, 0.25f, 0.2f, 0.38f), "GhostRed");
            deleteMaterial = CreateOverlayMaterial(new Color(1f, 0.15f, 0.12f, 1f), "DeleteRed");

            if (Zone != null && Runner != null)
            {
                // Стройка — состояние по умолчанию: шаг физики корабля не считается,
                // пока игрок не запустит полёт (R). В сцене без зоны режим не включаем.
                Runner.BuildMode = true;
                if (ZoneHost != null)
                {
                    ZoneHost.Enabled = false;
                }

                // Пиннинг и запуск полёта живут в кадре раннера: только там время
                // кадра уже продвинуто и совпадает с временем игрока/якоря.
                frameHook = OnBuildModeFrame;
                Runner.BuildModeFrameHook = frameHook;
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
                return;
            }

            // Alt: режим удаления — цель под прицелом краснеет, ПКМ удаляет;
            // призрак постановки при этом спрятан.
            if (!UiOpen && PlayerInput.AltHeld())
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
                if (PlayerInput.AltHeld())
                {
                    if (deleteTarget >= 0 && PlayerInput.MouseRightDown())
                    {
                        RemoveAt(deleteTarget);
                    }
                }
                else if (ghostVisible && ghostValid && PlayerInput.MouseRightDown())
                {
                    PlaceGhostedPart();
                }

                if (PlayerInput.Held(GameKey.LeftControl) && PlayerInput.Down(GameKey.Z))
                {
                    RemoveLast();
                }
            }
        }

        // ─── Удаление по прицелу (Alt) ───────────────────────────────────────

        /// <summary>
        /// Подсветить красным деталь под прицелом (Alt зажат). Цель ищется
        /// лучом по цилиндрам деталей — коллайдеры визуалам не нужны.
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
            int found = -1;
            if (BuildMath.TryRaycastParts(parts, rayOrigin, rayDirection, BuildMath.SnapReach, out int index, out _))
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

        /// <summary>Снять красную подсветку цели удаления.</summary>
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
        /// Зеркалирование визуала на тела зоны контакта: в полёте позы деталей
        /// берём прямо у PhysX-тел прокси (позиция+поворот — видно падение и
        /// опрокидывание). Вызывается VesselMirror'ом в самом конце кадра
        /// (порядок 900) — ПОСЛЕ того, как зона подвинула корень кадра; иначе
        /// визуал отстаёт от тел на сдвиг якоря и «кидает туда-сюда».
        /// </summary>
        public void MirrorVisualsToBodies()
        {
            if (Runner == null || !PhysicsOn || ZoneHost == null || !ContactZoneHost.Active)
            {
                return;
            }

            ContactZoneProxy proxy = ZoneHost.Proxy;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart part = parts[i];
                if (part.Visual == null || part.Visual.Root == null)
                {
                    continue;
                }

                Rigidbody body = proxy != null ? proxy.BodyAt(i) : null;
                if (body != null)
                {
                    // Тело прокси — ЦЕНТР формы детали (цилиндр-призма центрирован
                    // на теле), а визуал построен от ПИВОТА детали (низ бака и
                    // двигателя, центр отделителя). Опускаем корень визуала на
                    // Metrics.Center вдоль локального «вверх» тела: иначе каждая
                    // деталь висит выше своего коллайдера на половину высоты —
                    // ракета «парит» над площадкой, а между баком и двигателем
                    // появляется фальшивый зазор.
                    Transform bodyTransform = body.transform;
                    Vector3 pivot = bodyTransform.position
                        - (bodyTransform.up * (float)part.Metrics.Center);
                    part.Visual.Root.transform.SetPositionAndRotation(
                        pivot, bodyTransform.rotation);
                }
            }
        }

        // ─── Спавн и пиннинг ─────────────────────────────────────────────────

        /// <summary>
        /// Перенести корабль и игрока на стартовую площадку (один раз).
        /// Перед переносом — защита от «мусорной» точки: локальные координаты
        /// площадки не могут быть в километрах от её начала.
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
        /// Кадр раннера в режиме стройки: пиннинг аппарата (или запрошенный
        /// запуск полёта) на АКТУАЛЬНОМ времени кадра. Позже раннер поставит
        /// якорь рендера — всё согласовано в одном кадре, без отставания.
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

        /// <summary>Держать точку корабля у низа сборки (или у места спавна пустого черновика).</summary>
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

        // ВРЕМЕННАЯ отладка дрейфа (убрать после разбора).
        public long DebugUpdateCount;
        public Vector3 DebugLastPinLocal;
        public Galilego.Core.Vector3d DebugLastPinAstro;

        // ─── R: физика/стройка ───────────────────────────────────────────────

        private void UpdateRHold()
        {
            bool held = !UiOpen && PlayerInput.Held(GameKey.R);
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
        /// Переключить режим: true — физика (полёт), false — стройка.
        /// Включение физики откладывается до кадра раннера (EnterFlightAt):
        /// только там время кадра продвинуто и позиция ляжет ровно на плиту.
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

        private void EnterFlightAt(double t)
        {
            if (parts.Count == 0)
            {
                return;
            }

            // Перед передачей в физику — «сварка» свободно стоящих деталей:
            // деталь, висящая в пределах 2 м над чьим-то узлом и по центру,
            // притягивается вплотную и жёстко связывается. Иначе ракета,
            // собранная с зазорами, при запуске разваливалась на куски.
            WeldLoosePartsAtLaunch();

            PartDefinition[] defs = BuildMath.ToPartDefinitions(parts);
            Runner.SetVesselParts(defs);
            Runner.Ship.Mass = BuildMath.TotalMass(parts);

            // В полёте аппарат живёт телами прокси: визуальные коллайдеры деталей
            // гасим, иначе PhysX-тела сталкивались бы со своими же визуалами.
            // Игрок при этом не проходит сквозь аппарат — опора берёт коллайдеры
            // тел прокси (у них тот же маркер VesselPartCollider).
            SetPartColliders(false);

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
        /// Сварка перед запуском: свободные детали (ParentIndex −1), висящие в
        /// пределах 2 м над свободным верхом другой детали с почти совпадающим
        /// центром, притягиваются вплотную и связываются. Так собранная с
        /// зазорами ракета становится одним целым: падает и садится вместе,
        /// а не разваливается на отдельные куски при включении физики.
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

            // Обломки, если успели появиться (жёсткая посадка аналитики), убираем:
            // откат обязан вернуть мир ровно к моменту запуска полёта.
            if (Runner.Debris != null)
            {
                Runner.Debris.Clear();
            }

            SetPartColliders(true);

            RefreshVisualLocals();
            Debug.Log("[Build] возврат к стройке: состояние восстановлено");
        }

        // ─── Детали ──────────────────────────────────────────────────────────

        /// <summary>Шаблон двигателя (префаб и габариты) — для 3D-превью в интерфейсе.</summary>
        public PartCatalog.EngineTemplate Engine => engine;

        /// <summary>Метрики выбранного вида детали под текущие размеры игрока.</summary>
        public PartMetrics MetricsFor(PartKind kind)
        {
            float a = 0f;
            float b = 0f;
            switch (kind)
            {
                case PartKind.FuelTank:
                    a = TankRadius;
                    b = TankHeight;
                    break;
                case PartKind.Separator:
                    a = SeparatorRadius;
                    break;
            }

            return PartCatalog.Measure(kind, a, b, engine);
        }

        /// <summary>Поставить деталь в черновик (визуал + запись). parentIndex −1 — корень.</summary>
        public PlacedPart AddPart(PartKind kind, PartMetrics metrics, Vector3 pivot, int parentIndex, bool parentOnTop)
        {
            var part = new PlacedPart
            {
                Kind = kind,
                SizeA = metrics.Radius,
                SizeB = metrics.Height,
                Metrics = metrics,
                Pivot = pivot,
                ParentIndex = parentIndex,
                ParentOnTop = parentOnTop,
            };

            part.Visual = PartCatalog.CreateVisual(
                kind, metrics, engine, TankSideMaterial, TankCapMaterial, SeparatorMaterial,
                ShipTransform);

            parts.Add(part);

            if (parentIndex >= 0 && parentIndex < parts.Count - 1)
            {
                PlacedPart parent = parts[parentIndex];
                if (parentOnTop)
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

        /// <summary>Убрать последнюю поставленную деталь (Ctrl+Z).</summary>
        public void RemoveLast()
        {
            if (parts.Count == 0)
            {
                return;
            }

            RemoveAt(parts.Count - 1);
        }

        /// <summary>
        /// Убрать деталь по индексу (Alt+ПКМ по прицелу). Дети удалённой детали
        /// остаются на своих местах и становятся корнями (снап к удалённому
        /// родителю теряется — в стройке это просто отдельно стоящая деталь),
        /// индексы родителей сдвигаются.
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

                // Ребёнок удаляемого: отвязываем его узел от родителя.
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

        /// <summary>Убрать все детали черновика (кнопка разработчика в инвентаре).</summary>
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
        /// Поднять/опустить весь черновик по зениту места (м) — отладочный подъём
        /// для проверки падения физикой. Игрок это делает постановкой в воздухе.
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
        /// Включить/выключить визуальные коллайдеры деталей. В полёте они
        /// гасятся: тела прокси занимают их место (иначе PhysX-тела сталкивались
        /// бы со своими же визуалами), а опора игрока переключается на тела прокси.
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

                Collider collider = part.Visual.Root.GetComponent<Collider>();
                if (collider != null)
                {
                    collider.enabled = enabled;
                }
            }
        }

        /// <summary>Локальные позы визуалов: пивот минус низ сборки (оси места).</summary>
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
                    part.Visual.Root.transform.localRotation = Quaternion.identity;
                }
            }
        }

        // ─── Призрак и постановка ────────────────────────────────────────────

        private void UpdatePlacement()
        {
            if (!HasSelection || UiOpen)
            {
                HideGhost();
                return;
            }

            PartMetrics metrics = MetricsFor(SelectedKind);
            EnsureGhost(SelectedKind, metrics);

            Camera camera = Camera.main;
            if (camera == null || ghost == null)
            {
                HideGhost();
                return;
            }

            // Луч камеры: в осях места — для снапа по узлам, в render — для лучей
            // физики (коллайдеры живут в render-пространстве).
            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);

            bool hasSnap = BuildMath.TryFindSnap(parts, metrics, rayOrigin, rayDirection, out BuildMath.SnapHit snap);
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

                // Второй проход: точка в свободном месте, но, может, она в паре
                // сантиметров над узлом детали — доводим край-к-краю.
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
                // Луч никуда не попал: запасная точка в воздухе перед игроком.
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
        /// Валидность места: пятно целиком на площадке, без пересечений, в зоне
        /// досягаемости (пивот не выше потолка над плитой).
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
        /// Пересекается ли пробная деталь с ПОСТРОЙКАМИ места (стол, фермы,
        /// парапеты). Без этой проверки деталь можно было поставить внутрь
        /// коллайдеров стола: при включении физики она застревала в них и
        /// «висела», а соседи отваливались. Капсула чуть уже детали и на 6 см
        /// ужата от торцов, чтобы опора/пол не ловились как пересечение.
        /// </summary>
        private bool OverlapsSiteStructure(Vector3 pivot, PartMetrics metrics)
        {
            if (metrics.Height < 0.5f || metrics.Radius <= 0.01f)
            {
                return false; // тонкие детали (отделитель) не проверяем
            }

            float radius = (metrics.Radius * 0.92f) - 0.05f;
            if (radius <= 0.02f)
            {
                return false;
            }

            Vector3 centerLocal = pivot + new Vector3(0f, metrics.Center, 0f);
            float inner = Mathf.Max(0f, (metrics.Height * 0.5f) - metrics.Radius);
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
        /// Луч камеры в коллайдеры: ближайшее попадание. onPad — попал ли в
        /// ВЕРХНЮЮ грань коллайдера МЕСТА (плита или стартовый стол — только по
        /// ним и можно ставить).
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
                // Коллайдеры деталей аппарата луч постановки не заслоняют: снап
                // по узлам и так аналитический, а цель луча — плита/стол места.
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
                // Все попадания — коллайдеры деталей аппарата: поверхности места нет.
                return false;
            }

            RaycastHit hit = hits[best];
            hitPoint = Zone.RenderToSite(hit.point);

            // Только верхняя грань поверхности места: бок/низ — не поверхность постановки.
            Vector3 normalLocal = Zone.transform.InverseTransformDirection(hit.normal);
            onPad = LaunchPadZone.IsSiteCollider(hit.collider) && normalLocal.y > 0.7f;
            return true;
        }

        private void PlaceGhostedPart()
        {
            PlacedPart part = AddPart(
                SelectedKind,
                MetricsFor(SelectedKind),
                lastPivot,
                lastSnapFound ? lastSnap.PartIndex : -1,
                lastSnapFound && lastSnap.OnTop);
            if (part != null)
            {
                Debug.Log("[Build] поставлена деталь " + part.Kind + ", всего " + parts.Count);
            }
        }

        private void EnsureGhost(PartKind kind, PartMetrics metrics)
        {
            bool same = ghost != null
                && ghostKind == kind
                && Mathf.Abs(ghostRadius - metrics.Radius) < 1e-4f
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
                kind, metrics, engine,
                TankSideMaterial, TankCapMaterial, SeparatorMaterial, Zone.transform,
                withCollider: false);
            ghostKind = kind;
            ghostRadius = metrics.Radius;
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

        // ─── Отладочный стек ─────────────────────────────────────────────────

        /// <summary>Спайк: три жёстко заданные детали на площадке (проверка зоны контакта).</summary>
        private void BuildSpikeStack()
        {
            PartMetrics engineMetrics = MetricsFor(PartKind.Engine);
            Vector3 basePoint = Zone.VesselSpawnLocal;
            AddPart(PartKind.Engine, engineMetrics, basePoint, -1, false);

            PartMetrics tankMetrics = MetricsFor(PartKind.FuelTank);
            Vector3 tankPivot = new Vector3(basePoint.x, basePoint.y + engineMetrics.Top, basePoint.z);
            AddPart(PartKind.FuelTank, tankMetrics, tankPivot, 0, true);

            PartMetrics separatorMetrics = MetricsFor(PartKind.Separator);
            Vector3 separatorPivot = new Vector3(
                basePoint.x, tankPivot.y + tankMetrics.Height - separatorMetrics.Bottom, basePoint.z);
            AddPart(PartKind.Separator, separatorMetrics, separatorPivot, 1, true);

            Debug.Log("[Build] отладочный стек: двигатель + бак + отделитель");
        }

        // ─── Материалы ───────────────────────────────────────────────────────

        /// <summary>
        /// Материал призрака: ПЛОТНЫЙ одноцветный с подсветкой. Полупрозрачный
        /// HDRP-материал в рантайме в этом проекте не отдаёт цвет (ни через
        /// свойства, ни через HDMaterial.SetSurfaceType — при видимой тени
        /// объект не рисуется), поэтому призрак — плотная «проекция»: силуэт
        /// детали читается и по нему видно место постановки. Вернуть
        /// полупрозрачность — отдельной задачей с материалом-эталоном.
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
