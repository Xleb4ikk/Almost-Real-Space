using System.Collections.Generic;
using Galilego.Simulation.ContactZone;
using Galilego.Spacecraft;
using Galilego.Tablet;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// PROCEDURAL FAIRINGS (a part of SpacecraftBuilder).
    ///
    /// Flow in build mode:
    ///   1. The "Обтекатель" hotbar slot shows a white flat plate (like the separator) snapped to a free
    ///      node; its radius follows the node. RMB places it and starts building the shell.
    ///   2. While building, the aim (a plane through the axis facing the camera) defines the next ring:
    ///      height = aimed height, radius = distance from the axis. A translucent green projection (+ wire
    ///      outline) shows the segment, the HUD shows length and mass of the WHOLE fairing. RMB places the
    ///      segment. Snaps: straight up keeps the radius, the axis gives a nose point, the bottom/top edge
    ///      ring of a part above closes the shell onto it (Shift — free, no radius/apex snap).
    ///   3. A closing ring (part edge or nose point) finishes the fairing; F finishes it open.
    ///      Ctrl+Z (with Ctrl held, like the other undo) removes the last segment.
    ///   Aiming at an unfinished fairing with its slot selected continues it. Alt+RMB deletes it as a whole.
    ///
    /// The plate is an ordinary stack part (PartKind.Fairing); the shell (PlacedPart.Fairing) adds its mass to
    /// the plate's metrics, so draft mass, ΔV and the flight model see it. Jettison (key B) drops the shell mass
    /// and flies the petals apart as debris.
    /// </summary>
    public sealed partial class SpacecraftBuilder
    {
        [Header("Обтекатель")]
        [Tooltip("Материал дольки обтекателя (белая окраска). Пусто — создаётся белый HDRP/Lit.")]
        public Material FairingShellMaterial;

        [Tooltip("Материал плиты обтекателя. Пусто — как у дольки.")]
        public Material FairingPlateMaterial;

        [Tooltip("false — непрозрачная светящаяся проекция, как у остальных призраков (по умолчанию: полупрозрачные HDRP-материалы в этом проекте не рисуются). true — попробовать полупрозрачную зелёную; если её не видно, вернуть false. Каркас-контур виден всегда.")]
        public bool FairingTransparentGhost;

        [Tooltip("Скорость разлёта долек при отстреле, м/с.")]
        [Min(0f)] public float FairingSeparationSpeed = 3f;

        [Tooltip("Вращение долек после отстрела, град/с (максимум).")]
        [Min(0f)] public float FairingTumbleDegreesPerSecond = 25f;

        private sealed class FairingBuildState
        {
            public int PartIndex;
            public bool HasCandidate;
            public FairingRing Candidate;
            public FairingEnd CandidateEnd;
            public int DockPart = -1;
            public bool Valid;
            public string Reason;
        }

        private FairingBuildState fairingBuild;
        private int fairingContinueTarget = -1;
        private PartMetrics fairingGhostMetrics;

        private Material fairingShellRuntime;
        private Material fairingGhostFillGreen;
        private Material fairingGhostFillRed;
        private Material fairingGhostSolidGreen;
        private ProceduralFairing fairingSectionGhost;
        private FairingOutline fairingOutline;
        private FairingRing fairingGhostLast;
        private FairingRing fairingGhostCandidate;
        private int fairingGhostPetals = -1;

        // HUD
        private string fairingHudTitle;
        private string fairingHudValues;
        private string fairingHudHint;
        private bool fairingHudValid = true;
        private GUIStyle fairingHudStyle;

        /// <summary>A shell is being built right now (placement of other parts is suspended).</summary>
        public bool FairingBuilding
        {
            get { return fairingBuild != null; }
        }

        // ─── Materials ───────────────────────────────────────────────────────

        private void RegisterFairingMaterials()
        {
            if (fairingShellRuntime == null)
            {
                fairingShellRuntime = FairingShellMaterial != null
                    ? FairingShellMaterial
                    : FairingMaterials.CreateWhite("FairingWhite");
            }

            Material plate = FairingPlateMaterial != null ? FairingPlateMaterial : fairingShellRuntime;
            PartCatalog.RegisterFairingMaterials(plate, fairingShellRuntime);
        }

        private Material FairingGhostMaterial(bool valid)
        {
            if (FairingTransparentGhost)
            {
                if (fairingGhostFillGreen == null)
                {
                    fairingGhostFillGreen = FairingMaterials.CreateTransparentGhost(new Color(0.15f, 1f, 0.35f, 0.30f), "FairingGhostGreen");
                    fairingGhostFillRed = FairingMaterials.CreateTransparentGhost(new Color(1f, 0.2f, 0.15f, 0.30f), "FairingGhostRed");
                }

                Material fill = valid ? fairingGhostFillGreen : fairingGhostFillRed;
                if (fill != null)
                {
                    return fill;
                }
            }

            if (fairingGhostSolidGreen == null)
            {
                fairingGhostSolidGreen = CreateOverlayMaterial(new Color(0.15f, 1f, 0.35f, 1f), "FairingGhostSolidGreen");
            }

            return valid ? fairingGhostSolidGreen : invalidMaterial;
        }

        private Material FairingOutlineMaterial(bool valid)
        {
            if (fairingGhostSolidGreen == null)
            {
                fairingGhostSolidGreen = CreateOverlayMaterial(new Color(0.15f, 1f, 0.35f, 1f), "FairingGhostSolidGreen");
            }

            return valid ? fairingGhostSolidGreen : invalidMaterial;
        }

        // ─── Geometry helpers ────────────────────────────────────────────────

        /// <summary>Site height of the plate top: where ring Y = 0 lies.</summary>
        private static float FairingBaseY(PlacedPart part)
        {
            return part.Pivot.y + part.Metrics.Top;
        }

        private static ProceduralFairing FairingComponent(PlacedPart part)
        {
            return part != null && part.Visual != null && part.Visual.Root != null
                ? part.Visual.Root.GetComponent<ProceduralFairing>()
                : null;
        }

        /// <summary>Recompute the plate metrics (mass includes the shell) and rebuild the visual.</summary>
        private void RefreshFairingPart(PlacedPart part)
        {
            double shellKg = part.Fairing != null ? part.Fairing.MassKg : 0d;
            part.Metrics = PartCatalog.MeasureFairing(part.Metrics.Radius, shellKg);

            ProceduralFairing visual = FairingComponent(part);
            if (visual == null)
            {
                return;
            }

            visual.Shell = part.Fairing;
            visual.Rebuild();
            part.Visual.Renderers = part.Visual.Root.GetComponentsInChildren<Renderer>(true);
            part.Visual.CaptureNatural();
        }

        private static void SetFairingProximity(PlacedPart part, bool value)
        {
            ProceduralFairing visual = FairingComponent(part);
            if (visual != null)
            {
                visual.OpenByProximity = value;
            }
        }

        // ─── Base plate placement (the hotbar slot) ──────────────────────────

        /// <summary>Plate metrics fitted to the node the plate sits on (radius of that node).</summary>
        private PartMetrics FairingFitMetrics(BuildMath.SnapHit snap, PartMetrics fallback)
        {
            if (snap.PartIndex < 0 || snap.PartIndex >= parts.Count)
            {
                return fallback;
            }

            PartMetrics partner = parts[snap.PartIndex].Metrics;
            float radius = snap.OnTop ? partner.TopRadius : partner.Radius;
            radius = Mathf.Clamp(radius, PartCatalog.SeparatorMinRadius, PartCatalog.TankMaxRadius);
            return PartCatalog.Measure(PartKind.Fairing, radius, 0f, null);
        }

        private void UpdateFairingPlacement()
        {
            fairingContinueTarget = -1;
            fairingHudHint = null;
            Camera camera = Camera.main;
            if (camera == null)
            {
                HideGhost();
                return;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            float coneTangent = AimCone.Tangent(camera);

            // Aimed at an installed fairing (open or closed): RMB continues editing it.
            int aimed = FindAimedFairing(rayOrigin, rayDirection);
            if (aimed >= 0)
            {
                HideGhost();
                fairingContinueTarget = aimed;
                ghostVisible = true;
                ghostValid = true;
                fairingHudHint = parts[aimed].Fairing != null && parts[aimed].Fairing.IsClosed
                    ? "ПКМ — изменить · Alt+ПКМ — убрать секцию"
                    : "ПКМ — продолжить · Alt+ПКМ — убрать секцию";
                return;
            }

            PartMetrics preset = MetricsFor(SelectedPreset);
            PartMetrics metrics = preset;
            Vector3 pivot;
            bool valid;

            bool hasSnap = BuildMath.TryFindSnap(parts, preset, rayOrigin, rayDirection, coneTangent, out BuildMath.SnapHit snap);
            if (hasSnap)
            {
                metrics = FairingFitMetrics(snap, preset);
                pivot = snap.Pivot;
                valid = CheckPlacement(pivot, metrics, snap.PartIndex, PartKind.Fairing);
            }
            else if (TryRayToSurface(camera, out Vector3 hitPoint, out bool onPad))
            {
                pivot = new Vector3(hitPoint.x, hitPoint.y - metrics.Bottom, hitPoint.z);
                valid = onPad && CheckPlacement(pivot, metrics, -1, PartKind.Fairing);
                if (onPad && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near))
                {
                    snap = near;
                    hasSnap = true;
                    metrics = FairingFitMetrics(near, preset);
                    pivot = near.Pivot;
                    valid = CheckPlacement(pivot, metrics, near.PartIndex, PartKind.Fairing);
                }
            }
            else
            {
                Vector3 air = rayOrigin + (rayDirection * BuildMath.AirFallbackDistance);
                pivot = new Vector3(air.x, air.y - metrics.Bottom, air.z);
                float padTop = Zone.PadTopYAt(new Vector2(pivot.x, pivot.z));
                bool above = (pivot.y + metrics.Bottom) >= (padTop - 0.05f);
                bool notTooHigh = (pivot.y + metrics.Bottom) - padTop <= MaxBuildHeight;
                valid = above && notTooHigh && CheckPlacement(pivot, metrics, -1, PartKind.Fairing);
                if (valid && BuildMath.TryFindProximitySnap(parts, metrics, pivot, out BuildMath.SnapHit near))
                {
                    snap = near;
                    hasSnap = true;
                    metrics = FairingFitMetrics(near, preset);
                    pivot = near.Pivot;
                    valid = CheckPlacement(pivot, metrics, near.PartIndex, PartKind.Fairing);
                }
            }

            EnsureGhost(PartKind.Fairing, metrics, null);
            fairingGhostMetrics = metrics;
            lastPivot = pivot;
            lastSnap = snap;
            lastSnapFound = hasSnap;
            ghostVisible = true;
            ghostValid = valid;

            ghost.Root.SetActive(true);
            ghost.Root.transform.localPosition = pivot;
            ghost.Root.transform.localRotation = Quaternion.identity;
            ghost.SetMaterial(valid ? ghostMaterial : invalidMaterial);
            fairingHudHint = null;
        }

        /// <summary>Nearest fairing under the centre ray (plate or shell), -1 if none. Finished fairings included.</summary>
        private int FindAimedFairing(Vector3 rayOrigin, Vector3 rayDirection)
        {
            int best = -1;
            float bestT = BuildMath.SnapReach;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.Fairing)
                {
                    continue;
                }

                if (FairingRayHit(p, rayOrigin, rayDirection, out float t) && t < bestT)
                {
                    bestT = t;
                    best = i;
                }
            }

            if (best < 0)
            {
                return -1;
            }

            // Something solid in front of the fairing (e.g. a tank the shell hides behind) wins.
            aimDirections[0] = rayDirection;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (i == best || p.Radial || p.Kind == PartKind.Fairing)
                {
                    continue;
                }

                Vector3 baseCenter = p.Pivot + new Vector3(0f, p.Metrics.Bottom, 0f);
                if (BuildMath.RayFrustum(rayOrigin, rayDirection, baseCenter, p.Metrics.Radius, p.Metrics.TopRadius,
                        p.Metrics.Height, out float t) && t < bestT - 0.05f)
                {
                    return -1;
                }
            }

            return best;
        }

        /// <summary>Ray against the plate and every shell segment of a fairing (analytic).</summary>
        private static bool FairingRayHit(PlacedPart part, Vector3 rayOrigin, Vector3 rayDirection, out float distance)
        {
            distance = float.MaxValue;
            bool hit = false;
            Vector3 plateBase = part.Pivot + new Vector3(0f, part.Metrics.Bottom, 0f);
            if (BuildMath.RayFrustum(rayOrigin, rayDirection, plateBase, part.Metrics.Radius, part.Metrics.Radius,
                    part.Metrics.Height, out float t))
            {
                distance = t;
                hit = true;
            }

            FairingShell shell = part.Fairing;
            if (shell == null)
            {
                return hit;
            }

            float baseY = FairingBaseY(part);
            for (int i = 0; i + 1 < shell.Rings.Count; i++)
            {
                FairingRing a = shell.Rings[i];
                FairingRing b = shell.Rings[i + 1];
                float dy = b.Y - a.Y;
                if (dy < 0.01f)
                {
                    continue;
                }

                var center = new Vector3(part.Pivot.x, baseY + a.Y, part.Pivot.z);
                if (BuildMath.RayFrustum(rayOrigin, rayDirection, center, a.Radius, b.Radius, dy, out float ts) && ts < distance)
                {
                    distance = ts;
                    hit = true;
                }
            }

            return hit;
        }

        /// <summary>Alt+RMB deletion aims at the shell too (the plate alone is a 4 cm disc).</summary>
        private bool TryRaycastFairingShells(Vector3 rayOrigin, Vector3 rayDirection, out int index, out float distance)
        {
            index = -1;
            distance = float.MaxValue;
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.Fairing || !p.Fairing.HasShell)
                {
                    continue;
                }

                if (FairingRayHit(p, rayOrigin, rayDirection, out float t) && t < distance && t <= BuildMath.SnapReach)
                {
                    distance = t;
                    index = i;
                }
            }

            return index >= 0;
        }

        private void PlaceFairingBase()
        {
            if (fairingContinueTarget >= 0)
            {
                BeginFairingBuild(fairingContinueTarget);
                return;
            }

            int petals = FairingShell.ClampPetals(SelectedPreset.Count);
            PlacedPart part = AddPart(
                PartKind.Fairing, fairingGhostMetrics, lastPivot,
                lastSnapFound ? lastSnap.PartIndex : -1, lastSnapFound && lastSnap.OnTop);
            if (part == null)
            {
                return;
            }

            part.Fairing = new FairingShell { Petals = petals };
            part.Fairing.Rings.Add(new FairingRing(fairingGhostMetrics.Radius, 0f));
            RefreshFairingPart(part);
            Debug.Log("[Build] обтекатель: плита Ø " + (fairingGhostMetrics.Radius * 2f).ToString("F1") + " м, " + FairingShell.PetalsLabel(petals));
            BeginFairingBuild(parts.Count - 1);
        }

        // ─── Shell building ──────────────────────────────────────────────────

        private void BeginFairingBuild(int index)
        {
            if (index < 0 || index >= parts.Count)
            {
                return;
            }

            PlacedPart part = parts[index];
            if (part.Kind != PartKind.Fairing)
            {
                return;
            }

            // Every fairing is editable: an old plate without a shell gets one, and a finished
            // (closed) shell is unsealed — its closing ring is removed so new sections can follow.
            if (part.Fairing == null)
            {
                part.Fairing = new FairingShell();
                part.Fairing.Rings.Add(new FairingRing(part.Metrics.Radius, 0f));
            }

            bool changed = false;
            if (HasSelection && SelectedPreset.Kind == PartKind.Fairing)
            {
                int petals = FairingShell.ClampPetals(SelectedPreset.Count);
                if (part.Fairing.Petals != petals)
                {
                    // X/Z on the slot applies to the fairing being edited, not only to new ones.
                    part.Fairing.Petals = petals;
                    changed = true;
                }
            }

            if (part.Fairing.IsClosed && part.Fairing.PopRing())
            {
                changed = true;               // a nose cone / a shell docked to a part reopens
            }

            if (changed)
            {
                RefreshFairingPart(part);
            }

            fairingBuild = new FairingBuildState { PartIndex = index };
            fairingContinueTarget = -1;
            HideGhost();
            SetFairingProximity(part, false);
            ProceduralFairing visual = FairingComponent(part);
            if (visual != null)
            {
                visual.OpenByProximity = false;
            }
        }

        private void FinishFairingBuild(string reason)
        {
            if (fairingBuild == null)
            {
                return;
            }

            if (fairingBuild.PartIndex >= 0 && fairingBuild.PartIndex < parts.Count)
            {
                PlacedPart part = parts[fairingBuild.PartIndex];
                SetFairingProximity(part, true);
                if (part.Fairing != null)
                {
                    Debug.Log(string.Format("[Build] обтекатель готов ({0}): длина {1:F2} м, масса {2:F0} кг",
                        reason, part.Fairing.Height, part.Metrics.MassKg));
                }
            }

            fairingBuild = null;
            HideFairingSectionGhost();
            fairingHudHint = null;
        }

        /// <summary>Called when a physics launch is requested while a shell is still being built.</summary>
        private void FairingFinishForLaunch()
        {
            FinishFairingBuild("запуск");
        }

        /// <summary>
        /// Alt+RMB: drop the last section of a fairing. A plate-only fairing goes away entirely —
        /// there is no section left to remove. Returns true when something was removed.
        /// </summary>
        public bool RemoveFairingSection(int index)
        {
            if (index < 0 || index >= parts.Count)
            {
                return false;
            }

            PlacedPart part = parts[index];
            if (part.Kind != PartKind.Fairing)
            {
                return false;
            }

            // Drop the red Alt highlight first: RefreshFairingPart captures the natural
            // materials, and a captured red would stick to the part after the edit.
            ClearDeleteHighlight();

            if (part.Fairing == null)
            {
                RemoveAt(index);
                return true;
            }

            if (part.Fairing.PopRing())
            {
                RefreshFairingPart(part);
                Debug.Log("[Build] обтекатель: секция убрана, длина " + part.Fairing.Height.ToString("F2") + " м");
                return true;
            }

            // Only the plate is left: "убрать одну" takes the plate itself.
            RemoveAt(index);
            return true;
        }

        /// <summary>
        /// Radius the shell must clear at a height. Procedural tanks and engines are exact cones,
        /// but the Orion capsule's heat shield is a wide disc: its linear metrics (radius → 0.38 R)
        /// understate the surface near the bottom, so a capsule is a full-width cylinder here.
        /// </summary>
        private static float FairingClearanceRadius(PlacedPart p, float y)
        {
            return p.Kind == PartKind.Capsule
                ? p.Metrics.Radius
                : BuildMath.RadiusAtHeight(p.Pivot, p.Metrics, y);
        }

        /// <summary>
        /// True when the probe body would cut through a fairing shell. The shell is a thin wall of
        /// revolution, so the bodies intersect when the shell radius falls inside the probe's radial
        /// span at the same height. Flush surfaces (a payload exactly at the shell radius) are not a
        /// cut; placement uses this so nothing can be pushed through a built shell. The parent plate
        /// is checked too: its own shell surrounds the payload being placed on it.
        /// </summary>
        private bool FairingShellBlocks(Vector3 pivot, PartMetrics probe, bool probeFullWidth)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.Fairing || p.Fairing == null || !p.Fairing.HasShell)
                {
                    continue;
                }

                if (FairingShellBlocksPart(p, pivot, probe, probeFullWidth))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool FairingShellBlocksPart(PlacedPart fairing, Vector3 pivot, PartMetrics probe, bool probeFullWidth)
        {
            float shellBaseY = fairing.Pivot.y + fairing.Metrics.Top;
            float dx = pivot.x - fairing.Pivot.x;
            float dz = pivot.z - fairing.Pivot.z;
            float offset = Mathf.Sqrt((dx * dx) + (dz * dz));
            const float tolerance = 0.02f;

            for (int s = 0; s + 1 < fairing.Fairing.Rings.Count; s++)
            {
                FairingRing a = fairing.Fairing.Rings[s];
                FairingRing b = fairing.Fairing.Rings[s + 1];
                float lo = Mathf.Max(pivot.y + probe.Bottom, shellBaseY + a.Y);
                float hi = Mathf.Min(pivot.y + probe.Top, shellBaseY + b.Y);
                if (hi - lo <= 1e-3f)
                {
                    continue;
                }

                float span = b.Y - a.Y;
                for (int k = 0; k <= 2; k++)
                {
                    float y = Mathf.Lerp(lo, hi, k * 0.5f);
                    float shellRadius = span < 1e-4f
                        ? b.Radius
                        : Mathf.Lerp(a.Radius, b.Radius, (y - (shellBaseY + a.Y)) / span);
                    float probeRadius = probeFullWidth ? probe.Radius : BuildMath.RadiusAtHeight(pivot, probe, y);
                    float outer = offset + probeRadius;
                    float inner = Mathf.Max(0f, offset - probeRadius);
                    if (outer > shellRadius + tolerance && inner < shellRadius - tolerance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Empty hand: aiming at a fairing and pressing RMB continues building it — no fairing
        /// item has to be selected in the hotbar. Only the start is handled here; the section
        /// ghost then follows the aim through UpdateFairingSection.
        /// </summary>
        private void UpdateEmptyHandFairing()
        {
            HideGhost();
            if (fairingBuild != null || PhysicsOn || !PlayerInput.Down(GameAction.BuildPlace))
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            int aimed = FindAimedFairing(rayOrigin, rayDirection);
            if (aimed >= 0)
            {
                BeginFairingBuild(aimed);
                Debug.Log("[Build] обтекатель: продолжаем без детали в руке");
            }
        }

        /// <summary>Hook for RemoveLast: while building, undo pops the last segment (then cancels the build).</summary>
        private bool FairingUndo()
        {
            if (fairingBuild == null)
            {
                return false;
            }

            if (fairingBuild.PartIndex < 0 || fairingBuild.PartIndex >= parts.Count)
            {
                fairingBuild = null;
                return false;
            }

            PlacedPart part = parts[fairingBuild.PartIndex];
            if (part.Fairing != null && part.Fairing.PopRing())
            {
                RefreshFairingPart(part);
                return true;
            }

            // Only the plate is left: leave the building state; the next undo removes the plate.
            FinishFairingBuild("отмена");
            return true;
        }

        /// <summary>Hook for RemoveAt, called after the part list shifted.</summary>
        private void FairingOnPartRemoved(int index)
        {
            if (fairingBuild == null)
            {
                return;
            }

            if (fairingBuild.PartIndex == index)
            {
                fairingBuild = null;
                HideFairingSectionGhost();
            }
            else if (fairingBuild.PartIndex > index)
            {
                fairingBuild.PartIndex--;
            }
        }

        private void UpdateFairingSection()
        {
            FairingBuildState st = fairingBuild;
            if (st == null)
            {
                return;
            }

            PlacedPart part = st.PartIndex >= 0 && st.PartIndex < parts.Count ? parts[st.PartIndex] : null;
            if (part == null || part.Fairing == null || part.Visual == null || part.Visual.Root == null)
            {
                fairingBuild = null;
                HideFairingSectionGhost();
                return;
            }

            HideGhost();
            if (UiOpen)
            {
                HideFairingSectionGhost();
                return;
            }

            if (PlayerInput.Down(GameAction.BuildFairingFinish))
            {
                FinishFairingBuild("завершён");
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                HideFairingSectionGhost();
                return;
            }

            Vector3 rayOrigin = Zone.RenderToSite(camera.transform.position);
            Vector3 rayDirection = Zone.transform.InverseTransformDirection(camera.transform.forward);
            bool freeform = PlayerInput.ShiftHeld();

            if (!SolveFairingCandidate(part, st, rayOrigin, rayDirection, freeform))
            {
                st.HasCandidate = false;
                HideFairingSectionGhost();
                ghostVisible = false;
                ghostValid = false;
                SetFairingHud(part, null, false, "Наведите на место рядом с ракетой");
                return;
            }

            st.Valid = ValidateFairingCandidate(part, st, out st.Reason);

            // A radius lock that makes the segment invalid must not get in the way: retry without it.
            if (!st.Valid && !freeform)
            {
                FairingBuildState retry = new FairingBuildState { PartIndex = st.PartIndex };
                if (SolveFairingCandidate(part, retry, rayOrigin, rayDirection, true)
                    && ValidateFairingCandidate(part, retry, out retry.Reason))
                {
                    st = fairingBuild = retry;
                    st.Valid = true;
                }
            }

            UpdateFairingSectionGhost(part, st);
            ghostVisible = true;
            ghostValid = st.Valid;

            string hint;
            if (!st.Valid)
            {
                hint = st.Reason;
            }
            else if (st.CandidateEnd == FairingEnd.Docked)
            {
                hint = "ПКМ — замкнуть на детали · Alt+ПКМ — убрать секцию";
            }
            else if (st.CandidateEnd == FairingEnd.Apex)
            {
                hint = "ПКМ — носовой конус · Alt+ПКМ — убрать секцию";
            }
            else
            {
                hint = "ПКМ — секция · Alt+ПКМ — убрать · F — завершить";
            }

            SetFairingHud(part, st, st.Valid, hint);
        }

        /// <summary>Read the aim into a candidate ring (with the snaps). false — the aim is not on the plane.</summary>
        private bool SolveFairingCandidate(
            PlacedPart part, FairingBuildState st, Vector3 rayOrigin, Vector3 rayDirection, bool freeform)
        {
            FairingShell shell = part.Fairing;
            FairingRing last = shell.Last;
            float baseY = FairingBaseY(part);

            if (!FairingAim.TryAimRing(rayOrigin, rayDirection, new Vector2(part.Pivot.x, part.Pivot.z),
                    BuildMath.SnapReach, out float aimRadius, out float aimHeight, out float aimDistance))
            {
                st.HasCandidate = false;
                return false;
            }

            float relY = aimHeight - baseY;
            float radius = Mathf.Clamp(aimRadius, 0f, FairingShell.MaxRadiusM);
            float y = Mathf.Max(relY, last.Y);
            FairingEnd end = FairingEnd.Open;
            int dock = -1;

            // 1. Edge rings of the parts above: the shell closes onto them.
            float bestDistance = Mathf.Max(0.30f, 0.045f * aimDistance);
            for (int j = 0; j < parts.Count; j++)
            {
                PlacedPart p = parts[j];
                if (j == st.PartIndex || p.Radial || p.Kind == PartKind.Fairing)
                {
                    continue;
                }

                float bottomY = p.Pivot.y + p.Metrics.Bottom - baseY;
                float topY = p.Pivot.y + p.Metrics.Top - baseY;
                if (topY <= last.Y + 0.05f)
                {
                    continue;
                }

                for (int edge = 0; edge < 2; edge++)
                {
                    float ringY = edge == 0 ? bottomY : topY;
                    float ringR = edge == 0 ? p.Metrics.Radius : p.Metrics.TopRadius;
                    if (ringY <= last.Y + 0.05f)
                    {
                        continue;
                    }

                    float d = Vector2.Distance(new Vector2(aimRadius, relY), new Vector2(ringR, ringY));
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        radius = ringR;
                        y = ringY;
                        end = FairingEnd.Docked;
                        dock = j;
                    }
                }
            }

            // 2. Nose point on the axis.
            if (end == FairingEnd.Open && !freeform
                && y - last.Y >= FairingShell.MinSegmentMeters
                && aimRadius < Mathf.Max(0.25f, 0.03f * aimDistance))
            {
                radius = 0f;
                end = FairingEnd.Apex;
            }

            // 3. Straight up: keep the previous radius.
            if (end == FairingEnd.Open && !freeform
                && Mathf.Abs(radius - last.Radius) < Mathf.Max(0.12f, 0.02f * aimDistance))
            {
                radius = last.Radius;
            }

            st.Candidate = new FairingRing(radius, y);
            st.CandidateEnd = end;
            st.DockPart = dock;
            st.HasCandidate = true;
            return true;
        }

        private bool ValidateFairingCandidate(PlacedPart part, FairingBuildState st, out string reason)
        {
            reason = null;
            FairingShell shell = part.Fairing;
            FairingRing last = shell.Last;
            FairingRing c = st.Candidate;

            if (shell.Rings.Count >= FairingShell.MaxRings)
            {
                reason = "Слишком много секций";
                return false;
            }

            float dr = c.Radius - last.Radius;
            float dy = c.Y - last.Y;
            if (Mathf.Sqrt((dr * dr) + (dy * dy)) < FairingShell.MinSegmentMeters)
            {
                reason = "Секция слишком короткая";
                return false;
            }

            if (c.Y > FairingShell.MaxHeightM)
            {
                reason = "Слишком высоко";
                return false;
            }

            return FairingClearOfParts(part, st.PartIndex, last, c, st.DockPart, out reason);
        }

        /// <summary>The new segment must not cut through parts (stack bodies and radial RCS blocks).</summary>
        private bool FairingClearOfParts(
            PlacedPart basePart, int baseIndex, FairingRing a, FairingRing b, int dockIndex, out string reason)
        {
            reason = null;
            float baseY = FairingBaseY(basePart);
            // The wall has a real thickness: a shell flush with the payload (the usual case — the
            // plate takes the node radius and so does the part on it) must not count as cutting
            // through it. The inner skin is what must stay outside the part surface.
            float skin = FairingShell.ShellThicknessM + 0.01f;
            const int samples = 10;
            for (int k = 1; k <= samples; k++)
            {
                float t = k / (float)samples;
                float r = Mathf.Lerp(a.Radius, b.Radius, t);
                float y = baseY + Mathf.Lerp(a.Y, b.Y, t);
                for (int j = 0; j < parts.Count; j++)
                {
                    if (j == baseIndex)
                    {
                        continue;
                    }

                    PlacedPart p = parts[j];
                    if (p.Kind == PartKind.Fairing)
                    {
                        continue;
                    }

                    float ox = p.Pivot.x - basePart.Pivot.x;
                    float oz = p.Pivot.z - basePart.Pivot.z;
                    float offset = Mathf.Sqrt((ox * ox) + (oz * oz));
                    if (p.Radial)
                    {
                        if (Mathf.Abs(y - p.Pivot.y) <= RcsBlockPlacement.HalfHeight
                            && r + skin < offset + RcsBlockPlacement.BodySize.z + 0.02f)
                        {
                            reason = "Задевает блок РСУ";
                            return false;
                        }

                        continue;
                    }

                    if (y < p.Pivot.y + p.Metrics.Bottom - 1e-3f || y > p.Pivot.y + p.Metrics.Top + 1e-3f)
                    {
                        continue;
                    }

                    if (j == dockIndex && k == samples)
                    {
                        continue;                      // the exact closing ring
                    }

                    float need = FairingClearanceRadius(p, y) + offset;
                    if (r + skin < need)
                    {
                        reason = "Проходит сквозь деталь";
                        return false;
                    }
                }
            }

            return true;
        }

        private void PlaceFairingSection()
        {
            FairingBuildState st = fairingBuild;
            if (st == null || !st.HasCandidate || !st.Valid || st.PartIndex < 0 || st.PartIndex >= parts.Count)
            {
                return;
            }

            PlacedPart part = parts[st.PartIndex];
            part.Fairing.Rings.Add(st.Candidate);
            part.Fairing.End = st.CandidateEnd;
            RefreshFairingPart(part);
            st.HasCandidate = false;

            if (st.CandidateEnd != FairingEnd.Open)
            {
                FinishFairingBuild(st.CandidateEnd == FairingEnd.Apex ? "носовой конус" : "замкнут на детали");
            }
        }

        // ─── Projection ghost + HUD ──────────────────────────────────────────

        private void EnsureFairingSectionGhost()
        {
            if (fairingSectionGhost != null)
            {
                return;
            }

            var root = new GameObject("Fairing Section Ghost");
            root.transform.SetParent(Zone.transform, false);
            fairingSectionGhost = root.AddComponent<ProceduralFairing>();
            fairingSectionGhost.ShowPlate = false;
            fairingSectionGhost.BuildColliders = false;
            fairingSectionGhost.OpenByProximity = false;
            fairingSectionGhost.PlateMaterial = fairingShellRuntime;
            fairingSectionGhost.ShellMaterial = FairingGhostMaterial(true);
            fairingSectionGhost.Shell = new FairingShell();
            fairingOutline = new FairingOutline(root.transform, FairingOutlineMaterial(true));
            fairingGhostPetals = -1;
        }

        private void UpdateFairingSectionGhost(PlacedPart part, FairingBuildState st)
        {
            EnsureFairingSectionGhost();
            FairingRing last = part.Fairing.Last;
            FairingRing c = st.Candidate;
            Transform t = fairingSectionGhost.transform;
            t.localPosition = new Vector3(part.Pivot.x, FairingBaseY(part) + last.Y, part.Pivot.z);
            t.localRotation = Quaternion.identity;
            fairingSectionGhost.gameObject.SetActive(true);
            fairingSectionGhost.GetComponent<MeshRenderer>().enabled = false;

            int petals = part.Fairing.Petals;
            bool changed = petals != fairingGhostPetals
                || Mathf.Abs(last.Radius - fairingGhostLast.Radius) > 1e-3f
                || Mathf.Abs(last.Y - fairingGhostLast.Y) > 1e-3f
                || Mathf.Abs(c.Radius - fairingGhostCandidate.Radius) > 0.01f
                || Mathf.Abs(c.Y - fairingGhostCandidate.Y) > 0.01f;

            FairingShell gs = fairingSectionGhost.Shell;
            if (changed)
            {
                fairingGhostPetals = petals;
                fairingGhostLast = last;
                fairingGhostCandidate = c;
                gs.Petals = petals;
                gs.Rings.Clear();
                gs.Rings.Add(new FairingRing(last.Radius, 0f));
                gs.Rings.Add(new FairingRing(c.Radius, c.Y - last.Y));
                fairingSectionGhost.Rebuild();
                fairingSectionGhost.SetShadows(UnityEngine.Rendering.ShadowCastingMode.Off);
                fairingOutline.Set(last.Radius, c.Radius, c.Y - last.Y, petals);
            }

            fairingSectionGhost.SetShellMaterial(FairingGhostMaterial(st.Valid));
            fairingOutline.SetMaterial(FairingOutlineMaterial(st.Valid));
            fairingOutline.SetActive(true);
        }

        private void HideFairingSectionGhost()
        {
            if (fairingSectionGhost != null)
            {
                fairingSectionGhost.gameObject.SetActive(false);
            }
        }

        private void SetFairingHud(PlacedPart part, FairingBuildState st, bool valid, string hint)
        {
            fairingHudValid = valid;
            fairingHudHint = hint;
            FairingShell shell = part.Fairing;
            fairingHudTitle = "ОБТЕКАТЕЛЬ · " + FairingShell.PetalsLabel(shell.Petals);
            if (st != null && st.HasCandidate)
            {
                double mass = FairingShell.PlateMassKg(part.Metrics.Radius) + shell.MassWith(st.Candidate);
                fairingHudValues = "Длина  " + shell.HeightWith(st.Candidate).ToString("F2") + " м\n"
                    + "Масса  " + mass.ToString("F0") + " кг";
            }
            else
            {
                fairingHudValues = "Длина  " + shell.Height.ToString("F2") + " м\n"
                    + "Масса  " + part.Metrics.MassKg.ToString("F0") + " кг";
            }
        }

        private void OnGUI()
        {
            if (fairingBuild == null || PhysicsOn || UiOpen || string.IsNullOrEmpty(fairingHudTitle))
            {
                return;
            }

            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            if (fairingHudStyle == null)
            {
                fairingHudStyle = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.UpperLeft, wordWrap = false };
            }

            float scale = Mathf.Max(0.5f, Screen.height / 1080f);
            fairingHudStyle.fontSize = Mathf.RoundToInt(17f * scale);
            float w = 470f * scale;
            float h = 112f * scale;
            var box = new Rect((Screen.width * 0.5f) + (70f * scale), (Screen.height * 0.5f) + (30f * scale), w, h);
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = fairingHudValid ? new Color(0.55f, 1f, 0.65f, 1f) : new Color(1f, 0.55f, 0.5f, 1f);
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (8f * scale), w - (24f * scale), 26f * scale),
                "<b>" + fairingHudTitle + "</b>", fairingHudStyle);
            GUI.color = Color.white;
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (34f * scale), w - (24f * scale), 50f * scale),
                fairingHudValues, fairingHudStyle);
            fairingHudStyle.fontSize = Mathf.RoundToInt(13f * scale);
            GUI.color = new Color(0.85f, 0.87f, 0.9f, 1f);
            GUI.Label(new Rect(box.x + (12f * scale), box.y + (84f * scale), w - (24f * scale), 22f * scale),
                fairingHudHint ?? string.Empty, fairingHudStyle);
            GUI.color = previous;
        }

        // ─── Rollback / colliders ────────────────────────────────────────────

        /// <summary>Back to build mode: petals jettisoned in flight come back, debris visuals are dropped.</summary>
        private void FairingRestoreAfterFlight()
        {
            FairingDebrisVisuals.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                PlacedPart p = parts[i];
                if (p.Kind != PartKind.Fairing || p.Fairing == null)
                {
                    continue;
                }

                p.Fairing.Jettisoned = false;
                ProceduralFairing visual = FairingComponent(p);
                if (visual != null)
                {
                    visual.Rebuild();
                    visual.OpenByProximity = fairingBuild == null || fairingBuild.PartIndex != i;
                    p.Visual.Renderers = p.Visual.Root.GetComponentsInChildren<Renderer>(true);
                    p.Visual.CaptureNatural();
                }
            }
        }

        // ─── Flight: jettison ────────────────────────────────────────────────

        private void UpdateFairingJettison()
        {
            if (Runner == null || Runner.Paused
                || (Runner.PlayerMode != PlayerMode.InShip && !Runner.PlayerSeated)
                || ShipTablet.AnyExpanded
                || !PlayerInput.Down(GameAction.FairingJettison))
            {
                return;
            }

            int count = JettisonFairings();
            if (count == 0)
            {
                Debug.Log("[Fairing] нечего сбрасывать");
            }
        }

        /// <summary>Jettison every fairing that still has its petals. Returns how many were dropped.</summary>
        public int JettisonFairings()
        {
            int count = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                if (JettisonFairing(i))
                {
                    count++;
                }
            }

            return count;
        }

        private bool JettisonFairing(int index)
        {
            if (!PhysicsOn || Runner == null || index < 0 || index >= parts.Count)
            {
                return false;
            }

            PlacedPart part = parts[index];
            if (part.Kind != PartKind.Fairing || part.Fairing == null || !part.Fairing.HasShell || part.Fairing.Jettisoned)
            {
                return false;
            }

            ProceduralFairing visual = FairingComponent(part);
            if (visual == null || visual.PetalsDetached || visual.PetalCount == 0)
            {
                return false;
            }

            double shellKg = part.Fairing.MassKg;
            var petals = new List<ProceduralFairing.DetachedPetal>(visual.PetalCount);
            visual.DetachPetals(petals);
            part.Fairing.Jettisoned = true;

            // Mass: the flight model, the stage core and the ship lose the shell.
            Galilego.Flight.FlightRuntime flight = Galilego.Flight.FlightRuntime.Instance;
            if (flight != null)
            {
                flight.OnFairingReleased(index, shellKg);
            }

            // Petals: n-body debris slots carry the motion, the petal meshes are their visuals.
            for (int i = 0; i < petals.Count; i++)
            {
                SpawnPetalDebris(petals[i]);
            }

            part.Visual.Renderers = part.Visual.Root.GetComponentsInChildren<Renderer>(true);
            part.Visual.CaptureNatural();
            Debug.Log(string.Format("[Fairing] обтекатель сброшен: {0} долек, {1:F0} кг", petals.Count, shellKg));
            return true;
        }

        private void SpawnPetalDebris(ProceduralFairing.DetachedPetal petal)
        {
            if (petal.Transform == null)
            {
                return;
            }

            if (Runner.Debris == null || Runner.Ship == null)
            {
                Destroy(petal.Transform.gameObject);
                return;
            }

            Galilego.Core.Vector3d position = FloatingOrigin.Anchor + AstroFrame.ToAstro(petal.Transform.position);
            Galilego.Core.Vector3d velocity = Runner.Ship.Velocity
                + (AstroFrame.ToAstro(petal.OutwardWorld) * (double)FairingSeparationSpeed);
            int slot = Runner.Debris.Spawn(position, velocity, petal.MassKg, Runner.TimeSeconds, Runner.DebrisLifetimeSeconds);
            long sequence = Runner.Debris.SlotAt(slot).Sequence;

            var spin = new Vector3(
                Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * FairingTumbleDegreesPerSecond;
            FairingDebrisVisuals.Register(slot, sequence, petal.Transform, spin);
        }
    }
}
