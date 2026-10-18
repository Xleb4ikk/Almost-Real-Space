using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Decor instance matrices for GPU instancing, computed in Burst in parallel
    /// (the main thread no longer composes a TRS for every blade each frame).
    /// Near — local TRS × chunk transform; Far (billboard) — turns toward the camera
    /// around the surface normal + a size curve by distance.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorMatrixJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<GroundDecorInstance> Instances;

        /// <summary>NativeSlice, not NativeArray: the far trees of all chunks
        /// are written into one shared buffer, each into its own segment. The safety
        /// check is disabled because the segments overlap in the underlying allocation
        /// and jobs from different chunks run in parallel.</summary>
        [WriteOnly, NativeDisableContainerSafetyRestriction]
        public NativeSlice<Matrix4x4> WorldMatrices;

        public float4x4 ChunkToWorld;
        public float3 CameraWorld;
        public bool Billboard;
        public bool FlatOnGround;
        public float NearDistance;
        public float FarDistance;
        public float BillboardNearScale;
        public float BillboardFarScale;
        /// <summary>Distance at which the far LOD starts to grow (∝ d).
        /// Set to the layer's DensityCoreMeters: up to the edge of the flat core the
        /// density is full and size growth is not needed, and starting it earlier would
        /// cause a silhouette jump at the near→far boundary.</summary>
        public float DistanceCore;
        /// <summary>Mesh height fraction up to the pivot (0 — pivot at the base, as on a
        /// grass blade; 0.5 — centered quad). The billboard is raised by it
        /// so the far LOD stands on the ground the same way as the near one.</summary>
        public float BillboardPivotFraction;
        /// <summary>Current time (Time.time) and blade sprout duration.
        /// Scale grows from 0 to 1 over FadeSeconds from its BirthTime — new blades
        /// appear one by one and smoothly, without bands or «batches».</summary>
        public float Now;
        public float FadeSeconds;
        /// <summary>Duration of the sink under the ground (s). Instances with DeathTime &gt; 0
        /// fade out with scale 1→0 over this time from their DeathTime: blades dropped from
        /// the new selection sink into the ground instead of disappearing frame by frame.</summary>
        public float SinkSeconds;

        /// <summary>Global size multiplier (fade at the radius edge, pool retirement).
        /// Only applied when ApplyGlobalScale; other callers leave the fields zero.</summary>
        public bool ApplyGlobalScale;
        public float GlobalScale;

        /// <summary>Growth by distance to the camera (tree proxies): ScaleBoost &gt; 1 —
        /// size grows smoothly up to ScaleBoost at the distance BoostStart + BoostSpan.</summary>
        public float ScaleBoost;
        public float BoostStart;
        public float BoostSpan;

        /// <summary>Instance decimation: every Stride-th is processed.
        /// 0 or 1 — no decimation. At long distance a tree occupies
        /// 1-2 pixels, so drawing it whole is a pure waste of CPU on
        /// matrix recalculation and GPU on instancing.</summary>
        public int Stride;

        public void Execute(int index)
        {
            GroundDecorInstance instance = Instances[Stride > 1 ? index * Stride : index];
            float3 localPos = instance.Position;
            float3 localUp = instance.Normal;
            float upLength = math.length(localUp);
            localUp = upLength > 1e-6f ? localUp / upLength : new float3(0f, 1f, 0f);
            float scale = math.max(1e-4f, instance.Scale);
            // Sprouting: from BirthTime the scale grows 0 → 1 over FadeSeconds.
            if (FadeSeconds > 0f && instance.BirthTime > 0f)
            {
                scale *= math.saturate((Now - instance.BirthTime) / FadeSeconds);
            }

            // Sinking: from DeathTime the scale falls 1 → 0 over SinkSeconds
            // (a blade's pivot is at the base, so visually it «sinks into the
            // ground» with the same motion it appears with). Dead instances
            // collapse to a point — their matrix is degenerate and the GPU does not draw them.
            if (SinkSeconds > 0f && instance.DeathTime > 0f)
            {
                scale *= math.saturate((instance.DeathTime + SinkSeconds - Now) / SinkSeconds);
            }

            if (ApplyGlobalScale)
            {
                scale *= GlobalScale;
            }

            if (Billboard)
            {
                float3 worldPos = math.mul(ChunkToWorld, new float4(localPos, 1f)).xyz;
                float3 worldUp = math.mul(ChunkToWorld, new float4(localUp, 0f)).xyz;
                float worldUpLength = math.length(worldUp);
                worldUp = worldUpLength > 1e-6f ? worldUp / worldUpLength : new float3(0f, 1f, 0f);

                float3 toCamera = CameraWorld - worldPos;
                float3 facing = toCamera - (worldUp * math.dot(toCamera, worldUp));
                if (math.dot(facing, facing) < 1e-8f)
                {
                    facing = new float3(0f, 0f, 1f) - (worldUp * worldUp.z);
                }

                facing = math.normalize(facing);
                quaternion billboardRotation = quaternion.LookRotationSafe(facing, worldUp);

                float distance = math.length(toCamera);
                float blend = math.saturate((distance - NearDistance) / math.max(1f, FarDistance - NearDistance));

                // Size ∝ distance (capped by BillboardFarScale) pairs with density
                // ∝ (R/d)² for constant coverage; growth starts at DistanceCore.
                float grow = math.min(
                    BillboardFarScale,
                    math.max(1f, distance / math.max(1f, DistanceCore)));
                float size = scale * grow;

                // Orientation morphs too: at the near edge the far LOD stands like a
                // 3D blade, toward the far edge it turns to face the camera.
                quaternion rotation = billboardRotation;
                if (blend < 1f)
                {
                    quaternion chunkRotation = quaternion.LookRotationSafe(
                        ChunkToWorld.c2.xyz, ChunkToWorld.c1.xyz);
                    quaternion alignedRotation = math.mul(chunkRotation, LocalRotation(instance, localUp));
                    rotation = blend <= 0f
                        ? alignedRotation
                        : math.slerp(alignedRotation, billboardRotation, blend);
                }

                float3 finalPosition = worldPos + (worldUp * (BillboardPivotFraction * size));
                WorldMatrices[index] = ToMatrix(float4x4.TRS(finalPosition, rotation, new float3(size)));
                return;
            }

            if (ScaleBoost > 1f)
            {
                float3 boostPos = math.mul(ChunkToWorld, new float4(localPos, 1f)).xyz;
                float boostDistance = math.length(boostPos - CameraWorld);
                scale *= math.lerp(1f, ScaleBoost, math.saturate((boostDistance - BoostStart) / math.max(1f, BoostSpan)));
            }

            quaternion localRotation = LocalRotation(instance, localUp);
            WorldMatrices[index] = ToMatrix(math.mul(
                ChunkToWorld, float4x4.TRS(localPos, localRotation, new float3(scale))));
        }

        private quaternion LocalRotation(GroundDecorInstance instance, float3 up)
        {
            float3 reference = math.abs(up.y) < 0.99f ? new float3(0f, 1f, 0f) : new float3(1f, 0f, 0f);
            float3 tangent = math.normalize(math.cross(reference, up));
            if (FlatOnGround)
            {
                return quaternion.LookRotationSafe(up, tangent);
            }

            float3 forward = math.cross(up, tangent);
            quaternion rotation = math.mul(quaternion.AxisAngle(up, instance.Yaw), quaternion.LookRotationSafe(forward, up));
            if (instance.LeanDegrees > 0.001f)
            {
                float3 windForward = math.mul(rotation, new float3(0f, 0f, 1f));
                float3 leanAxis = math.cross(up, windForward);
                if (math.dot(leanAxis, leanAxis) > 1e-8f)
                {
                    rotation = math.mul(
                        quaternion.AxisAngle(math.normalize(leanAxis), math.radians(instance.LeanDegrees)), rotation);
                }
            }

            return rotation;
        }

        private static Matrix4x4 ToMatrix(float4x4 m)
        {
            return new Matrix4x4(
                new Vector4(m.c0.x, m.c0.y, m.c0.z, m.c0.w),
                new Vector4(m.c1.x, m.c1.y, m.c1.z, m.c1.w),
                new Vector4(m.c2.x, m.c2.y, m.c2.z, m.c2.w),
                new Vector4(m.c3.x, m.c3.y, m.c3.z, m.c3.w));
        }
    }
}
