using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Galilego.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Р’РµСЂС€РёРЅР° СЂРµР»СЊРµС„Р° РІ С„РѕСЂРјР°С‚Рµ, РєРѕС‚РѕСЂС‹Р№ РјРѕР¶РЅРѕ Р·Р°Р»РёС‚СЊ РІ Mesh РѕРґРЅРёРј
    /// SetVertexBufferData. РџРѕСЂСЏРґРѕРє РїРѕР»РµР№ = РїРѕСЂСЏРґРѕРє VertexAttribute РІ
    /// layout: Position, Normal, TexCoord1 (f32x3), TexCoord2 (f32x4).
    ///
    /// РћРґРёРЅ interleaved-Р±СѓС„РµСЂ РІРјРµСЃС‚Рѕ mesh.vertices / normals / SetUVs(1) /
    /// SetUVs(2): СЂР°РЅСЊС€Рµ СЌС‚Рѕ Р±С‹Р»Рё С‡РµС‚С‹СЂРµ РѕС‚РґРµР»СЊРЅС‹С… managed-РјР°СЃСЃРёРІР° Рё С‡РµС‚С‹СЂРµ
    /// РѕР±СЂР°С‰РµРЅРёСЏ Рє РјРµС€Сѓ, РїР»СЋСЃ РґРІР° List<Vector3> СЂР°РґРё РєРѕРЅРІРµСЂС‚Р°С†РёРё. Р—РґРµСЃСЊ
    /// РІРµСЂС€РёРЅС‹ СЃС‡РёС‚Р°СЋС‚СЃСЏ РІ РґР¶РѕР±Рµ СЃСЂР°Р·Сѓ РІ РЅСѓР¶РЅРѕР№ СЂР°СЃРєР»Р°РґРєРµ, Рё РЅР° РіР»Р°РІРЅС‹Р№ РїРѕС‚РѕРє
    /// РїСЂРёС…РѕРґРёС‚ РѕРґРЅР° memcpy.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TerrainVertex
    {
        public float3 Position;
        public float3 Normal;
        public float3 SurfaceDir;
        public float4 Extra;
    }

    /// <summary>Р’РµСЂС€РёРЅР° РІРѕРґРЅРѕР№ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё: РїРѕР·РёС†РёСЏ, РЅРѕСЂРјР°Р»СЊ, РіР»СѓР±РёРЅР° РІ UV2.
    /// РћС‚РґРµР»СЊРЅР°СЏ СЃС‚СЂСѓРєС‚СѓСЂР°, РїРѕС‚РѕРјСѓ С‡С‚Рѕ Сѓ РІРѕРґС‹ РЅРµС‚ TexCoord1 Рё СЂР°СЃРєР»Р°РґРєР°
    /// РґСЂСѓРіР°СЏ вЂ” РѕР±С‰РёР№ Р±СѓС„РµСЂ СЃ СЂРµР»СЊРµС„РѕРј РµС‘ Р±С‹ СЃРґРІРёРЅСѓР».</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WaterVertex
    {
        public float3 Position;
        public float3 Normal;
        public float4 Extra;
    }

    /// <summary>РРґРµРЅС‚РёС„РёРєР°С‚РѕСЂ СѓР·Р»Р° quadtree РґР»СЏ Р·Р°РїСЂРѕСЃР° РїРѕСЃС‚СЂРѕР№РєРё. РћС‚РґРµР»СЊРЅР°СЏ
    /// РїСѓР±Р»РёС‡РЅР°СЏ СЃС‚СЂСѓРєС‚СѓСЂР°, РїРѕС‚РѕРјСѓ С‡С‚Рѕ РІР»РѕР¶РµРЅРЅС‹Р№ Node Сѓ СЂРµРЅРґРµСЂРµСЂР° РїСЂРёРІР°С‚РЅС‹Р№,
    /// Р° РґР¶РѕР± Рё РїСѓР» Р¶РёРІСѓС‚ РІ РґСЂСѓРіРѕРј С„Р°Р№Р»Рµ. Р”СѓР±Р»РёСЂРѕРІР°РЅРёРµ С‚СЂРёРІРёР°Р»СЊРЅРѕРµ, РЅРѕ
    /// СЃРІСЏР·Р°С‚СЊ РёС… РЅР°РїСЂСЏРјСѓСЋ РЅРµР»СЊР·СЏ Р±РµР· РІС‹РЅРµСЃРµРЅРёСЏ Node РЅР°СЂСѓР¶Сѓ.</summary>
    public struct ChunkNodePlan
    {
        public int Face;
        public int Depth;
        public int Ix;
        public int Iy;
    }

    /// <summary>
    /// Р—Р°РїСЂРѕСЃ РЅР° Р°СЃРёРЅС…СЂРѕРЅРЅСѓСЋ РїРѕСЃС‚СЂРѕР№РєСѓ С‡Р°РЅРєР°. Р’Р»Р°РґРµРЅРёРµ РјР°СЃСЃРёРІР°РјРё вЂ” Сѓ РїСѓР»Р°:
    /// РѕРЅРё Persistent, РїРµСЂРµРёСЃРїРѕР»СЊР·СѓСЋС‚СЃСЏ Рё РІРѕР·РІСЂР°С‰Р°СЋС‚СЃСЏ РІ Finalize Р»РёР±Рѕ РїСЂРё
    /// РѕС‚РјРµРЅРµ. РЈС‚РµС‡РєР° С…РѕС‚СЏ Р±С‹ РѕРґРЅРѕРіРѕ С‚Р°РєРѕРіРѕ РјР°СЃСЃРёРІР° РЅР° РєР°Р¶РґС‹Р№ РѕС‚РјРµРЅС‘РЅРЅС‹Р№
    /// Р·Р°РїСЂРѕСЃ РІРёРґРЅР° С‚РѕР»СЊРєРѕ РІ РѕС‚С‡С‘С‚Рµ Unity Рѕ РЅР°С‚РёРІРЅРѕР№ РїР°РјСЏС‚Рё, РїРѕСЌС‚РѕРјСѓ РѕС‚РјРµРЅР°
    /// СЃРґРµР»Р°РЅР° РѕРґРЅРѕР№ С„СѓРЅРєС†РёРµР№ Рё РІС‹Р·С‹РІР°РµС‚СЃСЏ РѕС‚РѕРІСЃСЋРґСѓ.
    /// </summary>
    public sealed class ChunkBuildRequest
    {
        public ChunkNodePlan Plan;
        public int Res;
        public int N;
        public int Grid;
        public int CoreCount;
        public int TotalVerts;
        public int IndexCount;
        public float Priority;
        public int ParamsVersion;
        public JobHandle Handle;

        public NativeArray<double3> Dirs;
        public NativeArray<double> Heights;
        public NativeArray<float> Masks;
        public NativeArray<float> Details;
        public NativeArray<float3> Positions;
        public NativeArray<TerrainVertex> Vertices;
        public NativeArray<WaterVertex> WaterVertices;
        /// <summary>1 = РІ С‡Р°РЅРєРµ РµСЃС‚СЊ РІРѕРґР°, 0 = РЅРµС‚. РћРґРёРЅ СЌР»РµРјРµРЅС‚: Р·Р°РїРёСЃСЊ РёР·
        /// РґР¶РѕР±С‹ РґРѕР»Р¶РЅР° Р±С‹С‚СЊ РІРёРґРЅР° РЅР° РіР»Р°РІРЅРѕРј РїРѕС‚РѕРєРµ.</summary>
        public NativeArray<int> WaterFlags;
        public NativeArray<float3> BoundsMinMax;

        /// <summary>Р¦РµРЅС‚СЂ СѓР·Р»Р° РІ body-fixed double. РќСѓР¶РµРЅ С„РёРЅР°Р»РёР·Р°С†РёРё: РјРµС€
        /// СЃС‚СЂРѕРёС‚СЃСЏ РІ СЃРёСЃС‚РµРјРµ РѕС‚СЃС‡С‘С‚Р° РѕС‚ С†РµРЅС‚СЂР° С‡Р°РЅРєР°, Р° transform СЃС‚Р°РІРёС‚СЃСЏ
        /// СѓР¶Рµ РїРѕСЃР»Рµ Р·Р°Р»РёРІРєРё.</summary>
        public Vector3d CenterAstro;

        /// <summary>Р“Р»СѓР±РёРЅР° СЋР±РєРё СЂРµР»СЊРµС„Р° вЂ” РµСЋ СЂР°СЃС€РёСЂСЏСЋС‚СЃСЏ РіСЂР°РЅРёС†С‹ РјРµС€Р°.</summary>
        public double SkirtDepth;

        public bool HasWater => WaterFlags.Length > 0 && WaterFlags[0] != 0;
    }

    /// <summary>
    /// Burst-СЃРѕРІРјРµСЃС‚РёРјР°СЏ РєРѕРїРёСЏ CubeSphere.Direction.
    ///
    /// Р”СѓР±Р»РёСЂРѕРІР°РЅРёРµ Р·РґРµСЃСЊ РЅР°РјРµСЂРµРЅРЅРѕРµ, Рё СЌС‚Рѕ РµРґРёРЅСЃС‚РІРµРЅРЅРѕРµ РјРµСЃС‚Рѕ, РіРґРµ
    /// В«РїСЂРѕСЃС‚Рѕ РІС‹Р·РІР°С‚СЊ СЃСѓС‰РµСЃС‚РІСѓСЋС‰РёР№ РјРµС‚РѕРґВ» РЅРµРІРѕР·РјРѕР¶РЅРѕ: Vector3d.Normalized
    /// Р¶РёРІС‘С‚ РІ Core Рё РЅРµРґРѕСЃС‚СѓРїРµРЅ Burst. Р¤РѕСЂРјСѓР»Р° РїРµСЂРµРЅРµСЃРµРЅР° РѕРґРёРЅ РІ РѕРґРёРЅ вЂ”
    /// С‚Р° Р¶Рµ РїРµСЂРµСЃС‚Р°РЅРѕРІРєР° РѕСЃРµР№, С‚Рѕ Р¶Рµ РґРµР»РµРЅРёРµ РЅР° Math.Sqrt(xВІ+yВІ+zВІ), С‚РѕС‚ Р¶Рµ
    /// РїРѕСЂСЏРґРѕРє РѕРїРµСЂР°С†РёР№, вЂ” РёРЅР°С‡Рµ РІС‹СЃРѕС‚С‹ РїРѕРµРґСѓС‚ РЅР° РїРѕСЃР»РµРґРЅРµРј Р±РёС‚Рµ Рё С€РІС‹ РјРµР¶РґСѓ
    /// С‡Р°РЅРєР°РјРё СЂР°Р·СЉРµРґСѓС‚СЃСЏ. РџСЂРѕРІРµСЂСЏС‚СЊ СЌС‚Рѕ РЅР°РґРѕ СЃСЂР°РІРЅРµРЅРёРµРј РєР°СЂС‚РёРЅРєРё, Р° РЅРµ
    /// РіР»Р°Р·Р°РјРё РЅР° РіР»Р°Р·.
    /// </summary>
    public static class CubeSphereBurst
    {
        public static double3 Direction(int face, double u, double v)
        {
            double a = (u * 2d) - 1d;
            double b = (v * 2d) - 1d;
            double x;
            double y;
            double z;
            switch (face)
            {
                case 0: x = 1d; y = b; z = -a; break;
                case 1: x = -1d; y = b; z = a; break;
                case 2: x = a; y = 1d; z = -b; break;
                case 3: x = a; y = -1d; z = b; break;
                case 4: x = a; y = b; z = 1d; break;
                default: x = -a; y = b; z = -1d; break;
            }

            double length = math.sqrt((x * x) + (y * y) + (z * z));
            if (length > 0d)
            {
                return new double3(x / length, y / length, z / length);
            }

            return double3.zero;
        }
    }

    /// <summary>
    /// РЁР°Рі 1 С†РµРїРѕС‡РєРё: РЅР°РїСЂР°РІР»РµРЅРёСЏ РєСѓР±-СЃС„РµСЂС‹ РґР»СЏ РІСЃРµР№ СЃРµС‚РєРё СЃ halo.
    /// Р Р°РЅСЊС€Рµ СЌС‚Рѕ Р±С‹Р» РІР»РѕР¶РµРЅРЅС‹Р№ С†РёРєР» РЅР° РіР»Р°РІРЅРѕРј РїРѕС‚РѕРєРµ вЂ” 4489 РёС‚РµСЂР°С†РёР№
    /// double-РјР°С‚РµРјР°С‚РёРєРё РЅР° С‡Р°РЅРє, С‚Рѕ РµСЃС‚СЊ Р·Р°РјРµС‚РЅР°СЏ РґРѕР»СЏ С‚РµС… СЃР°РјС‹С… 4 РјСЃ.
    /// </summary>
    [BurstCompile]
    public struct FillDirectionsJob : IJobParallelFor
    {
        public int Face;
        public int Grid;
        public double U0;
        public double V0;
        public double StepUv;

        [WriteOnly]
        public NativeArray<double3> Dirs;

        public void Execute(int index)
        {
            int gi = index / Grid;
            int gj = index - (gi * Grid);
            double u = U0 + ((gi - 1) * StepUv);
            double v = V0 + ((gj - 1) * StepUv);
            Dirs[index] = CubeSphereBurst.Direction(Face, u, v);
        }
    }

    /// <summary>
    /// РЁР°Рі 3 С†РµРїРѕС‡РєРё: СЃР±РѕСЂРєР° РІРµСЂС€РёРЅ СЂРµР»СЊРµС„Р° Рё РІРѕРґС‹ РёР· РЅР°РїСЂР°РІР»РµРЅРёР№, РІС‹СЃРѕС‚,
    /// РјР°СЃРѕРє Рё РґРµС‚Р°Р»РµР№. РќРѕСЂРјР°Р»Рё С†РµРЅС‚СЂР°Р»СЊРЅС‹РјРё СЂР°Р·РЅРѕСЃС‚СЏРјРё, С„Р»РёРї РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ
    /// СЂР°РґРёР°Р»Р°, СЋР±РєРё РІРЅРёР·, РіСЂР°РЅРёС†С‹.
    ///
    /// Р’СЃС‘ СЃС‡РёС‚Р°РµС‚СЃСЏ РІ float3/float4 РІ С‚РѕРј Р¶Рµ РїРѕСЂСЏРґРєРµ, С‡С‚Рѕ Рё СЃС‚Р°СЂР°СЏ
    /// managed-РІРµСЂСЃРёСЏ, С‡С‚РѕР±С‹ СЂРµР·СѓР»СЊС‚Р°С‚ СЃРѕРІРїР°Р»: Vector3.Cross = math.cross,
    /// Vector3.magnitude = math.length, РґРµР»РµРЅРёРµ РЅР° РґР»РёРЅСѓ вЂ” РґРµР»РµРЅРёРµРј, Р° РЅРµ
    /// СѓРјРЅРѕР¶РµРЅРёРµРј РЅР° РѕР±СЂР°С‚РЅРѕРµ.
    /// </summary>
    [BurstCompile]
    public struct AssembleChunkMeshJob : IJob
    {
        [ReadOnly] public NativeArray<double3> Dirs;
        [ReadOnly] public NativeArray<double> Heights;
        [ReadOnly] public NativeArray<float> Masks;
        [ReadOnly] public NativeArray<float> Details;

        public int Res;
        public int N;
        public int Grid;
        public int CoreCount;
        public int TotalVerts;
        public double Amplitude;
        public double Radius;
        public double SeaLevel;
        public double SkirtDepth;
        public double WaterSkirtDepth;
        public double3 CenterAstro;

        [WriteOnly] public NativeArray<float3> Positions;
        [WriteOnly] public NativeArray<TerrainVertex> Vertices;
        [WriteOnly] public NativeArray<WaterVertex> WaterVertices;
        [WriteOnly] public NativeArray<int> WaterFlags;
        public NativeArray<float3> BoundsMinMax;

        /// <summary>РќРѕСЂРјР°Р»РёР·Р°С†РёСЏ СЂРѕРІРЅРѕ РєР°Рє Сѓ Vector3.normalized: СЃ РїРѕСЂРѕРіРѕРј
        /// kEpsilon = 1e-5 Рё РґРµР»РµРЅРёРµРј, Р° РЅРµ СѓРјРЅРѕР¶РµРЅРёРµРј РЅР° РѕР±СЂР°С‚РЅСѓСЋ РґР»РёРЅСѓ.
        /// math.normalizesafe С‚РѕР¶Рµ РґРµР»РёС‚, РЅРѕ Р±РµР· РїРѕСЂРѕРіР°, Рё РЅР° РїСЂР°РєС‚РёРєРµ СЌС‚Рѕ
        /// РµРґРёРЅСЃС‚РІРµРЅРЅРѕРµ РјРµСЃС‚Рѕ, РіРґРµ РјРѕРі Р±С‹ СЂР°Р·СЉРµС…Р°С‚СЊСЃСЏ РїРѕСЃР»РµРґРЅРёР№ Р±РёС‚.</summary>
        private static float3 NormalizeLikeUnity(float3 v)
        {
            float mag = math.length(v);
            return mag > 1e-5f ? v / mag : float3.zero;
        }

        public void Execute()
        {
            int gridCount = Grid * Grid;

            // РџРѕР·РёС†РёРё РІ double, РїРѕС‚РѕРј РІ float вЂ” РєР°Рє РІ СЃС‚Р°СЂРѕР№ РІРµСЂСЃРёРё. РџРѕСЂСЏРґРѕРє
            // РІР°Р¶РµРЅ: double РЅР° СЂР°РґРёСѓСЃРµ РїР»Р°РЅРµС‚С‹ (~1.14e6) РґР°С‘С‚ ulp ~0.1 Рј, Рё
            // Р»СЋР±РѕРµ РїРµСЂРµСЃС‚Р°РІР»РµРЅРёРµ РѕРїРµСЂР°С†РёР№ СЃРјРµСЃС‚РёС‚ РІРµСЂС€РёРЅСѓ РЅР° СЃР°РЅС‚РёРјРµС‚СЂС‹.
            for (int gi = 0; gi < Grid; gi++)
            {
                for (int gj = 0; gj < Grid; gj++)
                {
                    int vi = (gi * Grid) + gj;
                    double3 d = Dirs[vi];
                    double rawHeight = Heights[vi] * Amplitude;
                    double3 absAstro = new double3(d.x, d.y, d.z) * (Radius + rawHeight);
                    double3 rel = absAstro - CenterAstro;
                    Positions[vi] = new float3((float)rel.x, (float)rel.z, (float)-rel.y);
                }
            }

            float3 centerUnity = new float3(
                (float)CenterAstro.x, (float)CenterAstro.z, (float)-CenterAstro.y);

            bool hasWater = SeaLevel > -1e29d;

            for (int row = 0; row < N; row++)
            {
                for (int col = 0; col < N; col++)
                {
                    int halo = ((row + 1) * Grid) + (col + 1);
                    int index = (row * N) + col;
                    float3 position = Positions[halo];

                    float3 dCol = Positions[halo + 1] - Positions[halo - 1];
                    float3 dRow = Positions[halo + Grid] - Positions[halo - Grid];
                    float3 normal = math.cross(dCol, dRow);
                    float normalLength = math.length(normal);
                    float3 radial = NormalizeLikeUnity(position + centerUnity);
                    float3 unit = normalLength > 1e-10f ? normal / normalLength : radial;
                    if (math.dot(unit, radial) < 0f)
                    {
                        unit = -unit;
                    }

                    double3 d = Dirs[halo];
                    double rawHeight = Heights[halo] * Amplitude;

                    Vertices[index] = new TerrainVertex
                    {
                        Position = position,
                        Normal = unit,
                        SurfaceDir = new float3((float)d.x, (float)d.y, (float)d.z),
                        Extra = new float4(
                            (float)rawHeight, math.dot(unit, radial), Masks[halo], Details[halo])
                    };

                    if (hasWater)
                    {
                        double3 absWater = new double3(d.x, d.y, d.z) * (Radius + SeaLevel);
                        double3 wrel = absWater - CenterAstro;
                        float3 wpos = new float3((float)wrel.x, (float)wrel.z, (float)-wrel.y);
                        WaterVertices[index] = new WaterVertex
                        {
                            Position = wpos,
                            Normal = NormalizeLikeUnity(wpos + centerUnity),
                            Extra = new float4((float)math.max(0d, SeaLevel - rawHeight), 0f, 0f, 0f)
                        };
                    }
                }
            }

            // Р§Р°РЅРє С†РµР»РёРєРѕРј РЅР°Рґ РјРѕСЂРµРј вЂ” РІРѕРґСѓ РЅРµ СЃС‚СЂРѕРёРј.
            if (hasWater)
            {
                hasWater = false;
                for (int k = 0; k < CoreCount; k++)
                {
                    if (WaterVertices[k].Extra.x > 0f)
                    {
                        hasWater = true;
                        break;
                    }
                }
            }

            WaterFlags[0] = hasWater ? 1 : 0;

            // Р®Р±РєРё СЂРµР»СЊРµС„Р°: РґСѓР±Р»РёСЂСѓРµРј СЂС‘Р±СЂР°, С‚РѕРїРёРј Рє С†РµРЅС‚СЂСѓ РїР»Р°РЅРµС‚С‹.
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = CoreCount + (edge * N);
                for (int k = 0; k < N; k++)
                {
                    int coreIndex = EdgeCoreIndex(Res, N, edge, k);
                    TerrainVertex core = Vertices[coreIndex];
                    float3 inward = NormalizeLikeUnity(core.Position + centerUnity);
                    Vertices[baseIndex + k] = new TerrainVertex
                    {
                        Position = core.Position - (inward * (float)SkirtDepth),
                        Normal = core.Normal,
                        SurfaceDir = core.SurfaceDir,
                        Extra = core.Extra
                    };

                    if (hasWater)
                    {
                        WaterVertex wcore = WaterVertices[coreIndex];
                        float3 winward = NormalizeLikeUnity(wcore.Position + centerUnity);
                        WaterVertices[baseIndex + k] = new WaterVertex
                        {
                            Position = wcore.Position - (winward * (float)WaterSkirtDepth),
                            Normal = wcore.Normal,
                            Extra = wcore.Extra
                        };
                    }
                }
            }

            BoundsMinMax[0] = float3.zero;
            BoundsMinMax[1] = float3.zero;
            if (hasWater)
            {
                AccumulateBounds(WaterVertices, TotalVerts);
            }
            else
            {
                AccumulateBounds(Vertices, TotalVerts);
            }
        }

        private void AccumulateBounds(NativeArray<TerrainVertex> source, int count)
        {
            float3 min = source[0].Position;
            float3 max = source[0].Position;
            for (int i = 1; i < count; i++)
            {
                float3 p = source[i].Position;
                min = math.min(min, p);
                max = math.max(max, p);
            }

            BoundsMinMax[0] = min;
            BoundsMinMax[1] = max;
        }

        private void AccumulateBounds(NativeArray<WaterVertex> source, int count)
        {
            float3 min = source[0].Position;
            float3 max = source[0].Position;
            for (int i = 1; i < count; i++)
            {
                float3 p = source[i].Position;
                min = math.min(min, p);
                max = math.max(max, p);
            }

            BoundsMinMax[0] = min;
            BoundsMinMax[1] = max;
        }

        /// <summary>РРЅРґРµРєСЃ core-РІРµСЂС€РёРЅС‹ РЅР° СЂРµР±СЂРµ СЋР±РєРё. РџРѕСЂСЏРґРѕРє СЂС‘Р±РµСЂ (row0,
        /// rowRes, col0, colRes) РѕР±СЏР·Р°РЅ СЃРѕРІРїР°РґР°С‚СЊ СЃ РїРѕСЂСЏРґРєРѕРј РІ РёРЅРґРµРєСЃР°С…,
        /// РёРЅР°С‡Рµ СЋР±РєР° РїСЂРёРєР»РµРёС‚СЃСЏ РЅРµ Рє С‚РѕР№ СЃС‚РѕСЂРѕРЅРµ.</summary>
        private static int EdgeCoreIndex(int res, int n, int edge, int k)
        {
            switch (edge)
            {
                case 0: return k;
                case 1: return (res * n) + k;
                case 2: return k * n;
                default: return (k * n) + res;
            }
        }
    }

    /// <summary>
    /// РџСѓР» Persistent-РјР°СЃСЃРёРІРѕРІ Рё РѕР±С‰РёС… РёРЅРґРµРєСЃРЅС‹С… Р±СѓС„РµСЂРѕРІ.
    ///
    /// РўРѕРїРѕР»РѕРіРёСЏ С‡Р°РЅРєР° Р·Р°РІРёСЃРёС‚ С‚РѕР»СЊРєРѕ РѕС‚ res, Р° РЅРµ РѕС‚ РїРѕР»РѕР¶РµРЅРёСЏ, РїРѕСЌС‚РѕРјСѓ
    /// РёРЅРґРµРєСЃС‹ СЃС‚СЂРѕСЏС‚СЃСЏ РћР”РРќ СЂР°Р· РЅР° РєР°Р¶РґРѕРµ res Рё РєРѕРїРёСЂСѓСЋС‚СЃСЏ РІ РјРµС€. Р Р°РЅСЊС€Рµ РЅР°
    /// РєР°Р¶РґС‹Р№ С‡Р°РЅРѕРє СЃРѕР±РёСЂР°Р»СЃСЏ СЃРІРѕР№ int[] РЅР° 51840 СЌР»РµРјРµРЅС‚РѕРІ Рё СЂР°Р·Р±РёСЂР°Р»СЃСЏ
    /// mesh.triangles вЂ” СЌС‚Рѕ Рё Р°Р»Р»РѕРєР°С†РёСЏ, Рё СЂР°Р·Р±РѕСЂ РЅР° СЃС‚РѕСЂРѕРЅРµ Unity.
    /// Р§РёСЃР»Рѕ РІРµСЂС€РёРЅ С‡Р°РЅРєР° Р·Р°РІРµРґРѕРјРѕ РјРµРЅСЊС€Рµ 65535, РїРѕСЌС‚РѕРјСѓ С…РІР°С‚Р°РµС‚ ushort.
    /// </summary>
    public static class SurfaceChunkPool
    {
        private static readonly Dictionary<int, NativeArray<ushort>> sharedIndices =
            new Dictionary<int, NativeArray<ushort>>();

        private static readonly List<ChunkBuildRequest> idle = new List<ChunkBuildRequest>();

        /// <summary>РРЅРґРµРєСЃС‹ РґР»СЏ res: core-СЃРµС‚РєР° РїР»СЋСЃ С‡РµС‚С‹СЂРµ СЋР±РєРё, РїРѕ 6 РёРЅРґРµРєСЃРѕРІ
        /// РЅР° РєРІР°Рґ. РљСЌС€РёСЂСѓСЋС‚СЃСЏ; РІС‹Р·С‹РІР°С‚СЊ РёР· РґР¶РѕР±С‹ РЅРµР»СЊР·СЏ.</summary>
        public static NativeArray<ushort> GetSharedIndices(int res)
        {
            if (sharedIndices.TryGetValue(res, out NativeArray<ushort> cached) && cached.IsCreated)
            {
                return cached;
            }

            int n = res + 1;
            int coreCount = n * n;
            int indexCount = (((n - 1) * (n - 1)) + (4 * (n - 1))) * 3;
            var indices = new NativeArray<ushort>(indexCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            int t = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int a = (row * n) + col;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    indices[t++] = (ushort)a;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)d;
                }
            }

            int coreIndexCount = (n - 1) * (n - 1) * 6;
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * n);
                for (int k = 0; k < n - 1; k++)
                {
                    int a;
                    int b;
                    switch (edge)
                    {
                        case 0: a = k; b = k + 1; break;
                        case 1: a = (res * n) + k; b = a + 1; break;
                        case 2: a = k * n; b = a + n; break;
                        default: a = (k * n) + res; b = a + n; break;
                    }

                    int c = baseIndex + k;
                    int d = baseIndex + k + 1;
                    indices[t++] = (ushort)a;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)b;
                    indices[t++] = (ushort)c;
                    indices[t++] = (ushort)d;
                }
            }

            if (t != indexCount || coreIndexCount + ((4 * (n - 1)) * 6) != indexCount)
            {
                // РЎС‚СЂР°С…РѕРІРєР° РѕС‚ С‚РёС…РѕР№ РїРѕСЂС‡Рё С‚РѕРїРѕР»РѕРіРёРё: РёРЅРґРµРєСЃС‹ СѓР¶Рµ Р·Р°РїРёСЃР°РЅС‹ РІ
                // РјРµС€, Рё РѕС€РёР±РєР° Р·РґРµСЃСЊ СЃС‚РѕРёР»Р° Р±С‹ РґС‹СЂ РІ СЂРµР»СЊРµС„Рµ РїРѕ РІСЃРµРјСѓ РєСЂСѓРіСѓ.
                indices.Dispose();
                throw new InvalidOperationException(
                    "SurfaceChunkPool: topology mismatch, t=" + t + " indexCount=" + indexCount);
            }

            sharedIndices[res] = indices;
            return indices;
        }

        public static int IndexCountFor(int res)
        {
            int n = res + 1;
            return (((n - 1) * (n - 1)) + (4 * (n - 1))) * 3;
        }

        public static ChunkBuildRequest Rent()
        {
            while (idle.Count > 0)
            {
                int last = idle.Count - 1;
                ChunkBuildRequest req = idle[last];
                idle.RemoveAt(last);
                if (!req.Dirs.IsCreated)
                {
                    continue;
                }

                return req;
            }

            return new ChunkBuildRequest();
        }

        /// <summary>Р“РѕС‚РѕРІРёС‚ РјР°СЃСЃРёРІС‹ Р·Р°РїСЂРѕСЃР° РїРѕРґ РєРѕРЅРєСЂРµС‚РЅС‹Р№ СЂР°Р·РјРµСЂ С‡Р°РЅРєР°.
        /// РџРµСЂРµРІС‹РґРµР»РµРЅРёРµ С‚РѕР»СЊРєРѕ РєРѕРіРґР° РїСѓР» РїСѓСЃС‚ РёР»Рё РІС‹СЂРѕСЃР»Р° РїРѕС‚СЂРµР±РЅРѕСЃС‚СЊ: РЅР°
        /// РїСЂР°РєС‚РёРєРµ РїРѕС‡С‚Рё РІСЃРµРіРґР° РїРµСЂРµРёСЃРїРѕР»СЊР·СѓРµС‚СЃСЏ, РїРѕС‚РѕРјСѓ С‡С‚Рѕ РІ РїРѕР»С‘С‚Рµ
        /// С‡РµСЂРµРґСѓСЋС‚СЃСЏ РѕРґРЅРё Рё С‚Рµ Р¶Рµ res.</summary>
        public static void Prepare(ChunkBuildRequest req, int res, int n, int grid, int totalVerts)
        {
            int gridCount = grid * grid;
            req.Res = res;
            req.N = n;
            req.Grid = grid;
            req.CoreCount = n * n;
            req.TotalVerts = totalVerts;
            req.IndexCount = IndexCountFor(res);

            EnsureDouble3(ref req.Dirs, gridCount);
            EnsureDouble(ref req.Heights, gridCount);
            EnsureFloat(ref req.Masks, gridCount);
            EnsureFloat(ref req.Details, gridCount);
            EnsureFloat3(ref req.Positions, gridCount);
            EnsureTerrain(ref req.Vertices, totalVerts);
            EnsureWater(ref req.WaterVertices, totalVerts);
            EnsureInt(ref req.WaterFlags, 1);
            EnsureFloat3(ref req.BoundsMinMax, 2);
        }

        public static void Return(ChunkBuildRequest req)
        {
            if (req == null)
            {
                return;
            }

            idle.Add(req);
        }

        /// <summary>РџРѕР»РЅС‹Р№ СЃР±СЂРѕСЃ РЅР° teardown: Persistent-РјР°СЃСЃРёРІС‹ РЅРµР»СЊР·СЏ
        /// РѕСЃС‚Р°РІР»СЏС‚СЊ Р¶РёС‚СЊ РјРµР¶РґСѓ play-СЃРµСЃСЃРёСЏРјРё, РёРЅР°С‡Рµ Unity РЅР°РїРёС€РµС‚ РѕР± СѓС‚РµС‡РєРµ
        /// РЅР°С‚РёРІРЅРѕР№ РїР°РјСЏС‚Рё РїСЂРё РІС‹С…РѕРґРµ.</summary>
        public static void ClearAll()
        {
            for (int i = 0; i < idle.Count; i++)
            {
                DisposeRequest(idle[i]);
            }

            idle.Clear();

            foreach (KeyValuePair<int, NativeArray<ushort>> kv in sharedIndices)
            {
                if (kv.Value.IsCreated)
                {
                    kv.Value.Dispose();
                }
            }

            sharedIndices.Clear();
        }

        public static void DisposeRequest(ChunkBuildRequest req)
        {
            if (req == null)
            {
                return;
            }

            DisposeIfCreated(ref req.Dirs);
            DisposeIfCreated(ref req.Heights);
            DisposeIfCreated(ref req.Masks);
            DisposeIfCreated(ref req.Details);
            DisposeIfCreated(ref req.Positions);
            DisposeIfCreated(ref req.Vertices);
            DisposeIfCreated(ref req.WaterVertices);
            DisposeIfCreated(ref req.WaterFlags);
            DisposeIfCreated(ref req.BoundsMinMax);
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
                array = default;
            }
        }

        private static void EnsureDouble3(ref NativeArray<double3> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<double3>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureDouble(ref NativeArray<double> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<double>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureFloat(ref NativeArray<float> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<float>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureInt(ref NativeArray<int> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<int>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureFloat3(ref NativeArray<float3> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<float3>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureTerrain(ref NativeArray<TerrainVertex> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<TerrainVertex>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static void EnsureWater(ref NativeArray<WaterVertex> array, int length)
        {
            if (array.IsCreated && array.Length >= length)
            {
                return;
            }

            DisposeIfCreated(ref array);
            array = new NativeArray<WaterVertex>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }
    }
}
