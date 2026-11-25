using System;
using System.Collections.Generic;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>
    /// Part catalog: names, size limits, metrics (mass/shape/nodes) and visual
    /// assembly. Tank and separator are procedural (radii/height are set by the
    /// player), the engine uses the RS-25 model (dimensions measured from meshes).
    /// The tank can be a cylinder or a truncated cone adapter: bottom and top
    /// radii then differ, and the shape is a body of revolution with a slanted
    /// generatrix. Masses: tank — shell + propellant (ProceduralFuelTank formulas),
    /// separator — steel volume of a solid ribbed disc, engine — placeholder until real
    /// calculations. Joint strengths are parameters (placeholders); the separator
    /// is the weakest: it is meant to break first.
    /// </summary>
    public static class PartCatalog
    {
        // Tank
        public const float TankDefaultRadius = 1.5f;
        public const float TankDefaultHeight = 6f;
        public const float TankMinRadius = 0.3f;
        public const float TankMaxRadius = 5f;
        public const float TankMinHeight = 1f;
        public const float TankMaxHeight = 15f;

        // Separator
        public const float SeparatorDefaultRadius = 1.5f;
        public const float SeparatorMinRadius = 0.3f;
        public const float SeparatorMaxRadius = 5f;

        // Hull ring (M-05): diameter comes from the host node, the player picks
        // the height with the aim. Wall 128 mm = 16 outer + 96 cavity + 16 liner.
        public const float HullRingDefaultRadius = 1.5f;
        public const float HullRingDefaultHeight = 1f;
        public const float HullRingMinRadius = 0.3f;
        public const float HullRingMaxRadius = 5f;
        public const float HullRingMinHeight = 0.2f;
        public const float HullRingMaxHeight = 6f;

        // Physics placeholders
        private const double SeparatorSteelDensity = 7850d;

        /// <summary>Orion capsule mass (placeholder, like the engines), kg.</summary>
        private const double CapsuleMassKg = 10400d;

        /// <summary>One RCS block (matches FlightSystemsConfig.RcsDryKgPerBlock), kg.</summary>
        private const double RcsBlockMassKg = 15d;

        /// <summary>
        /// Top radius of the capsule's physics body (fraction of the bottom one):
        /// the capsule is a cone from the heat shield (Ø5 m) to the nose. This is
        /// enough for overlaps/rays; the exact silhouette comes from the visual.
        /// </summary>
        private const float CapsuleTopRadiusFraction = 0.38f;

        // Joint strengths, N: high enough that the rocket survives a drop from a
        // few metres; the separator is the weakest — it must break first.
        private const double EngineJointNewtons = 1.5e8d;
        private const double TankJointNewtons = 1.5e8d;
        private const double SeparatorJointNewtons = 3.0e7d;

        /// <summary>Display name of the part kind (for UI/icons/tooltips).</summary>
        public static string DisplayName(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.Engine: return "Двигатель";
                case PartKind.FuelTank: return "Топливный бак";
                case PartKind.Capsule: return "Капсула Orion";
                case PartKind.RcsBlock: return "Блок РСУ";
                case PartKind.Fairing: return "Обтекатель";
                case PartKind.HullRing: return "Кольцо корпуса M-05";
                default: return "Отделитель";
            }
        }

        /// <summary>
        /// Part name with the engine spec: the engine gets the model appended
        /// ("Engine AJ10-118K"), for other parts the spec is irrelevant.
        /// </summary>
        public static string DisplayName(PartKind kind, EngineSpec engine)
        {
            if (kind != PartKind.Engine || engine == null)
            {
                return DisplayName(kind);
            }

            return "Двигатель " + engine.DisplayName;
        }

        /// <summary>Clamp sizes to the allowed limits (otherwise mesh/physics break).</summary>
        public static void ClampSizes(PartKind kind, ref float radius, ref float height)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                    radius = Mathf.Clamp(radius, TankMinRadius, TankMaxRadius);
                    height = Mathf.Clamp(height, TankMinHeight, TankMaxHeight);
                    break;
                case PartKind.Separator:
                    radius = Mathf.Clamp(radius, SeparatorMinRadius, SeparatorMaxRadius);
                    height = ProceduralSeparator.Thickness;
                    break;
                case PartKind.Fairing:
                    radius = Mathf.Clamp(radius, SeparatorMinRadius, TankMaxRadius);
                    height = ProceduralSeparator.Thickness;
                    break;
                case PartKind.HullRing:
                    radius = Mathf.Clamp(radius, HullRingMinRadius, HullRingMaxRadius);
                    height = Mathf.Clamp(height, HullRingMinHeight, HullRingMaxHeight);
                    break;
                case PartKind.Capsule:
                    // Capsule sizes are fixed by the model — leave them alone.
                    break;
                case PartKind.RcsBlock:
                    // Fixed model: one block is 1.12 m across and 0.72 m deep.
                    radius = 0.56f;
                    height = 1.12f;
                    break;
                default:
                    radius = 1f;
                    height = 1f;
                    break;
            }
        }

        /// <summary>Metrics for the given sizes (engine — from its model template).</summary>
        public static PartMetrics Measure(PartKind kind, float sizeA, float sizeB, EngineTemplate engine)
        {
            return Measure(kind, TankForm.Cylinder, sizeA, sizeA, sizeB, null, engine);
        }

        /// <summary>
        /// Metrics for the given sizes (engine — from its model template).
        /// For the tank: form — cylinder or cone, bottomRadius/topRadius — end
        /// radii (the top one is ignored for a cylinder), height — length.
        /// </summary>
        public static PartMetrics Measure(
            PartKind kind, TankForm form, float bottomRadius, float topRadius, float height,
            EngineTemplate engine)
        {
            return Measure(kind, form, bottomRadius, topRadius, height, null, engine);
        }

        /// <summary>
        /// Same as Measure above, but with a tank load:
        /// tank mass is computed from the load (TankMassModel). null —
        /// the default load from the PropellantLoads registry.
        /// </summary>
        public static PartMetrics Measure(
            PartKind kind, TankForm form, float bottomRadius, float topRadius, float height,
            PropellantLoad load, EngineTemplate engine)
        {
            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    float radius = Mathf.Clamp(bottomRadius, TankMinRadius, TankMaxRadius);
                    bool cone = form == TankForm.Cone;
                    float top = Mathf.Clamp(cone ? topRadius : radius, TankMinRadius, TankMaxRadius);
                    height = Mathf.Clamp(height, TankMinHeight, TankMaxHeight);

                    // Tank mass from form, size and load: volume,
                    // wall metal (with a bulkhead), insulation and propellant —
                    // in TankMassModel (volume/area — from ProceduralFuelTank).
                    TankMassInfo info = TankMassModel.Compute(radius, top, height, load, 1d);
                    return new PartMetrics(radius, top, height, height * 0.5f, 0f, height,
                        info.TotalMassKg, TankJointNewtons, 1e12d,
                        info.DryMassKg, info.PropellantMassKg, info.VolumeM3, info.Load);
                }

                case PartKind.Separator:
                {
                    float radius = Mathf.Clamp(bottomRadius, SeparatorMinRadius, SeparatorMaxRadius);
                    float thickness = ProceduralSeparator.Thickness;
                    // Solid ribbed disc: mass from the same feature set the mesh is built from.
                    double mass = ProceduralSeparator.MetalVolume(radius) * SeparatorSteelDensity;
                    return new PartMetrics(radius, radius, thickness, 0f, -thickness * 0.5f, thickness * 0.5f, mass, SeparatorJointNewtons, 1e12d);
                }

                case PartKind.Fairing:
                    // Base plate only; the shell mass is added by MeasureFairing as sections are built.
                    return MeasureFairing(bottomRadius, 0d);

                case PartKind.HullRing:
                    return MeasureHullRing(bottomRadius, height, false);

                case PartKind.Capsule:
                    // Dimensions come from the model (CapsuleTemplate); input numbers are irrelevant.
                    return Capsule != null ? Capsule.Metrics : default;

                case PartKind.RcsBlock:
                    // Fixed model: one block, 15 kg (the flight model's RCS hardware
                    // mass). The pivot is the hull contact point, so the shape is
                    // centred on it and spans ±HalfHeight for the vessel bottom.
                    return new PartMetrics(
                        0.56f, 0.56f, 1.12f, 0f, -0.56f, 0.56f,
                        RcsBlockMassKg, TankJointNewtons, 1e12d);

                default:
                    return engine != null ? engine.Metrics : default;
            }
        }

        // ─── Hull ring (M-05) ────────────────────────────────────────────────

        /// <summary>
        /// Hull ring metrics: the radius comes from the host node (the outer skin
        /// is flush with it), the height from the aim. The mass follows the state:
        /// an open ring has no liner panels.
        /// </summary>
        public static PartMetrics MeasureHullRing(float radius, float height, bool sealedRing)
        {
            radius = Mathf.Clamp(radius, HullRingMinRadius, HullRingMaxRadius);
            height = Mathf.Clamp(height, HullRingMinHeight, HullRingMaxHeight);
            double mass = ProceduralHullRing.EstimateMassKg(radius, height, sealedRing);
            return new PartMetrics(radius, radius, height, height * 0.5f, 0f, height,
                mass, TankJointNewtons, 1e12d);
        }

        /// <summary>Hull-ring materials (registered by the builder).</summary>
        public static Material HullRingHullMaterial { get; private set; }

        public static Material HullRingPrimerMaterial { get; private set; }

        public static Material HullRingLinerMaterial { get; private set; }

        public static Material HullRingSolderMaterial { get; private set; }

        public static Material HullRingSteelMaterial { get; private set; }

        /// <summary>External status band: green — cavity open, amber — liner sealed.</summary>
        public static Material HullRingIndicatorOpenMaterial { get; private set; }

        public static Material HullRingIndicatorSealedMaterial { get; private set; }

        public static void RegisterHullRingMaterials(
            Material hull, Material primer, Material liner, Material solder, Material steel,
            Material indicatorOpen, Material indicatorSealed)
        {
            HullRingHullMaterial = hull;
            HullRingPrimerMaterial = primer;
            HullRingLinerMaterial = liner;
            HullRingSolderMaterial = solder;
            HullRingSteelMaterial = steel;
            HullRingIndicatorOpenMaterial = indicatorOpen;
            HullRingIndicatorSealedMaterial = indicatorSealed;
        }

        /// <summary>
        /// Create the five M-05 materials on demand (builder, icon renderer, edit-mode
        /// tests). Without this, a visual built before the builder's Awake gets null
        /// materials and renders pink.
        /// </summary>
        public static void EnsureHullRingMaterials()
        {
            if (HullRingHullMaterial != null)
            {
                return;
            }

            Material hull = CreateHullRingMaterial(
                "HullRingHull", new Color(0.63f, 0.645f, 0.655f), 1f, 0.50f);
            Material primer = CreateHullRingMaterial(
                "HullRingPrimer", new Color(0.10f, 0.28f, 0.12f), 0f, 0.22f);
            // The cavity is deep and shadowed: a faint green self-lit term keeps
            // the 96 mm recess readable instead of a black hole.
            primer.SetColor("_EmissiveColor", new Color(0.010f, 0.030f, 0.012f));
            primer.SetFloat("_EmissiveColorMode", 1f);
            primer.SetFloat("_EmissiveIntensity", 1f);
            Material liner = CreateHullRingMaterial(
                "HullRingLiner", new Color(0.50f, 0.48f, 0.42f), 0f, 0.25f, true);
            // The liner sits inside a closed cavity and gets almost no direct
            // light; a faint warm self-lit term keeps it readable as a sealed wall.
            liner.SetColor("_EmissiveColor", new Color(0.025f, 0.023f, 0.018f));
            liner.SetColor("_EmissiveColorLDR", new Color(0.025f, 0.023f, 0.018f));
            liner.SetFloat("_EmissiveColorMode", 1f);
            liner.SetFloat("_EmissiveIntensity", 1f);
            Material solder = CreateHullRingMaterial(
                "HullRingSolder", new Color(0.16f, 0.13f, 0.11f), 1f, 0.45f, true);
            Material steel = CreateHullRingMaterial(
                "HullRingSteel", new Color(0.20f, 0.21f, 0.22f), 1f, 0.48f);
            Material indicatorOpen = CreateHullRingMaterial(
                "HullRingIndicatorOpen", new Color(0.055f, 0.42f, 0.10f), 0f, 0.30f);
            indicatorOpen.SetColor("_EmissiveColor", new Color(0.020f, 0.110f, 0.030f));
            indicatorOpen.SetFloat("_EmissiveColorMode", 1f);
            indicatorOpen.SetFloat("_EmissiveIntensity", 1f);
            Material indicatorSealed = CreateHullRingMaterial(
                "HullRingIndicatorSealed", new Color(0.72f, 0.36f, 0.055f), 0f, 0.30f);
            indicatorSealed.SetColor("_EmissiveColor", new Color(0.130f, 0.050f, 0.005f));
            indicatorSealed.SetFloat("_EmissiveColorMode", 1f);
            indicatorSealed.SetFloat("_EmissiveIntensity", 1f);
            RegisterHullRingMaterials(hull, primer, liner, solder, steel, indicatorOpen, indicatorSealed);
        }

        private static Material CreateHullRingMaterial(
            string name, Color color, float metallic, float smoothness, bool doubleSided = false)
        {
            Shader shader = Shader.Find("HDRP/Lit");
            if (shader == null)
            {
                Debug.LogError("[HullRing] шейдер HDRP/Lit не найден.");
                return null;
            }

            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            if (doubleSided)
            {
                // The liner is a concave surface seen from inside the ring: a
                // single-sided HDRP material culls it and the cavity shows through.
                material.SetFloat("_DoubleSidedEnable", 1f);
                material.SetFloat("_CullMode", 0f);
                material.SetFloat("_CullModeForward", 0f);
                material.SetFloat("_DoubleSidedNormalMode", 0f);
                material.EnableKeyword("_DOUBLESIDED_ON");
            }

            material.enableInstancing = true;
            return material;
        }

        // ─── Fairing ─────────────────────────────────────────────────────────

        /// <summary>Plate / shell materials of the fairing (registered by the builder).</summary>
        public static Material FairingPlateMaterial { get; private set; }

        public static Material FairingShellMaterial { get; private set; }

        public static void RegisterFairingMaterials(Material plate, Material shell)
        {
            FairingPlateMaterial = plate;
            FairingShellMaterial = shell;
        }

        /// <summary>
        /// Fairing base plate metrics (a 40 mm disc like the separator, pivot in the middle). The mass is
        /// the plate plus the whole shell, so draft mass, ΔV and the flight model see the fairing.
        /// </summary>
        public static PartMetrics MeasureFairing(float plateRadius, double shellMassKg)
        {
            float radius = Mathf.Clamp(plateRadius, SeparatorMinRadius, TankMaxRadius);
            float thickness = ProceduralSeparator.Thickness;
            double mass = FairingShell.PlateMassKg(radius) + shellMassKg;
            return new PartMetrics(radius, radius, thickness, 0f, -thickness * 0.5f, thickness * 0.5f,
                mass, TankJointNewtons, 1e12d);
        }

        /// <summary>
        /// Tall metrics for the inventory icon and preview only: CreateVisual sees Height &gt; 1 and draws a
        /// demo shell on the plate so the icon shows a fairing, not a flat disc.
        /// </summary>
        public static PartMetrics MeasureFairingIcon(float plateRadius)
        {
            float radius = Mathf.Clamp(plateRadius, SeparatorMinRadius, TankMaxRadius);
            float height = radius * 3.2f;
            return new PartMetrics(radius, radius, height, height * 0.5f, 0f, height, 0d, TankJointNewtons, 1e12d);
        }

        // ─── Visual ──────────────────────────────────────────────────────────

        /// <summary>
        /// Capsule template (Orion): prefab and dimensions measured from the meshes
        /// (pivot — bottom on the axis center, like the engine). Registered by the
        /// builder at startup (RegisterCapsule): there is a single capsule per
        /// catalog, so there is no need to carry it through every CreateVisual/Measure call.
        /// </summary>
        public sealed class CapsuleTemplate
        {
            public GameObject Prefab;
            public PartMetrics Metrics;
            public Vector3 ModelOffset;
        }

        /// <summary>Capsule template; null — not registered (no prefab in the scene).</summary>
        public static CapsuleTemplate Capsule { get; private set; }

        /// <summary>
        /// Measure the capsule from the prefab (once, at builder startup). Dimensions
        /// are world-space, like for engines; the physics body is a cone from the heat
        /// shield (bottom radius) to the nose (top radius — fraction CapsuleTopRadiusFraction).
        /// </summary>
        public static void RegisterCapsule(GameObject prefab)
        {
            if (prefab == null)
            {
                Capsule = null;
                return;
            }

            GameObject probe = UnityEngine.Object.Instantiate(prefab);
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.position = Vector3.zero;
            Bounds bounds = EngineTemplate.MeasureWorldBounds(probe);
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(probe);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }

            var center = new Vector3(
                (bounds.min.x + bounds.max.x) * 0.5f,
                0f,
                (bounds.min.z + bounds.max.z) * 0.5f);
            float height = Mathf.Max(0.05f, bounds.size.y);
            float radius = Mathf.Max(0.05f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f);
            float topRadius = radius * CapsuleTopRadiusFraction;

            Capsule = new CapsuleTemplate
            {
                Prefab = prefab,
                Metrics = new PartMetrics(
                    radius, topRadius, height, height * 0.5f, 0f, height,
                    CapsuleMassKg, TankJointNewtons, 1e12d),
                ModelOffset = new Vector3(-center.x, -bounds.min.y, -center.z),
            };
        }

        /// <summary>
        /// RCS block model (Models/Parts/RCS): a saddle-mounted thruster block. Registered
        /// by the builder at startup; CreateVisual puts four of them around every fuel tank.
        /// </summary>
        public static GameObject RcsBlock { get; private set; }

        /// <summary>Register the RCS block model (null — tanks without visual RCS).</summary>
        public static void RegisterRcsBlock(GameObject prefab)
        {
            RcsBlock = prefab;
        }

        /// <summary>Assembled part visual: pivot root and its renderers.</summary>
        public sealed class PartVisual
        {
            public GameObject Root;
            public Renderer[] Renderers;

            private Material[][] natural;

            /// <summary>Remember the original materials (to restore after the ghost).</summary>
            public void CaptureNatural()
            {
                natural = new Material[Renderers.Length][];
                for (int i = 0; i < Renderers.Length; i++)
                {
                    natural[i] = Renderers[i] != null ? Renderers[i].sharedMaterials : null;
                }
            }

            /// <summary>Paint all renderers with one material (ghost/invalid).</summary>
            public void SetMaterial(Material material)
            {
                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (Renderers[i] == null)
                    {
                        continue;
                    }

                    var materials = new Material[Renderers[i].sharedMaterials.Length];
                    for (int m = 0; m < materials.Length; m++)
                    {
                        materials[m] = material;
                    }

                    Renderers[i].sharedMaterials = materials;
                }
            }

            /// <summary>Restore the original materials.</summary>
            public void RestoreNatural()
            {
                if (natural == null)
                {
                    return;
                }

                for (int i = 0; i < Renderers.Length; i++)
                {
                    if (Renderers[i] != null && natural[i] != null)
                    {
                        Renderers[i].sharedMaterials = natural[i];
                    }
                }
            }
        }

        /// <summary>
        /// Player collision of a hull ring: a ring of convex boxes through the
        /// wall (24 sectors). The centre stays open, so the crew can walk inside;
        /// the box tops form the flange floor. A convex MeshCollider of the whole
        /// ring would fill the passage with a solid disc.
        /// </summary>
        private static void BuildHullRingWallColliders(Transform root, float radius, float height)
        {
            const int sectors = 24;
            const float wall = 0.128f;
            float chord = 2f * radius * Mathf.Tan(Mathf.PI / sectors);
            float centerRadius = Mathf.Max(0.05f, radius - (wall * 0.5f));

            for (int i = 0; i < sectors; i++)
            {
                float angle = (Mathf.PI * 2f * i) / sectors;
                float ca = Mathf.Cos(angle);
                float sa = Mathf.Sin(angle);
                var go = new GameObject("HullRingWall " + (i + 1));
                go.transform.SetParent(root, false);
                go.transform.localPosition = new Vector3(ca * centerRadius, height * 0.5f, sa * centerRadius);
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(ca, 0f, sa), Vector3.up);

                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(chord, height, wall);
            }
        }

        /// <summary>
        /// Build the part visual for ready metrics. Original materials are
        /// captured; the ghost overlay is painted separately (SetMaterial).
        /// withCollider: placed parts get a collider marked with VesselPartCollider
        /// (the player cannot pass through the vessel): the capsule by its
        /// dimensions, and a conical tank gets a convex mesh of its side; the
        /// placement ghost and previews get no collider.
        /// </summary>
        public static PartVisual CreateVisual(
            PartKind kind, PartMetrics metrics, EngineTemplate engine,
            Material tankSide, Material tankCap, Material separatorMaterial, Transform parent,
            bool withCollider = true, bool hullRingSealed = false)
        {
            var root = new GameObject(DisplayName(kind, engine != null ? engine.Spec : null));
            root.transform.SetParent(parent, false);

            switch (kind)
            {
                case PartKind.FuelTank:
                {
                    var tank = root.AddComponent<ProceduralFuelTank>();
                    tank.Radius = metrics.Radius;
                    tank.TopRadius = metrics.TopRadius > 0.01f ? metrics.TopRadius : metrics.Radius;
                    tank.Height = metrics.Height;
                    tank.Material = tankSide;
                    tank.CapMaterial = tankCap != null ? tankCap : tankSide;
                    tank.Rebuild();
                    break;
                }

                case PartKind.Separator:
                {
                    var separator = root.AddComponent<ProceduralSeparator>();
                    separator.Radius = metrics.Radius;
                    separator.Material = separatorMaterial;
                    separator.Rebuild();
                    break;
                }

                case PartKind.Fairing:
                {
                    var fairing = root.AddComponent<ProceduralFairing>();
                    fairing.PlateRadius = metrics.Radius;
                    fairing.PlateMaterial = FairingPlateMaterial != null ? FairingPlateMaterial : separatorMaterial;
                    fairing.ShellMaterial = FairingShellMaterial != null ? FairingShellMaterial : fairing.PlateMaterial;
                    fairing.BuildColliders = withCollider;
                    fairing.Shell = metrics.Height > 1f
                        ? FairingShell.CreateDemo(metrics.Radius, metrics.Height)
                        : new FairingShell();
                    fairing.Rebuild();
                    break;
                }

                case PartKind.HullRing:
                {
                    EnsureHullRingMaterials();
                    var ring = root.AddComponent<ProceduralHullRing>();
                    ring.Radius = metrics.Radius;
                    ring.Height = metrics.Height;
                    ring.Sealed = hullRingSealed;
                    ring.HullMaterial = HullRingHullMaterial != null ? HullRingHullMaterial : tankSide;
                    ring.PrimerMaterial = HullRingPrimerMaterial != null ? HullRingPrimerMaterial : tankCap;
                    ring.LinerMaterial = HullRingLinerMaterial != null ? HullRingLinerMaterial : tankCap;
                    ring.SolderMaterial = HullRingSolderMaterial != null ? HullRingSolderMaterial : tankSide;
                    ring.SteelMaterial = HullRingSteelMaterial != null ? HullRingSteelMaterial : tankSide;
                    ring.IndicatorMaterial = hullRingSealed
                        ? (HullRingIndicatorSealedMaterial != null ? HullRingIndicatorSealedMaterial : tankSide)
                        : (HullRingIndicatorOpenMaterial != null ? HullRingIndicatorOpenMaterial : tankSide);
                    ring.Rebuild();
                    break;
                }

                case PartKind.Capsule:
                {
                    if (Capsule == null || Capsule.Prefab == null)
                    {
                        break;                           // capsule not registered — root only
                    }

                    GameObject model = UnityEngine.Object.Instantiate(Capsule.Prefab, root.transform);
                    OperatorMonitorLayout.Apply(model);
                    // Do NOT touch the model rotation: the prefab root carries a
                    // Blender axis conversion, like the engines — zeroing it lays the capsule on its side.
                    model.transform.localPosition = Capsule.ModelOffset;
                    break;
                }

                case PartKind.RcsBlock:
                {
                    if (RcsBlock == null)
                    {
                        break;                           // model not registered — root only
                    }

                    // The FBX root carries the Blender axis conversion; the extra 180°
                    // yaw puts the model's outward axis (-Z after import) along the
                    // part's +Z, so a part rotation is LookRotation(outward, up) and
                    // the model origin (hull contact point) sits on the part pivot.
                    var pose = new GameObject("RCS Block Model");
                    pose.transform.SetParent(root.transform, false);
                    pose.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    GameObject block = UnityEngine.Object.Instantiate(RcsBlock, pose.transform, false);
                    block.transform.localPosition = Vector3.zero;

                    // Five nozzles feed the flight model: the factory reads markers on
                    // the part visual and gets thrust/torque from their transforms.
                    // Marker forward is the exhaust, the force is backwards.
                    for (int i = 0; i < RcsBlockPlacement.NozzleMouths.Length; i++)
                    {
                        var nozzle = new GameObject("RCS Nozzle " + (i + 1));
                        nozzle.transform.SetParent(root.transform, false);
                        nozzle.transform.localPosition = RcsBlockPlacement.NozzleMouths[i];
                        Vector3 exhaust = RcsBlockPlacement.NozzleExhausts[i];
                        Vector3 up = Mathf.Abs(Vector3.Dot(exhaust, Vector3.up)) > 0.9f
                            ? Vector3.forward
                            : Vector3.up;
                        nozzle.transform.localRotation = Quaternion.LookRotation(exhaust, up);

                        var marker = nozzle.AddComponent<Galilego.Flight.FlightMountMarker>();
                        marker.Kind = Galilego.Flight.ThrusterKind.Rcs;
                        marker.ThrustN = RcsBlockPlacement.NozzleThrustN;
                        marker.IspSeconds = RcsBlockPlacement.NozzleIspSeconds;
                        marker.PropellantKg = RcsBlockPlacement.NozzlePropellantKg;
                    }

                    break;
                }

                default:
                {
                    if (engine == null || engine.Prefab == null)
                    {
                        break;                           // no model — part root only
                    }

                    GameObject model = UnityEngine.Object.Instantiate(engine.Prefab, root.transform);
                    // Do NOT touch the model rotation: the FBX root carries a 270° X
                    // rotation (mesh data is in Blender axes) — zeroing it lays the engine on its side.
                    model.transform.localPosition = engine.ModelOffset;
                    break;
                }
            }

            var visual = new PartVisual
            {
                Root = root,
                Renderers = root.GetComponentsInChildren<Renderer>(true)
            };
            visual.CaptureNatural();

            if (withCollider && metrics.Radius > 0.01f && kind != PartKind.RcsBlock)
            {
                if (kind == PartKind.Capsule)
                {
                    // Capsule: colliders from the model meshes (OrionCollisionBuilder):
                    // convex shell wedges, fine mesh around the hatch (the opening stays
                    // an opening), door box. Non-convex MeshColliders are unusable here:
                    // Physics.ComputePenetration ignores them.
                    if (!OrionCollisionBuilder.Build(root.transform))
                    {
                        // Fallback (the model lacks the required meshes): non-convex
                        // colliders per mesh. Meshes that already have a collider
                        // (monitor housings) are left alone.
                        MeshFilter[] modelMeshes = root.GetComponentsInChildren<MeshFilter>(true);
                        for (int i = 0; i < modelMeshes.Length; i++)
                        {
                            MeshFilter filter = modelMeshes[i];
                            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
                            {
                                continue;
                            }

                            var hull = filter.gameObject.AddComponent<MeshCollider>();
                            hull.sharedMesh = filter.sharedMesh;
                            hull.convex = false;
                        }
                    }
                }
                else
                {
                    MeshFilter filter = root.GetComponent<MeshFilter>();
                    if (kind == PartKind.HullRing)
                    {
                        // The ring is a hollow tube: a convex hull would seal the
                        // passage. Convex wall sectors keep the hole walkable and
                        // give the top flange a floor for the player.
                        BuildHullRingWallColliders(root.transform, metrics.Radius, metrics.Height);
                    }
                    else if ((kind == PartKind.FuelTank || kind == PartKind.Separator || kind == PartKind.Fairing)
                        && filter != null && filter.sharedMesh != null)
                    {
                        // Tank/separator: the procedural mesh is already the exact body
                        // (flat-capped cylinder/cone, thin ring). A primitive gets both
                        // wrong for the player: a CapsuleCollider puts a hemisphere on a
                        // flat tank top (the player sinks into the rim), and a short wide
                        // separator collapses into a sphere of its outer radius that fills
                        // the capsule hatch placed above it. The ring's convex hull is the
                        // flat disc the player should stand on.
                        var hull = root.AddComponent<MeshCollider>();
                        hull.convex = true;
                        hull.sharedMesh = filter.sharedMesh;
                    }
                    else
                    {
                        // Part collider for the player: a capsule by dimensions (axis — local Y).
                        var capsule = root.AddComponent<CapsuleCollider>();
                        capsule.direction = 1;
                        capsule.radius = metrics.Radius;
                        capsule.height = Mathf.Max(metrics.Height, metrics.Radius * 2f);
                        capsule.center = new Vector3(0f, metrics.Center, 0f);
                    }
                }

                root.AddComponent<VesselPartCollider>();
            }
            else
            {
                // Placement ghost and previews: the model's own colliders (e.g.
                // monitor housings inside the Orion capsule) must stay silent:
                // the ghost lives under the launch pad, whose SurfaceSite treats
                // them as site structures, so the ghost would catch its own volume
                // and block placement. They would also occlude the placement ray.
                Collider[] ownColliders = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < ownColliders.Length; i++)
                {
                    ownColliders[i].enabled = false;
                }
            }

            return visual;
        }

        /// <summary>
        /// Engine template: the model prefab and its dimensions measured from the
        /// meshes (pivot — bottom on the axis center), plus the spec (EngineSpec) —
        /// propellant pair, thrust, impulse, mass. Measured once at startup; the
        /// spec is matched by model name (EngineSpecs.MatchPrefab).
        /// </summary>
        public sealed class EngineTemplate
        {
            public EngineSpec Spec;
            public GameObject Prefab;
            public PartMetrics Metrics;
            public Vector3 ModelOffset;

            public static EngineTemplate Measure(GameObject prefab)
            {
                if (prefab == null)
                {
                    return null;
                }

                EngineSpec spec = EngineSpecs.MatchPrefab(prefab.name);
                if (spec == null)
                {
                    Debug.LogError("[Build] нет паспорта двигателя для модели «" + prefab.name
                        + "» — добавь запись в EngineSpecs.");
                    return null;
                }

                GameObject probe = UnityEngine.Object.Instantiate(prefab);
                probe.hideFlags = HideFlags.HideAndDontSave;
                probe.transform.position = Vector3.zero;
                Bounds bounds = MeasureWorldBounds(probe);
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(probe);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(probe);
                }

                var center = new Vector3(
                    (bounds.min.x + bounds.max.x) * 0.5f,
                    0f,
                    (bounds.min.z + bounds.max.z) * 0.5f);
                float height = Mathf.Max(0.05f, bounds.size.y);
                float radius = Mathf.Max(0.05f, Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f);

                // Top radius from the vertices of the model's TOP quarter: nozzle
                // engines are a wide bell at the bottom and a narrow cluster of
                // units on top. Vertices are only available with Read/Write enabled
                // on the model; otherwise the bottom radius is used.
                // Radii are measured about the axis that ModelOffset later aligns
                // with the pivot (world zero would skew non-centered FBX meshes).
                float topRadius = MeasureTopRadius(probe, bounds, radius);

                return new EngineTemplate
                {
                    Spec = spec,
                    Prefab = prefab,
                    Metrics = new PartMetrics(radius, topRadius, height, height * 0.5f, 0f, height, spec.MassKg, EngineJointNewtons, 1e12d),
                    ModelOffset = new Vector3(-center.x, -bounds.min.y, -center.z),
                };
            }

            /// <summary>
            /// Radius of the top quarter of the bounds from the mesh vertices — an
            /// estimate of how the body tapers upward (engine: bell → cluster).
            /// Probe world coordinates (it stands at the origin): the bounds are also
            /// measured in world space, so differently scaled FBX nodes are accounted for.
            /// </summary>
            private static float MeasureTopRadius(GameObject probe, Bounds bounds, float radius)
            {
                float from = bounds.min.y + (bounds.size.y * 0.75f);
                bool any = false;
                float maxRadial = 0f;
                MeshFilter[] meshes = probe.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < meshes.Length; i++)
                {
                    Mesh mesh = meshes[i].sharedMesh;
                    if (mesh == null || !mesh.isReadable)
                    {
                        continue;
                    }

                    Vector3[] vertices = mesh.vertices;
                    Transform node = meshes[i].transform;
                    for (int v = 0; v < vertices.Length; v++)
                    {
                        Vector3 world = node.TransformPoint(vertices[v]);
                        if (world.y < from)
                        {
                            continue;
                        }

                        any = true;
                        // ModelOffset centers the model on its world bounds center,
                        // so the engine's mathematical axis here must be that center,
                        // not (0, 0, 0).
                        float dx = world.x - bounds.center.x;
                        float dz = world.z - bounds.center.z;
                        float radial = Mathf.Sqrt((dx * dx) + (dz * dz));
                        if (radial > maxRadial)
                        {
                            maxRadial = radial;
                        }
                    }
                }

                return any ? Mathf.Clamp(maxRadial, 0.05f, radius) : radius;
            }

            /// <summary>
            /// World bounds of the hierarchy. World, not local: the FBX model carries
            /// its unit scale (×100, centimeters) on the root node, and bounds computed
            /// in "root local axes" would lose that scale.
            /// </summary>
            internal static Bounds MeasureWorldBounds(GameObject root)
            {
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                var bounds = new Bounds(Vector3.zero, Vector3.zero);
                bool has = false;
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                    {
                        continue;
                    }

                    if (!has)
                    {
                        bounds = renderers[i].bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }

                return bounds;
            }
        }
    }
}
