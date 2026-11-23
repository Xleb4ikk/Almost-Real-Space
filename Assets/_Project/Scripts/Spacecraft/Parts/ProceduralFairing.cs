using System.Collections.Generic;
using Galilego.Building;
using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Procedural fairing: a flat white base plate (the separator mesh, same 40 mm disc) on the
    /// part root plus N petals (children) that make the shell. Axis is local Y, the plate pivot is
    /// its middle, the shell starts on the plate top.
    ///
    /// Build mode: the petals have NO active colliders (the player walks through) and open like orange
    /// peel when the camera comes close (OpenByProximity). Physics mode: SetSolid(true) enables a
    /// convex collider slice per petal (marked VesselPartCollider + PlayerObstacleOnly), petals stay closed.
    /// Jettison: DetachPetals hands the petal transforms over to the debris visuals.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ProceduralFairing : MonoBehaviour
    {
        [Header("Плита")]
        [Min(0.1f)] public float PlateRadius = 1.5f;
        public bool ShowPlate = true;
        public Material PlateMaterial;

        [Header("Оболочка")]
        public Material ShellMaterial;
        [Min(0.5f)] public float TextureMetersPerTile = 2f;

        [Tooltip("Строить коллайдеры долек (у призраков и иконок — нет).")]
        public bool BuildColliders = true;

        [Header("Раскрытие (только стройка)")]
        public bool OpenByProximity;
        [Range(0f, 80f)] public float OpenAngleDegrees = 34f;
        [Tooltip("Ближе этого расстояния до поверхности дольки раскрыты полностью, м.")]
        public float OpenNearMeters = 2.5f;
        [Tooltip("Дальше этого расстояния дольки закрыты, м.")]
        public float OpenFarMeters = 7f;
        [Tooltip("Скорость сглаживания раскрытия, 1/с.")]
        public float OpenSpeed = 5f;

        /// <summary>Shell data (runtime only: built by SpacecraftBuilder and shared with PlacedPart.Fairing).</summary>
        [System.NonSerialized]
        public FairingShell Shell = new FairingShell();

        public const float PlateThickness = ProceduralSeparator.Thickness;

        /// <summary>Local height of the plate top: where the shell starts.</summary>
        public const float ShellBaseLocalY = PlateThickness * 0.5f;

        /// <summary>A petal handed over for jettison.</summary>
        public struct DetachedPetal
        {
            public Transform Transform;
            public Vector3 OutwardWorld;
            public float MassKg;
        }

        private sealed class Petal
        {
            public Transform T;
            public Quaternion Yaw;
            public float Angle;
            public Collider[] Colliders;
        }

        private readonly List<Petal> petals = new List<Petal>();
        private readonly List<Mesh> collisionMeshes = new List<Mesh>();
        private Mesh plateMesh;
        private Mesh petalMesh;
        private float open;
        private bool solid;

        /// <summary>The petals were jettisoned (they now belong to the debris visuals).</summary>
        public bool PetalsDetached { get; private set; }

        public int PetalCount
        {
            get { return petals.Count; }
        }

        public float OpenAmount
        {
            get { return open; }
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnDestroy()
        {
            if (plateMesh != null)
            {
                Destroy(plateMesh);
            }

            if (petalMesh != null)
            {
                Destroy(petalMesh);
            }

            for (int i = 0; i < collisionMeshes.Count; i++)
            {
                if (collisionMeshes[i] != null)
                {
                    Destroy(collisionMeshes[i]);
                }
            }
        }

        /// <summary>Rebuild the plate and the petals for the current Shell. Cheap when the petal count is unchanged.</summary>
        public void Rebuild()
        {
            RebuildPlate();
            RebuildPetals();
        }

        private void RebuildPlate()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            MeshRenderer plateRenderer = GetComponent<MeshRenderer>();
            if (plateMesh == null)
            {
                plateMesh = new Mesh { hideFlags = HideFlags.DontSave };
            }

            ProceduralSeparator.FillSeparator(plateMesh, Mathf.Max(0.1f, PlateRadius), PlateThickness, 64, 0.25f);
            filter.sharedMesh = plateMesh;
            plateRenderer.sharedMaterial = PlateMaterial;
            plateRenderer.enabled = ShowPlate;
        }

        private void RebuildPetals()
        {
            int wanted = Shell != null && Shell.HasShell ? Shell.Petals : 0;
            if (wanted == 0 || PetalsDetached && wanted != petals.Count)
            {
                ClearPetals();
                PetalsDetached = false;
            }

            if (wanted == 0)
            {
                return;
            }

            if (petals.Count != wanted)
            {
                ClearPetals();
                PetalsDetached = false;
            }

            if (petalMesh == null)
            {
                petalMesh = new Mesh { hideFlags = HideFlags.DontSave };
            }

            FairingMeshBuilder.FillPetal(petalMesh, Shell, TextureMetersPerTile);

            int slices = BuildColliders ? FairingMeshBuilder.CollisionSlices(wanted) : 0;
            while (collisionMeshes.Count < slices)
            {
                collisionMeshes.Add(new Mesh { hideFlags = HideFlags.DontSave });
            }

            for (int s = 0; s < slices; s++)
            {
                FairingMeshBuilder.FillCollisionSlice(collisionMeshes[s], Shell, s, slices);
            }

            float step = (2f * Mathf.PI) / wanted;
            float r0 = Shell.BaseRadius;
            bool create = petals.Count == 0;
            for (int i = 0; i < wanted; i++)
            {
                float angle = (i + 0.5f) * step;
                Petal petal;
                if (create)
                {
                    petal = CreatePetal(i, slices);
                    petals.Add(petal);
                }
                else
                {
                    petal = petals[i];
                    RefreshPetalColliders(petal, slices);
                }

                petal.Angle = angle;
                petal.Yaw = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                petal.T.localPosition = new Vector3(r0 * Mathf.Cos(angle), ShellBaseLocalY, r0 * Mathf.Sin(angle));
            }

            ApplyOpen();
            SetSolid(solid);
        }

        private Petal CreatePetal(int index, int slices)
        {
            var go = new GameObject("Petal " + index);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = petalMesh;
            MeshRenderer meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = ShellMaterial;

            var petal = new Petal { T = go.transform };
            if (slices > 0)
            {
                petal.Colliders = new Collider[slices];
                for (int s = 0; s < slices; s++)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.convex = true;
                    mc.sharedMesh = collisionMeshes[s];
                    mc.enabled = false;
                    petal.Colliders[s] = mc;
                }

                // The player support treats these as part of the ship, but only as obstacles
                // (a sloped fairing wall is never a floor).
                go.AddComponent<VesselPartCollider>();
                go.AddComponent<PlayerObstacleOnly>();
            }

            return petal;
        }

        private void RefreshPetalColliders(Petal petal, int slices)
        {
            if (petal.Colliders == null)
            {
                return;
            }

            for (int s = 0; s < petal.Colliders.Length && s < slices; s++)
            {
                var mc = petal.Colliders[s] as MeshCollider;
                if (mc != null)
                {
                    mc.sharedMesh = null;               // force a re-cook of the refilled mesh
                    mc.sharedMesh = collisionMeshes[s];
                }
            }
        }

        private void ClearPetals()
        {
            for (int i = 0; i < petals.Count; i++)
            {
                if (petals[i].T != null)
                {
                    Destroy(petals[i].T.gameObject);
                }
            }

            petals.Clear();
        }

        /// <summary>Physics mode: petal colliders on/off.</summary>
        public void SetSolid(bool value)
        {
            solid = value;
            for (int i = 0; i < petals.Count; i++)
            {
                Collider[] colliders = petals[i].Colliders;
                if (colliders == null)
                {
                    continue;
                }

                for (int c = 0; c < colliders.Length; c++)
                {
                    if (colliders[c] != null)
                    {
                        colliders[c].enabled = value;
                    }
                }
            }

            if (value)
            {
                // Physics never shows the "player came close" spread.
                OpenByProximity = false;
                open = 0f;
                ApplyOpen();
            }
        }

        public void SetShellMaterial(Material material)
        {
            ShellMaterial = material;
            for (int i = 0; i < petals.Count; i++)
            {
                MeshRenderer r = petals[i].T != null ? petals[i].T.GetComponent<MeshRenderer>() : null;
                if (r != null)
                {
                    r.sharedMaterial = material;
                }
            }
        }

        public void SetShadows(UnityEngine.Rendering.ShadowCastingMode mode)
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                all[i].shadowCastingMode = mode;
            }
        }

        // ─── Opening ─────────────────────────────────────────────────────────

        private void Update()
        {
            float target = OpenByProximity && petals.Count > 0 && !solid ? ComputeOpenTarget() : 0f;
            if (Mathf.Abs(target - open) < 0.002f)
            {
                if (!Mathf.Approximately(open, target))
                {
                    open = target;
                    ApplyOpen();
                }

                return;
            }

            open += (target - open) * (1f - Mathf.Exp(-Time.unscaledDeltaTime * OpenSpeed));
            ApplyOpen();
        }

        private float ComputeOpenTarget()
        {
            Camera camera = Camera.main;
            if (camera == null || Shell == null || !Shell.HasShell)
            {
                return 0f;
            }

            Vector3 p = transform.InverseTransformPoint(camera.transform.position);
            float radius = Mathf.Sqrt((p.x * p.x) + (p.z * p.z));
            float d = Shell.DistanceTo(radius, p.y - ShellBaseLocalY);
            float k = Mathf.Clamp01(Mathf.InverseLerp(OpenFarMeters, OpenNearMeters, d));
            return k * k * (3f - (2f * k));
        }

        private void ApplyOpen()
        {
            Quaternion tilt = Quaternion.AngleAxis(OpenAngleDegrees * open, Vector3.back);
            for (int i = 0; i < petals.Count; i++)
            {
                if (petals[i].T != null)
                {
                    petals[i].T.localRotation = petals[i].Yaw * tilt;
                }
            }
        }

        // ─── Jettison ────────────────────────────────────────────────────────

        /// <summary>
        /// Unparent the petals (world pose kept) and hand them to the caller: they become debris.
        /// Colliders are switched off. The component keeps the plate; Rebuild() brings the petals back.
        /// </summary>
        public void DetachPetals(List<DetachedPetal> into)
        {
            if (petals.Count == 0 || Shell == null)
            {
                return;
            }

            float massEach = (float)(Shell.MassKg / petals.Count);
            for (int i = 0; i < petals.Count; i++)
            {
                Petal petal = petals[i];
                if (petal.T == null)
                {
                    continue;
                }

                Vector3 outward = transform.TransformDirection(new Vector3(Mathf.Cos(petal.Angle), 0f, Mathf.Sin(petal.Angle)));
                if (petal.Colliders != null)
                {
                    for (int c = 0; c < petal.Colliders.Length; c++)
                    {
                        if (petal.Colliders[c] != null)
                        {
                            petal.Colliders[c].enabled = false;
                        }
                    }
                }

                petal.T.SetParent(null, true);
                into.Add(new DetachedPetal { Transform = petal.T, OutwardWorld = outward, MassKg = massEach });
            }

            petals.Clear();
            PetalsDetached = true;
        }
    }
}
