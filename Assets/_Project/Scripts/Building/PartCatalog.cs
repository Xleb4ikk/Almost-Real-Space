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
    /// separator — ring volume of steel, engine — placeholder until real
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

        // Physics placeholders
        private const double SeparatorSteelDensity = 7850d;

        /// <summary>Orion capsule mass (placeholder, like the engines), kg.</summary>
        private const double CapsuleMassKg = 10400d;

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
                case PartKind.Capsule:
                    // Capsule sizes are fixed by the model — leave them alone.
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
                    float width = ProceduralSeparator.RingWidthFraction * radius;
                    float thickness = ProceduralSeparator.Thickness;
                    float midRadius = radius - (width * 0.5f);
                    double volume = 2d * Math.PI * midRadius * width * thickness;
                    double mass = volume * SeparatorSteelDensity;
                    return new PartMetrics(radius, radius, thickness, 0f, -thickness * 0.5f, thickness * 0.5f, mass, SeparatorJointNewtons, 1e12d);
                }

                case PartKind.Capsule:
                    // Dimensions come from the model (CapsuleTemplate); input numbers are irrelevant.
                    return Capsule != null ? Capsule.Metrics : default;

                default:
                    return engine != null ? engine.Metrics : default;
            }
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
            bool withCollider = true)
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

            if (withCollider && metrics.Radius > 0.01f)
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
                    bool conical = kind == PartKind.FuelTank
                        && metrics.TopRadius > 0.01f
                        && Mathf.Abs(metrics.TopRadius - metrics.Radius) > 1e-3f;
                    if (conical)
                    {
                        // A capsule cannot describe the cone: the convex side mesh follows
                        // the taper, so the player does not hit an invisible cylinder of the
                        // bottom radius. The mesh is already built above (tank Rebuild).
                        var hull = root.AddComponent<MeshCollider>();
                        hull.convex = true;
                        MeshFilter filter = root.GetComponent<MeshFilter>();
                        hull.sharedMesh = filter != null ? filter.sharedMesh : null;
                    }
                    else
                    {
                        // Part collider for the player: a capsule by dimensions (axis — local
                        // Y; the separator is shorter than its diameter, PhysX clamps it to
                        // a sphere — the flat ring still blocks the way).
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
