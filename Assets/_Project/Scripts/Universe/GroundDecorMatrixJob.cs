using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Матрицы инстансов декора на GPU-инстансинг: считается в Burst параллельно
    /// (главный поток больше не композирует TRS на каждую травинку каждый кадр).
    /// Near — локальный TRS × трансформ чанка; Far (billboard) — доворот к камере
    /// вокруг нормали поверхности + кривая размера по дистанции.
    /// </summary>
    [BurstCompile]
    public struct GroundDecorMatrixJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<GroundDecorInstance> Instances;

        [WriteOnly]
        public NativeArray<Matrix4x4> WorldMatrices;

        public float4x4 ChunkToWorld;
        public float3 CameraWorld;
        public bool Billboard;
        public bool FlatOnGround;
        public float NearDistance;
        public float FarDistance;
        public float BillboardNearScale;
        public float BillboardFarScale;
        /// <summary>Доля высоты меша до пивота (0 — пивот в основании, как у
        /// клинка травы; 0.5 — центрированный квад). Биллборд поднимаем на неё,
        /// чтобы дальний LOD стоял на земле так же, как ближний.</summary>
        public float BillboardPivotFraction;
        /// <summary>Текущее время (Time.time) и длительность прорастания травинки.
        /// Масштаб растёт от 0 к 1 за FadeSeconds от её BirthTime — новое
        /// появляется поштучно и плавно, без полос и «пачек».</summary>
        public float Now;
        public float FadeSeconds;

        public void Execute(int index)
        {
            GroundDecorInstance instance = Instances[index];
            float3 localPos = instance.Position;
            float3 localUp = instance.Normal;
            float upLength = math.length(localUp);
            localUp = upLength > 1e-6f ? localUp / upLength : new float3(0f, 1f, 0f);
            float scale = math.max(1e-4f, instance.Scale);
            // Прорастание: от BirthTime масштаб растёт 0 → 1 за FadeSeconds.
            if (FadeSeconds > 0f && instance.BirthTime > 0f)
            {
                scale *= math.saturate((Now - instance.BirthTime) / FadeSeconds);
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
                float size = scale * math.lerp(BillboardNearScale, BillboardFarScale, blend);

                // Ориентация тоже морфится: у ближней границы дальний LOD стоит
                // так же, как 3D-клинок (yaw+наклон), к дальней — доворачивается
                // к камере. Мгновенный разворот на границе near→far давал щелчок
                // силуэта; теперь переход невидим.
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
