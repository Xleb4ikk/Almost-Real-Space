using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// M-05 HULL RING (a part of SpacecraftBuilder).
    ///
    /// Flow in build mode:
    ///   1. The "Кольцо корпуса M-05" hotbar slot shows an OPEN ring on a free
    ///      stack node. Its diameter is taken from that node (the outer skin is
    ///      flush with the host, like a hull section); the pad fallback uses the
    ///      preset diameter.
    ///   2. RMB starts the height drag. While the button is held, moving the aim
    ///      up or down changes the ring height (release — place). A plain click
    ///      keeps the preset height. The ring is always placed open: the 96 mm
    ///      cavity is visible.
    ///   3. Sealing is a separate action on an INSTALLED ring: empty hand, aim at
    ///      it and press RMB. The four liner panels are welded in; the next RMB
    ///      on the same ring removes them again (seal / unseal toggle). The visual
    ///      is rebuilt and the mass changes.
    ///   4. The ring is an ordinary stack part: the next part snaps to its top.
    ///      Its collision is a ring of convex wall sectors, so the centre stays
    ///      walkable.
    ///
    /// The aim math is the fairing's vertical plane through the axis (FairingAim),
    /// but only the height is read: the radius is locked to the host node.
    /// </summary>
    public sealed partial class SpacecraftBuilder
    {
        private sealed class HullRingAimState
        {
            public int HostIndex = -1;
            public Vector3 Pivot;
            public float Radius;
            public float Height;
            public float StartHeight;
            public float AnchorAimY;
            public bool Valid;
        }

        private HullRingAimState hullRingAim;

        private ProceduralHullRing hullRingAimGhost;
        private readonly Material[] hullRingGhostMaterials = new Material[6];

        // Candidate from UpdateHullRingPlacement, read by BeginHullRingAim.
        private int hullRingCandidateHost = -1;
        private float hullRingCandidateRadius;
        private float hullRingCandidateHeight;

        // Ring under the crosshair while the hand is empty (-1 — none): RMB
        // toggles its liner (seal / unseal) instead of placing a new part.
        private int hullRingSealTarget = -1;

        // HUD
        private string hullRingHudTitle;
        private string hullRingHudValues;
        private bool hullRingHudValid = true;
        private GUIStyle hullRingHudStyle;
        private GUIStyle hullRingSealStyle;

        // ─── Materials ───────────────────────────────────────────────────────

        private void RegisterHullRingMaterials()
        {
            // The catalog owns the M-05 materials: icons/editor tests can run
            // before the builder's Awake and still get painted geometry.
            PartCatalog.EnsureHullRingMaterials();
        }

        // ─── Sealing an installed ring (empty-hand RMB) ──────────────────────

        /// <summary>
        /// The hull ring under the centre ray, or -1. A nearer solid part blocks
        /// the ring (same rule as the fairing editor). Sealed rings are included:
        /// an empty-hand RMB toggles them back to open.
        /// </summary>
        private int FindAimedHullRingIndex()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return -1;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            int best = -1;
            float bestT = BuildMath.SnapReach;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.HullRing)
                {
                    continue;
                }

                if (BuildMath.RayFrustum(rayOrigin, rayDirection, p.Pivot,
                        p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                    && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }

            if (best < 0)
            {
                return -1;
            }

            // Something solid in front of the ring wins (the ring behind a tank).
            // The ring's own host is not an obstacle: its top cap is below the ring.
            int host = parts[best].ParentIndex;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (i == best || i == host || p.Radial || p.Kind == PartKind.HullRing)
                {
                    continue;
                }

                Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                if (BuildMath.RayFrustum(rayOrigin, rayDirection, baseCenter,
                        p.Metrics.Radius, p.Metrics.TopRadius, p.Metrics.Height, out float t)
                    && t < bestT - 0.05f)
                {
                    return -1;
                }
            }

            return best;
        }

        /// <summary>Toggle an installed ring in place: seal (liner panels) or unseal again.</summary>
        private bool ToggleHullRingSeal(int index)
        {
            if (index < 0 || index >= parts.Count)
            {
                return false;
            }

            PlacedPart part = parts[index];
            if (part.Kind != PartKind.HullRing)
            {
                return false;
            }

            bool sealedNow = !part.Sealed;
            part.Sealed = sealedNow;
            part.Metrics = PartCatalog.MeasureHullRing(
                part.Metrics.Radius, part.Metrics.Height, sealedNow);
            RefreshHullRingPart(part);
            Debug.Log(string.Format(
                "[Build] кольцо корпуса {0}: Ø {1:F1} м, высота {2:F2} м, масса {3:F0} кг",
                sealedNow ? "запаяно" : "распаяно",
                part.Metrics.Radius * 2f, part.Metrics.Height, part.Metrics.MassKg));
            return true;
        }

        /// <summary>Rebuild the visual of a placed ring after its state changed.</summary>
        private static void RefreshHullRingPart(PlacedPart part)
        {
            if (part == null || part.Visual == null || part.Visual.Root == null)
            {
                return;
            }

            ProceduralHullRing ring = part.Visual.Root.GetComponent<ProceduralHullRing>();
            if (ring == null)
            {
                return;
            }

            ring.Sealed = part.Sealed;
            ring.IndicatorMaterial = part.Sealed
                ? PartCatalog.HullRingIndicatorSealedMaterial
                : PartCatalog.HullRingIndicatorOpenMaterial;
            ring.Rebuild();
            part.Visual.Renderers = part.Visual.Root.GetComponentsInChildren<Renderer>(true);
            part.Visual.CaptureNatural();
        }

        // ─── Ghost placement (before the height drag) ────────────────────────

        private void UpdateHullRingPlacement()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                HideGhost();
                return;
            }

            PartPreset preset = SelectedPreset;
            // Always open at placement; sealing is the separate empty-hand RMB
            // action on an installed ring.
            PartMetrics fallback = PartCatalog.MeasureHullRing(preset.Radius, preset.Height, false);

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            float coneTangent = AimCone.Tangent(camera);

            PartMetrics metrics = fallback;
            Vector3 pivot;
            bool valid;
            bool hasSnap = false;
            BuildMath.SnapHit snap = default;
            int host = -1;

            // A free TOP node of a stack part: the ring takes its radius.
            if (BuildMath.TryFindSnap(parts, fallback, rayOrigin, rayDirection, coneTangent,
                    out BuildMath.SnapHit hit)
                && hit.OnTop && parts[hit.PartIndex].Kind != PartKind.Capsule)
            {
                PartMetrics partner = parts[hit.PartIndex].Metrics;
                float radius = Mathf.Clamp(
                    partner.TopRadius, PartCatalog.HullRingMinRadius, PartCatalog.HullRingMaxRadius);
                metrics = PartCatalog.MeasureHullRing(radius, fallback.Height, false);
                pivot = hit.Pivot;
                host = hit.PartIndex;
                hasSnap = true;
                snap = hit;
                valid = CheckPlacement(pivot, metrics, host, PartKind.HullRing);
            }
            else if (TryRayToSurface(camera, out Vector3 hitPoint, out bool onPad))
            {
                pivot = new Vector3(hitPoint.x, hitPoint.y - metrics.Bottom, hitPoint.z);
                valid = onPad && CheckPlacement(pivot, metrics, -1, PartKind.HullRing);

                if (onPad && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near)
                    && near.OnTop && parts[near.PartIndex].Kind != PartKind.Capsule)
                {
                    PartMetrics partner = parts[near.PartIndex].Metrics;
                    float radius = Mathf.Clamp(
                        partner.TopRadius, PartCatalog.HullRingMinRadius, PartCatalog.HullRingMaxRadius);
                    metrics = PartCatalog.MeasureHullRing(radius, fallback.Height, false);
                    pivot = near.Pivot;
                    host = near.PartIndex;
                    hasSnap = true;
                    snap = near;
                    valid = CheckPlacement(pivot, metrics, host, PartKind.HullRing);
                }
            }
            else
            {
                Vector3 air = rayOrigin + (rayDirection * BuildMath.AirFallbackDistance);
                pivot = new Vector3(air.x, air.y - metrics.Bottom, air.z);
                float padTop = Zone.PadTopYAt(new Vector2(pivot.x, pivot.z));
                bool above = (pivot.y + metrics.Bottom) >= (padTop - 0.05f);
                bool notTooHigh = (pivot.y + metrics.Bottom) - padTop <= MaxBuildHeight;
                valid = above && notTooHigh && CheckPlacement(pivot, metrics, -1, PartKind.HullRing);

                if (valid && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near)
                    && near.OnTop && parts[near.PartIndex].Kind != PartKind.Capsule)
                {
                    PartMetrics partner = parts[near.PartIndex].Metrics;
                    float radius = Mathf.Clamp(
                        partner.TopRadius, PartCatalog.HullRingMinRadius, PartCatalog.HullRingMaxRadius);
                    metrics = PartCatalog.MeasureHullRing(radius, fallback.Height, false);
                    pivot = near.Pivot;
                    host = near.PartIndex;
                    hasSnap = true;
                    snap = near;
                    valid = CheckPlacement(pivot, metrics, host, PartKind.HullRing);
                }
            }

            EnsureGhost(PartKind.HullRing, metrics, null);
            lastPivot = pivot;
            lastSnap = snap;
            lastSnapFound = hasSnap;
            hullRingCandidateHost = host;
            hullRingCandidateRadius = metrics.Radius;
            hullRingCandidateHeight = metrics.Height;
            ghostVisible = true;
            ghostValid = valid;

            if (ghost != null)
            {
                ghost.Root.SetActive(true);
                ghost.Root.transform.localPosition = pivot;
                ghost.Root.transform.localRotation = Quaternion.identity;
                ghost.SetMaterial(valid ? ghostMaterial : invalidMaterial);
            }
        }

        // ─── Height drag (RMB held) ──────────────────────────────────────────

        /// <summary>RMB on a valid ring ghost starts the drag instead of placing at once.</summary>
        private void BeginHullRingAim()
        {
            if (!ghostValid || hullRingCandidateRadius <= 0f)
            {
                return;
            }

            if (hullRingCandidateHost >= parts.Count)
            {
                return;
            }

            float height = Mathf.Clamp(
                hullRingCandidateHeight, PartCatalog.HullRingMinHeight, PartCatalog.HullRingMaxHeight);

            var st = new HullRingAimState
            {
                HostIndex = hullRingCandidateHost,
                Pivot = lastPivot,
                Radius = hullRingCandidateRadius,
                Height = height,
                StartHeight = height,
                Valid = true,
            };

            st.AnchorAimY = AimHeightAt(st.Pivot, st.Pivot.y);
            hullRingAim = st;

            HideGhost();
            EnsureHullRingAimGhost();
            UpdateHullRingAimGhost(st);
            SetHullRingHud(st);
            ghostVisible = false;
            ghostValid = false;
        }

        /// <summary>Height the aim ray picks on the vertical plane through the ring axis.</summary>
        private bool TryAimHeight(Vector3 pivot, out float aimY)
        {
            aimY = pivot.y;
            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            return FairingAim.TryAimRing(rayOrigin, rayDirection, new Vector2(pivot.x, pivot.z),
                BuildMath.SnapReach + 10f, out _, out aimY, out _);
        }

        private float AimHeightAt(Vector3 pivot, float fallback)
        {
            return TryAimHeight(pivot, out float aimY) ? aimY : fallback;
        }

        private void UpdateHullRingAim()
        {
            HullRingAimState st = hullRingAim;
            if (st == null)
            {
                return;
            }

            if (PlayerInput.Up(GameAction.BuildPlace) || !PlayerInput.Held(GameAction.BuildPlace))
            {
                CommitHullRingAim();
                return;
            }

            if (TryAimHeight(st.Pivot, out float aimY))
            {
                // Relative drag: a plain click keeps the preset height; pulling the
                // crosshair up makes the ring taller, down makes it shorter.
                st.Height = Mathf.Clamp(
                    st.StartHeight + (aimY - st.AnchorAimY),
                    PartCatalog.HullRingMinHeight,
                    PartCatalog.HullRingMaxHeight);
            }

            PartMetrics metrics = PartCatalog.MeasureHullRing(st.Radius, st.Height, false);
            st.Valid = st.HostIndex < parts.Count
                && CheckPlacement(st.Pivot, metrics, st.HostIndex, PartKind.HullRing);

            UpdateHullRingAimGhost(st);
            SetHullRingHud(st);

            ghostVisible = false;
            ghostValid = false;
        }

        private void CommitHullRingAim()
        {
            HullRingAimState st = hullRingAim;
            if (st == null)
            {
                return;
            }

            PartMetrics metrics = PartCatalog.MeasureHullRing(st.Radius, st.Height, false);
            bool valid = st.HostIndex < parts.Count
                && CheckPlacement(st.Pivot, metrics, st.HostIndex, PartKind.HullRing);

            if (!valid && TryFitHullRingHeight(st, out float fitted))
            {
                st.Height = fitted;
                metrics = PartCatalog.MeasureHullRing(st.Radius, st.Height, false);
                valid = true;
            }

            if (!valid)
            {
                Debug.LogWarning("[Build] кольцо корпуса: место занято, установка отменена");
                CancelHullRingAim();
                return;
            }

            int parent = st.HostIndex >= 0 && st.HostIndex < parts.Count ? st.HostIndex : -1;
            PlacedPart part = AddPart(
                PartKind.HullRing, metrics, st.Pivot, parent, parent >= 0);
            if (part == null)
            {
                CancelHullRingAim();
                return;
            }

            Debug.Log(string.Format(
                "[Build] кольцо корпуса: Ø {0:F1} м, высота {1:F2} м, полость открыта, {2:F0} кг",
                metrics.Radius * 2f, metrics.Height, metrics.MassKg));
            CancelHullRingAim();
        }

        /// <summary>Shrink the ring until it fits under the parts above (if the minimum fits at all).</summary>
        private bool TryFitHullRingHeight(HullRingAimState st, out float height)
        {
            height = PartCatalog.HullRingMinHeight;
            PartMetrics minimum = PartCatalog.MeasureHullRing(
                st.Radius, PartCatalog.HullRingMinHeight, false);
            if (!CheckPlacement(st.Pivot, minimum, st.HostIndex, PartKind.HullRing))
            {
                return false;
            }

            float lo = PartCatalog.HullRingMinHeight;
            float hi = st.Height;
            for (int i = 0; i < 10; i++)
            {
                float mid = (lo + hi) * 0.5f;
                PartMetrics probe = PartCatalog.MeasureHullRing(st.Radius, mid, false);
                if (CheckPlacement(st.Pivot, probe, st.HostIndex, PartKind.HullRing))
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            height = lo;
            return true;
        }

        private void CancelHullRingAim()
        {
            if (hullRingAim == null && hullRingAimGhost == null)
            {
                hullRingHudTitle = null;
                hullRingHudValues = null;
                return;
            }

            hullRingAim = null;
            hullRingHudTitle = null;
            hullRingHudValues = null;
            if (hullRingAimGhost != null)
            {
                hullRingAimGhost.gameObject.SetActive(false);
            }
        }

        // ─── Aim ghost + HUD ─────────────────────────────────────────────────

        private void EnsureHullRingAimGhost()
        {
            if (hullRingAimGhost != null)
            {
                return;
            }

            var root = new GameObject("HullRing Aim Ghost");
            root.transform.SetParent(Zone.transform, false);
            hullRingAimGhost = root.AddComponent<ProceduralHullRing>();
            hullRingAimGhost.Segments = 128;
            hullRingAimGhost.BoltCount = 32;
            hullRingAimGhost.TextureMetersPerTile = 0.5f;
            hullRingAimGhost.Sealed = false;
        }

        private void UpdateHullRingAimGhost(HullRingAimState st)
        {
            EnsureHullRingAimGhost();
            Transform t = hullRingAimGhost.transform;
            t.localPosition = st.Pivot;
            t.localRotation = Quaternion.identity;

            if (!hullRingAimGhost.gameObject.activeSelf)
            {
                hullRingAimGhost.gameObject.SetActive(true);
            }

            bool changed = Mathf.Abs(hullRingAimGhost.Radius - st.Radius) > 1e-3f
                || Mathf.Abs(hullRingAimGhost.Height - st.Height) > 0.002f;
            if (changed)
            {
                hullRingAimGhost.Radius = st.Radius;
                hullRingAimGhost.Height = st.Height;
                hullRingAimGhost.Rebuild();
            }

            Material material = st.Valid ? ghostMaterial : invalidMaterial;
            var renderer = hullRingAimGhost.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                for (int i = 0; i < hullRingGhostMaterials.Length; i++)
                {
                    hullRingGhostMaterials[i] = material;
                }

                renderer.sharedMaterials = hullRingGhostMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        private void SetHullRingHud(HullRingAimState st)
        {
            PartMetrics metrics = PartCatalog.MeasureHullRing(st.Radius, st.Height, false);
            hullRingHudValid = st.Valid;
            hullRingHudTitle = "КОЛЬЦО КОРПУСА M-05 · ПОЛОСТЬ ОТКРЫТА";
            string mass = metrics.MassKg >= 1000d
                ? (metrics.MassKg / 1000d).ToString("F1") + " т"
                : metrics.MassKg.ToString("F0") + " кг";
            hullRingHudValues = "Ø " + (st.Radius * 2f).ToString("F1") + " м  ·  высота "
                + st.Height.ToString("F2") + " м\n"
                + "Масса " + mass + "  ·  стена 128 мм (16 / 96 / 16)\n"
                + "ПКМ вверх/вниз — высота · отпустите — поставить";
        }

        private void DrawHullRingHud()
        {
            if (hullRingAim == null || string.IsNullOrEmpty(hullRingHudTitle))
            {
                return;
            }

            EnsureHullRingHudStyle();
            float scale = Mathf.Max(0.5f, Screen.height / 1080f);
            hullRingHudStyle.fontSize = Mathf.RoundToInt(17f * scale);
            float w = 500f * scale;
            float h = 116f * scale;
            var box = new Rect((Screen.width * 0.5f) + (70f * scale),
                (Screen.height * 0.5f) + (30f * scale), w, h);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = hullRingHudValid
                ? new Color(0.55f, 1f, 0.65f, 1f)
                : new Color(1f, 0.55f, 0.5f, 1f);
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (8f * scale),
                w - (24f * scale), 26f * scale), "<b>" + hullRingHudTitle + "</b>", hullRingHudStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (34f * scale),
                w - (24f * scale), 74f * scale), hullRingHudValues, hullRingHudStyle);
            GUI.color = previous;
        }

        /// <summary>Small prompt while Ctrl is held over an installed ring.</summary>
        private void DrawHullRingSealPrompt()
        {
            if (hullRingSealTarget < 0 || hullRingSealTarget >= parts.Count)
            {
                return;
            }

            EnsureHullRingHudStyle();
            if (hullRingSealStyle == null)
            {
                hullRingSealStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.MiddleLeft,
                    wordWrap = false
                };
            }

            float scale = Mathf.Max(0.5f, Screen.height / 1080f);
            hullRingSealStyle.fontSize = Mathf.RoundToInt(16f * scale);
            PlacedPart target = parts[hullRingSealTarget];
            bool sealedRing = target.Sealed;
            string text = sealedRing
                ? "<b>ЛАЙНЕР ЗАПАЯН</b> · 4 панели\n<b>ПКМ</b> — распаять"
                : "<b>ПОЛОСТЬ ОТКРЫТА</b> · 96 мм\n<b>ПКМ</b> — запаять лайнером";
            float w = 380f * scale;
            float h = 56f * scale;
            var box = new Rect((Screen.width * 0.5f) + (70f * scale),
                (Screen.height * 0.5f) + (30f * scale), w, h);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = sealedRing
                ? new Color(1f, 0.80f, 0.50f, 1f)
                : new Color(0.55f, 1f, 0.65f, 1f);
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (2f * scale),
                w - (24f * scale), h - (4f * scale)), text, hullRingSealStyle);
            GUI.color = previous;
        }

        private void EnsureHullRingHudStyle()
        {
            if (hullRingHudStyle == null)
            {
                hullRingHudStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false
                };
            }
        }
    }
}
