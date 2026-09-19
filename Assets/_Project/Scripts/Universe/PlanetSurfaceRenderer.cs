using System.Collections.Generic;
using Galilego.Core;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Galilego.Universe
{
    /// <summary>
    /// РџР»Р°РЅРµС‚Р°СЂРЅС‹Р№ СЂРµРЅРґРµСЂ: cube-sphere СЃ quadtree LOD. Р—Р°РјРµРЅСЏРµС‚ СЃС‚Р°СЂС‹Р№ С‚Р°Р№Р»РѕРІС‹Р№
    /// BodyTerrainView Рё РїСЂРёРјРёС‚РёРІ-СЃС„РµСЂСѓ.
    ///
    /// Р“РµРѕРјРµС‚СЂРёСЏ: 6 РіСЂР°РЅРµР№ РєСѓР±Р° в†’ РµРґРёРЅРёС‡РЅС‹Рµ РЅР°РїСЂР°РІР»РµРЅРёСЏ (CubeSphere) в†’
    /// РІС‹СЃРѕС‚Р°/РјР°СЃРєР° РёР· Р•Р”РРќРћР™ TerrainNoise (С‚РѕС‚ Р¶Рµ Burst-job, С‡С‚Рѕ С„РёР·РёРєР°) в†’
    /// С‚РµР»Рѕ-fixed РїРѕР·РёС†РёРё (Radius + h). Р§Р°РЅРєРё вЂ” РґРµС‚Рё РѕР±С‰РµРіРѕ РєРѕСЂРЅСЏ, РєРѕС‚РѕСЂС‹Р№
    /// РЅР°СЃР»РµРґСѓРµС‚ С‚СЂР°РЅСЃС„РѕСЂРј С‚РµР»Р° (BodyView), РЅРѕ РёРјРµРµС‚ РєРѕРјРїРµРЅСЃРёСЂРѕРІР°РЅРЅС‹Р№ СЃРєРµР№Р»,
    /// РїРѕСЌС‚РѕРјСѓ РІРµСЂС€РёРЅС‹ Р¶РёРІСѓС‚ РІ РјРµС‚СЂР°С… РѕС‚ С†РµРЅС‚СЂР°.
    ///
    /// LOD: СѓР·РµР» РґРµР»РёС‚СЃСЏ, РїРѕРєР° РґРёСЃС‚Р°РЅС†РёСЏ РєР°РјРµСЂС‹ < СЂР°Р·РјРµСЂР° СѓР·Р»Р° Г— SplitFactor
    /// (Рё depth < MaxDepth). Р“РѕС‚РѕРІС‹Рµ РјРµС€Рё РєСЌС€РёСЂСѓСЋС‚СЃСЏ РїРѕ id СѓР·Р»Р° (СЂРµР»СЊРµС„
    /// СЃС‚Р°С‚РёС‡РµРЅ РІ С‚РµР»-fixed РєР°РґСЂРµ), РІРёРґРёРјС‹Р№ РЅР°Р±РѕСЂ вЂ” РїРѕ РєСѓР»Р»РёРЅРіСѓ Р·Р°РґРЅРµР№
    /// РїРѕР»СѓСЃС„РµСЂС‹. Р®Р±РєРё РїРѕ РєСЂР°СЏРј СЃРєСЂС‹РІР°СЋС‚ С‚СЂРµС‰РёРЅС‹ РјРµР¶РґСѓ СЂР°Р·РЅС‹РјРё LOD.
    /// РћРєРµР°РЅ вЂ” РІРЅСѓС‚СЂРё С‡Р°РЅРєРѕРІ: Р·Р°С‚РѕРїР»РµРЅРЅС‹Рµ РІРµСЂС€РёРЅС‹ РЅРµСЃСѓС‚ water-С„Р»Р°Рі (color.a),
    /// С€РµР№РґРµСЂ СЂРёСЃСѓРµС‚ РёРј РІРѕРґСѓ (fresnel + Р±Р»РёРє). Р’С‹Р·РѕРІ вЂ” LateUpdate РїРѕСЃР»Рµ BodyView.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-70)]
    public sealed class PlanetSurfaceRenderer : MonoBehaviour
    {
        // ===== [Р“Р РђР¤РРљРђ] РљР°С‡РµСЃС‚РІРѕ Рё РґР°Р»СЊРЅРѕСЃС‚СЊ СЂРµР»СЊРµС„Р° =====
        // TileResolution / MaxDepth / SplitFactor / BuildsPerFrame / MaxNodes /
        // MaxCachedChunks вЂ” РєР°РЅРґРёРґР°С‚С‹ РІ Р±СѓРґСѓС‰РёРµ РїСЂРµСЃРµС‚С‹ РЅР°СЃС‚СЂРѕРµРє РіСЂР°С„РёРєРё (РѕС‚
        // РјРёРЅРёРјР°Р»СЊРЅРѕРіРѕ РґРѕ РјР°РєСЃРёРјР°Р»СЊРЅРѕРіРѕ РєР°С‡РµСЃС‚РІР°).
        // Р”Р°Р»СЊРЅРѕСЃС‚СЊ РІРёРґРёРјРѕСЃС‚Рё Р·Р°РґР°С‘С‚СЃСЏ РєР°РјРµСЂРѕР№ (FirstPersonCamera.UpdateClipPlanes,
        // С„РёР·РёС‡РµСЃРєРёР№ РіРѕСЂРёР·РѕРЅС‚ СЃ Р·Р°РїР°СЃРѕРј РЅР° РІРµСЂС€РёРЅС‹) вЂ” Р·РґРµСЃСЊ СЂРµР»СЊРµС„ СЃС‚СЂРѕРёС‚СЃСЏ РЅР°
        // РІСЃСЋ РІРёРґРёРјСѓСЋ РїРѕР»СѓСЃС„РµСЂСѓ Р±РµР· РІС‹СЃРѕС‚РЅРѕРіРѕ РїРѕСЂРѕРіР°.
        // РЎРіР»Р°Р¶РёРІР°РЅРёРµ РєР°РґСЂР° (SMAA/TAA) Р·Р°РґР°С‘С‚СЃСЏ РЅР° Main Camera РІ СЃС†РµРЅРµ
        // OutdoorsScene: HDAdditionalCameraData (antialiasing / SMAAQuality / TAA*).
        // РџРѕРёСЃРє РїРѕ С‚РµРіСѓ: [Р“Р РђР¤РРљРђ].

        [Tooltip("SimulationRunner СЃС†РµРЅС‹.")]
        public SimulationRunner Runner;

        [Tooltip("Р’РµСЂС€РёРЅ РЅР° СЃС‚РѕСЂРѕРЅСѓ СѓР·Р»Р° (65 в‰€ 64 РєРІР°РґР°).")]
        [Range(9, 257)]
        public int TileResolution = 65;

        [Tooltip("РњР°РєСЃРёРјР°Р»СЊРЅР°СЏ РіР»СѓР±РёРЅР° quadtree (0 = 6 РіСЂР°РЅРµР№ С†РµР»РёРєРѕРј).")]
        [Range(0, 16)]
        public int MaxDepth = 12;

        [Tooltip("РЎРїР»РёС‚: СѓР·РµР» РґРµР»РёС‚СЃСЏ, РµСЃР»Рё РґРёСЃС‚Р°РЅС†РёСЏ РґРѕ С†РµРЅС‚СЂР° < СЂР°Р·РјРµСЂР° СѓР·Р»Р° Г— С„Р°РєС‚РѕСЂ.")]
        [Range(1f, 12f)]
        public float SplitFactor = 3.5f;

        [Tooltip("Р“РёСЃС‚РµСЂРµР·РёСЃ СЃР»РёСЏРЅРёСЏ: СѓР¶Рµ РїРѕРґРµР»С‘РЅРЅС‹Р№ СѓР·РµР» СЃР»РёРІР°РµС‚СЃСЏ РѕР±СЂР°С‚РЅРѕ С‚РѕР»СЊРєРѕ РїСЂРё РґРёСЃС‚Р°РЅС†РёРё > СЂР°Р·РјРµСЂР° Г— SplitFactor Г— СЌС‚Рѕ. РњРµР¶РґСѓ РїРѕСЂРѕРіР°РјРё РґРµСЂР¶РёС‚СЃСЏ РїСЂРѕС€Р»С‹Р№ СѓСЂРѕРІРµРЅСЊ (РїСЂРѕС‚РёРІ LOD-С„Р»Р°РїР° РЅР° СЃРєРѕСЂРѕСЃС‚Рё).")]
        [Range(1f, 3f)]
        public float MergeHysteresis = 1.3f;

        [Tooltip("Р“Р»СѓР±РёРЅР° СЋР±РєРё РєР°Рє РґРѕР»СЏ СЂР°Р·РјРµСЂР° СѓР·Р»Р°.")]
        [Range(0f, 0.2f)]
        public float SkirtFactor = 0.03f;

        [Tooltip("РЎРєРѕР»СЊРєРѕ С‡Р°РЅРєРѕРІ СЃС‚СЂРѕРёС‚СЊ Р·Р° РєР°РґСЂ (РїРµСЂРІС‹Р№ РєР°РґСЂ вЂ” Р±РµР· Р»РёРјРёС‚Р°).")]
        [Range(1, 64)]
        public int BuildsPerFrame = 16;

        [Tooltip("РЎРєРѕР»СЊРєРѕ СЃР±РѕСЂРѕРє СЃР»РѕС‘РІ РґРµРєРѕСЂР° Р·Р° РєР°РґСЂ (Р»РµРЅРёРІР°СЏ РіРµРЅРµСЂР°С†РёСЏ: Р±Р»РёР¶РЅРёРµ РїРµСЂРІС‹РјРё).")]
        [Range(1, 16)]
        public int DecorBuildsPerFrame = 2;

        [Tooltip("Р‘СЋРґР¶РµС‚ РіР»Р°РІРЅРѕРіРѕ РїРѕС‚РѕРєР° РЅР° С€Р°Рі Р°СЃРёРЅС…СЂРѕРЅРЅС‹С… СЃР±РѕСЂРѕРє РґРµРєРѕСЂР° (РјСЃ/РєР°РґСЂ): " +
                 "С‚СЏР¶С‘Р»С‹Рµ СЃР±РѕСЂРєРё РёРґСѓС‚ РїРѕСЂС†РёСЏРјРё Рё РЅРµ Р±Р»РѕРєРёСЂСѓСЋС‚ РєР°РґСЂ С†РµР»РёРєРѕРј.")]
        [Range(0.5f, 8f)]
        public float DecorBuildBudgetMs = 3f;

        [Tooltip("РњР°РєСЃРёРјСѓРј РѕРґРЅРѕРІСЂРµРјРµРЅРЅС‹С… Р°СЃРёРЅС…СЂРѕРЅРЅС‹С… СЃР±РѕСЂРѕРє РґРµРєРѕСЂР° (РїР°РјСЏС‚СЊ/Р»Р°С‚РµРЅС‚РЅРѕСЃС‚СЊ).")]
        [Range(1, 8)]
        public int MaxDecorBuildSessions = 4;

        [Header("Р”РёР°РіРЅРѕСЃС‚РёРєР° РґРµРєРѕСЂР°")]
        [Tooltip("Р Р°Р· РІ СЃРµРєСѓРЅРґСѓ РїРёСЃР°С‚СЊ РІ Р»РѕРі СЂР°РґРёСѓСЃ РѕР±СЂРµР·РєРё Рё С‡РёСЃР»Рѕ С‚СЂР°РІРёРЅРѕРє РїСѓР»Р° " +
                 "РґР»СЏ С‡Р°РЅРєР° Сѓ РёРіСЂРѕРєР° вЂ” РїРѕ РЅРµРјСѓ РІРёРґРЅРѕ, РєСѓРґР° СЃРµР»Р° РіСЂР°РЅРёС†Р°.")]
        public bool LogDecorAllocation;

        [Tooltip("РћРіСЂР°РЅРёС‡РёС‚РµР»СЊ Р°РєС‚РёРІРЅС‹С… СѓР·Р»РѕРІ (Р·Р°С‰РёС‚Р° РѕС‚ Р»Р°РІРёРЅС‹).")]
        [Min(64)]
        public int MaxNodes = 6000;
        [Tooltip("РњР°РєСЃРёРјСѓРј Р·Р°РєСЌС€РёСЂРѕРІР°РЅРЅС‹С… РјРµС€РµР№ (Р»РёС€РЅРёРµ РІС‹С‚РµСЃРЅСЏСЋС‚СЃСЏ).")]
        [Min(64)]
        public int MaxCachedChunks = 2048;

        [Header("РџСЂРѕСЃС‚Р°СЏ С‚РµРЅСЊ С‚СЂР°РІС‹ (Р±Р»РѕР±)")]
        [Tooltip("Р¦РІРµС‚ РїСЏС‚РЅР° РїСЂРѕСЃС‚РѕР№ С‚РµРЅРё (opaque alpha-test; Р°Р»СЊС„Р° РЅРµ РёСЃРїРѕР»СЊР·СѓРµС‚СЃСЏ).")]
        public Color BlobShadowColor = new Color(0.05f, 0.08f, 0.04f, 1f);

        [Tooltip("Р Р°Р·РјРµСЂ РїСЏС‚РЅР° РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РјР°СЃС€С‚Р°Р±Р° РёРЅСЃС‚Р°РЅСЃР° С‚СЂР°РІС‹.")]
        [Range(0.2f, 2f)]
        public float BlobScaleFactor = 1.5f;

        [Tooltip("РЎРјРµС‰РµРЅРёРµ РїСЏС‚РЅР° РѕС‚ СЃРѕР»РЅС†Р° (РґРѕР»СЏ СЂР°Р·РјРµСЂР°): С‚РµРЅСЊ СѓС…РѕРґРёС‚ РёР·-РїРѕРґ РєСѓСЃС‚Р° Рё СЃС‚Р°РЅРѕРІРёС‚СЃСЏ РІРёРґРЅР°, РєР°Рє РЅР°СЃС‚РѕСЏС‰РёР№ РєР°СЃС‚.")]
        [Range(0f, 2f)]
        public float BlobSunOffset = 0.6f;

        private struct Node
        {
            public int Face;
            public int Depth;
            public int Ix;
            public int Iy;
        }

        private sealed class Chunk
        {
            public GameObject Go;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public bool Visible;
            public int DecorBuiltMask;
            /// <summary>Р›РѕРєР°Р»СЊРЅР°СЏ РєР°РјРµСЂР° СЃР±РѕСЂРєРё РїРѕ СЃР»РѕСЏРј (РґР°Р¶Рµ РµСЃР»Рё СЃР»РѕР№ РґР°Р»
            /// 0 РёРЅСЃС‚Р°РЅСЃРѕРІ): РїРѕ РЅРµР№ СЂРµС€Р°РµС‚СЃСЏ РїРµСЂРµСЃР±РѕСЂРєР° В«РѕР±Р»Р°РєР°В» РїСЂРё РїРѕРґС…РѕРґРµ.</summary>
            public Vector3[] DecorBuildCameraLocal;
            public Node Node;
            public int Depth;
            public Vector3d CenterAstro;
            public float BoundsRadius;
            public readonly List<DecorLayerRuntime> Decor = new List<DecorLayerRuntime>();
        }

        /// <summary>Р“РѕС‚РѕРІС‹Рµ РёРЅСЃС‚Р°РЅСЃС‹ РѕРґРЅРѕРіРѕ СЃР»РѕСЏ РґРµРєРѕСЂР° РІ С‡Р°РЅРєРµ (Р»РѕРєР°Р»СЊРЅРѕ С‡Р°РЅРєСѓ).</summary>
        private sealed class DecorLayerRuntime
        {
            public GroundDecorLayer Profile;
            public int LayerIndex;
            /// <summary>РџРѕР·РёС†РёСЏ РєР°РјРµСЂС‹ РЅР° РјРѕРјРµРЅС‚ СЃР±РѕСЂРєРё: Сѓ СЃР»РѕС‘РІ СЃ Р·Р°С‚СѓС…Р°РЅРёРµРј
            /// РѕР±Р»Р°РєРѕ РїР»РѕС‚РЅРѕСЃС‚Рё РїСЂРёРІСЏР·Р°РЅРѕ Рє РЅРµР№ Рё РґРѕР»Р¶РЅРѕ РµС…Р°С‚СЊ Р·Р° РёРіСЂРѕРєРѕРј.</summary>
            public Vector3 BuildCameraRenderPos;
            public Vector3 BuildCameraLocalPosition;
            public NativeArray<GroundDecorInstance> Instances;
            public Matrix4x4[] Matrices;
            /// <summary>Таблица раскладки пула по клеткам (та же, что у
            /// аллокатора): нужна следующей пересборке, чтобы перенести уже
            /// выросшие травинки (GroundDecorMergeKeptJob) с их BirthTime.</summary>
            public NativeArray<int> CellOffsets;
            public NativeArray<int> CellCounts;
            /// <summary>РњРёСЂРѕРІС‹Рµ РјР°С‚СЂРёС†С‹ РёРЅСЃС‚Р°РЅСЃРѕРІ, СЃС‡РёС‚Р°СЋС‚СЃСЏ Burst-РґР¶РѕР±РѕР№ РєР°Р¶РґС‹Р№
            /// РєР°РґСЂ (РґР»СЏ СЃР»РѕС‘РІ СЃ PerInstanceDensity) вЂ” РіР»Р°РІРЅС‹Р№ РїРѕС‚РѕРє РЅРµ С‚СЂР°С‚РёС‚
            /// РІСЂРµРјСЏ РЅР° TRS РЅР° РєР°Р¶РґСѓСЋ С‚СЂР°РІРёРЅРєСѓ.</summary>
            public NativeArray<Matrix4x4> WorldMatrices;

            /// <summary>РРЅРґРёСЂРµРєС‚-РїСѓС‚СЊ С‚СЂР°РІС‹: РјР°С‚СЂРёС†С‹ РёРЅСЃС‚Р°РЅСЃРѕРІ (С‡Р°РЅРє-С„СЂРµР№Рј) РІ
            /// Р±СѓС„РµСЂРµ + Р°СЂРіСѓРјРµРЅС‚С‹ РґСЂРѕСѓ. РЎС‡РёС‚Р°СЋС‚СЃСЏ РћР”РРќ Р РђР— РїСЂРё СЃР±РѕСЂРєРµ, РјРёСЂРѕРІР°СЏ
            /// С‡Р°СЃС‚СЊ вЂ” С‡РµСЂРµР· _DecorChunkToWorld. РћРґРёРЅ РґСЂРѕСѓ РЅР° СЃР°Р±РјРµС€ РІРјРµСЃС‚Рѕ
            /// count/1023 Р±Р°С‚С‡РµР№.</summary>
            public GraphicsBuffer MatrixBuffer;
            public GraphicsBuffer ArgsBuffer;
            public Mesh DrawMesh;
        }

        /// <summary>
        /// РђСЃРёРЅС…СЂРѕРЅРЅР°СЏ СЃР±РѕСЂРєР° СЃР»РѕСЏ РґРµРєРѕСЂР° РІ С‡Р°РЅРєРµ: РїСЂРµР¶РЅСЏСЏ СЃР±РѕСЂРєР° Р±Р»РѕРєРёСЂРѕРІР°Р»Р°
        /// РєР°РґСЂ С†РµР»РёРєРѕРј (РґРѕ СЃРѕС‚РµРЅ РјСЃ РЅР° РєСЂСѓРїРЅРѕРј LOD) Рё РЅР° С…РѕРґСЊР±Рµ РґР°РІР°Р»Р° С„СЂРёР·С‹.
        /// РўРµРїРµСЂСЊ СЃС‚Р°РґРёРё РёРґСѓС‚ РїРѕСЂС†РёСЏРјРё РїРѕ Р±СЋРґР¶РµС‚Сѓ РєР°РґСЂР°: РґР¶РёС‚С‚РµСЂ-С…РµС€Рё вЂ” РєСѓСЂСЃРѕСЂРѕРј,
        /// Burst-С„РёР»СЊС‚СЂ РєР°РЅРґРёРґР°С‚РѕРІ вЂ” РІ С„РѕРЅРµ (РѕРїСЂРѕСЃ IsCompleted), СЂР°Р·РІРѕСЂРѕС‚
        /// РїРѕРґС‚СѓС„С‚РѕРІ вЂ” РєСѓСЂСЃРѕСЂРѕРј, С„РёРЅР°Р»РёР·Р°С†РёСЏ вЂ” Р·Р°РјРµРЅРѕР№ РіРѕС‚РѕРІРѕРіРѕ СЃР»РѕСЏ.
        /// РњР°С‚РµРјР°С‚РёРєР° Рё РїРѕСЂСЏРґРѕРє С…РµС€РµР№ РЅРµ РјРµРЅСЏСЋС‚СЃСЏ РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ СЃС‚Р°СЂРѕР№ СЃР±РѕСЂРєРё.
        /// </summary>
        private sealed class DecorBuildSession
        {
            public long ChunkId;
            public Chunk Chunk;
            public int LayerIndex;
            public GroundDecorLayer Layer;
            public Node Node;

            public Vector3 CameraPosition;
            /// <summary>Р¦РµРЅС‚СЂ Р·Р°РїРµРєР°РЅРёСЏ РѕР±Р»Р°РєР° (РєР°РјРµСЂР° + РѕРїРµСЂРµР¶РµРЅРёРµ РїРѕ СЃРєРѕСЂРѕСЃС‚Рё).</summary>
            public Vector3 CameraLocal;
            /// <summary>РЎС‹СЂР°СЏ Р»РѕРєР°Р»СЊРЅР°СЏ РєР°РјРµСЂР° вЂ” РґР»СЏ РїСЂРѕРІРµСЂРєРё СѓСЃС‚Р°СЂРµРІР°РЅРёСЏ СЃР»РѕСЏ
            /// (РѕРїРµСЂРµР¶РµРЅРёРµ С‚СѓРґР° РЅРµ РІС…РѕРґРёС‚, РёРЅР°С‡Рµ РїРѕРІРѕСЂРѕС‚С‹/СЂС‹РІРєРё СЃС‚Р°СЂРёР»Рё Р±С‹ СЃР»РѕР№).</summary>
            public Vector3 BuildCameraLocal;
            /// <summary>РЎР±РѕСЂРєР° В«Сѓ РёРіСЂРѕРєР°В» (РїРµСЂРµСЃР±РѕСЂРєР°/РІРёРґРёРјР°СЏ Р·РѕРЅР°) вЂ” С‚Р°РєРёРµ СЃРµСЃСЃРёРё
            /// РЅРµ РѕРіСЂР°РЅРёС‡РµРЅС‹ СЂРµР·РµСЂРІРѕРј СЃР»РѕС‚РѕРІ РїРѕРґ РґР°Р»СЊРЅРёРµ РїРµСЂРІС‹Рµ СЃР±РѕСЂРєРё.</summary>
            public bool NearPlayer;
            /// <summary>РџСЂРµР¶РЅРёР№ СЃР»РѕР№ СЌС‚РѕРіРѕ Р¶Рµ С‡Р°РЅРєР°: РµРіРѕ С‚СЂР°РІРёРЅРєРё РІРґР°Р»Рё РѕС‚ РЅРѕРІРѕРіРѕ
            /// СЏРґСЂР° СЃРѕС…СЂР°РЅСЏСЋС‚СЃСЏ (РЅР°РєРѕРїР»РµРЅРёРµ РІРјРµСЃС‚Рѕ РїРѕР»РЅРѕР№ Р·Р°РјРµРЅС‹) вЂ” С‚СЂР°РІР° РЅРµ
            /// В«РїРѕСЏРІР»СЏРµС‚СЃСЏ Р·Р°РЅРѕРІРѕВ» РЅР° СѓР¶Рµ РїСЂРѕР№РґРµРЅРЅРѕРј РјРµСЃС‚Рµ.</summary>
            public DecorLayerRuntime Previous;
            public NativeArray<GroundDecorInstance> KeptOld;
            public int KeptOldCount;
            /// <summary>РЎРєРѕР»СЊРєРѕ Р·Р°РїРёСЃРµР№ РјРѕР¶РµС‚ РґРѕР±Р°РІРёС‚СЊ РЅРѕРІР°СЏ СЃР±РѕСЂРєР° (Р·Р° РІС‹С‡РµС‚РѕРј
            /// СЃРѕС…СЂР°РЅС‘РЅРЅС‹С… СЃС‚Р°СЂС‹С…).</summary>
            public int ExpandTake;
            public GroundDecorPlacementParams Placement;
            public Vector3[] MeshVertices;
            public int CoreN;

            public double SizeUv;
            public double U0;
            public double V0;
            public double StepUv;
            public double ChunkArc;
            public double Spacing;
            public int Cells;
            public int Count;
            public double MinSink;
            public double MaxSink;
            public int MeshCount;

            public NativeArray<double3> Dirs;
            public NativeArray<float2> Uvs;
            public NativeArray<double3> Randoms;
            public NativeArray<double> MeshPicks;
            public NativeArray<double> BuryRandoms;
            public NativeArray<double> LeanRandoms;
            public NativeArray<int> Accepted;
            public NativeArray<GroundDecorInstance> Instances;
            public NativeArray<GroundDecorInstance> Stored;

            /// <summary>Burst-РїСѓС‚СЊ (СЃР»РѕРё СЃ PerInstanceDensity): РІРµСЂС€РёРЅС‹ РјРµС€Р°
            /// РІ РЅР°С‚РёРІРЅРѕР№ РїР°РјСЏС‚Рё, СЃС‡С‘С‚С‡РёРєРё С‚СЂР°РІРёРЅРѕРє, СЃР»РѕС‚С‹ Р·Р°РїРёСЃРё.</summary>
            public NativeArray<Vector3> MeshVerticesNative;
            /// <summary>Р§РёСЃР»Рѕ С‚СЂР°РІРёРЅРѕРє РєР»РµС‚РєРё РѕС‚ Burst-РІРµСЃР° (РІС…РѕРґ Р°Р»Р»РѕРєР°С‚РѕСЂР°).</summary>
            public NativeArray<int> SubsFor;
            /// <summary>РЎРєРѕР»СЊРєРѕ С‚СЂР°РІРёРЅРѕРє РєР»РµС‚РєР° СЂРµР°Р»СЊРЅРѕ РїРёС€РµС‚ РїРѕСЃР»Рµ РѕР±СЂРµР·РєРё РєР°РїР°.</summary>
            public NativeArray<int> WriteCounts;
            public NativeArray<float> Distances;
            /// <summary>[0] вЂ” СЂР°РґРёСѓСЃ РІС‹Р±РѕСЂРєРё, [1] вЂ” РґРѕР»СЏ РіСЂР°РЅРёС‡РЅРѕР№ РїРѕР»РѕСЃС‹, [2] вЂ” С€РёСЂРёРЅР°.</summary>
            public NativeArray<float> Selection;

            /// <summary>РџРѕСЂСЏРґРѕРє РєР»РµС‚РѕРє РїРѕ Р±Р°РєРµС‚Р°Рј РґРёСЃС‚Р°РЅС†РёРё (СЃРєСЂРµС‚С‡ Р°Р»Р»РѕРєР°С‚РѕСЂР°).</summary>
            public NativeArray<int> AllocCellOrder;

            /// <summary>Р”РёР°РіРЅРѕСЃС‚РёРєР° Р·Р°РґРµСЂР¶РєРё СЃР±РѕСЂРєРё: РІСЂРµРјСЏ Рё РґРёСЃС‚Р°РЅС†РёСЏ РїРѕСЃС‚Р°РЅРѕРІРєРё.</summary>
            public float QueueTimeSeconds;
            public float QueueTangentialDistance;
            public NativeArray<int> WriteOffsets;
            /// <summary>[0] вЂ” Р·Р°РїРёСЃР°РЅРѕ С‚СЂР°РІРёРЅРѕРє, [1] вЂ” СЂР°РґРёСѓСЃ РѕР±СЂРµР·РєРё (Рј).</summary>
            public NativeArray<float> WriteTotal;

            public JobHandle CandidateJob;
            public bool CandidateScheduled;
            public bool CancelRequested;
            public bool Finished;

            /// <summary>0 вЂ” РїСЂРµРї РєР°РЅРґРёРґР°С‚РѕРІ, 1 вЂ” РѕР¶РёРґР°РЅРёРµ Burst-РґР¶РѕР±С‹,
            /// 2 вЂ” РїСЂРѕС…РѕРґ accepted/РІРµСЃР°, 3 вЂ” СЂР°Р·РІРѕСЂРѕС‚ РїРѕРґС‚СѓС„С‚РѕРІ, 4 вЂ” С„РёРЅР°Р»РёР·Р°С†РёСЏ.</summary>
            public int Stage;
            public int Cursor;
            public int AcceptedCount;
            public double WeightSum;
            /// <summary>ОЈ РІРµСЃГ—СЌС„С„РµРєС‚РёРІРЅС‹Рµ РїРѕРґС‚СѓС„С‚С‹ вЂ” РѕР¶РёРґР°РµРјРѕРµ С‡РёСЃР»Рѕ РёРЅСЃС‚Р°РЅСЃРѕРІ
            /// (РІРµСЃ СѓР¶Рµ СЃ Р·Р°С‚СѓС…Р°РЅРёРµРј, РїРѕРґС‚СѓС„С‚С‹ РІРґР°Р»Рё СѓСЂРµР·Р°РЅС‹ РґРѕ РѕРґРЅРѕРіРѕ).</summary>
            public double ExpectedWeight;
            public double CapScale = 1d;
            public int SubPerCell = 1;
            public int Take;
            public int Stride = 1;
            public int Write;

            public int K;
            public int SubIndex;
            /// <summary>Р§РёСЃР»Рѕ РїРѕРґС‚СѓС„С‚ РґР»СЏ РўР•РљРЈР©Р•Р™ РєР»РµС‚РєРё: РІР±Р»РёР·Рё вЂ” РїРѕР»РЅРѕРµ,
            /// РІРґР°Р»Рё вЂ” 1 (С‚Р°Рј РїР»РѕС‚РЅРѕСЃС‚СЊ РєРѕРїРµРµС‡РЅР°СЏ).</summary>
            public int CurrentSubs = 1;
            public GroundDecorInstance BaseInstance;
            public double CellU0;
            public double CellV0;
            public double CellSizeUv;
        }

        private OrbitingBody body;
        private HeightfieldTerrain terrain;
        private TerrainNoiseParams noiseParams;
        private Transform surfaceRoot;
        private Material materialCache;
        private BodyAuthoring bodyAuthoring;
        private GroundDecorProfile decorProfile;
        private Matrix4x4[] decorBatchScratch;
        private Matrix4x4[] decorPerMeshScratch;
        private Matrix4x4[] decorCastScratch;
        private Matrix4x4[] decorRestScratch;
        private Matrix4x4[] decorBlobScratch;
        private Plane[] decorFrustumPlanes;
        private Mesh blobMesh;
        private Material blobMaterial;

        private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        private readonly List<Node> desired = new List<Node>();
        private readonly HashSet<long> keep = new HashSet<long>();
        private readonly HashSet<long> splitNodes = new HashSet<long>();
        private readonly HashSet<long> splitNext = new HashSet<long>();
        private readonly List<long> toEvict = new List<long>();
        private readonly List<DecorBuildSession> decorBuildSessions = new List<DecorBuildSession>();
        private readonly List<BurstDecorDraw> burstDecorDraws = new List<BurstDecorDraw>();
        private MaterialPropertyBlock decorDrawPropertyBlock;
        private bool decorIndirectDiagLogged;
        private static readonly int DecorChunkToWorldId = Shader.PropertyToID("_DecorChunkToWorld");
        private static readonly int DecorMatricesId = Shader.PropertyToID("_DecorMatrices");
        private const int DecorPrepItemsPerStep = 2048;
        private const int DecorScanItemsPerStep = 4096;
        private const int DecorExpandSubsPerStep = 2048;
        /// <summary>РЎРєРѕР»СЊРєРѕ СЃР»РѕС‚РѕРІ СЃРµСЃСЃРёР№ РІСЃРµРіРґР° РґРµСЂР¶РёРј РїРѕРґ СЂР°Р±РѕС‚Сѓ Сѓ РёРіСЂРѕРєР°
        /// (РїРµСЂРµСЃР±РѕСЂРєРё РѕР±Р»Р°РєР° Рё РІРёРґРёРјС‹Рµ РїРµСЂРІС‹Рµ СЃР±РѕСЂРєРё).</summary>
        private const int PlayerReservedSessions = 2;
        /// <summary>РќРёР¶Рµ СЌС‚РѕРіРѕ Р·Р°С‚СѓС…Р°РЅРёСЏ РїРѕРґС‚СѓС„С‚С‹ РєР»РµС‚РєРё РЅРµ СЂР°Р·РІРѕСЂР°С‡РёРІР°СЋС‚СЃСЏ
        /// (РєР»РµС‚РєР° РґР°С‘С‚ РѕРґРёРЅ РёРЅСЃС‚Р°РЅСЃ): РїР»РѕС‚РЅРѕСЃС‚СЊ РЅР° С‚Р°РєРѕР№ РґРёСЃС‚Р°РЅС†РёРё РІСЃС‘ СЂР°РІРЅРѕ
        /// РєРѕРїРµРµС‡РЅР°СЏ, Р° СЃР±РѕСЂРєР° РґРµС€РµРІРµРµС‚ РІ СЂР°Р·С‹ (в‰€100 Рј РѕС‚ РёРіСЂРѕРєР° РїСЂРё 45 Рј).</summary>
        /// <summary>РћРїРµСЂРµР¶РµРЅРёРµ РѕР±Р»Р°РєР° С‚СЂР°РІС‹ РїРѕ СЃРєРѕСЂРѕСЃС‚Рё РёРіСЂРѕРєР°: РґРѕР»СЏ СЃРµРєСѓРЅРґС‹ С…РѕРґР°,
        /// С‡С‚РѕР±С‹ РїР»РѕС‚РЅР°СЏ Р·РѕРЅР° РіРѕС‚РѕРІРёР»Р°СЃСЊ Р’РџР•Р Р•Р”Р (С‚СЂР°РІР° РІСЃС‚СЂРµС‡Р°РµС‚ РёРіСЂРѕРєР°, Р° РЅРµ
        /// РґРѕРіРѕРЅСЏРµС‚СЃСЏ РёРј). РЎРІРµСЂС…Сѓ РѕРіСЂР°РЅРёС‡РµРЅРѕ РґРѕР»РµР№ РїР»РѕСЃРєРѕРіРѕ СЏРґСЂР° вЂ” РёРЅР°С‡Рµ РёРіСЂРѕРє
        /// СЃР°Рј РѕРєР°Р·С‹РІР°РµС‚СЃСЏ РІ Р·РѕРЅРµ Р·Р°С‚СѓС…Р°РЅРёСЏ, Рё РІРїРµСЂРµРґРё РІРёРґРЅР° В«СЃС‚СѓРїРµРЅСЊРєР°В»
        /// РїР»РѕС‚РЅРѕСЃС‚Рё (С‚СЂР°РІР° РіСѓС‰Рµ РІ N РјРµС‚СЂР°С… РїРµСЂРµРґ РёРіСЂРѕРєРѕРј).</summary>
        private const float GrassLeadSeconds = 0.5f;
        private const float GrassLeadMaxMeters = 60f;
        private const float GrassLeadCoreShare = 0.5f;
        /// <summary>Р”РѕР»СЏ Р±СЋРґР¶РµС‚Р° РїСѓР»Р° С‚СЂР°РІС‹, РѕС‚РґР°РЅРЅР°СЏ РїР»Р°РІРЅРѕРјСѓ Р·Р°С‚СѓС…Р°РЅРёСЋ Сѓ
        /// РіСЂР°РЅРёС†С‹ РѕР±СЂРµР·РєРё: РёРЅР°С‡Рµ РєСЂР°Р№ РїСѓР»Р° РІРёРґРµРЅ СЂРѕРІРЅРѕР№ Р»РёРЅРёРµР№ РїРѕ Р·РµРјР»Рµ.</summary>
        private const float DecorCutFadeShare = 0.25f;
        /// <summary>РџРѕР»РѕСЃР° Р·Р°С‚СѓС…Р°РЅРёСЏ РїР»РѕС‚РЅРѕСЃС‚Рё РїРµСЂРµРґ СЂР°РґРёСѓСЃРѕРј РїСѓР»Р° (Рј).</summary>
        private const float DecorPoolRadiusFadeMeters = 30f;
        /// <summary>За сколько секунд травинка «прорастает» (масштаб 0→1).</summary>
        private const float DecorBladeFadeSeconds = 0.6f;
        /// <summary>Разброс момента появления по травинкам (с): новые появляются
        /// поштучно в этом окне, а не всей пачкой.</summary>
        private const float DecorBladeStaggerSeconds = 1.2f;
        /// <summary>Прорастание пропов (деревья/камни/кактусы/ромашки): длительность
        /// и разброс появления. Крупные объекты растут медленнее травинок.</summary>
        private const float DecorPropFadeSeconds = 1.0f;
        private const float DecorPropStaggerSeconds = 2.0f;
        /// <summary>РџСѓР» С‚СЂР°РІС‹ С‡Р°РЅРєР° РґР°Р»СЊС€Рµ MaxDistance+СЌС‚РѕС‚ Р·Р°РїР°СЃ РІС‹РіСЂСѓР¶Р°РµС‚СЃСЏ:
        /// РёРЅР°С‡Рµ РїСЂРѕР№РґРµРЅРЅС‹Рµ С‡Р°РЅРєРё РєРѕРїСЏС‚ РїРѕР»РЅС‹Рµ РїСѓР»С‹, Р±СЋРґР¶РµС‚ СѓС…РѕРґРёС‚ РѕС‚ РёРіСЂРѕРєР°
        /// (Рё В«РѕР±Р»Р°РєРѕВ» Сѓ РЅРµРіРѕ РїСѓСЃС‚РµРµС‚).</summary>
        private const float DistantDecorTrimMargin = 80f;
        private int firstFrameGuard = -1;
        private int diagnosticChunksLogged;
        private float decorAllocLogTime;
        private bool blobDiagLogged;
        private Vector3 bodyRenderPosition;
        /// <summary>РЎРєРѕСЂРѕСЃС‚СЊ РёРіСЂРѕРєР° РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё РІ body-fixed РѕСЃСЏС…
        /// (СЃРѕРІРїР°РґР°СЋС‚ СЃ Р»РѕРєР°Р»СЊРЅС‹РјРё РѕСЃСЏРјРё С‡Р°РЅРєРѕРІ): РІСЂР°С‰РµРЅРёРµ РїР»Р°РЅРµС‚С‹ РёСЃРєР»СЋС‡РµРЅРѕ.</summary>
        private Vector3 surfaceVelocityLocal;
        private Vector3 lastSurfaceLocalPosition;
        private bool hasSurfaceLocalPosition;
        private Vector3 lastDecorScanCamera;
        private bool hasDecorScanCamera;
        private int decorScanTick;
        private Quaternion currentBodyRotation = Quaternion.identity;
        private Vector3d currentBodyPosition;
        private QuaternionD currentBodyOrientation = QuaternionD.Identity;

        private void Start()
        {
            if (Runner == null || Runner.SystemState == null)
            {
                enabled = false;
                return;
            }

            foreach (OrbitingBody candidate in Runner.SystemState.AllBodies)
            {
                if (candidate.Name == gameObject.name)
                {
                    body = candidate;
                    break;
                }
            }

            terrain = body?.Terrain as HeightfieldTerrain;
            if (body == null || terrain == null)
            {
                // Р“Р»Р°РґРєР°СЏ СЃС„РµСЂР° РёР»Рё С‚РµР»Рѕ РЅРµ РЅР°Р№РґРµРЅРѕ вЂ” СЃС‚Р°СЂС‹Р№ РІРёР·СѓР°Р» РЅРµ С‚СЂРѕРіР°РµРј.
                enabled = false;
                return;
            }

            noiseParams = TerrainNoiseParams.FromTerrain(terrain);
            // РџСЂРёРјРёС‚РёРІ-СЃС„РµСЂР° С‚РµР»Р° вЂ” С‚РѕР»СЊРєРѕ Р·Р°РіР»СѓС€РєР° РЅР° РІСЂРµРјСЏ, РїРѕРєР° РЅРµ Р±С‹Р»
            // cube-sphere. Р•С‘ СЂР°РґРёСѓСЃ СЂР°РІРµРЅ СЃСЂРµРґРЅРµРјСѓ (СѓСЂРѕРІРЅСЋ РјРѕСЂСЏ): СЃ РІРєР»СЋС‡С‘РЅРЅС‹РјРё
            // С‡Р°РЅРєР°РјРё РѕРЅР° РєРѕРїР»Р°РЅР°СЂРЅР° РѕРєРµР°РЅСѓ Рё РјРµСЂС†Р°РµС‚, РїРѕСЌС‚РѕРјСѓ РіР°СЃРёРј РЅР°РІСЃРµРіРґР°.
            // РќРµРїСЂРѕР·СЂР°С‡РЅРѕСЃС‚СЊ РїР»Р°РЅРµС‚С‹ РЅР° Р»СЋР±РѕР№ РґРёСЃС‚Р°РЅС†РёРё С‚РµРїРµСЂСЊ РґР°С‘С‚ СЃР°Рј СЂРµР»СЊРµС„
            // (cube-sphere СЂРµРЅРґРµСЂРёС‚СЃСЏ Рё РІ РєРѕСЃРјРѕСЃРµ, Р±РµР· РІС‹СЃРѕС‚РЅРѕРіРѕ РїРѕСЂРѕРіР°).
            MeshRenderer sphereRenderer = GetComponent<MeshRenderer>();
            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = false;
            }


            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            // Identity-РєРѕСЂРµРЅСЊ РґР»СЏ С‡Р°РЅРєРѕРІ: С‡Р°РЅРєРё РќР• РїРѕРґ С‚СЂР°РЅСЃС„РѕСЂРјРѕРј С‚РµР»Р° (РёРЅР°С‡Рµ
            // РёС… РєРѕРѕСЂРґРёРЅР°С‚С‹ ~СЂР°РґРёСѓСЃ РїР»Р°РЅРµС‚С‹ Рё float-РґСЂРѕР¶СЊ). РџРѕР·РёС†РёСЋ/РїРѕРІРѕСЂРѕС‚
            // РєР°Р¶РґРѕРіРѕ С‡Р°РЅРєР° СЃС‚Р°РІРёРј РєР°Р¶РґС‹Р№ РєР°РґСЂ С‡РµСЂРµР· FloatingOrigin.
            surfaceRoot = new GameObject("PlanetSurfaceChunks").transform;
            surfaceRoot.position = Vector3.zero;
            surfaceRoot.rotation = Quaternion.identity;
            surfaceRoot.localScale = Vector3.one;

            bodyAuthoring = GetComponent<BodyAuthoring>();
            decorProfile = bodyAuthoring != null && bodyAuthoring.GroundDecorPreset != null
                ? bodyAuthoring.GroundDecorPreset.Profile
                : null;
            // Р§РёСЃС‚РѕРµ СЃРѕСЃС‚РѕСЏРЅРёРµ РЅР° РєР°Р¶РґС‹Р№ Play: СЃ Enter Play Mode Options (Р±РµР·
            // Reload Scene/Domain) РїСЂРёРІР°С‚РЅС‹Рµ РїРѕР»СЏ РєРѕРјРїРѕРЅРµРЅС‚Р° РјРѕРіСѓС‚ РїРµСЂРµР¶РёС‚СЊ
            // РїСЂРѕС€Р»С‹Р№ Р·Р°РїСѓСЃРє, Р° РёС… С‡Р°РЅРєРё СѓР¶Рµ СѓРЅРёС‡С‚РѕР¶РµРЅС‹.
            ClearChunkCache();
        }

        private void OnDestroy()
        {
            CancelAllDecorBuilds();
            if (surfaceRoot != null)
            {
                Destroy(surfaceRoot.gameObject);
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DisposeDecor(kv.Value);
            }

            if (materialCache != null)
            {
                Destroy(materialCache);
            }

            if (blobMaterial != null)
            {
                Destroy(blobMaterial);
            }

            if (blobMesh != null)
            {
                Destroy(blobMesh);
            }

            // Teardown: дренируем пул буферов декора (иначе Persistent-массивы
            // утекут на выходе из play/при перезагрузке домена).
            DecorArrayPool.ClearAll();
        }

        /// <summary>РЎР±СЂРѕСЃРёС‚СЊ РєСЌС€ РјРµС€РµР№ (РїРѕСЃР»Рµ live-СЃРјРµРЅС‹ РїР°СЂР°РјРµС‚СЂРѕРІ СЂРµР»СЊРµС„Р°).</summary>
        [ContextMenu("Clear chunk cache")]
        public void ClearChunkCache()
        {
            CancelAllDecorBuilds();
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DisposeDecor(kv.Value);
                if (kv.Value.Go != null)
                {
                    Destroy(kv.Value.Go);
                }
            }

            chunks.Clear();
            splitNodes.Clear();
        }

        /// <summary>РЎРЅСЏС‚СЊ РІСЃРµ РЅРµР·Р°РІРµСЂС€С‘РЅРЅС‹Рµ СЃР±РѕСЂРєРё РґРµРєРѕСЂР° (teardown): РґР¶РѕР±С‹
        /// РґРѕСЃС‡РёС‚С‹РІР°РµРј Рё РѕСЃРІРѕР±РѕР¶РґР°РµРј Persistent-РјР°СЃСЃРёРІС‹, РёРЅР°С‡Рµ СѓС‚РµС‡РєР°.</summary>
        private void CancelAllDecorBuilds()
        {
            for (int i = 0; i < decorBuildSessions.Count; i++)
            {
                DecorBuildSession session = decorBuildSessions[i];
                if (session.CandidateScheduled)
                {
                    session.CandidateJob.Complete();
                    session.CandidateScheduled = false;
                }

                DisposeDecorBuildSession(session);
            }

            decorBuildSessions.Clear();
        }

        private static void DisposeDecor(Chunk chunk)
        {
            for (int i = 0; i < chunk.Decor.Count; i++)
            {
                DecorArrayPool.Return(ref chunk.Decor[i].Instances);
                DecorArrayPool.Return(ref chunk.Decor[i].WorldMatrices);
                DecorArrayPool.Return(ref chunk.Decor[i].CellOffsets);
                DecorArrayPool.Return(ref chunk.Decor[i].CellCounts);
            }

            chunk.Decor.Clear();
        }

        /// <summary>
        /// Live-С‚СЋРЅРёРЅРі РІ Play: РїРµСЂРµРЅРµСЃС‚Рё РїР°СЂР°РјРµС‚СЂС‹ СЂРµР»СЊРµС„Р° РёР· СЃРѕСЃРµРґРЅРµРіРѕ
        /// BodyAuthoring РІ Р¶РёРІРѕР№ HeightfieldTerrain, РѕР±РЅРѕРІРёС‚СЊ СЃРЅРёРјРѕРє С€СѓРјР° Рё
        /// РїРµСЂРµСЃС‚СЂРѕРёС‚СЊ РІСЃРµ С‡Р°РЅРєРё. Р’РЅРµ Play РјРµРЅСЏРµС‚ С‚РѕР»СЊРєРѕ СЃРµСЂРёР°Р»РёР·РѕРІР°РЅРЅС‹Рµ РїРѕР»СЏ.
        /// </summary>
        [ContextMenu("Apply authoring params + rebuild")]
        public void ApplyAuthoringAndRebuild()
        {
            BodyAuthoring authoring = GetComponent<BodyAuthoring>();
            if (authoring == null || terrain == null)
            {
                if (Application.isPlaying)
                {
                    Debug.LogWarning("[PlanetSurfaceRenderer] РЅРµС‚ BodyAuthoring/HeightfieldTerrain вЂ” РЅРµС‡РµРіРѕ РїСЂРёРјРµРЅСЏС‚СЊ.");
                }

                return;
            }

            authoring.ApplyToTerrain(terrain);
            noiseParams = TerrainNoiseParams.FromTerrain(terrain);
            ApplyTerrainGlobals();

            ClearChunkCache();
            firstFrameGuard = -1;
        }

        /// <summary>
        /// Live-С‚СЋРЅРёРЅРі: РµСЃР»Рё РїРѕР»СЏ СЂРµР»СЊРµС„Р° РІ BodyAuthoring РёР·РјРµРЅРёР»Рё РІ Play,
        /// РїРµСЂРµРЅРѕСЃРёРј РёС… РІ Р¶РёРІРѕР№ terrain Рё РїРµСЂРµСЃС‚СЂР°РёРІР°РµРј С‡Р°РЅРєРё Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё вЂ”
        /// РёРЅР°С‡Рµ РїСЂР°РІРєРё РІ Inspector РЅРµ РІР»РёСЏСЋС‚ РЅР° СѓР¶Рµ РїРѕСЃС‚СЂРѕРµРЅРЅСѓСЋ РіРµРѕРјРµС‚СЂРёСЋ.
        /// </summary>
        private void Update()
        {
            if (body == null || terrain == null)
            {
                return;
            }

            if (bodyAuthoring == null)
            {
                return;
            }

            bodyAuthoring.ApplyToTerrain(terrain);

            GroundDecorProfile currentDecor = bodyAuthoring.GroundDecorPreset != null
                ? bodyAuthoring.GroundDecorPreset.Profile
                : null;
            if (!ReferenceEquals(currentDecor, decorProfile))
            {
                decorProfile = currentDecor;
                ClearChunkCache();
                firstFrameGuard = -1;
            }

            TerrainNoiseParams current = TerrainNoiseParams.FromTerrain(terrain);
            if (!ParamsEqual(current, noiseParams))
            {
                noiseParams = current;
                ApplyTerrainGlobals();
                ClearChunkCache();
                firstFrameGuard = -1;
            }
            else if (!ColorStateEquals(CaptureColorState(), colorStateCache))
            {
                // РџСЂР°РІРєРё РўРћР›Р¬РљРћ С†РІРµС‚Р° (СЃРёР»Р°/С‡Р°СЃС‚РѕС‚Р° РјРѕС‚С‚Р»РёРЅРіР°, РїРѕСЂРѕРіРё СЃРєР°Р»С‹/СЃРЅРµРіР°)
                // РЅРµ РјРµРЅСЏСЋС‚ РіРµРѕРјРµС‚СЂРёСЋ вЂ” РїРµСЂРµСЃС‚СЂР°РёРІР°С‚СЊ С‡Р°РЅРєРё РЅРµ РЅСѓР¶РЅРѕ, РґРѕСЃС‚Р°С‚РѕС‡РЅРѕ
                // РїРµСЂРµР»РёС‚СЊ СѓРЅРёС„РѕСЂРјС‹ РІ РіР»РѕР±Р°Р»С‹.
                ApplyTerrainGlobals();
            }
        }

        /// <summary>РЎРЅРёРјРѕРє С†РІРµС‚РѕРІС‹С… СѓРЅРёС„РѕСЂРј (Р±РµР· РіРµРѕРјРµС‚СЂРёРё) РґР»СЏ live-С‚СЋРЅРёРЅРіР°.</summary>
        private struct TerrainColorState
        {
            public double BeachHeightMeters;
            public double RockSlopeTan;
            public double RockSlopeWidth;
            public double RockHeightMin;
            public double SnowSlopeTan;
            public double NoiseFrequency;
            public int NoiseOctaves;
            public double NoiseStrength;
            public double DetailFrequency;
            public int DetailOctaves;
            public double DetailStrength;
            public TerrainPaletteData Palette;
        }

        private TerrainColorState colorStateCache;

        private TerrainColorState CaptureColorState()
        {
            return new TerrainColorState
            {
                BeachHeightMeters = terrain.BeachHeightMeters,
                RockSlopeTan = terrain.ColorRockSlopeTan,
                RockSlopeWidth = terrain.ColorRockSlopeWidth,
                RockHeightMin = terrain.ColorRockHeightMin,
                SnowSlopeTan = terrain.ColorSnowSlopeTan,
                NoiseFrequency = terrain.ColorNoiseFrequency,
                NoiseOctaves = terrain.ColorNoiseOctaves,
                NoiseStrength = terrain.ColorNoiseStrength,
                DetailFrequency = terrain.ColorDetailFrequency,
                DetailOctaves = terrain.ColorDetailOctaves,
                DetailStrength = terrain.ColorDetailStrength,
                Palette = terrain.Palette
            };
        }

        private static bool ColorStateEquals(TerrainColorState a, TerrainColorState b)
        {
            return a.BeachHeightMeters == b.BeachHeightMeters
                && a.RockSlopeTan == b.RockSlopeTan
                && a.RockSlopeWidth == b.RockSlopeWidth
                && a.RockHeightMin == b.RockHeightMin
                && a.SnowSlopeTan == b.SnowSlopeTan
                && a.NoiseFrequency == b.NoiseFrequency
                && a.NoiseOctaves == b.NoiseOctaves
                && a.NoiseStrength == b.NoiseStrength
                && a.DetailFrequency == b.DetailFrequency
                && a.DetailOctaves == b.DetailOctaves
                && a.DetailStrength == b.DetailStrength
                && ((a.Palette == null && b.Palette == null)
                    || (a.Palette != null && a.Palette.Matches(b.Palette)));
        }

        private static bool ParamsEqual(TerrainNoiseParams a, TerrainNoiseParams b)
        {
            return a.Seed == b.Seed
                && a.BaseFrequency == b.BaseFrequency
                && a.Octaves == b.Octaves
                && a.Lacunarity == b.Lacunarity
                && a.Gain == b.Gain
                && a.ContinentFrequency == b.ContinentFrequency
                && a.ContinentOctaves == b.ContinentOctaves
                && a.ContinentThreshold == b.ContinentThreshold
                && a.ContinentSharpness == b.ContinentSharpness
                && a.ContinentDepth == b.ContinentDepth
                && a.RidgedMix == b.RidgedMix
                && a.PlainMix == b.PlainMix
                && a.PlainFrequency == b.PlainFrequency
                && a.PlainOctaves == b.PlainOctaves
                && a.PlainThreshold == b.PlainThreshold
                && a.PlainSharpness == b.PlainSharpness
                && a.PlainElevation == b.PlainElevation
                && a.DetailMix == b.DetailMix
                && a.DetailFrequency == b.DetailFrequency
                && a.DetailOctaves == b.DetailOctaves
                && a.WarpStrength == b.WarpStrength
                && a.WarpFrequency == b.WarpFrequency
                && a.WarpOctaves == b.WarpOctaves
                && a.WarpSeedOffset == b.WarpSeedOffset
                && a.SeaLevelMeters == b.SeaLevelMeters
                && a.AmplitudeMeters == b.AmplitudeMeters
                && a.BeachShelfAltitudeMeters == b.BeachShelfAltitudeMeters
                && a.BeachShelfWidth == b.BeachShelfWidth
                && a.ColorNoiseFrequency == b.ColorNoiseFrequency
                && a.ColorNoiseOctaves == b.ColorNoiseOctaves
                && a.ColorNoiseSeedOffset == b.ColorNoiseSeedOffset
                && a.ColorDetailFrequency == b.ColorDetailFrequency
                && a.ColorDetailOctaves == b.ColorDetailOctaves
                && a.ColorDetailSeedOffset == b.ColorDetailSeedOffset
                && a.ComputeMask == b.ComputeMask
                && a.ComputeDetail == b.ComputeDetail;
        }

        private void LateUpdate()
        {
            if (body == null || terrain == null || Runner.Ship == null)
            {
                return;
            }
            if (Runner.DominantBody != body)
            {
                SetAllInvisible();
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);

            // [Р“Р РђР¤РРљРђ] РЎРіР»Р°Р¶РёРІР°РЅРёРµ (SMAA/TAA) Р¶РёРІС‘С‚ РЅР° СЌС‚РѕРј Р¶Рµ Main Camera:
            // HDAdditionalCameraData РІ СЃС†РµРЅРµ (antialiasing / SMAAQuality / TAA*).
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            Shader.SetGlobalVector("_PlanetCameraPos", cameraPosition);

            // РўСЂР°РІРµСЂСЃ Р›РћР” вЂ” РєР°Р¶РґС‹Р№ РєР°РґСЂ. Р Р°РЅСЊС€Рµ Р±С‹Р» РіРёСЃС‚РµСЂРµР·РёСЃ РїРѕ СЂРµРЅРґРµСЂ-РїРѕР·РёС†РёРё
            // РєР°РјРµСЂС‹, РЅРѕ РїРѕРґ floating origin РѕРЅР° ~0 Рё РїСЂРё РґРІРёР¶РµРЅРёРё РёРіСЂРѕРєР° РїРѕ С‚РµР»Сѓ
            // РїРѕС‡С‚Рё РЅРµ РјРµРЅСЏРµС‚СЃСЏ в†’ РЅР°Р±РѕСЂ СѓР·Р»РѕРІ В«Р·Р°РјРёСЂР°Р»В» РЅР° СЃРїР°РІРЅРµ Рё РґР°Р»СЊРЅРёРµ С‡Р°РЅРєРё
            // СѓСЃС‚Р°СЂРµРІР°Р»Рё/РєРѕСЃРѕР±РѕС‡РёР»РёСЃСЊ. РўСЂР°РІРµСЂСЃ РґС‘С€РµРІ (РјРµС€Рё РєСЌС€РёСЂРѕРІР°РЅС‹).
            bodyRenderPosition = FloatingOrigin.ToRender(bodyPosition);
            currentBodyPosition = bodyPosition;
            currentBodyOrientation = body.GetVisualOrientation(Runner.TimeSeconds);
            currentBodyRotation = FloatingOrigin.RenderRotation(currentBodyOrientation);

            // РЎРєРѕСЂРѕСЃС‚СЊ РёРіСЂРѕРєР° РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РџРћР’Р•Р РҐРќРћРЎРўР (body-fixed, РѕСЃРё С‡Р°РЅРєР°):
            // РІСЂР°С‰РµРЅРёРµ РїР»Р°РЅРµС‚С‹ РІС‹С‡РёС‚Р°РµРј, РёРЅР°С‡Рµ В«РѕРїРµСЂРµР¶РµРЅРёРµВ» РѕР±Р»Р°РєР° С‚СЂР°РІС‹ СѓРµР·Р¶Р°Р»Рѕ
            // Р±С‹ РґР°Р¶Рµ РєРѕРіРґР° РёРіСЂРѕРє СЃС‚РѕРёС‚. РЎС‡РёС‚Р°РµРј РІ double РѕС‚ astro-РїРѕР·РёС†РёР№:
            // float-СЂР°Р·РЅРѕСЃС‚Рё РЅР° РґРёСЃС‚Р°РЅС†РёРё ~1e6 Рј С€СѓРјСЏС‚ РЅР° РјРµС‚СЂС‹ РІ СЃРµРєСѓРЅРґСѓ.
            Vector3 surfaceLocal = AstroFrame.ToSimulation(
                currentBodyOrientation.Conjugated.Rotate(Runner.PlayerPosition - currentBodyPosition));
            if (hasSurfaceLocalPosition)
            {
                surfaceVelocityLocal = (surfaceLocal - lastSurfaceLocalPosition) / Mathf.Max(1e-4f, Time.deltaTime);
            }

            lastSurfaceLocalPosition = surfaceLocal;
            hasSurfaceLocalPosition = true;
            UpdateTerrainTextureOrigin();
            ApplyTerrainGlobals();

            {
                // РўРµР»-fixed РЅР°РїСЂР°РІР»РµРЅРёСЏ РґР»СЏ current-РєР°РґСЂР°.
                desired.Clear();
                splitNext.Clear();
                for (int face = 0; face < CubeSphere.FaceCount; face++)
                {
                    Traverse(face, 0, 0, 0, cameraPosition);
                }

                // Р—Р°С„РёРєСЃРёСЂРѕРІР°С‚СЊ РїРѕРґСЂР°Р·Р±РёРµРЅРёРµ РєР°РґСЂР° вЂ” Р±Р°Р·Р° РіРёСЃС‚РµСЂРµР·РёСЃР° РІ Traverse
                // РЅР° СЃР»РµРґСѓСЋС‰РµРј РєР°РґСЂРµ, С‡С‚РѕР±С‹ СѓР·РµР» РЅРµ РґСЂРѕР±РёР»СЃСЏ/СЃР»РёРІР°Р»СЃСЏ РєР°Р¶РґС‹Р№ РєР°РґСЂ.
                splitNodes.Clear();
                splitNodes.UnionWith(splitNext);

                // РЎРЅР°С‡Р°Р»Р° РЎРўР РћРРњ/РџРћРљРђР—Р«Р’РђР•Рњ Р¶РµР»Р°РµРјС‹Рµ Рё С‚РѕР»СЊРєРѕ РїРѕС‚РѕРј СЃС‡РёС‚Р°РµРј keep
                // РїРѕ С„Р°РєС‚РёС‡РµСЃРєРѕРјСѓ СЃРѕСЃС‚РѕСЏРЅРёСЋ РєР°РґСЂР°. РРЅР°С‡Рµ РЅР° РєР°РґСЂРµ РїРѕСЏРІР»РµРЅРёСЏ СЂРµР±С‘РЅРєР°
                // keep РµС‰С‘ РІРєР»СЋС‡Р°РµС‚ СЂРѕРґРёС‚РµР»СЏ (СЂРµР±С‘РЅРѕРє Р±С‹Р» РєСЌС€-РїСЂРѕРјР°С…РѕРј) вЂ” РіСЂСѓР±С‹Р№ Рё
                // РїРѕРґСЂРѕР±РЅС‹Р№ С‡Р°РЅРєРё РІРёРґРЅС‹ РІРјРµСЃС‚Рµ: z-fight/В«С…Р»РѕРїРѕРєВ» РЅР° РєР°Р¶РґРѕРј РїРµСЂРµС…РѕРґРµ.
                int budget = firstFrameGuard < 0 ? int.MaxValue : BuildsPerFrame;
                firstFrameGuard = 0;
                int built = 0;
                for (int i = 0; i < desired.Count; i++)
                {
                    long id = NodeId(desired[i]);
                    if (chunks.TryGetValue(id, out Chunk cached))
                    {
                        if (!cached.Visible)
                        {
                            cached.Go.SetActive(true);
                            cached.Visible = true;
                        }

                        continue;
                    }

                    if (built >= budget)
                    {
                        continue;
                    }

                    BuildChunk(desired[i]);
                    built++;
                }

                // keep = РїРѕСЃС‚СЂРѕРµРЅРЅС‹Рµ Р¶РµР»Р°РµРјС‹Рµ Р»РёСЃС‚СЊСЏ + РїСЂРµРґРєРё С‚РµС… Р¶РµР»Р°РµРјС‹С…, С‡С‚Рѕ РЅРµ
                // СѓСЃРїРµР»Рё РїРѕСЃС‚СЂРѕРёС‚СЊ РІ СЌС‚РѕРј РєР°РґСЂРµ (В«Р·Р°С‚С‹С‡РєР°В» РѕС‚ РґС‹СЂ РЅР° СЃРјРµРЅРµ LOD).
                keep.Clear();
                for (int i = 0; i < desired.Count; i++)
                {
                    if (chunks.ContainsKey(NodeId(desired[i])))
                    {
                        keep.Add(NodeId(desired[i]));
                    }
                }

                for (int i = 0; i < desired.Count; i++)
                {
                    Node node = desired[i];
                    if (chunks.ContainsKey(NodeId(node)))
                    {
                        continue;
                    }

                    int face = node.Face;
                    int depth = node.Depth;
                    int ix = node.Ix;
                    int iy = node.Iy;
                    while (depth > 0)
                    {
                        depth--;
                        ix >>= 1;
                        iy >>= 1;
                        keep.Add(NodeId(face, depth, ix, iy));
                    }
                }

                // РџСЂСЏС‡РµРј РІСЃС‘ РІРёРґРёРјРѕРµ, С‡РµРіРѕ РЅРµС‚ РІ keep.
                foreach (KeyValuePair<long, Chunk> kv in chunks)
                {
                    if (kv.Value.Visible && !keep.Contains(kv.Key))
                    {
                        kv.Value.Go.SetActive(false);
                        kv.Value.Visible = false;
                    }
                }

                EvictIfNeeded();
            }

            // РўСЂР°РЅСЃС„РѕСЂРјС‹ РІРёРґРёРјС‹С… С‡Р°РЅРєРѕРІ вЂ” РєР°Р¶РґС‹Р№ РєР°РґСЂ (floating origin + СЃРїРёРЅ).
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Visible)
                {
                    kv.Value.Go.transform.position = NodeRenderPosition(kv.Value.CenterAstro);
                    kv.Value.Go.transform.rotation = currentBodyRotation;
                }
            }

            // Р”РµРєРѕСЂ РїРѕСЃР»Рµ СЂР°СЃСЃС‚Р°РЅРѕРІРєРё С‚СЂР°РЅСЃС„РѕСЂРјРѕРІ С‡Р°РЅРєРѕРІ: РјР°С‚СЂРёС†С‹ РёРЅСЃС‚Р°РЅСЃРѕРІ
            // Р±РµСЂСѓС‚ РјРёСЂРѕРІРѕР№ С‚СЂР°РЅСЃС„РѕСЂРј С‡Р°РЅРєР° С‚РµРєСѓС‰РµРіРѕ РєР°РґСЂР°.
            Shader.SetGlobalFloat("_GroundDecorTime", Time.time);
            // РћР±С…РѕРґС‹ С‡Р°РЅРєРѕРІ (РїРѕРёСЃРє СѓСЃС‚Р°СЂРµРІС€РёС…/РЅРµРїРѕСЃС‚СЂРѕРµРЅРЅС‹С…, РІС‹РіСЂСѓР·РєР° РґР°Р»СЊРЅРёС…)
            // вЂ” РЅРµ РєР°Р¶РґС‹Р№ РєР°РґСЂ: РѕРЅРё O(С‡Р°РЅРєРёГ—СЃР»РѕРё). Р”РѕСЃС‚Р°С‚РѕС‡РЅРѕ РїСЂРё СЃРґРІРёРіРµ
            // РєР°РјРµСЂС‹ > 0.25 Рј РёР»Рё СЂР°Р· РІ 6 РєР°РґСЂРѕРІ (РґР»СЏ В«РѕСЃРµРґР°РЅРёСЏВ» РѕР±Р»Р°РєР° СЃС‚РѕСЏ).
            bool decorScanDue = !hasDecorScanCamera
                || (cameraPosition - lastDecorScanCamera).sqrMagnitude > 0.0625f
                || (++decorScanTick % 6) == 0;
            if (decorScanDue)
            {
                // РџРµСЂРµСЃР±РѕСЂРєРё В«РѕР±Р»Р°РєР°В» Сѓ РёРіСЂРѕРєР° вЂ” РїРµСЂРІС‹РјРё: РёРј РіР°СЂР°РЅС‚РёСЂРѕРІР°РЅ СЃР»РѕС‚
                // СЃРµСЃСЃРёРё, РёРЅР°С‡Рµ РѕС‡РµСЂРµРґСЊ Р·Р°РЅРёРјР°СЋС‚ РґР°Р»СЊРЅРёРµ РїРµСЂРІС‹Рµ СЃР±РѕСЂРєРё.
                RefreshMovingDecor(cameraPosition);
                EnsureVisibleDecor(cameraPosition);
                TrimDistantDecor(cameraPosition);
                lastDecorScanCamera = cameraPosition;
                hasDecorScanCamera = true;
            }

            StepDecorBuilds();
            UpdateDecorCollision();
            DrawDecor(cameraPosition);
        }

        /// <summary>
        /// РўСЂРёРїР»Р°РЅР°СЂРЅС‹Рµ UV С‚РµРєСЃС‚СѓСЂ СЂРµР»СЊРµС„Р° вЂ” camera-relative. РђР±СЃРѕР»СЋС‚РЅС‹Рµ РєРѕРѕСЂРґРёРЅР°С‚С‹
        /// РѕС‚ С†РµРЅС‚СЂР° РїР»Р°РЅРµС‚С‹ (~1.14e6 Рј) РІРѕ float РєРІР°РЅС‚СѓСЋС‚СЃСЏ С€Р°РіРѕРј ~6 СЃРј (~2.5
        /// С‚РµРєСЃРµР»СЏ РїСЂРё TextureScale 0.04) вЂ” РёР· СЌС‚РѕРіРѕ РєРІР°РЅС‚РѕРІР°РЅРёСЏ СЂРѕР¶РґР°Р»СЃСЏ РјСѓР°СЂ
        /// (В«РїРѕР»РѕСЃС‹В», В«РґРІРёРіР°Р»РёСЃСЊВ» РІРјРµСЃС‚Рµ СЃ origin). РЁРµР№РґРµСЂ СЃС‡РёС‚Р°РµС‚ РґРµР»СЊС‚Сѓ РїРѕР·РёС†РёРё
        /// РѕС‚ РєР°РјРµСЂС‹ (РјР°Р»С‹Рµ С‚РѕС‡РЅС‹Рµ С‡РёСЃР»Р° РІ СЃС†РµРЅРµ СЃ floating origin) Рё РїРѕРІРѕСЂР°С‡РёРІР°РµС‚
        /// РµС‘ РІ С‚РµР»Рѕ-fixed РѕСЃРё РјР°С‚СЂРёС†РµР№ _TerrainWorldToBody; С„Р°Р·Сѓ С‚РµРєСЃС‚СѓСЂС‹ (РґРѕР»Рё
        /// UV РІ double) РІРѕР·РІСЂР°С‰Р°СЋС‚ РіР»РѕР±Р°Р»С‹ С„Р°Р· вЂ” РїР°С‚С‚РµСЂРЅ В«РїСЂРёР±РёС‚В» Рє Р·РµРјР»Рµ.
        /// </summary>
        private void UpdateTerrainTextureOrigin()
        {
            if (terrain == null || !(terrain.TextureScale > 0d))
            {
                return;
            }

            double scale = terrain.TextureScale;
            Vector3d relative = Runner.PlayerPosition - currentBodyPosition;
            Vector3d bodyAstro = currentBodyOrientation.Conjugated.Rotate(relative);

            // Р¤Р°Р·С‹ вЂ” РІ sim-РѕСЃСЏС… С€РµР№РґРµСЂР° (positionWS): _TerrainWorldToBody РґР°С‘С‚
            // РґРµР»СЊС‚Сѓ РІ sim-С‚РµР»Рѕ-РѕСЃСЏС…, Р° astro-РІРµРєС‚РѕСЂ Р·РґРµСЃСЊ РІ Z-up. РњРѕСЃС‚ С‚РѕС‚ Р¶Рµ,
            // С‡С‚Рѕ AstroFrame.ToSimulation: sim = (x, z, в€’y). Р‘РµР· РєРѕРЅРІРµСЂСЃРёРё С„Р°Р·Р°
            // Рё РґРµР»СЊС‚Р° Р¶РёРІСѓС‚ РІ СЂР°Р·РЅС‹С… РєР°РґСЂР°С… вЂ” С‚РµРєСЃС‚СѓСЂР° В«РїРѕР»Р·С‘С‚В» РїСЂРё РґРІРёР¶РµРЅРёРё.
            double sx = bodyAstro.X;
            double sy = bodyAstro.Z;
            double sz = -bodyAstro.Y;

            // РњРёСЂ в†’ С‚РµР»Рѕ-fixed (РѕР±СЉРµРєС‚РЅС‹Рµ РѕСЃРё РјРµС€РµР№): РѕР±СЂР°С‚РЅС‹Р№ РїРѕРІРѕСЂРѕС‚ С‡Р°РЅРєР°.
            Shader.SetGlobalMatrix("_TerrainWorldToBody", Matrix4x4.Rotate(Quaternion.Inverse(currentBodyRotation)));

            // uvX = pos.zy, uvY = pos.xz, uvZ = pos.xy.
            Shader.SetGlobalVector("_TerrainUVPhase0", new Vector4(
                (float)Wrap01(sz * scale), (float)Wrap01(sy * scale),
                (float)Wrap01(sx * scale), (float)Wrap01(sz * scale)));
            Shader.SetGlobalVector("_TerrainUVPhase1", new Vector4(
                (float)Wrap01(sx * scale), (float)Wrap01(sy * scale), 0f, 0f));
        }

        private static double Wrap01(double value)
        {
            return value - System.Math.Floor(value);
        }

        /// <summary>
        /// РћР±РЅРѕРІРёС‚СЊ С†РёР»РёРЅРґСЂС‹-РєРѕР»Р»Р°Р№РґРµСЂС‹ СЃС‚РІРѕР»РѕРІ (СЃР»РѕРё СЃ Collides) РґР»СЏ С„РёР·РёРєРё
        /// РёРіСЂРѕРєР°: РёРЅСЃС‚Р°РЅСЃС‹ С‚РѕР»СЊРєРѕ РёР· С‡Р°РЅРєРѕРІ РІ ~60 Рј РѕС‚ РёРіСЂРѕРєР°, РїРѕР·РёС†РёРё вЂ” РІ
        /// РёРЅРµСЂС†РёР°Р»СЊРЅС‹Р№ astro-РєР°РґСЂ (РєР°Рє PlayerPosition).
        /// </summary>
        private void UpdateDecorCollision()
        {
            if (decorProfile == null || decorProfile.Layers == null || body == null || Runner == null)
            {
                return;
            }

            GroundDecorCollisionRegistry.Begin(body.Name);
            bool anyCollides = false;
            for (int i = 0; i < decorProfile.Layers.Count; i++)
            {
                GroundDecorLayer layer = decorProfile.Layers[i];
                if (layer != null && layer.Enabled && layer.Collides)
                {
                    anyCollides = true;
                    break;
                }
            }

            if (!anyCollides)
            {
                return;
            }

            const float collisionRange = 60f;
            Vector3 playerRender = FloatingOrigin.ToRender(Runner.PlayerPosition);
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (chunk.Decor.Count == 0)
                {
                    continue;
                }

                float chunkDistance = Mathf.Max(
                    0f, Vector3.Distance(playerRender, chunk.Go.transform.position) - chunk.BoundsRadius);
                // РљРѕР»Р»Р°Р№РґРµСЂС‹ РЅСѓР¶РЅС‹ РЅРµР·Р°РІРёСЃРёРјРѕ РѕС‚ РІРёРґРёРјРѕСЃС‚Рё: РІРѕ РІСЂРµРјСЏ LOD-РїРµСЂРµС…РѕРґР°
                // Р±Р»РёР¶РЅРёР№ С‡Р°РЅРє РјРѕР¶РµС‚ Р±С‹С‚СЊ СЃРєСЂС‹С‚, РЅРѕ СЃС‚РІРѕР»С‹ С„РёР·РёС‡РµСЃРєРё РЅР° РјРµСЃС‚Рµ.
                if (chunkDistance > collisionRange)
                {
                    continue;
                }

                Matrix4x4 chunkMatrix = chunk.Go.transform.localToWorldMatrix;
                for (int i = 0; i < chunk.Decor.Count; i++)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    GroundDecorLayer layer = runtime.Profile;
                    if (layer == null || !layer.Collides || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    int count = runtime.Instances.Length;
                    for (int k = 0; k < count; k++)
                    {
                        GroundDecorInstance instance = runtime.Instances[k];
                        Vector3 localPosition = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
                        if ((chunkMatrix.MultiplyPoint3x4(localPosition) - playerRender).sqrMagnitude
                            > collisionRange * collisionRange)
                        {
                            continue;
                        }

                        Vector3 localNormal = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                        if (localNormal.sqrMagnitude < 1e-6f)
                        {
                            localNormal = Vector3.up;
                        }

                        Vector3d relAstro = AstroFrame.ToAstro(localPosition);
                        Vector3d bodyFixed = chunk.CenterAstro + relAstro;
                        Vector3d inertial = currentBodyPosition + currentBodyOrientation.Rotate(bodyFixed);
                        Vector3d upAstro = AstroFrame.ToAstro(localNormal).Normalized;
                        Vector3d upInertial = currentBodyOrientation.Rotate(upAstro);
                        GroundDecorCollisionRegistry.Add(
                            inertial, upInertial, layer.CollisionRadiusMeters, layer.CollisionHeightMeters);
                    }
                }
            }
        }

        private void Traverse(int face, int depth, int ix, int iy, Vector3 cameraPosition)
        {
            Vector3d dir = CubeSphere.NodeCenterDirection(face, depth, ix, iy);
            Vector3 worldCenter = NodeRenderPosition(dir * body.Radius);

            Vector3 bodyToCam = cameraPosition - bodyRenderPosition;
            Vector3 bodyToNode = worldCenter - bodyRenderPosition;
            if (bodyToNode.sqrMagnitude > 1e-6f && bodyToCam.sqrMagnitude > 1e-6f)
            {
                // Р—Р°РґРЅСЏСЏ РїРѕР»СѓСЃС„РµСЂР° РЅРµ СЃС‚СЂРѕРёС‚СЃСЏ.
                if (Vector3.Dot(bodyToNode.normalized, bodyToCam.normalized) < -0.15f)
                {
                    return;
                }
            }

            double nodeSize = body.Radius * 1.5707963267948966d / (1 << depth);
            float distance = Vector3.Distance(cameraPosition, worldCenter);
            // Р”РёСЃС‚Р°РЅС†РёСЏ РґРѕ Р‘Р›РР–РђР™РЁР•Р™ С‚РѕС‡РєРё СѓР·Р»Р°, Р° РЅРµ РґРѕ С†РµРЅС‚СЂР°: Сѓ РєСЂСѓРїРЅРѕРіРѕ
            // СѓР·Р»Р° С†РµРЅС‚СЂ РјРѕР¶РµС‚ Р±С‹С‚СЊ РґР°Р»РµРєРѕ Р·Р° РїРѕСЂРѕРіРѕРј, С…РѕС‚СЏ РµРіРѕ РєСЂР°Р№ РІРёРґРµРЅ
            // РІРїР»РѕС‚РЅСѓСЋ. РЎ С†РµРЅС‚СЂРѕРј С‚Р°РєРѕР№ СѓР·РµР» РЅРµ РґСЂРѕР±РёР»СЃСЏ, Р° РїСЂРё РїРѕРІРѕСЂРѕС‚Рµ РєР°РјРµСЂС‹
            // РєСЂР°Р№ В«РІРЅРµР·Р°РїРЅРѕВ» С‚СЂРµР±РѕРІР°Р» РґРµС‚Р°Р»РёР·Р°С†РёРё вЂ” LOD-СЃРєР°С‡РѕРє/РјРёРіР°РЅРёРµ.
            // РџРѕР»СѓРґРёР°РіРѕРЅР°Р»СЊ РїР°С‚С‡Р° в‰€ nodeSizeВ·в€љ2/2.
            float closestDistance = Mathf.Max(0f, distance - (float)(nodeSize * 0.70710678d));

            bool split;
            if (depth < MaxDepth && desired.Count + 4 < MaxNodes)
            {
                // Р“РёСЃС‚РµСЂРµР·РёСЃ: РґРµР»РёРј РЅР° splitDistance, Р° СЃР»РёРІР°РµРј С‚РѕР»СЊРєРѕ РєРѕРіРґР° СѓС€Р»Рё
                // Р·Р°РјРµС‚РЅРѕ РґР°Р»СЊС€Рµ (Г—MergeHysteresis). РњРµР¶РґСѓ РїРѕСЂРѕРіР°РјРё РґРµСЂР¶РёРј РїСЂРѕС€Р»С‹Р№
                // СѓСЂРѕРІРµРЅСЊ вЂ” РёРЅР°С‡Рµ СѓР·РµР» В«РґСЂРѕР±РёС‚СЃСЏ/СЃР»РёРІР°РµС‚СЃСЏВ» РєР°Р¶РґС‹Р№ РєР°РґСЂ (LOD-С„Р»Р°Рї,
                // СЃРёР»СЊРЅРµРµ Р·Р°РјРµС‚РЅС‹Р№ РЅР° СЃРєРѕСЂРѕСЃС‚Рё).
                double splitDistance = nodeSize * SplitFactor;
                split = splitNodes.Contains(NodeId(face, depth, ix, iy))
                    ? closestDistance < splitDistance * MergeHysteresis
                    : closestDistance < splitDistance;
            }
            else
            {
                split = false;
            }

            if (split)
            {
                splitNext.Add(NodeId(face, depth, ix, iy));
                Traverse(face, depth + 1, (ix * 2) + 0, (iy * 2) + 0, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 1, (iy * 2) + 0, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 0, (iy * 2) + 1, cameraPosition);
                Traverse(face, depth + 1, (ix * 2) + 1, (iy * 2) + 1, cameraPosition);
                return;
            }

            desired.Add(new Node { Face = face, Depth = depth, Ix = ix, Iy = iy });
        }

        /// <summary>
        /// Р РµРЅРґРµСЂ-РїРѕР·РёС†РёСЏ С‚РѕС‡РєРё, Р·Р°РґР°РЅРЅРѕР№ С‚РµР»-fixed РІРµРєС‚РѕСЂРѕРј РѕС‚ С†РµРЅС‚СЂР° С‚РµР»Р°.
        /// РЎС‡РёС‚Р°РµРј Р’ DOUBLE (bodyPos + orientationВ·local в€’ anchor), РїРѕС‚РѕРј float:
        /// РёРЅР°С‡Рµ СЃСѓРјРјР° РґРІСѓС… Р±РѕР»СЊС€РёС… float-С‡РёСЃРµР» (~СЂР°РґРёСѓСЃ) С‚РµСЂСЏРµС‚ С‚РѕС‡РЅРѕСЃС‚СЊ Рё
        /// С‡Р°РЅРє/РєР°РјРµСЂР° РґСЂРѕР¶Р°С‚ РЅР° РґРµСЃСЏС‚РєРё СЃР°РЅС‚РёРјРµС‚СЂРѕРІ.
        /// </summary>
        private Vector3 NodeRenderPosition(Vector3d centerBodyFixed)
        {
            Vector3d absolute = currentBodyPosition + currentBodyOrientation.Rotate(centerBodyFixed);
            return FloatingOrigin.ToRender(absolute);
        }

        private void BuildChunk(Node node)
        {
            int res = System.Math.Max(2, TileResolution);
            int n = res + 1;
            int grid = n + 2;
            int coreCount = n * n;
            int perEdge = n;
            int totalVerts = coreCount + (4 * perEdge);
            int totalTris = (((n - 1) * (n - 1)) + (4 * (n - 1))) * 6;

            double sizeUv = 1d / (1 << node.Depth);
            double u0 = node.Ix * sizeUv;
            double v0 = node.Iy * sizeUv;
            double stepUv = sizeUv / (n - 1);

            // Р¦РµРЅС‚СЂ СѓР·Р»Р° (С‚РµР»-fixed, double): РІРµСЂС€РёРЅС‹ РјРµС€Р° С…СЂР°РЅРёРј Р›РћРљРђР›Р¬РќРћ
            // РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РЅРµРіРѕ. РРЅР°С‡Рµ float РЅР° СЂР°РґРёСѓСЃРµ РїР»Р°РЅРµС‚С‹ (~1.1e6 Рј) РґР°С‘С‚
            // ulp ~0.06 Рј в†’ РєР°РјРµСЂР°/СЂРµР»СЊРµС„ РґСЂРѕР¶Р°С‚; РѕРіСЂРѕРјРЅС‹Рµ РІРµСЂС€РёРЅС‹ Р»РѕРјР°СЋС‚ Рё
            // camera-relative СЂРµРЅРґРµСЂ (С‚СЂР°РЅСЃС„РѕСЂРј С‚РµР»Р° РІ СЏРєРѕСЂРµ ~0).
            Vector3d centerAstro = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy) * body.Radius;
            Vector3 centerUnity = AstroFrame.ToSimulation(centerAstro);

            double amplitude = System.Math.Max(1d, terrain.AmplitudeMeters);
            double seaLevel = terrain.SeaLevelMeters;
            double seaEps = amplitude * 0.001d;

            // РўР°Р№Р»С‹ СЃ РѕРІРµСЂР»Р°РїРѕРј РІ 1 РІРµСЂС€РёРЅСѓ вЂ” РЅРѕСЂРјР°Р»Рё РёР· С†РµРЅС‚СЂР°Р»СЊРЅС‹С… СЂР°Р·РЅРѕСЃС‚РµР№.
            var dirs = new NativeArray<double3>(grid * grid, Allocator.TempJob);
            var jobHeights = new NativeArray<double>(grid * grid, Allocator.TempJob);
            var jobMasks = new NativeArray<float>(grid * grid, Allocator.TempJob);
            var jobDetails = new NativeArray<float>(grid * grid, Allocator.TempJob);

            for (int gi = 0; gi < grid; gi++)
            {
                double u = u0 + ((gi - 1) * stepUv);
                for (int gj = 0; gj < grid; gj++)
                {
                    double v = v0 + ((gj - 1) * stepUv);
                    Vector3d dir = CubeSphere.Direction(node.Face, u, v);
                    dirs[(gi * grid) + gj] = new double3(dir.X, dir.Y, dir.Z);
                }
            }

            // Р¦РІРµС‚ РјР°СЃРєРё/РґРµС‚Р°Р»Рё С‚РµРїРµСЂСЊ СЃС‡РёС‚Р°РµС‚СЃСЏ РЅР° РїРёРєСЃРµР»СЊ РІ С€РµР№РґРµСЂРµ вЂ” РІ
            // tile-job'Рµ СЌС‚Рё РїРѕС‚РѕРєРё РЅРµ РЅСѓР¶РЅС‹. РџСЂР°РІРєСѓ РґРµР»Р°РµРј РІ Р›РћРљРђР›Р¬РќРћР™ РєРѕРїРёРё,
            // С‡С‚РѕР±С‹ РЅРµ СЃР»РѕРјР°С‚СЊ ParamsEqual РїРѕ РєСЌС€РёСЂРѕРІР°РЅРЅРѕРјСѓ noiseParams.
            // Color mask + detail come from the SAME CPU noise the decor
            // placement reads (parity paint/placement): the shader interpolates
            // per-vertex values and runs no Fbm of its own. noiseParams is used
            // as-is, so ParamsEqual caching is untouched.
            TerrainTileJob tileJob = new TerrainTileJob
            {
                Params = noiseParams,
                Directions = dirs,
                Heights = jobHeights,
                ColorMasks = jobMasks,
                ColorDetails = jobDetails
            };
            tileJob.Schedule(grid * grid, 64, new JobHandle()).Complete();

            var positions = new Vector3[grid * grid];
            for (int gi = 0; gi < grid; gi++)
            {
                for (int gj = 0; gj < grid; gj++)
                {
                    int vi = (gi * grid) + gj;
                    double3 d = dirs[vi];
                    double rawHeight = jobHeights[vi] * amplitude;
                    double height = rawHeight < seaLevel ? seaLevel : rawHeight;
                    Vector3d absAstro = new Vector3d(d.x, d.y, d.z) * (body.Radius + height);
                    positions[vi] = AstroFrame.ToSimulation(absAstro - centerAstro);
                }
            }

            // dirs/jobHeights РµС‰С‘ РЅСѓР¶РЅС‹ РґР»СЏ per-pixel Р°С‚СЂРёР±СѓС‚РѕРІ РЅРёР¶Рµ.
            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            // Albedo: fragment shader interpolates per-vertex data (vertex color on coarse LOD
            // gave blocky patch borders). We pass what geometry cannot derive: body-fixed direction,
            // raw height (pre-sea-clamp), slope cosine (rock/snow bands) AND the CPU color mask +
            // detail — the same pattern the decor placement reads (parity paint/placement).
            var surfaceDirs = new Vector3[totalVerts];
            var surfaceExtra = new Vector4[totalVerts];

            for (int row = 0; row < n; row++)
            {
                for (int col = 0; col < n; col++)
                {
                    int halo = ((row + 1) * grid) + (col + 1);
                    int index = (row * n) + col;
                    Vector3 position = positions[halo];
                    vertices[index] = position;

                    Vector3 dCol = positions[halo + 1] - positions[halo - 1];
                    Vector3 dRow = positions[halo + grid] - positions[halo - grid];
                    Vector3 normal = Vector3.Cross(dCol, dRow);
                    float normalLength = normal.magnitude;
                    Vector3 radial = (position + centerUnity).normalized;
                    normals[index] = normalLength > 1e-10f ? normal / normalLength : radial;
                    if (Vector3.Dot(normals[index], radial) < 0f)
                    {
                        normals[index] = -normals[index];
                    }

                    double3 d = dirs[halo];
                    surfaceDirs[index] = new Vector3((float)d.x, (float)d.y, (float)d.z);
                    double rawHeight = jobHeights[halo] * amplitude;
                    surfaceExtra[index] = new Vector4(
                        (float)rawHeight, Vector3.Dot(normals[index], radial),
                        jobMasks[halo], jobDetails[halo]);
                }
            }

            dirs.Dispose();
            jobHeights.Dispose();
            jobMasks.Dispose();
            jobDetails.Dispose();

            if (diagnosticChunksLogged < 8)
            {
                diagnosticChunksLogged++;
                int landCore = 0;
                double minH = double.MaxValue;
                double maxH = double.MinValue;
                for (int k = 0; k < coreCount; k++)
                {
                    double raw = surfaceExtra[k].x;
                    if (raw > seaLevel + seaEps)
                    {
                        landCore++;
                    }

                    minH = System.Math.Min(minH, raw);
                    maxH = System.Math.Max(maxH, raw);
                }

                Debug.Log(string.Format(
                    "[PlanetSurface] С‡Р°РЅРє f{0} d{1}: СЃСѓС€Р° {2:P0} РІРµСЂС€., h=[{3:F0}..{4:F0}] Рј",
                    node.Face, node.Depth, (double)landCore / coreCount, minH, maxH));
            }

            // Р®Р±РєРё: РґСѓР±Р»РёСЂСѓРµРј СЂС‘Р±СЂР°, СѓС‚РѕРїР»РµРЅРЅС‹Рµ Рє С†РµРЅС‚СЂСѓ РїР»Р°РЅРµС‚С‹ вЂ” РїРµСЂРµРєСЂС‹РІР°СЋС‚
            // С‚СЂРµС‰РёРЅС‹ РјРµР¶РґСѓ СЃРѕСЃРµРґРЅРёРјРё LOD-СѓСЂРѕРІРЅСЏРјРё.
            float skirtDepth = (float)(body.Radius * 1.5707963267948966d / (1 << node.Depth) * SkirtFactor);
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * perEdge);
                for (int k = 0; k < perEdge; k++)
                {
                    int coreIndex;
                    switch (edge)
                    {
                        case 0: coreIndex = k; break;                        // row 0
                        case 1: coreIndex = (res * n) + k; break;            // row res
                        case 2: coreIndex = k * n; break;                    // col 0
                        default: coreIndex = (k * n) + res; break;           // col res
                    }

                    Vector3 p = vertices[coreIndex];
                    Vector3 inward = (p + centerUnity).normalized;
                    vertices[baseIndex + k] = p - (inward * skirtDepth);
                    normals[baseIndex + k] = normals[coreIndex];
                    surfaceDirs[baseIndex + k] = surfaceDirs[coreIndex];
                    surfaceExtra[baseIndex + k] = surfaceExtra[coreIndex];
                }
            }

            var triangles = new int[totalTris];
            int t = 0;
            for (int row = 0; row < n - 1; row++)
            {
                for (int col = 0; col < n - 1; col++)
                {
                    int a = (row * n) + col;
                    int b = a + 1;
                    int c = a + n;
                    int d = c + 1;
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * perEdge);
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
                    triangles[t++] = a;
                    triangles[t++] = c;
                    triangles[t++] = b;
                    triangles[t++] = b;
                    triangles[t++] = c;
                    triangles[t++] = d;
                }
            }

            long id = NodeId(node);
            Chunk chunk = new Chunk
            {
                Go = new GameObject("PlanetChunk_" + node.Face + "_" + node.Depth + "_" + node.Ix + "_" + node.Iy),
                Mesh = new Mesh()
            };
            chunk.Mesh.MarkDynamic();
            chunk.Go.transform.SetParent(surfaceRoot, false);
            chunk.Go.transform.localScale = Vector3.one;
            chunk.CenterAstro = centerAstro;
            chunk.Node = node;
            chunk.Depth = node.Depth;
            chunk.BoundsRadius = (float)(body.Radius * 1.5707963267948966d / (1 << node.Depth) * 0.70710678d);
            chunk.Go.transform.position = NodeRenderPosition(centerAstro);
            chunk.Go.transform.rotation = currentBodyRotation;
            MeshFilter filter = chunk.Go.AddComponent<MeshFilter>();
            chunk.Renderer = chunk.Go.AddComponent<MeshRenderer>();
            chunk.Renderer.sharedMaterial = GetMaterial();
            filter.sharedMesh = chunk.Mesh;

            chunk.Mesh.Clear();
            chunk.Mesh.vertices = vertices;
            chunk.Mesh.normals = normals;
            chunk.Mesh.SetUVs(1, new List<Vector3>(surfaceDirs));
            chunk.Mesh.SetUVs(2, new List<Vector4>(surfaceExtra));
            chunk.Mesh.triangles = triangles;
            chunk.Mesh.RecalculateBounds();
            // Р—Р°РїР°СЃ Рє РіСЂР°РЅРёС†Р°Рј (РїРѕР»СЂР°Р·РјРµСЂР° СѓР·Р»Р°): СЃС‚СЂР°С…РѕРІРєР° РѕС‚ Р»РѕР¶РЅРѕРіРѕ
            // С„СЂСѓСЃС‚СѓРј-РєСѓР»Р»РёРЅРіР° С‡Р°РЅРєР° РЅР° РєСЂР°СЋ РєР°РґСЂР° вЂ” РёРЅР°С‡Рµ РїСЂРё РїРѕРІРѕСЂРѕС‚Рµ РєР°РјРµСЂС‹
            // РґР°Р»С‘РєРёРµ С‡Р°РЅРєРё РјРёРіР°СЋС‚ (В«РїРѕСЏРІР»СЏРµС‚СЃСЏ/РїСЂРѕРїР°РґР°РµС‚В»). Р¦РµРЅР° вЂ” С‡СѓС‚СЊ РјРµРЅСЊС€Рµ
            // РѕС‚СЃРµРєР°РµС‚СЃСЏ Р·Р° РєР°РґСЂРѕРј, С‚РѕС‡РЅРѕСЃС‚СЊ РІРёРґРёРјРѕСЃС‚Рё РЅРµ СЃС‚СЂР°РґР°РµС‚.
            Bounds paddedBounds = chunk.Mesh.bounds;
            paddedBounds.Expand(chunk.BoundsRadius);
            chunk.Mesh.bounds = paddedBounds;

            chunk.Visible = true;
            chunks[id] = chunk;
        }

        /// <summary>
        /// РџРѕСЃС‚Р°РЅРѕРІРєР° Р°СЃРёРЅС…СЂРѕРЅРЅРѕР№ СЃР±РѕСЂРєРё СЃР»РѕСЏ РґРµРєРѕСЂР° РІ С‡Р°РЅРєРµ. РўСЏР¶С‘Р»Р°СЏ СЂР°Р±РѕС‚Р°
        /// РёРґС‘С‚ РїРѕСЂС†РёСЏРјРё РїРѕ Р±СЋРґР¶РµС‚Сѓ РєР°РґСЂР° (StepDecorBuilds), СЂРµР·СѓР»СЊС‚Р°С‚
        /// РїРѕСЏРІР»СЏРµС‚СЃСЏ С‡РµСЂРµР· РЅРµСЃРєРѕР»СЊРєРѕ РєР°РґСЂРѕРІ. Р’РѕР·РІСЂР°С‰Р°РµС‚ false, РµСЃР»Рё СЃР±РѕСЂРєР°
        /// СѓР¶Рµ РёРґС‘С‚ РёР»Рё РёСЃС‡РµСЂРїР°РЅ Р»РёРјРёС‚ РѕРґРЅРѕРІСЂРµРјРµРЅРЅС‹С… СЃР±РѕСЂРѕРє.
        /// </summary>
        private bool TryQueueDecorBuild(Chunk chunk, int layerIndex, Vector3 cameraPosition, bool nearPlayer)
        {
            if (decorProfile == null || decorProfile.Layers == null
                || layerIndex < 0 || layerIndex >= decorProfile.Layers.Count)
            {
                return false;
            }

            GroundDecorLayer layer = decorProfile.Layers[layerIndex];
            if (layer == null || !layer.Enabled || layer.NearMeshes == null || layer.NearMeshes.Length == 0
                || layer.MaxInstancesPerChunk <= 0)
            {
                return false;
            }

            if (decorBuildSessions.Count >= MaxDecorBuildSessions)
            {
                return false;
            }

            if (!nearPlayer)
            {
                // Р РµР·РµСЂРІ СЃР»РѕС‚РѕРІ РїРѕРґ РёРіСЂРѕРєР°: РґР°Р»СЊРЅРёРµ РїРµСЂРІС‹Рµ СЃР±РѕСЂРєРё (РґРµСЂРµРІСЊСЏ/РєР°РјРЅРё
                // Р·Р° СЃРїРёРЅРѕР№) РЅРµ РґРѕР»Р¶РЅС‹ Р·Р°РЅРёРјР°С‚СЊ РѕС‡РµСЂРµРґСЊ, РїРѕРєР° СЂСЏРґРѕРј РЅСѓР¶РЅР° С‚СЂР°РІР°.
                int farSessions = 0;
                for (int i = 0; i < decorBuildSessions.Count; i++)
                {
                    if (!decorBuildSessions[i].NearPlayer)
                    {
                        farSessions++;
                    }
                }

                if (farSessions >= MaxDecorBuildSessions - PlayerReservedSessions)
                {
                    return false;
                }
            }

            long chunkId = NodeId(chunk.Node);
            if (HasDecorBuildSession(chunkId, layerIndex))
            {
                return false;
            }

            DecorLayerRuntime previous = null;
            for (int i = 0; i < chunk.Decor.Count; i++)
            {
                if (chunk.Decor[i].LayerIndex == layerIndex)
                {
                    previous = chunk.Decor[i];
                    break;
                }
            }

            // РЎРЅРёРјРѕРє Р Р•РќР”Р•Р -РјРµС€Р° С‡Р°РЅРєР°: Р±Р°Р·С‹ РґРµРєРѕСЂР° СЃР°Р¶Р°РµРј РЅР° РЅРµРіРѕ, Р° РЅРµ РЅР°
            // Р°РЅР°Р»РёС‚РёС‡РµСЃРєСѓСЋ РІС‹СЃРѕС‚Сѓ вЂ” РЅР° РіСЂСѓР±РѕРј LOD (С€Р°Рі РІРµСЂС€РёРЅС‹ ~7 Рј) РјРµС€
            // РѕС‚Р»РёС‡Р°РµС‚СЃСЏ РЅР° РґРµСЃСЏС‚РєРё СЃР°РЅС‚РёРјРµС‚СЂРѕРІ, РёР·-Р·Р° С‡РµРіРѕ С‚СЂР°РІР° РІРёСЃРµР»Р° Рё
            // С‚РѕРЅСѓР»Р° РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РІРёРґРёРјРѕР№ Р·РµРјР»Рё. Р”Р»СЏ С‚СЂР°РІС‹ (Burst-РїСѓС‚СЊ) вЂ”
            // РЅР°С‚РёРІРЅР°СЏ РєРѕРїРёСЏ Р±РµР· managed-Р°Р»Р»РѕРєР°С†РёРё.
            int coreN = System.Math.Max(2, TileResolution) + 1;
            NativeArray<Vector3> meshVerticesNative = default;
            Vector3[] meshVertices = null;
            if (layer.PerInstanceDensity)
            {
                int vertexCount = chunk.Mesh.vertexCount;
                if (vertexCount > 0)
                {
                    meshVerticesNative = DecorArrayPool.Rent<Vector3>(vertexCount);
                    meshVerticesNative.CopyFrom(chunk.Mesh.vertices);
                }
            }
            else
            {
                meshVertices = chunk.Mesh.vertices;
            }

            double sizeUv = 1d / (1 << chunk.Node.Depth);
            double u0 = chunk.Node.Ix * sizeUv;
            double v0 = chunk.Node.Iy * sizeUv;
            double stepUv = sizeUv / (coreN - 1);
            double chunkArc = body.Radius * 1.5707963267948966d * sizeUv;

            double spacing = System.Math.Max(0.25d, layer.SpacingMeters);
            int cells = (int)System.Math.Ceiling(chunkArc / spacing);
            cells = System.Math.Max(1, System.Math.Min(System.Math.Max(1, layer.MaxCellsPerAxis), cells));
            int count = cells * cells;

            if (layer.PerInstanceDensity)
            {
                // Р“СЂСѓР±С‹Рµ LOD (РѕРіСЂРѕРјРЅС‹Рµ РїР°С‚С‡Рё) С‚СЂР°РІСѓ РЅРµ РїСЂРµРґСЃС‚Р°РІР»СЏСЋС‚: СЃРµС‚РєР° РЅР°
                // РЅРёС… РЅРµ СЂР°Р·СЂРµС€Р°РµС‚ С€Р°Рі, Р° В«РґРёСЃС‚Р°РЅС†РёСЏ 0В» РґРµСЂР¶Р°Р»Р° РёС… РІ РѕС‡РµСЂРµРґРё
                // РІРµС‡РЅРѕ Рё РІС‹С‚РµСЃРЅСЏР»Р° СЃР±РѕСЂРєРё Сѓ РёРіСЂРѕРєР°. РџРѕРјРµС‡Р°РµРј РєР°Рє СЃРґРµР»Р°РЅРЅС‹Рµ.
                double maxArc = spacing * layer.MaxCellsPerAxis * 8d;
                if (maxArc > 0d && chunkArc > maxArc)
                {
                    chunk.DecorBuiltMask |= 1 << layerIndex;
                    return false;
                }

            }

            double windAzimuthRad = 0d;
            if (layer.WindZoneFrequency > 0d)
            {
                double3 chunkDir = math.normalize(new double3(
                    chunk.CenterAstro.X, chunk.CenterAstro.Y, chunk.CenterAstro.Z));
                // РћРґРёРЅ РєР°РЅР°Р» (РЅРµ atan2 РґРІСѓС…): СѓРіРѕР» вЂ” РїР»Р°РІРЅРѕРµ РїРѕР»Рµ, Р±РµР·
                // В«Р·Р°РєСЂСѓС‚РєРёВ» РЅР°РїСЂР°РІР»РµРЅРёСЏ РІ С‚РѕС‡РєР°С…, РіРґРµ РѕР±Р° РєР°РЅР°Р»Р° РѕРєРѕР»Рѕ РЅСѓР»СЏ.
                windAzimuthRad = TerrainNoise.SampleDecorNoise(
                    noiseParams, layer.WindZoneSeedOffset, layer.WindZoneFrequency,
                    layer.WindZoneOctaves, chunkDir) * System.Math.PI;
            }

            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            GroundDecorPlacementParams placement = GroundDecorPlacementParams.FromLayer(
                layer, terrain, body.Radius, chunk.CenterAstro);
            placement.WindAzimuthRad = windAzimuthRad;
            placement.PerInstanceDensity = layer.PerInstanceDensity;
            Vector3 cameraLocal = chunk.Go.transform.InverseTransformPoint(cameraPosition);

            // РћРїРµСЂРµР¶РµРЅРёРµ РѕР±Р»Р°РєР° РІРґРѕР»СЊ СЃРєРѕСЂРѕСЃС‚Рё РёРіСЂРѕРєР° РћРўРќРћРЎРРўР•Р›Р¬РќРћ РџРћР’Р•Р РҐРќРћРЎРўР:
            // РїСЂРё Р±РµРіРµ РїР»РѕС‚РЅР°СЏ Р·РѕРЅР° РіРѕС‚РѕРІРёС‚СЃСЏ Р’РџР•Р Р•Р”Р, Рё С‚СЂР°РІР° РІСЃС‚СЂРµС‡Р°РµС‚
            // РёРіСЂРѕРєР°, Р° РЅРµ РґРѕРіРѕРЅСЏРµС‚СЃСЏ РёРј. РЎРєРѕСЂРѕСЃС‚СЊ РІСЂР°С‰РµРЅРёСЏ РїР»Р°РЅРµС‚С‹ СЃСЋРґР° РЅРµ
            // РІС…РѕРґРёС‚ (СЃС‚РѕСЏ РёРіСЂРѕРє В«РЅРµ РґРІРёР¶РµС‚СЃСЏВ»), РїРѕСЌС‚РѕРјСѓ СЃС‚РѕСЏ РѕР±Р»Р°РєРѕ РЅРµ СѓРµР·Р¶Р°РµС‚.
            // surfaceVelocityLocal вЂ” СѓР¶Рµ РІ body-fixed РѕСЃСЏС…, СЃРѕРІРїР°РґР°СЋС‰РёС… СЃ
            // Р»РѕРєР°Р»СЊРЅС‹РјРё РѕСЃСЏРјРё С‡Р°РЅРєР°, РїРѕСЌС‚РѕРјСѓ РґРѕСЃС‚Р°С‚РѕС‡РЅРѕ СѓР±СЂР°С‚СЊ РЅРѕСЂРјР°Р»СЊРЅСѓСЋ
            // СЃРѕСЃС‚Р°РІР»СЏСЋС‰СѓСЋ (РїРѕРґСЉС‘Рј/СЃРїСѓСЃРє Р·РѕРЅСѓ РЅРµ СЃРґРІРёРіР°РµС‚).
            Vector3 cloudLocal = cameraLocal;
            if (layer.PerInstanceDensity)
            {
                float speed = surfaceVelocityLocal.magnitude;
                float leadCap = Mathf.Min(
                    GrassLeadMaxMeters,
                    (float)System.Math.Max(0d, layer.DensityCoreMeters) * GrassLeadCoreShare);
                if (speed > 0.5f && leadCap > 0.5f)
                {
                    Vector3 leadLocal = surfaceVelocityLocal;
                    Vector3 upLocal = cameraLocal.sqrMagnitude > 1e-6f
                        ? cameraLocal.normalized
                        : Vector3.up;
                    leadLocal -= upLocal * Vector3.Dot(leadLocal, upLocal);
                    if (leadLocal.sqrMagnitude > 1e-6f)
                    {
                        float lead = Mathf.Min(leadCap, speed * GrassLeadSeconds);
                        cloudLocal = cameraLocal + (leadLocal.normalized * lead);
                    }
                }
            }

            placement.UseCameraFalloff = true;
            placement.CameraLocalX = cloudLocal.x;
            placement.CameraLocalY = cloudLocal.y;
            placement.CameraLocalZ = cloudLocal.z;

            float chunkDistance = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
            double cellArc = chunkArc / System.Math.Max(1, cells);
            int subPerCell = 1;
            if ((layer.PerInstanceDensity || chunkDistance <= layer.NearDistanceMeters + 20f)
                && cellArc > spacing * 1.01d)
            {
                // РЎРµС‚РєР° РїРѕРґС‚СѓС„С‚ СЂРѕРІРЅРѕ РїРѕ С€Р°РіСѓ СЃР»РѕСЏ (gridNГ—gridN), Р° РЅРµ В«СЃРєРѕР»СЊРєРѕ
                // РІР»РµР·РµС‚В»: Р±РµР· СЌС‚РѕРіРѕ РѕСЃС‚Р°РІР°Р»РёСЃСЊ С‰РµР»Рё РјРµР¶РґСѓ РїРѕРґС‚СѓС„С‚Р°РјРё.
                int gridN = (int)System.Math.Ceiling(cellArc / spacing);
                long wantedSubs = (long)gridN * gridN;
                int maxSubs = System.Math.Max(1, layer.SubInstancesPerCell);
                subPerCell = wantedSubs >= maxSubs
                    ? maxSubs
                    : System.Math.Max(1, (int)wantedSubs);
            }

            var session = new DecorBuildSession
            {
                ChunkId = chunkId,
                Chunk = chunk,
                LayerIndex = layerIndex,
                Layer = layer,
                Node = chunk.Node,
                CameraPosition = cameraPosition,
                CameraLocal = cloudLocal,
                BuildCameraLocal = cameraLocal,
                NearPlayer = nearPlayer,
                Previous = previous,
                Placement = placement,
                MeshVertices = meshVertices,
                CoreN = coreN,
                SizeUv = sizeUv,
                U0 = u0,
                V0 = v0,
                StepUv = stepUv,
                ChunkArc = chunkArc,
                Spacing = spacing,
                Cells = cells,
                Count = count,
                MinSink = layer.MinGroundSinkFactor >= 0d ? layer.MinGroundSinkFactor : layer.GroundSinkFactor,
                MaxSink = layer.MaxGroundSinkFactor >= 0d ? layer.MaxGroundSinkFactor : layer.GroundSinkFactor,
                MeshCount = layer.NearMeshes.Length,
                SubPerCell = subPerCell,
                // Р’СЃРµ Р±СѓС„РµСЂС‹ вЂ” РёР· РїСѓР»Р° (DecorArrayPool): РїРµСЂРµСЃР±РѕСЂРєР° С‚СЂР°РІС‹ РЅРµ
                // РґРѕР»Р¶РЅР° Р°Р»Р»РѕС†РёСЂРѕРІР°С‚СЊ РґРµСЃСЏС‚РєРё РњР‘ РЅР°С‚РёРІРЅС‹С… РјР°СЃСЃРёРІРѕРІ Рё С‚СѓС‚ Р¶Рµ РёС…
                // РѕСЃРІРѕР±РѕР¶РґР°С‚СЊ, РёРЅР°С‡Рµ С„РёРЅР°Р»РёР·Р°С†РёСЏ РґР°С‘С‚ РїСЂРѕР»Р°Рі.
                Dirs = DecorArrayPool.Rent<double3>(count),
                Uvs = DecorArrayPool.Rent<float2>(count),
                Randoms = DecorArrayPool.Rent<double3>(count),
                MeshPicks = DecorArrayPool.Rent<double>(count),
                BuryRandoms = DecorArrayPool.Rent<double>(count),
                LeanRandoms = DecorArrayPool.Rent<double>(count),
                Accepted = DecorArrayPool.Rent<int>(count),
                Instances = DecorArrayPool.Rent<GroundDecorInstance>(count),
                MeshVerticesNative = meshVerticesNative,
                SubsFor = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<int>(count)
                    : default,
                WriteCounts = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<int>(count)
                    : default,
                Distances = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<float>(count)
                    : default,
                Selection = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<float>(3)
                    : default,
                AllocCellOrder = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<int>(count)
                    : default,
                WriteOffsets = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<int>(count)
                    : default,
                WriteTotal = layer.PerInstanceDensity
                    ? DecorArrayPool.Rent<float>(2)
                    : default
            };

            if (session.Selection.IsCreated)
            {
                // Р Р°РґРёСѓСЃ РІС‹Р±РѕСЂРєРё вЂ” РІСЃСЏ Р·РѕРЅР° РІРёРґРёРјРѕСЃС‚Рё СЃР»РѕСЏ; РєР°Рї СЂРµР¶РµС‚СЃСЏ
                // Р°Р»Р»РѕРєР°С‚РѕСЂРѕРј В«Р±Р»РёР¶РЅРёРµ РїРµСЂРІС‹РјРёВ» СЃ РїР»Р°РІРЅС‹Рј РєСЂР°РµРј, РїРѕСЌС‚РѕРјСѓ
                // РіСЂР°РЅРёС†Р° РѕР±СЂРµР·РєРё СЃР°РјР° СѓС…РѕРґРёС‚ Р·Р° РїСЂРµРґРµР»С‹ РїР»РѕС‚РЅРѕР№ Р·РѕРЅС‹.
                session.Selection[0] = layer.MaxDistanceMeters;
                session.Selection[1] = 1f;
                session.Selection[2] = 0f;
            }

            if (layer.PerInstanceDensity)
            {
                session.QueueTimeSeconds = Time.realtimeSinceStartup;
                session.QueueTangentialDistance = chunkDistance;
            }

            decorBuildSessions.Add(session);
            return true;
        }

        private bool HasDecorBuildSession(long chunkId, int layerIndex)
        {
            for (int i = 0; i < decorBuildSessions.Count; i++)
            {
                DecorBuildSession session = decorBuildSessions[i];
                if (session.ChunkId == chunkId && session.LayerIndex == layerIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>РЁР°Рі РІСЃРµС… Р°РєС‚РёРІРЅС‹С… СЃР±РѕСЂРѕРє РІ СЂР°РјРєР°С… Р±СЋРґР¶РµС‚Р° РєР°РґСЂР°. РЎРµСЃСЃРёРё
        /// РёРґСѓС‚ РїРѕ РєСЂСѓРіСѓ, РїРѕРєР° РµСЃС‚СЊ РїСЂРѕРіСЂРµСЃСЃ Рё РЅРµ РёСЃС‡РµСЂРїР°РЅ Р±СЋРґР¶РµС‚.</summary>
        private void StepDecorBuilds()
        {
            if (decorBuildSessions.Count == 0)
            {
                return;
            }

            var budget = new System.Diagnostics.Stopwatch();
            budget.Start();
            bool progressed = true;
            while (progressed)
            {
                progressed = false;
                for (int i = decorBuildSessions.Count - 1; i >= 0; i--)
                {
                    DecorBuildSession session = decorBuildSessions[i];
                    if (!SessionChunkAlive(session))
                    {
                        // Р§Р°РЅРє РІС‹С‚РµСЃРЅРµРЅ/СЃРјРµРЅРёР»СЃСЏ LOD вЂ” СЃР±РѕСЂРєСѓ РѕС‚РјРµРЅСЏРµРј.
                        session.CancelRequested = true;
                    }

                    if (session.CancelRequested)
                    {
                        if (CancelDecorBuild(session))
                        {
                            decorBuildSessions.RemoveAt(i);
                        }

                        continue;
                    }

                    // РСЃРєР»СЋС‡РµРЅРёРµ РІ РѕРґРЅРѕР№ СЃР±РѕСЂРєРµ РЅРµ РґРѕР»Р¶РЅРѕ СЂРѕРЅСЏС‚СЊ РІРµСЃСЊ LateUpdate
                    // (РёРЅР°С‡Рµ DrawDecor РЅРµ РІС‹РїРѕР»РЅСЏРµС‚СЃСЏ Рё РјРёСЂ РїСЂРѕРїР°РґР°РµС‚).
                    try
                    {
                        if (AdvanceDecorBuild(session))
                        {
                            progressed = true;
                        }
                    }
                    catch (System.Exception exception)
                    {
                        Debug.LogError("[PlanetSurfaceRenderer] СЃР±РѕСЂРєР° РґРµРєРѕСЂР° СѓРїР°Р»Р° (СЃР»РѕР№ "
                            + session.LayerIndex + ", С‡Р°РЅРє " + session.ChunkId + "): " + exception);
                        try
                        {
                            DisposeDecorBuildSession(session);
                        }
                        catch (System.Exception disposeException)
                        {
                            Debug.LogWarning("[PlanetSurfaceRenderer] РѕС‡РёСЃС‚РєР° СѓРїР°РІС€РµР№ СЃР±РѕСЂРєРё: " + disposeException.Message);
                        }

                        decorBuildSessions.RemoveAt(i);
                        continue;
                    }

                    if (session.Finished)
                    {
                        decorBuildSessions.RemoveAt(i);
                    }

                    if (budget.Elapsed.TotalMilliseconds >= DecorBuildBudgetMs)
                    {
                        return;
                    }
                }
            }
        }

        private bool SessionChunkAlive(DecorBuildSession session)
        {
            return session.Chunk != null && session.Chunk.Go != null
                && chunks.TryGetValue(session.ChunkId, out Chunk current) && current == session.Chunk;
        }

        /// <summary>РћС‚РјРµРЅР° СЃР±РѕСЂРєРё: РґР¶РѕР±Р° РјРѕРіР»Р° РµС‰С‘ СЃС‡РёС‚Р°С‚СЊСЃСЏ РЅР° РІРѕСЂРєРµСЂР°С… вЂ”
        /// С‚РѕРіРґР° РјР°СЃСЃРёРІС‹ РѕСЃРІРѕР±РѕР¶РґР°РµРј РІ СЃР»РµРґСѓСЋС‰РµРј С€Р°РіРµ, РёРЅР°С‡Рµ РїРѕСЂС‡Р° РїР°РјСЏС‚Рё.</summary>
        private bool CancelDecorBuild(DecorBuildSession session)
        {
            if (session.CandidateScheduled && !session.CandidateJob.IsCompleted)
            {
                return false;
            }

            if (session.CandidateScheduled)
            {
                session.CandidateJob.Complete();
                session.CandidateScheduled = false;
            }

            DisposeDecorBuildSession(session);
            return true;
        }

        private static void DisposeDecorBuildSession(DecorBuildSession session)
        {
            if (session.CandidateScheduled)
            {
                session.CandidateJob.Complete();
                session.CandidateScheduled = false;
            }

            DisposeIfCreated(ref session.Dirs);
            DisposeIfCreated(ref session.Uvs);
            DisposeIfCreated(ref session.Randoms);
            DisposeIfCreated(ref session.MeshPicks);
            DisposeIfCreated(ref session.BuryRandoms);
            DisposeIfCreated(ref session.LeanRandoms);
            DisposeIfCreated(ref session.Accepted);
            DisposeIfCreated(ref session.Instances);
            DisposeIfCreated(ref session.Stored);
            DisposeIfCreated(ref session.MeshVerticesNative);
            DisposeIfCreated(ref session.SubsFor);
            DisposeIfCreated(ref session.WriteCounts);
            DisposeIfCreated(ref session.Distances);
            DisposeIfCreated(ref session.Selection);
            DisposeIfCreated(ref session.AllocCellOrder);
            DisposeIfCreated(ref session.KeptOld);
            DisposeIfCreated(ref session.WriteOffsets);
            DisposeIfCreated(ref session.WriteTotal);
        }

        /// <summary>Р’РµСЂРЅСѓС‚СЊ РјР°СЃСЃРёРІ РІ РїСѓР» Рё РѕР±РЅСѓР»РёС‚СЊ СЃСЃС‹Р»РєСѓ Сѓ Р’Р›РђР”Р•Р›Р¬Р¦Рђ (ref):
        /// РёРЅР°С‡Рµ РєРѕРїРёСЏ-РїР°СЂР°РјРµС‚СЂ РѕР±РЅСѓР»СЏРµС‚СЃСЏ, РїРѕР»Рµ РѕСЃС‚Р°С‘С‚СЃСЏ В«СЃРѕР·РґР°РЅРЅС‹РјВ», Рё
        /// РїРѕРІС‚РѕСЂРЅС‹Р№ РІРѕР·РІСЂР°С‚ РѕС‚РґР°С‘С‚ РІ РїСѓР» РѕРґРёРЅ Р±СѓС„РµСЂ РґРІР°Р¶РґС‹.</summary>
        private static void DisposeIfCreated<T>(ref NativeArray<T> array) where T : struct
        {
            DecorArrayPool.Return(ref array);
        }

        private static void DisposeDecorLayerRuntime(DecorLayerRuntime runtime)
        {
            if (runtime == null)
            {
                return;
            }

            DecorArrayPool.Return(ref runtime.Instances);
            DecorArrayPool.Return(ref runtime.WorldMatrices);
            DecorArrayPool.Return(ref runtime.CellOffsets);
            DecorArrayPool.Return(ref runtime.CellCounts);

            if (runtime.MatrixBuffer != null)
            {
                runtime.MatrixBuffer.Release();
                runtime.MatrixBuffer = null;
            }

            if (runtime.ArgsBuffer != null)
            {
                runtime.ArgsBuffer.Release();
                runtime.ArgsBuffer = null;
            }
        }

        /// <summary>РЎС‡РёС‚Р°РµС‚ Р»РѕРєР°Р»СЊРЅС‹Рµ (С‡Р°РЅРє-С„СЂРµР№Рј) РјР°С‚СЂРёС†С‹ С‚СЂР°РІС‹ РћР”РРќ СЂР°Р· Рё
        /// РїР°РєСѓРµС‚ РёС… РІ ComputeBuffer + Р°СЂРіСѓРјРµРЅС‚С‹ РёРЅРґРёСЂРµРєС‚-РґСЂРѕСѓ. РњРёСЂРѕРІР°СЏ С‡Р°СЃС‚СЊ
        /// РїРѕРґСЃС‚Р°РІР»СЏРµС‚СЃСЏ РЅР° РѕС‚СЂРёСЃРѕРІРєРµ С‡РµСЂРµР· _DecorChunkToWorld (MPB), РїРѕСЌС‚РѕРјСѓ
        /// per-frame РґР¶РѕР±С‹ РјР°С‚СЂРёС† Р±РѕР»СЊС€Рµ РЅРµ РЅСѓР¶РЅС‹.</summary>
        private bool BuildDecorIndirectBuffers(
            GroundDecorLayer layer, NativeArray<GroundDecorInstance> instances, DecorLayerRuntime runtime)
        {
            int count = instances.Length;
            Mesh mesh = layer.NearMeshes != null && layer.NearMeshes.Length > 0 ? layer.NearMeshes[0] : null;
            if (mesh == null || count <= 0)
            {
                return false;
            }

            try
            {
                var local = DecorArrayPool.Rent<Matrix4x4>(count);
                var job = new GroundDecorMatrixJob
                {
                    Instances = instances,
                    WorldMatrices = local,
                    ChunkToWorld = ToFloat4x4(Matrix4x4.identity),
                    CameraWorld = float3.zero,
                    Billboard = false,
                    FlatOnGround = layer.FlatOnGround,
                    NearDistance = layer.NearDistanceMeters,
                    FarDistance = Mathf.Max(layer.NearDistanceMeters + 1f, layer.MaxDistanceMeters),
                    BillboardNearScale = 1f,
                    BillboardFarScale = 1f
                };
                job.Schedule(count, 256, default(JobHandle)).Complete();

                runtime.MatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count, 64);
                runtime.MatrixBuffer.SetData(local);
                DecorArrayPool.Return(ref local);

                int subMeshes = Mathf.Max(1, mesh.subMeshCount);
                var args = new GraphicsBuffer.IndirectDrawIndexedArgs[subMeshes];
                for (int sub = 0; sub < subMeshes; sub++)
                {
                    args[sub] = new GraphicsBuffer.IndirectDrawIndexedArgs
                    {
                        indexCountPerInstance = mesh.GetIndexCount(sub),
                        instanceCount = (uint)count,
                        startIndex = mesh.GetIndexStart(sub),
                        baseVertexIndex = (uint)mesh.GetBaseVertex(sub),
                        startInstance = 0
                    };
                }

                runtime.ArgsBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.IndirectArguments, subMeshes, GraphicsBuffer.IndirectDrawIndexedArgs.size);
                runtime.ArgsBuffer.SetData(args);
                runtime.DrawMesh = mesh;
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[PlanetSurfaceRenderer] Р±СѓС„РµСЂС‹ РёРЅРґРёСЂРµРєС‚-С‚СЂР°РІС‹ РЅРµ СЃРѕР±СЂР°Р»РёСЃСЊ: " + exception.Message);
                if (runtime.MatrixBuffer != null)
                {
                    runtime.MatrixBuffer.Release();
                    runtime.MatrixBuffer = null;
                }

                if (runtime.ArgsBuffer != null)
                {
                    runtime.ArgsBuffer.Release();
                    runtime.ArgsBuffer = null;
                }

                runtime.DrawMesh = null;
                return false;
            }
        }

        /// <summary>РћРґРёРЅ РёРЅРґРёСЂРµРєС‚-РґСЂРѕСѓ РЅР° СЃР°Р±РјРµС€: РјР°С‚СЂРёС†С‹ вЂ” РёР· Р±СѓС„РµСЂР°, РјРёСЂРѕРІР°СЏ
        /// С‡Р°СЃС‚СЊ вЂ” _DecorChunkToWorld.</summary>
        private void DrawDecorIndirect(
            DecorLayerRuntime runtime, Matrix4x4 chunkMatrix, Bounds worldBounds, ShadowCastingMode shadowMode)
        {
            Mesh mesh = runtime.DrawMesh;
            Material material = runtime.Profile != null ? runtime.Profile.NearMaterial : null;
            if (mesh == null || material == null || runtime.MatrixBuffer == null || runtime.ArgsBuffer == null)
            {
                return;
            }

            if (decorDrawPropertyBlock == null)
            {
                decorDrawPropertyBlock = new MaterialPropertyBlock();
            }

            decorDrawPropertyBlock.SetMatrix(DecorChunkToWorldId, chunkMatrix);
            decorDrawPropertyBlock.SetBuffer(DecorMatricesId, runtime.MatrixBuffer);

            if (!decorIndirectDiagLogged)
            {
                decorIndirectDiagLogged = true;
                Debug.Log(string.Format(
                    "[DecorIndirect] mesh={0} subMeshes={1} instances={2} matBuf={3} argsBuf={4} " +
                    "kwIndirect={5} instancing={6} bounds={7}",
                    mesh.name, mesh.subMeshCount, runtime.Instances.Length,
                    runtime.MatrixBuffer.count, runtime.ArgsBuffer.count,
                    material.IsKeywordEnabled("_DECOR_INDIRECT_MATRICES"),
                    material.enableInstancing, worldBounds.size));
            }

            var renderParams = new RenderParams(material)
            {
                worldBounds = worldBounds,
                matProps = decorDrawPropertyBlock,
                shadowCastingMode = shadowMode,
                receiveShadows = false,
                layer = gameObject.layer
            };

            int subMeshes = Mathf.Max(1, mesh.subMeshCount);
            for (int sub = 0; sub < subMeshes; sub++)
            {
                // SRP-РїСѓС‚СЊ: РѕРґРёРЅ РёРЅРґРёСЂРµРєС‚-РґСЂРѕСѓ РЅР° СЃР°Р±РјРµС€ (РІ РѕС‚Р»РёС‡РёРµ РѕС‚ legacy
                // DrawMeshInstancedIndirect, РєРѕС‚РѕСЂС‹Р№ HDRP РёРіРЅРѕСЂРёСЂСѓРµС‚).
                Graphics.RenderMeshIndirect(renderParams, mesh, runtime.ArgsBuffer, 1, sub);
            }
        }

        private bool AdvanceDecorBuild(DecorBuildSession session)
        {
            switch (session.Stage)
            {
                case 0:
                    return AdvanceDecorPrep(session);

                case 1:
                    if (!session.CandidateScheduled || !session.CandidateJob.IsCompleted)
                    {
                        return false;
                    }

                    session.CandidateJob.Complete();
                    session.CandidateScheduled = false;
                    if (session.Layer.PerInstanceDensity && session.MeshVerticesNative.IsCreated)
                    {
                        // РўСЂР°РІР°: РІРµСЃР°/РїРѕРґС‚СѓС„С‚С‹ СЃС‡РёС‚Р°РµС‚ Burst, Р° РЅРµ РіР»Р°РІРЅС‹Р№ РїРѕС‚РѕРє.
                        ScheduleDecorWeightChain(session);
                        session.Stage = 5;
                        return true;
                    }

                    session.Stage = 2;
                    session.Cursor = 0;
                    return true;

                case 2:
                    return AdvanceDecorScan(session);

                case 3:
                    return AdvanceDecorExpand(session);

                case 5:
                    if (!session.CandidateScheduled || !session.CandidateJob.IsCompleted)
                    {
                        return false;
                    }

                    session.CandidateJob.Complete();
                    session.CandidateScheduled = false;
                    // Р§РёСЃР»Рѕ С‚СЂР°РІРёРЅРѕРє С‚РµРїРµСЂСЊ Р·Р°РґР°С‘С‚ РџР РћР¤РР›Р¬ РїР»РѕС‚РЅРѕСЃС‚Рё (РїР»РѕСЃРєРѕРµ
                    // СЏРґСЂРѕ + Р·Р°С‚СѓС…Р°РЅРёРµ), Р° РєР°Рї СЂРµР¶РµС‚СЃСЏ Р°Р»Р»РѕРєР°С‚РѕСЂРѕРј РїРѕ РґРёСЃС‚Р°РЅС†РёРё.
                    session.ExpectedWeight = 0d;
                    session.AcceptedCount = session.Count;
                    FinishDecorScan(session);
                    session.ExpandTake = System.Math.Max(1, session.Take);
                    if (session.Take > 0 && session.Stored.IsCreated)
                    {
                        ScheduleDecorOffsetAndExpand(session);
                        session.Stage = 6;
                    }
                    else
                    {
                        session.Write = 0;
                        session.Stage = 4;
                    }

                    return true;

                case 6:
                    if (!session.CandidateScheduled || !session.CandidateJob.IsCompleted)
                    {
                        return false;
                    }

                    session.CandidateJob.Complete();
                    session.CandidateScheduled = false;
                    session.Write = System.Math.Min(
                        (int)session.WriteTotal[0], session.ExpandTake);
                    if (LogDecorAllocation && session.Layer.PerInstanceDensity && session.NearPlayer
                        && Time.realtimeSinceStartup >= decorAllocLogTime)
                    {
                        decorAllocLogTime = Time.realtimeSinceStartup + 1f;
                        Camera logCamera = Camera.main;
                        Vector3 finalizeCameraLocal = logCamera != null && session.Chunk.Go != null
                            ? session.Chunk.Go.transform.InverseTransformPoint(logCamera.transform.position)
                            : session.BuildCameraLocal;
                        Debug.Log("[DecorAlloc] С‚СЂР°РІР° chunk=" + session.ChunkId
                            + " Р·Р°РїРёСЃР°РЅРѕ=" + session.Write + "/" + session.ExpandTake
                            + " СЂР°РґРёСѓСЃРћР±СЂРµР·РєРё=" + session.WriteTotal[1].ToString("F0") + " Рј"
                            + " СЃРЅРѕСЃР¦РµРЅС‚СЂР°=" + (finalizeCameraLocal - session.CameraLocal).magnitude.ToString("F0") + " Рј"
                            + " РѕС‚СЃС‚Р°РІР°РЅРёРµРљР°РјРµСЂС‹=" + (finalizeCameraLocal - session.BuildCameraLocal).magnitude.ToString("F0") + " Рј"
                            + " СЃРєРѕСЂРѕСЃС‚СЊ=" + surfaceVelocityLocal.magnitude.ToString("F0") + " Рј/СЃ"
                            + " РєР»РµС‚РѕРє=" + session.Count
                            + " РґРёСЃС‚РџРѕСЃС‚Р°РЅРѕРІРєРё=" + session.QueueTangentialDistance.ToString("F0") + " Рј"
                            + " Р·Р°РґРµСЂР¶РєР°РЎР±РѕСЂРєРё=" + (Time.realtimeSinceStartup - session.QueueTimeSeconds).ToString("F2") + " СЃ");
                    }

                    // РЎРѕС…СЂР°РЅС‘РЅРЅС‹Рµ СЃС‚Р°СЂС‹Рµ С‚СЂР°РІРёРЅРєРё (РІРґР°Р»Рё РѕС‚ РЅРѕРІРѕРіРѕ СЏРґСЂР°) вЂ” РІ
                    // С…РІРѕСЃС‚ РїСѓР»Р°: РїР»РѕС‚РЅРѕСЃС‚СЊ Сѓ РёРіСЂРѕРєР° РЅРµ СЃС‚СЂР°РґР°РµС‚, Р° РјРёСЂ РЅРµ
                    // В«РїРѕСЏРІР»СЏРµС‚СЃСЏ Р·Р°РЅРѕРІРѕВ» РЅР° СѓР¶Рµ РїСЂРѕР№РґРµРЅРЅС‹С… РјРµСЃС‚Р°С….
                    if (session.KeptOldCount > 0 && session.Stored.IsCreated)
                    {
                        int copy = System.Math.Min(session.KeptOldCount, session.Stored.Length - session.Write);
                        if (copy > 0)
                        {
                            NativeArray<GroundDecorInstance>.Copy(
                                session.KeptOld, 0, session.Stored, session.Write, copy);
                            session.Write += copy;
                        }
                    }

                    session.Stage = 4;
                    return true;

                default:
                    FinalizeDecorBuild(session);
                    session.Finished = true;
                    return true;
            }
        }

        /// <summary>РЎС‚Р°РґРёСЏ 5: Burst-С†РµРїРѕС‡РєР° В«РІРµСЃ СЃ Р·Р°С‚СѓС…Р°РЅРёРµРј в†’ СЃС‡С‘С‚С‡РёРєРё С‚СЂР°РІРёРЅРѕРєВ».</summary>
        private void ScheduleDecorWeightChain(DecorBuildSession session)
        {
            var weightJob = new GroundDecorWeightJob
            {
                Accepted = session.Accepted,
                Instances = session.Instances,
                Uvs = session.Uvs,
                MeshVertices = session.MeshVerticesNative,
                CoreN = session.CoreN,
                U0 = session.U0,
                V0 = session.V0,
                StepUv = session.StepUv,
                CameraLocal = new float3(session.CameraLocal.x, session.CameraLocal.y, session.CameraLocal.z),
                Placement = session.Placement,
                SubPerCell = session.SubPerCell,
                Face = session.Node.Face,
                Ix = session.Node.Ix,
                Iy = session.Node.Iy,
                PoolRadius = session.Selection.IsCreated
                    ? session.Selection[0]
                    : session.Layer.MaxDistanceMeters,
                RadiusFadeMeters = DecorPoolRadiusFadeMeters,
                Counts = session.SubsFor,
                Distances = session.Distances
            };

            session.CandidateJob = weightJob.Schedule(session.Count, 64, default(JobHandle));
            session.CandidateScheduled = true;
        }

        /// <summary>РЎС‚Р°РґРёСЏ 6: СЃР»РѕС‚С‹ Р·Р°РїРёСЃРё (Р±Р»РёР¶РЅРёРµ РїРµСЂРІС‹РјРё, СЃ РїР»Р°РІРЅС‹Рј РєСЂР°РµРј)
        /// + Burst-СЂР°Р·РІРѕСЂРѕС‚ СЂРѕРІРЅРѕ Р·Р°РїР»Р°РЅРёСЂРѕРІР°РЅРЅРѕРіРѕ С‡РёСЃР»Р° С‚СЂР°РІРёРЅРѕРє.</summary>
        private void ScheduleDecorOffsetAndExpand(DecorBuildSession session)
        {
            // Р‘Р°РєРµС‚С‹ РїРѕ РґРёСЃС‚Р°РЅС†РёРё: С€РёСЂРёРЅР° ~2 Рј (СЂР°Р·СЂРµС€РµРЅРёРµ СЂР°РґРёСѓСЃР° РѕР±СЂРµР·РєРё),
            // С‡С‚РѕР±С‹ РіСЂР°РЅРёС†Р° РєР°РїР° Р±С‹Р»Р° РєРѕР»СЊС†РѕРј РІРѕРєСЂСѓРі РёРіСЂРѕРєР°, Р° РЅРµ СЃС‚СЂРѕРєРѕР№ СЃРµС‚РєРё.
            float maxDistance = Mathf.Max(1f, session.Layer.MaxDistanceMeters);
            int buckets = Mathf.Clamp(Mathf.CeilToInt(maxDistance / 2f), 16, 512);
            var allocJob = new GroundDecorAllocateJob
            {
                Counts = session.SubsFor,
                Distances = session.Distances,
                Count = session.Count,
                Take = session.ExpandTake,
                BucketWidth = maxDistance / buckets,
                BucketCount = buckets,
                FadeShare = DecorCutFadeShare,
                Salt = session.LayerIndex + 1,
                CellOrder = session.AllocCellOrder,
                Offsets = session.WriteOffsets,
                WriteCounts = session.WriteCounts,
                WrittenTotal = session.WriteTotal
            };

            var expandJob = BuildDecorExpandJob(session);
            JobHandle allocHandle = allocJob.Schedule(default(JobHandle));
            JobHandle expandHandle = expandJob.Schedule(session.Count, 64, allocHandle);

            // Инкрементальность: переносим из прежнего пула травинки, оставшиеся
            // в новой выборке, вместе с их BirthTime — уже выросшие не прорастают
            // заново. Новыми (со свежим BirthTime) остаются только добавленные.
            DecorLayerRuntime previous = session.Previous;
            if (previous != null && previous.Instances.IsCreated
                && previous.CellOffsets.IsCreated && previous.CellCounts.IsCreated)
            {
                var mergeJob = new GroundDecorMergeKeptJob
                {
                    PrevInstances = previous.Instances,
                    PrevOffsets = previous.CellOffsets,
                    PrevCounts = previous.CellCounts,
                    NewOffsets = session.WriteOffsets,
                    NewCounts = session.WriteCounts,
                    CellCount = session.Count,
                    Take = session.ExpandTake,
                    Stored = session.Stored
                };

                expandHandle = mergeJob.Schedule(expandHandle);
            }

            session.CandidateJob = expandHandle;
            session.CandidateScheduled = true;
        }

        private GroundDecorExpandJob BuildDecorExpandJob(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;
            return new GroundDecorExpandJob
            {
                Accepted = session.Accepted,
                Instances = session.Instances,
                Uvs = session.Uvs,
                MeshVertices = session.MeshVerticesNative,
                WriteCounts = session.WriteCounts,
                CoreN = session.CoreN,
                Face = session.Node.Face,
                Ix = session.Node.Ix,
                Iy = session.Node.Iy,
                Cells = session.Cells,
                SizeUv = session.SizeUv,
                U0 = session.U0,
                V0 = session.V0,
                StepUv = session.StepUv,
                CameraLocal = new float3(session.CameraLocal.x, session.CameraLocal.y, session.CameraLocal.z),
                Terrain = noiseParams,
                Placement = session.Placement,
                SubPerCell = session.SubPerCell,
                // 13 РІР·Р°РёРјРЅРѕ РїСЂРѕСЃС‚Рѕ СЃ 36: СЂР°Р·СЂРµР¶РµРЅРЅС‹Р№ РїРѕСЂСЏРґРѕРє РїРѕРєСЂС‹РІР°РµС‚ СЃРµС‚РєСѓ 6Г—6
                // С†РµР»РёРєРѕРј, РїРµСЂРІС‹Рµ k РїРѕРґС‚СѓС„С‚РѕРІ СЂР°СЃСЃС‹РїР°РЅС‹ РїРѕ РІСЃРµР№ РєР»РµС‚РєРµ.
                SpreadStride = 13,
                MinScale = layer.MinScale,
                MaxScale = layer.MaxScale,
                WindJitterDegrees = layer.WindJitterDegrees,
                WindLeanMinDegrees = layer.WindLeanMinDegrees,
                WindLeanMaxDegrees = layer.WindLeanMaxDegrees,
                MinSink = session.MinSink,
                MaxSink = session.MaxSink,
                GroundOffsetMeters = layer.GroundOffsetMeters,
                MeshCount = session.MeshCount,
                Take = session.ExpandTake,
                BuildTime = Time.time,
                StaggerSeconds = DecorBladeStaggerSeconds,
                Stored = session.Stored,
                WriteOffsets = session.WriteOffsets,
                Selection = session.Selection
            };
        }

        /// <summary>РЎС‚Р°РґРёСЏ 0: РґР¶РёС‚С‚РµСЂ-СЃРµС‚РєР° РєР°РЅРґРёРґР°С‚РѕРІ РІ UV С‡Р°РЅРєР° вЂ” РєСѓСЂСЃРѕСЂРѕРј.</summary>
        private bool AdvanceDecorPrep(DecorBuildSession session)
        {
            Node node = session.Node;
            int cells = session.Cells;
            int end = System.Math.Min(session.Count, session.Cursor + DecorPrepItemsPerStep);
            for (int index = session.Cursor; index < end; index++)
            {
                int a = index / cells;
                int b = index % cells;
                // Р”Р¶РёС‚С‚РµСЂ РїРѕ РЇР§Р•Р™РљР• (u Рё v Р·Р°РІРёСЃСЏС‚ Рё РѕС‚ a, Рё РѕС‚ b):
                // РїРѕСЃС‚СЂРѕС‡РЅС‹Р№ РґР¶РёС‚С‚РµСЂ РґР°РІР°Р» СЃС‚СЂРѕРіРѕ РєРѕР»Р»РёРЅРµР°СЂРЅС‹Рµ СЂСЏРґС‹ вЂ”
                // РґРµСЂРµРІСЊСЏ РІС‹СЃС‚СЂР°РёРІР°Р»РёСЃСЊ РІ Р»РёРЅРёРё В«СЃР°Р¶РµРЅРѕРіРѕВ» Р»РµСЃР°.
                double uJitter = GroundDecorDistribution.Hash01(
                    node.Face, node.Depth, node.Ix, (a * 97) + node.Iy + (b * 131), 11);
                double vJitter = GroundDecorDistribution.Hash01(
                    node.Face, node.Depth, node.Iy, (b * 89) + node.Ix + (a * 137), 12);
                double u = session.U0 + (((a + uJitter) / cells) * session.SizeUv);
                double v = session.V0 + (((b + vJitter) / cells) * session.SizeUv);
                Vector3d direction = CubeSphere.Direction(node.Face, u, v);
                session.Dirs[index] = new double3(direction.X, direction.Y, direction.Z);
                session.Uvs[index] = new float2((float)u, (float)v);
                session.Randoms[index] = new double3(
                    GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix + node.Iy, (index * 31) + session.LayerIndex, 21),
                    GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Iy, (index * 37) + session.LayerIndex, 22),
                    GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 41) + session.LayerIndex, 23));
                session.MeshPicks[index] = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 43) + session.LayerIndex, 24);
                session.BuryRandoms[index] = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 47) + session.LayerIndex, 25);
                session.LeanRandoms[index] = GroundDecorDistribution.Hash01(node.Face, node.Depth, node.Ix, (index * 53) + session.LayerIndex, 26);
            }

            session.Cursor = end;
            if (session.Cursor >= session.Count)
            {
                ScheduleDecorCandidateJob(session);
                session.Stage = 1;
            }

            return true;
        }

        private void ScheduleDecorCandidateJob(DecorBuildSession session)
        {
            var job = new GroundDecorCandidateJob
            {
                Directions = session.Dirs,
                Randoms = session.Randoms,
                MeshPicks = session.MeshPicks,
                BuryRandoms = session.BuryRandoms,
                LeanRandoms = session.LeanRandoms,
                Terrain = noiseParams,
                Placement = session.Placement,
                Accepted = session.Accepted,
                Instances = session.Instances
            };
            session.CandidateJob = job.Schedule(session.Count, 64, default(JobHandle));
            session.CandidateScheduled = true;
        }

        /// <summary>РЎС‚Р°РґРёСЏ 2: РїРѕРґСЃС‡С‘С‚ РїСЂРёРЅСЏС‚С‹С… Рё СЃСѓРјРјР°СЂРЅРѕРіРѕ РІРµСЃР° вЂ” РєСѓСЂСЃРѕСЂРѕРј.</summary>
        private bool AdvanceDecorScan(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;
            int end = System.Math.Min(session.Count, session.Cursor + DecorScanItemsPerStep);
            for (int i = session.Cursor; i < end; i++)
            {
                if (session.Accepted[i] == 0)
                {
                    continue;
                }

                session.AcceptedCount++;
                double weight = session.Instances[i].DensityWeight;
                int subsForCell = session.SubPerCell;
                if (layer.PerInstanceDensity)
                {
                    Vector3 normal = new Vector3(
                        session.Instances[i].Normal.x, session.Instances[i].Normal.y, session.Instances[i].Normal.z);
                    if (normal.sqrMagnitude > 1e-6f)
                    {
                        normal.Normalize();
                        Vector2 uv = new Vector2(session.Uvs[i].x, session.Uvs[i].y);
                        Vector3 meshPoint = MeshSurfacePoint(
                            session.MeshVertices, session.CoreN,
                            (uv.x - (float)session.U0) / (float)session.StepUv,
                            (uv.y - (float)session.V0) / (float)session.StepUv);
                        Vector3 toCamera = meshPoint - session.CameraLocal;
                        float alongNormal = Vector3.Dot(toCamera, normal);
                        Vector3 tangential = toCamera - (normal * alongNormal);
                        double falloff = GroundDecorDistribution.Falloff(session.Placement, tangential.magnitude);
                        weight *= falloff;
                    }
                }

                session.WeightSum += weight;
                session.ExpectedWeight += weight * subsForCell;
            }

            session.Cursor = end;
            if (session.Cursor < session.Count)
            {
                return true;
            }

            FinishDecorScan(session);
            session.Stage = 3;
            session.Write = 0;
            session.K = 0;
            session.SubIndex = 0;
            return true;
        }

        /// <summary>РС‚РѕРі СЃС‚Р°РґРёРё 2: РїРѕРґС‚СѓС„С‚С‹, РєР°Рї Рё СЂР°Р·РІРѕСЂРѕС‚ вЂ” РєР°Рє РІ РїСЂРµР¶РЅРµР№
        /// СЃРёРЅС…СЂРѕРЅРЅРѕР№ СЃР±РѕСЂРєРµ, РїРѕСЂСЏРґРѕРє Рё С…РµС€Рё РЅРµ РјРµРЅСЏСЋС‚СЃСЏ.</summary>
        private void FinishDecorScan(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;

            if (layer.PerInstanceDensity && session.ExpectedWeight > 0d)
            {
                double expected = session.ExpectedWeight * layer.Density;
                if (expected > layer.MaxInstancesPerChunk)
                {
                    // РћСЃС‚Р°РІР»СЏРµРј Р·Р°РїР°СЃ РЅР° РґРёСЃРїРµСЂСЃРёСЋ СЃР»СѓС‡Р°Р№РЅРѕРіРѕ РѕС‚Р±РѕСЂР°, С‡С‚РѕР±С‹
                    // РЅРµ СЃСЂР°Р±Р°С‚С‹РІР°С‚СЊ РЅР° Р¶С‘СЃС‚РєРѕРј С…РІРѕСЃС‚РѕРІРѕРј Р»РёРјРёС‚Рµ Рё РЅРµ РїРѕР»СѓС‡Р°С‚СЊ
                    // РїРѕРІС‚РѕСЂСЏСЋС‰РёРµСЃСЏ РїРѕР»РѕСЃС‹ РѕС‚ РїРѕСЂСЏРґРєР° РѕР±С…РѕРґР° РєР»РµС‚РѕРє.
                    session.CapScale = (layer.MaxInstancesPerChunk * 0.9d) / expected;
                }
            }

            int capacity = session.AcceptedCount * session.SubPerCell;
            if (layer.PerInstanceDensity)
            {
                // РџСѓР» С‚СЂР°РІС‹ вЂ” В«N Р±Р»РёР¶Р°Р№С€РёС… РїРѕ РїСЂРѕС„РёР»СЋ РїР»РѕС‚РЅРѕСЃС‚РёВ»: РєР°Рї вЂ” РїР°СЃРїРѕСЂС‚РЅС‹Р№
                // Р»РёРјРёС‚ СЃР»РѕСЏ, Р° РіСЂР°РЅРёС†Сѓ СЂРµР¶РµС‚ Р°Р»Р»РѕРєР°С‚РѕСЂ РїРѕ РґРёСЃС‚Р°РЅС†РёРё (Р±Р»РёР¶РЅРёРµ
                // РїРµСЂРІС‹РјРё) СЃ РїР»Р°РІРЅС‹Рј Р·Р°С‚СѓС…Р°РЅРёРµРј Сѓ РєСЂР°СЏ. Р—Р°РїР°СЃ +2048/+4096 Р±С‹Р»
                // РЅСѓР¶РµРЅ В«СЃР»РµРїРѕРјСѓВ» СЂРµР·РµСЂРІРёСЂРѕРІР°РЅРёСЋ СЃР»РѕС‚РѕРІ вЂ” С‚РµРїРµСЂСЊ СЃС‡С‘С‚С‡РёРєРё С‚РѕС‡РЅС‹Рµ.
                capacity = System.Math.Max(0, layer.MaxInstancesPerChunk);
            }
            else
            {
                capacity = System.Math.Min(capacity, layer.MaxInstancesPerChunk);
            }

            session.Take = capacity;

            // Р›РёРјРёС‚ вЂ” РЅРµ В«РѕР±СЂРµР·Р°С‚СЊ С…РІРѕСЃС‚В» С‡Р°РЅРєР° (СЌС‚Рѕ РґР°РІР°Р»Рѕ РїРѕР»РѕСЃСѓ РґРµРєРѕСЂР°
            // СЃ РѕРґРЅРѕРіРѕ РєСЂР°СЏ СЃРµС‚РєРё), Р° СЂР°РІРЅРѕРјРµСЂРЅРѕ РїСЂРѕСЂРµРґРёС‚СЊ: РѕР±С…РѕРґ РїРѕ
            // РїРµСЂРµСЃС‚Р°РЅРѕРІРєРµ i = (kВ·stride) mod count СЃРѕ stride Р·РѕР»РѕС‚РѕРіРѕ
            // СЃРµС‡РµРЅРёСЏ, РІР·Р°РёРјРЅРѕ РїСЂРѕСЃС‚С‹Рј СЃ count. Р›СЋР±РѕР№ РїСЂРµС„РёРєСЃ СЂР°РІРЅРѕРјРµСЂРµРЅ.
            if (!layer.PerInstanceDensity && session.AcceptedCount * session.SubPerCell > session.Take
                && session.Count > 1)
            {
                int stride = (int)(session.Count * 0.6180339887498949d);
                if (stride < 1)
                {
                    stride = 1;
                }

                if (stride >= session.Count)
                {
                    stride = session.Count - 1;
                }

                while (Gcd(stride, session.Count) != 1)
                {
                    stride++;
                    if (stride >= session.Count)
                    {
                        stride = 1;
                        break;
                    }
                }

                session.Stride = stride;
            }

            if (session.Take > 0)
            {
                session.Stored = DecorArrayPool.Rent<GroundDecorInstance>(session.Take);
            }

            // РљР°РЅРґРёРґР°С‚РЅС‹Рµ РјР°СЃСЃРёРІС‹ Р±РѕР»СЊС€Рµ РЅРµ РЅСѓР¶РЅС‹ вЂ” РѕСЃРІРѕР±РѕР¶РґР°РµРј СЃСЂР°Р·Сѓ.
            DisposeIfCreated(ref session.Dirs);
            DisposeIfCreated(ref session.Randoms);
            DisposeIfCreated(ref session.MeshPicks);
            DisposeIfCreated(ref session.BuryRandoms);
            DisposeIfCreated(ref session.LeanRandoms);
        }

        /// <summary>РЎРѕС…СЂР°РЅСЏРµС‚ РёР· РїСЂРµР¶РЅРµРіРѕ РїСѓР»Р° С‚СЂР°РІРёРЅРєРё Р’Р”РђР›Р РѕС‚ РЅРѕРІРѕРіРѕ СЏРґСЂР°
        /// (РЅР°РєРѕРїР»РµРЅРёРµ): РѕРЅРё РѕСЃС‚Р°СЋС‚СЃСЏ РЅР° РјРµСЃС‚Рµ, РЅРѕРІР°СЏ СЃР±РѕСЂРєР° РґРѕР±Р°РІР»СЏРµС‚ РїР»РѕС‚РЅРѕРµ
        /// СЏРґСЂРѕ Сѓ РёРіСЂРѕРєР° РІ РѕСЃС‚Р°С‚РѕРє Р±СЋРґР¶РµС‚Р°.</summary>
        private static void CollectKeptOldInstances(DecorBuildSession session)
        {
            session.KeptOldCount = 0;
            GroundDecorLayer layer = session.Layer;
            if (!layer.PerInstanceDensity || session.Previous == null
                || !session.Previous.Instances.IsCreated || session.Take <= 0)
            {
                session.ExpandTake = System.Math.Max(1, session.Take);
                return;
            }

            const float KeptBudgetShare = 0.35f;
            int keptBudget = (int)(session.Take * KeptBudgetShare);
            NativeArray<GroundDecorInstance> previous = session.Previous.Instances;
            int maxKeep = System.Math.Min(keptBudget, previous.Length);
            if (maxKeep > 0)
            {
                session.KeptOld = DecorArrayPool.Rent<GroundDecorInstance>(maxKeep);
                float keepRadius = layer.DensityFalloffMeters * 0.7f;
                int kept = 0;
                for (int i = 0; i < previous.Length && kept < maxKeep; i++)
                {
                    if (InstanceFalloffDistance(previous[i], session.CameraLocal) > keepRadius)
                    {
                        session.KeptOld[kept++] = previous[i];
                    }
                }

                session.KeptOldCount = kept;
            }

            session.ExpandTake = System.Math.Max(1, session.Take - session.KeptOldCount);
        }

        /// <summary>РўР°РЅРіРµРЅС†РёР°Р»СЊРЅР°СЏ РґРёСЃС‚Р°РЅС†РёСЏ РёРЅСЃС‚Р°РЅСЃР° РґРѕ С†РµРЅС‚СЂР° РѕР±Р»Р°РєР° (С‚Р° Р¶Рµ
        /// РјРµС‚СЂРёРєР°, С‡С‚Рѕ Сѓ Falloff РІ РґР¶РѕР±Р°С…).</summary>
        private static float InstanceFalloffDistance(GroundDecorInstance instance, Vector3 center)
        {
            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
            Vector3 normal = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
            if (normal.sqrMagnitude < 1e-6f)
            {
                normal = Vector3.up;
            }
            else
            {
                normal.Normalize();
            }

            Vector3 toCamera = position - center;
            float along = Vector3.Dot(toCamera, normal);
            return (toCamera - (normal * along)).magnitude;
        }

        /// <summary>РЎС‚Р°РґРёСЏ 3: СЂР°Р·РІРѕСЂРѕС‚ РїРѕРґС‚СѓС„С‚РѕРІ РІ persistent-РјР°СЃСЃРёРІ вЂ”
        /// РєСѓСЂСЃРѕСЂРѕРј РїРѕ РєР»РµС‚РєР°Рј/РїРѕРґС‚СѓС„С‚Р°Рј, СЂРµР·СѓР»СЊС‚Р°С‚ РґРµС‚РµСЂРјРёРЅРёСЂРѕРІР°РЅ.</summary>
        private bool AdvanceDecorExpand(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;
            int work = 0;
            int subPerCell = session.SubPerCell;
            while (session.K < session.Count && session.Write < session.Take
                && work < DecorExpandSubsPerStep)
            {
                int i = (int)(((long)session.K * session.Stride) % session.Count);
                if (session.Accepted[i] == 0)
                {
                    session.K++;
                    session.SubIndex = 0;
                    continue;
                }

                if (session.SubIndex == 0)
                {
                    session.BaseInstance = session.Instances[i];
                    int cellA = i / session.Cells;
                    int cellB = i % session.Cells;
                    session.CellSizeUv = session.SizeUv / session.Cells;
                    session.CellU0 = session.U0 + (cellA * session.CellSizeUv);
                    session.CellV0 = session.V0 + (cellB * session.CellSizeUv);
                    session.CurrentSubs = subPerCell;

                }

                while (session.SubIndex < session.CurrentSubs && session.Write < session.Take
                    && work < DecorExpandSubsPerStep)
                {
                    int subIndex = session.SubIndex;
                    session.SubIndex++;
                    work++;
                    GroundDecorInstance sub = session.BaseInstance;
                    // Прорастание из-под земли: у каждого пропа (дерево/камень/
                    // кактус/ромашка) своё время появления со сдвигом по хешу —
                    // растут поштучно, а не пачкой. Трава идёт Burst-путём.
                    sub.BirthTime = DecorPropStaggerSeconds > 0f
                        ? Time.time + (float)(GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 1997,
                            session.Node.Iy, 42) * DecorPropStaggerSeconds)
                        : 0f;

                    double finalU;
                    double finalV;
                    if (session.CurrentSubs == 1)
                    {
                        // РљР°РЅРґРёРґР°С‚ СѓР¶Рµ РґР¶РёС‚С‚РµСЂРёС‚СЃСЏ РїРѕ СЃРµС‚РєРµ вЂ” Р±РµСЂС‘Рј РµРіРѕ UV.
                        finalU = session.Uvs[i].x;
                        finalV = session.Uvs[i].y;
                    }
                    else
                    {
                        // РџРѕРґС‚СѓС„С‚С‹ РІРЅСѓС‚СЂРё СЏС‡РµР№РєРё: РїРѕР·РёС†РёРё СЃ С…РµС€-РґР¶РёС‚С‚РµСЂРѕРј
                        // (С„РёРєСЃ-СЃРµС‚РєР° 3Г—3 РґР°РІР°Р»Р° РїРѕРІС‚РѕСЂСЏСЋС‰РёР№СЃСЏ СѓР·РѕСЂ),
                        // РѕСЃС‚Р°Р»СЊРЅС‹Рµ РїР°СЂР°РјРµС‚СЂС‹ вЂ” СЃРІРѕРё РЅР° РєР°Р¶РґСѓСЋ С‚СЂР°РІРёРЅРєСѓ.
                        int subGridN = (int)System.Math.Ceiling(System.Math.Sqrt((double)session.CurrentSubs));
                        int subRow = subIndex / subGridN;
                        int subCol = subIndex % subGridN;
                        double jU = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 397, session.Node.Iy, 36);
                        double jV = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix, session.Node.Iy + 1399, 37);
                        finalU = session.CellU0 + (((subCol + jU) / subGridN) * session.CellSizeUv);
                        finalV = session.CellV0 + (((subRow + jV) / subGridN) * session.CellSizeUv);

                        double subScaleHash = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 997, session.Node.Iy, 33);
                        sub.Scale = (float)(layer.MinScale + subScaleHash * System.Math.Max(0d, layer.MaxScale - layer.MinScale));

                        double subSinkHash = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 1397, session.Node.Iy + 293, 38);
                        sub.SinkFactor = (float)(session.MinSink
                            + (subSinkHash * System.Math.Max(0d, session.MaxSink - session.MinSink)));

                        double subMeshHash = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 1497, session.Node.Iy + 593, 39);
                        sub.MeshIndex = session.MeshCount > 1
                            ? System.Math.Min(session.MeshCount - 1, (int)(subMeshHash * session.MeshCount))
                            : 0;

                        double subYawHash = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix, session.Node.Iy + 1999, 34);
                        double jitter = (subYawHash - 0.5d) * 2d * layer.WindJitterDegrees * 0.017453292519943295d;
                        sub.Yaw = (float)(session.BaseInstance.Yaw + jitter);

                        double subLeanHash = GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 1997, session.Node.Iy + 1993, 35);
                        sub.LeanDegrees = (float)(layer.WindLeanMinDegrees
                            + (subLeanHash * System.Math.Max(0d, layer.WindLeanMaxDegrees - layer.WindLeanMinDegrees)));
                    }

                    Vector3 normal = new Vector3(sub.Normal.x, sub.Normal.y, sub.Normal.z);
                    if (normal.sqrMagnitude < 1e-6f)
                    {
                        normal = Vector3.up;
                    }
                    else
                    {
                        normal.Normalize();
                    }

                    if (session.CurrentSubs > 1)
                    {
                        // Подтуфт ушёл от проверенного TryEvaluate центра клетки:
                        // перепроверяем жёсткие фильтры (вода/песок/горы/биом) по
                        // его собственному направлению. Без этого край клетки на
                        // грубом LOD сажал декор в воду и на пляж.
                        Vector3d subDirection = CubeSphere.Direction(session.Node.Face, finalU, finalV);
                        if (!GroundDecorDistribution.IsSurfaceAllowed(
                            session.Placement, noiseParams,
                            new double3(subDirection.X, subDirection.Y, subDirection.Z)))
                        {
                            continue;
                        }
                    }

                    Vector3 meshPoint = MeshSurfacePoint(
                        session.MeshVertices, session.CoreN,
                        (finalU - session.U0) / session.StepUv,
                        (finalV - session.V0) / session.StepUv);

                    // Страж снэпа: грубый меш чанка отклоняется от аналитической
                    // высоты на метры — точка обязана сама быть над водой,
                    // иначе деревья/камни «тонут» там, где аналитика видит сушу.
                    if (!GroundDecorDistribution.IsMeshPointAboveWater(
                        session.Placement, new float3(meshPoint.x, meshPoint.y, meshPoint.z)))
                    {
                        continue;
                    }

                    if (layer.PerInstanceDensity)
                    {
                        // РџСЂРёС‘Рј РЅР° РљРђР–Р”РЈР® С‚СЂР°РІРёРЅРєСѓ. Р—Р°С‚СѓС…Р°РЅРёРµ вЂ” РїРѕ
                        // С‚Р°РЅРіРµРЅС†РёР°Р»СЊРЅРѕР№ РґРёСЃС‚Р°РЅС†РёРё РґРѕ РєР°РјРµСЂС‹ Р’ РњР•РЁ-Р¤Р Р•Р™РњР•
                        // С‡Р°РЅРєР°: meshPoint Рё cameraLocal РІ РѕРґРЅРѕР№ СЃРёСЃС‚РµРјРµ,
                        // РїРѕСЌС‚РѕРјСѓ РѕР±Р»Р°РєРѕ СЂРµР°Р»СЊРЅРѕ СЃР»РµРґСѓРµС‚ Р·Р° РёРіСЂРѕРєРѕРј.
                        Vector3 toCamera = meshPoint - session.CameraLocal;
                        float alongNormal = Vector3.Dot(toCamera, normal);
                        Vector3 tangential = toCamera - (normal * alongNormal);
                        double accept = layer.Density * sub.DensityWeight * session.CapScale
                            * GroundDecorDistribution.Falloff(session.Placement, tangential.magnitude);
                        if (session.CurrentSubs == 1 && subPerCell > 1)
                        {
                            // Р”Р°Р»РµРєРѕ РїРѕРґС‚СѓС„С‚ РѕРґРёРЅ РІРјРµСЃС‚Рѕ СЃРµС‚РєРё вЂ” РІРµСЂРѕСЏС‚РЅРѕСЃС‚СЊ
                            // РїРѕРґРЅРёРјР°РµРј РІРѕ СЃС‚РѕР»СЊРєРѕ Р¶Рµ СЂР°Р·: РѕР¶РёРґР°РµРјРѕРµ С‡РёСЃР»Рѕ
                            // С‚СЂР°РІРёРЅРѕРє РЅР° РєР»РµС‚РєСѓ РЅРµ РїР°РґР°РµС‚, В«С…РІРѕСЃС‚В» С‚СЂР°РІС‹
                            // РґРѕ MaxDistance РѕСЃС‚Р°С‘С‚СЃСЏ.
                            accept *= subPerCell;
                        }

                        if (accept <= 0d)
                        {
                            continue;
                        }

                        if (accept < 1d)
                        {
                            double roll = GroundDecorDistribution.Hash01(
                                subIndex, session.Node.Face, i + session.Node.Ix + 797, session.Node.Iy + 397, 40);
                            if (roll >= accept)
                            {
                                continue;
                            }
                        }
                    }

                    float layerOffset = (float)(layer.GroundOffsetMeters - (sub.Scale * sub.SinkFactor));
                    sub.Position = new float3(
                        meshPoint.x + (normal.x * layerOffset),
                        meshPoint.y + (normal.y * layerOffset),
                        meshPoint.z + (normal.z * layerOffset));

                    session.Stored[session.Write++] = sub;
                }

                if (session.SubIndex >= session.CurrentSubs)
                {
                    session.SubIndex = 0;
                    session.K++;
                }
            }

            if (session.K >= session.Count || session.Write >= session.Take)
            {
                session.Stage = 4;
            }

            return work > 0;
        }

        /// <summary>РЎС‚Р°РґРёСЏ 4: РіРѕС‚РѕРІС‹Р№ СЃР»РѕР№ РІСЃС‚Р°С‘С‚ РІ С‡Р°РЅРє (РґР»СЏ РїРµСЂРµСЃР±РѕСЂРєРё вЂ”
        /// Р·Р°РјРµРЅРѕР№ СЃР»РѕСЏ С‚РѕР№ Р¶Рµ Р»РёС‡РЅРѕСЃС‚Рё; СЃС‚Р°СЂС‹Р№ СЂРёСЃСѓРµС‚СЃСЏ РґРѕ СЌС‚РѕРіРѕ РјРѕРјРµРЅС‚Р°).</summary>
        private void FinalizeDecorBuild(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;
            int write = session.Write;

            NativeArray<GroundDecorInstance> stored = session.Stored;
            if (stored.IsCreated && write < stored.Length)
            {
                if (write > 0)
                {
                    // Точный размер — из пула (обычно там уже есть буфер такой
                    // длины): остаётся только копия, без alloc/free на кадр.
                    NativeArray<GroundDecorInstance> shrunk =
                        DecorArrayPool.Rent<GroundDecorInstance>(write);
                    NativeArray<GroundDecorInstance>.Copy(stored, shrunk, write);
                    DecorArrayPool.Return(ref stored);
                    stored = shrunk;
                }
                else
                {
                    DecorArrayPool.Return(ref stored);
                    stored = default;
                }
            }
            else if (!stored.IsCreated)
            {
                write = 0;
            }

            session.Stored = default;

            // РќР°С…РѕРґРёРј РїСЂРµР¶РЅРёР№ СЃР»РѕР№ СЌС‚РѕР№ Р»РёС‡РЅРѕСЃС‚Рё (РµСЃР»Рё СЌС‚Рѕ РїРµСЂРµСЃР±РѕСЂРєР°):
            // Р·Р°РјРµРЅСЏРµРј РіРѕС‚РѕРІС‹Рј, Р° РїСЂРё РїСѓСЃС‚РѕРј СЂРµР·СѓР»СЊС‚Р°С‚Рµ вЂ” СѓР±РёСЂР°РµРј, РёРЅР°С‡Рµ
            // СЃС‚Р°СЂС‹Р№ СЃР»РѕР№ РѕСЃС‚Р°Р»СЃСЏ Р±С‹ В«СѓСЃС‚Р°СЂРµРІС€РёРјВ» РЅР°РІСЃРµРіРґР° Рё РїРµСЂРµСЃРѕР±РёСЂР°Р»СЃСЏ
            // Р±С‹ Р±РµСЃРєРѕРЅРµС‡РЅРѕ.
            int existingIndex = -1;
            for (int i = 0; i < session.Chunk.Decor.Count; i++)
            {
                if (session.Chunk.Decor[i].LayerIndex == session.LayerIndex)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (write > 0)
            {
                var runtime = new DecorLayerRuntime
                {
                    Profile = layer,
                    LayerIndex = session.LayerIndex,
                    BuildCameraRenderPos = session.CameraPosition,
                    BuildCameraLocalPosition = session.BuildCameraLocal,
                    Instances = stored,
                    // Трава рисуется Burst-матрицами (WorldMatrices), managed
                    // Matrices ей не нужен: раньше на каждую пересборку
                    // аллоцировался массив до 6.4 МБ в мусор.
                    Matrices = layer.PerInstanceDensity ? null : new Matrix4x4[write]
                };

                if (layer.PerInstanceDensity)
                {
                    runtime.WorldMatrices = DecorArrayPool.Rent<Matrix4x4>(write);
                    // Таблица раскладки пула по клеткам переходит рантайму:
                    // по ней следующая пересборка перенесёт уже выросшие травинки.
                    runtime.CellOffsets = session.WriteOffsets;
                    runtime.CellCounts = session.WriteCounts;
                    session.WriteOffsets = default;
                    session.WriteCounts = default;
                }

                if (existingIndex >= 0)
                {
                    DisposeDecorLayerRuntime(session.Chunk.Decor[existingIndex]);
                    session.Chunk.Decor[existingIndex] = runtime;
                }
                else
                {
                    session.Chunk.Decor.Add(runtime);
                }
            }
            else if (existingIndex >= 0)
            {
                DisposeDecorLayerRuntime(session.Chunk.Decor[existingIndex]);
                session.Chunk.Decor.RemoveAt(existingIndex);
            }

            session.Chunk.DecorBuiltMask |= 1 << session.LayerIndex;
            if (layer.DensityFalloffMeters > 0f)
            {
                // Р—Р°РїРѕРјРёРЅР°РµРј РїРѕР·РёС†РёСЋ СЃР±РѕСЂРєРё РґР°Р¶Рµ РґР»СЏ РїСѓСЃС‚РѕРіРѕ СЂРµР·СѓР»СЊС‚Р°С‚Р°: РїРѕ РЅРµР№
                // СЃР»РѕР№ РїРµСЂРµСЃРѕР±РёСЂР°РµС‚СЃСЏ, РєРѕРіРґР° РёРіСЂРѕРє РїРѕРґРѕР№РґС‘С‚ Р±Р»РёР¶Рµ (РёРЅР°С‡Рµ С‡Р°РЅРє,
                // РїРѕСЃС‚СЂРѕРµРЅРЅС‹Р№ РёР·РґР°Р»РµРєР° РїСѓСЃС‚С‹Рј, РЅР°РІСЃРµРіРґР° РѕСЃС‚Р°Р»СЃСЏ Р±С‹ Р±РµР· С‚СЂР°РІС‹).
                if (session.Chunk.DecorBuildCameraLocal == null)
                {
                    int layerCount = decorProfile != null && decorProfile.Layers != null
                        ? decorProfile.Layers.Count
                        : session.LayerIndex + 1;
                    session.Chunk.DecorBuildCameraLocal = new Vector3[Mathf.Max(1, layerCount)];
                }

                if (session.LayerIndex < session.Chunk.DecorBuildCameraLocal.Length)
                {
                    session.Chunk.DecorBuildCameraLocal[session.LayerIndex] = session.BuildCameraLocal;
                }
            }

            DisposeDecorBuildSession(session);
        }

        /// <summary>Р›РµРЅРёРІР°СЏ СЃР±РѕСЂРєР° СЃР»РѕС‘РІ РґРµРєРѕСЂР°: С‡Р°РЅРє РґРµРєРѕСЂРёСЂСѓРµС‚СЃСЏ РїРѕ СЃР»РѕСЋ,
        /// РєРѕРіРґР° РІРїРµСЂРІС‹Рµ РІС…РѕРґРёС‚ РІ РµРіРѕ MaxDistance+SpawnMargin. Р—Р° РїРѕСЃС‚Р°РЅРѕРІРєСѓ
        /// РѕС‚РІРµС‡Р°РµС‚ СЌС‚РѕС‚ РїРѕРёСЃРє (Р±Р»РёР¶РЅРёРµ РїРµСЂРІС‹РјРё), СЃР°РјР° СЃР±РѕСЂРєР° РёРґС‘С‚ РїРѕСЂС†РёСЏРјРё РїРѕ
        /// Р±СЋРґР¶РµС‚Сѓ РєР°РґСЂР° (TryQueueDecorBuild/StepDecorBuilds) вЂ” Р±РµР· С„СЂРёР·РѕРІ.</summary>
        private void EnsureVisibleDecor(Vector3 cameraPosition)
        {
            if (decorProfile == null || decorProfile.Layers == null || decorProfile.Layers.Count == 0)
            {
                return;
            }

            int budget = DecorBuildsPerFrame;
            if (budget <= 0)
            {
                return;
            }

            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            // РўСЂР°РІР° (per-instance) вЂ” РІРЅРµ РѕС‡РµСЂРµРґРё Р’Рћ Р’РЎР•Р™ Р·РѕРЅРµ РґРѕСЃР±РѕСЂРєРё СЃР»РѕСЏ
            // (MaxDistance + Р·Р°РїР°СЃ): РёРЅР°С‡Рµ РµС‘ СЃР±РѕСЂРєРё Р¶РґСѓС‚ РґР°Р»СЊРЅРёС… СЃР»РѕС‘РІ
            // (РґРµСЂРµРІСЊСЏ/РєР°РјРЅРё РґРѕ 3 РєРј), Рё С†РµР»С‹Рµ РєРІР°РґСЂР°С‚С‹ С‚СЂР°РІС‹ РґРѕСЃС‚СЂР°РёРІР°СЋС‚СЃСЏ,
            // РєРѕРіРґР° РёРіСЂРѕРє СѓР¶Рµ Р±Р»РёР·РєРѕ вЂ” СЌС‚Рѕ РІРёРґРЅРѕ РєР°Рє В«СЃРїР°РІРЅ РєРІР°РґСЂР°С‚Р°РјРёВ».
            for (int built = 0; built < budget; built++)
            {
                Chunk bestChunk = null;
                int bestLayer = -1;
                float bestDistance = float.MaxValue;

                for (int pass = 0; pass < 2 && bestChunk == null; pass++)
                {
                    bool grassPass = pass == 0;
                    foreach (KeyValuePair<long, Chunk> kv in chunks)
                    {
                        Chunk chunk = kv.Value;
                        if (!chunk.Visible || chunk.Go == null)
                        {
                            continue;
                        }

                        long chunkId = NodeId(chunk.Node);
                        float distance = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
                        for (int layerIndex = 0; layerIndex < decorProfile.Layers.Count; layerIndex++)
                        {
                            if ((chunk.DecorBuiltMask & (1 << layerIndex)) != 0)
                            {
                                continue;
                            }

                            GroundDecorLayer layer = decorProfile.Layers[layerIndex];
                            if (layer == null || !layer.Enabled || layer.NearMeshes == null || layer.NearMeshes.Length == 0)
                            {
                                continue;
                            }

                            bool isGrass = layer.PerInstanceDensity;
                            if (grassPass)
                            {
                                if (!isGrass)
                                {
                                    continue;
                                }
                            }
                            else if (isGrass)
                            {
                                // Р’СЃСЋ С‚СЂР°РІСѓ СѓР¶Рµ СЂР°Р·РѕР±СЂР°Р» РїРµСЂРІС‹Р№ РїСЂРѕС…РѕРґ.
                                continue;
                            }

                            if (HasDecorBuildSession(chunkId, layerIndex))
                            {
                                continue;
                            }

                            if (distance <= layer.MaxDistanceMeters + layer.SpawnMarginMeters && distance < bestDistance)
                            {
                                bestDistance = distance;
                                bestChunk = chunk;
                                bestLayer = layerIndex;
                            }
                        }
                    }
                }

                if (bestChunk == null)
                {
                    return;
                }

                // В«Р СЏРґРѕРјВ» вЂ” С‚Рѕ, С‡С‚Рѕ РёРіСЂРѕРє РІРёРґРёС‚ СѓР¶Рµ СЃРµР№С‡Р°СЃ (РІ РїСЂРµРґРµР»Р°С…
                // MaxDistance). Р”Р°Р»СЊС€Рµ вЂ” Р·РѕРЅР° РѕРїРµСЂРµР¶Р°СЋС‰РµР№ СЃР±РѕСЂРєРё: РµС‘ СЃР»РѕС‚С‹
                // РѕРіСЂР°РЅРёС‡РµРЅС‹ СЂРµР·РµСЂРІРѕРј РїРѕРґ РёРіСЂРѕРєР°.
                bool nearPlayer = bestDistance <= decorProfile.Layers[bestLayer].MaxDistanceMeters;
                if (!TryQueueDecorBuild(bestChunk, bestLayer, cameraPosition, nearPlayer))
                {
                    return;
                }
            }
        }

        /// <summary>РўР°РЅРіРµРЅС†РёР°Р»СЊРЅР°СЏ РґРёСЃС‚Р°РЅС†РёСЏ РґРѕ С‡Р°РЅРєР° (РєР°Рє РІ DrawDecor): РІС‹СЃРѕС‚Р°
        /// РїРѕР»С‘С‚Р° РЅРµ РІС‹РєР»СЋС‡Р°РµС‚ РґРµРєРѕСЂ СЂР°Р·РѕРј.</summary>
        private static float ChunkTangentialDistance(Chunk chunk, Vector3 cameraPosition, Vector3 cameraUp)
        {
            Vector3 toChunk = chunk.Go.transform.position - cameraPosition;
            Vector3 tangential = toChunk - (Vector3.Dot(toChunk, cameraUp) * cameraUp);
            return Mathf.Max(0f, tangential.magnitude - chunk.BoundsRadius);
        }

        /// <summary>РњРёСЂРѕРІС‹Рµ РіСЂР°РЅРёС†С‹ РґРµРєРѕСЂР° С‡Р°РЅРєР°: AABB Р Р•РќР”Р•Р -РјРµС€Р° (СЃ Р·Р°РїР°СЃРѕРј
        /// BoundsRadius) СЃ РєРѕСЂСЂРµРєС‚РЅС‹Рј РїРѕРІРѕСЂРѕС‚РѕРј (|M|В·extents) + Р·Р°РїР°СЃ РїРѕ РІС‹СЃРѕС‚Рµ
        /// РЅР° С‚СЂР°РІРёРЅРєРё/Р±РёР»Р»Р±РѕСЂРґС‹. Р’Р°Р¶РЅРѕ: РїРѕР·РёС†РёСЏ С‡Р°РЅРєР° вЂ” РЅР° СѓСЂРѕРІРЅРµ РјРѕСЂСЏ
        /// (radius), Р° С‚СЂР°РІР° Р»РµР¶РёС‚ РЅР° СЂРµР»СЊРµС„Рµ, РїРѕСЌС‚РѕРјСѓ СЃС‚Р°СЂС‹Р№ Р±РѕРєСЃ РІРѕРєСЂСѓРі
        /// РїРѕР·РёС†РёРё С‡Р°РЅРєР° РЅРµ СЃРѕРґРµСЂР¶Р°Р» РёРЅСЃС‚Р°РЅСЃС‹ РЅР° РІРѕР·РІС‹С€РµРЅРЅРѕСЃС‚СЏС…, Рё Unity
        /// РєСѓР»Р»РёР»Р° Р±Р°С‚С‡ С†РµР»РёРєРѕРј В«РїРѕ СѓРіР»Сѓ РєР°РјРµСЂС‹В».</summary>
        private static Bounds ChunkWorldBounds(Chunk chunk)
        {
            // Р‘РµР·РѕРїР°СЃРЅС‹Р№ Р·Р°РїР°СЃ РЅР° С‚СЂР°РІСѓ (РґРѕ ~2.6 Рј), Р±РёР»Р»Р±РѕСЂРґС‹ Рё РІРµС‚РµСЂ.
            const float DecorMargin = 8f;
            if (chunk == null || chunk.Mesh == null)
            {
                return new Bounds(chunk != null && chunk.Go != null ? chunk.Go.transform.position : Vector3.zero,
                    Vector3.one * (DecorMargin * 2f));
            }

            Matrix4x4 m = chunk.Go.transform.localToWorldMatrix;
            Bounds local = chunk.Mesh.bounds;
            Vector3 e = local.extents;
            Vector3 ax = m.MultiplyVector(new Vector3(e.x, 0f, 0f));
            Vector3 ay = m.MultiplyVector(new Vector3(0f, e.y, 0f));
            Vector3 az = m.MultiplyVector(new Vector3(0f, 0f, e.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(ax.x) + Mathf.Abs(ay.x) + Mathf.Abs(az.x),
                Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y),
                Mathf.Abs(ax.z) + Mathf.Abs(ay.z) + Mathf.Abs(az.z));

            Vector3 center = m.MultiplyPoint3x4(local.center);
            return new Bounds(center, (worldExtents + (Vector3.one * DecorMargin)) * 2f);
        }

        /// <summary>
        /// РћР±Р»Р°РєРѕ РїР»РѕС‚РЅРѕСЃС‚Рё СЃР»РѕС‘РІ СЃ DensityFalloffMeters Р·Р°РїРµРєР°РµС‚СЃСЏ РїРѕ РїРѕР·РёС†РёРё
        /// РєР°РјРµСЂС‹ РїСЂРё СЃР±РѕСЂРєРµ. РРіСЂРѕРє РѕС‚РѕС€С‘Р» вЂ” РѕР±Р»Р°РєРѕ РѕСЃС‚Р°Р»РѕСЃСЊ РїРѕР·Р°РґРё, РІРїРµСЂРµРґРё
        /// В«РїСѓСЃС‚РѕВ». РџСЂРё РўРђРќР“Р•РќР¦РРђР›Р¬РќРћРњ СЃРјРµС‰РµРЅРёРё Р±РѕР»СЊС€Рµ РїРѕСЂРѕРіР° (РґРѕР»СЏ РјР°СЃС€С‚Р°Р±Р°
        /// Р·Р°С‚СѓС…Р°РЅРёСЏ) СЃС‚Р°РІРёРј РїРµСЂРµСЃР±РѕСЂРєСѓ Р±Р»РёР¶Р°Р№С€РµРіРѕ СѓСЃС‚Р°СЂРµРІС€РµРіРѕ СЃР»РѕСЏ. РЎР±РѕСЂРєР°
        /// Р°СЃРёРЅС…СЂРѕРЅРЅР°СЏ: СЃС‚Р°СЂС‹Р№ СЃР»РѕР№ СЂРёСЃСѓРµС‚СЃСЏ РґРѕ РіРѕС‚РѕРІРЅРѕСЃС‚Рё РЅРѕРІРѕРіРѕ, РєР°РґСЂ РЅРµ
        /// Р±Р»РѕРєРёСЂСѓРµС‚СЃСЏ. Р‘С‹СЃС‚СЂС‹Р№ РїРѕР»С‘С‚ РЅРµ РїРµСЂРµСЃРѕР±РёСЂР°РµС‚: С‚Р°Рј С‡Р°РЅРєРё Рё С‚Р°Рє РЅРѕРІС‹Рµ.
        /// </summary>
        private void RefreshMovingDecor(Vector3 cameraPosition)
        {
            if (decorProfile == null || decorProfile.Layers == null)
            {
                return;
            }

            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            Chunk bestChunk = null;
            int bestLayerIndex = -1;
            // Р‘РµСЂС‘Рј РЎРђРњР«Р™ РЈРЎРўРђР Р•Р’РЁРР™ СЃР»РѕР№: РїСЂРё СЂР°РІРЅС‹С… РґРёСЃС‚Р°РЅС†РёСЏС… (РєСЂСѓРїРЅС‹Рµ С‡Р°РЅРєРё
            // РїРµСЂРµРєСЂС‹РІР°СЋС‚ РёРіСЂРѕРєР°) РІС‹Р±РѕСЂ В«РїРѕ Р±Р»РёР·РѕСЃС‚РёВ» РІСЃРµРіРґР° СѓРєР°Р·С‹РІР°Р» РЅР° РѕРґРёРЅ Рё
            // С‚РѕС‚ Р¶Рµ С‡Р°РЅРє, Р° СЃРѕСЃРµРґРЅРёРµ РєРѕРїРёР»Рё РѕС‚СЃС‚Р°РІР°РЅРёРµ РѕР±Р»Р°РєР° вЂ” С‚Р°Рј С‚СЂР°РІР°
            // РїСЂРѕРїР°РґР°Р»Р°. РџРµСЂРµСЃР±РѕСЂРєР° РѕР±РЅСѓР»СЏРµС‚ В«СѓСЃС‚Р°СЂРµРІР°РЅРёРµВ», РїРѕСЌС‚РѕРјСѓ РѕС‡РµСЂРµРґСЊ
            // СЃР°РјР° РїРµСЂРµР±РёСЂР°РµС‚ С‡Р°РЅРєРё РїРѕ РєСЂСѓРіСѓ.
            float bestStale = -1f;
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.Go == null || chunk.DecorBuiltMask == 0)
                {
                    continue;
                }

                long chunkId = NodeId(chunk.Node);
                float chunkDistance = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
                Vector3 currentCameraLocal = Vector3.zero;
                bool hasCameraLocal = false;
                Vector3 localUp = Vector3.zero;
                for (int layerIndex = 0; layerIndex < decorProfile.Layers.Count; layerIndex++)
                {
                    if ((chunk.DecorBuiltMask & (1 << layerIndex)) == 0)
                    {
                        continue;
                    }

                    GroundDecorLayer layer = decorProfile.Layers[layerIndex];
                    if (layer == null || layer.DensityFalloffMeters <= 0f
                        || chunkDistance > layer.MaxDistanceMeters)
                    {
                        continue;
                    }

                    if (HasDecorBuildSession(chunkId, layerIndex))
                    {
                        continue;
                    }

                    // РџРѕР·РёС†РёСЏ СЃР±РѕСЂРєРё: Сѓ РЅРµРїСѓСЃС‚РѕРіРѕ СЃР»РѕСЏ вЂ” РёР· СЂР°РЅС‚Р°Р№РјР°, Сѓ РїСѓСЃС‚РѕРіРѕ вЂ”
                    // РёР· Р·Р°РїРёСЃР°РЅРЅРѕР№ РїСЂРё С„РёРЅР°Р»РёР·Р°С†РёРё (РёРЅР°С‡Рµ СЃР»РѕР№, СЃРѕР±СЂР°РЅРЅС‹Р№
                    // РёР·РґР°Р»РµРєР° РїСѓСЃС‚С‹Рј, РЅРёРєРѕРіРґР° Р±С‹ РЅРµ РїРµСЂРµСЃРѕР±СЂР°Р»СЃСЏ Р±Р»РёР¶Рµ).
                    DecorLayerRuntime runtime = null;
                    for (int i = 0; i < chunk.Decor.Count; i++)
                    {
                        if (chunk.Decor[i].LayerIndex == layerIndex)
                        {
                            runtime = chunk.Decor[i];
                            break;
                        }
                    }

                    Vector3 buildCameraLocal;
                    if (runtime != null)
                    {
                        buildCameraLocal = runtime.BuildCameraLocalPosition;
                    }
                    else if (chunk.DecorBuildCameraLocal != null
                        && layerIndex < chunk.DecorBuildCameraLocal.Length)
                    {
                        buildCameraLocal = chunk.DecorBuildCameraLocal[layerIndex];
                    }
                    else
                    {
                        continue;
                    }

                    if (!hasCameraLocal)
                    {
                        currentCameraLocal = chunk.Go.transform.InverseTransformPoint(cameraPosition);
                        localUp = chunk.Go.transform.InverseTransformDirection(cameraUp);
                        if (localUp.sqrMagnitude > 1e-6f)
                        {
                            localUp.Normalize();
                        }

                        hasCameraLocal = true;
                    }

                    // РџРѕСЂРѕРі СѓСЃС‚Р°СЂРµРІР°РЅРёСЏ: РїР»РѕС‚РЅРѕСЃС‚СЊ РІРЅСѓС‚СЂРё РїР»РѕСЃРєРѕРіРѕ СЏРґСЂР°
                    // (DensityCoreMeters) РѕС‚ СЃРјРµС‰РµРЅРёСЏ С†РµРЅС‚СЂР° РЅРµ РјРµРЅСЏРµС‚СЃСЏ,
                    // РїРѕСЌС‚РѕРјСѓ РёРіСЂРѕРє РґРѕР»Р¶РµРЅ РѕСЃС‚Р°РІР°С‚СЊСЃСЏ Р’РќРЈРўР Р СЏРґСЂР° вЂ” С‚РѕРіРґР°
                    // РїРµСЂРµСЃР±РѕСЂРєР° Р»РёС€СЊ СѓС‚РѕС‡РЅСЏРµС‚ РґР°Р»СЊРЅРёР№ РєСЂР°Р№ Рё С‚СЂР°РІР° РЅРµ В«РґС‹С€РёС‚В».
                    // Р‘РѕР»СЊС€Рµ РїРѕСЂРѕРі вЂ” СЂРµР¶Рµ РїРµСЂРµСЃР±РѕСЂРєРё (РѕРЅРё РґРѕСЂРѕРіРёРµ: РїСЂРѕРіРѕРЅ
                    // РєР°РЅРґРёРґР°С‚РѕРІ + СЂР°Р·РІРѕСЂРѕС‚ РїСѓР»Р°).
                    float core = (float)System.Math.Max(0d, layer.DensityCoreMeters);
                    float threshold = surfaceVelocityLocal.magnitude < 0.5f
                        ? 8f
                        : Mathf.Clamp(core * 0.6f, 16f, Mathf.Max(16f, layer.MaxDistanceMeters * 0.3f));
                    Vector3 buildDelta = currentCameraLocal - buildCameraLocal;
                    if (localUp.sqrMagnitude > 1e-6f)
                    {
                        // РўРѕР»СЊРєРѕ С‚Р°РЅРіРµРЅС†РёР°Р»СЊРЅРѕРµ СЃРјРµС‰РµРЅРёРµ: РїРѕРґСЉС‘Рј/СЃРїСѓСЃРє РѕР±Р»Р°РєРѕ РЅРµ СЃС‚Р°СЂРёС‚.
                        buildDelta -= localUp * Vector3.Dot(buildDelta, localUp);
                    }

                    float moved = buildDelta.sqrMagnitude;
                    if (moved <= threshold * threshold)
                    {
                        continue;
                    }

                    if (moved > bestStale)
                    {
                        bestStale = moved;
                        bestChunk = chunk;
                        bestLayerIndex = layerIndex;
                    }
                }
            }

            if (bestChunk == null)
            {
                return;
            }

            TryQueueDecorBuild(bestChunk, bestLayerIndex, cameraPosition, true);
        }

        /// <summary>Р’С‹РіСЂСѓР·РєР° РїСѓР»РѕРІ С‚СЂР°РІС‹ РґР°Р»РµРєРѕ Р·Р° РїСЂРµРґРµР»Р°РјРё РІРёРґРёРјРѕСЃС‚Рё: РёРЅР°С‡Рµ
        /// РїСЂРѕР№РґРµРЅРЅС‹Рµ С‡Р°РЅРєРё РєРѕРїСЏС‚ РїРѕР»РЅС‹Рµ РїСѓР»С‹ (РїРѕ РєР°РїСѓ РЅР° С‡Р°РЅРє), Р±СЋРґР¶РµС‚
        /// РёРЅСЃС‚Р°РЅСЃРѕРІ СѓС…РѕРґРёС‚ РѕС‚ РёРіСЂРѕРєР° Рё РІРёРґРёРјР°СЏ Р·РѕРЅР° РїСѓСЃС‚РµРµС‚. Р’РѕР·РІСЂР°С‚ РёРіСЂРѕРєР°
        /// РїРµСЂРµСЃРѕР±РµСЂС‘С‚ СЃР»РѕР№ Р·Р°РЅРѕРІРѕ (РјР°СЃРєР° СЃРЅРёРјР°РµС‚СЃСЏ).</summary>
        private void TrimDistantDecor(Vector3 cameraPosition)
        {
            if (decorProfile == null || decorProfile.Layers == null)
            {
                return;
            }

            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (chunk.Go == null || chunk.DecorBuiltMask == 0)
                {
                    continue;
                }

                float distance = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
                for (int layerIndex = 0; layerIndex < decorProfile.Layers.Count; layerIndex++)
                {
                    if ((chunk.DecorBuiltMask & (1 << layerIndex)) == 0)
                    {
                        continue;
                    }

                    GroundDecorLayer layer = decorProfile.Layers[layerIndex];
                    if (layer == null || layer.DensityFalloffMeters <= 0f)
                    {
                        continue;
                    }

                    // РўРѕР»СЊРєРѕ Р·Р° РїСЂРµРґРµР»Р°РјРё Р—РћРќР« Р”РћРЎР‘РћР РљР (MaxDistance + Р·Р°РїР°СЃ
                    // СЃРїР°РІРЅР°): РёРЅР°С‡Рµ РІС‹РіСЂСѓР¶РµРЅРЅС‹Р№ С‡Р°РЅРє С‚СѓС‚ Р¶Рµ СЃС‚Р°РІРёС‚СЃСЏ РІ РѕС‡РµСЂРµРґСЊ
                    // Р·Р°РЅРѕРІРѕ Рё Р·Р°Р±РёРІР°РµС‚ СЃРµСЃСЃРёРё, Р° С‚СЂР°РІР° Сѓ РёРіСЂРѕРєР° РЅРµ РїРµСЂРµСЃРѕР±РёСЂР°РµС‚СЃСЏ.
                    if (distance <= layer.MaxDistanceMeters + layer.SpawnMarginMeters + 20f)
                    {
                        continue;
                    }

                    if (HasDecorBuildSession(NodeId(chunk.Node), layerIndex))
                    {
                        continue;
                    }

                    for (int i = chunk.Decor.Count - 1; i >= 0; i--)
                    {
                        if (chunk.Decor[i].LayerIndex == layerIndex)
                        {
                            DisposeDecorLayerRuntime(chunk.Decor[i]);
                            chunk.Decor.RemoveAt(i);
                        }
                    }

                    chunk.DecorBuiltMask &= ~(1 << layerIndex);
                }
            }
        }

        /// <summary>Р›РѕРєР°Р»СЊРЅР°СЏ (С‡Р°РЅРєСѓ) РјР°С‚СЂРёС†Р° РёРЅСЃС‚Р°РЅСЃР°: РїРѕР·РёС†РёСЏ/РЅРѕСЂРјР°Р»СЊ/yaw/
        /// РЅР°РєР»РѕРЅ/РјР°СЃС€С‚Р°Р±. РќРµ Р·Р°РІРёСЃРёС‚ РѕС‚ РєР°РјРµСЂС‹ вЂ” СЃС‡РёС‚Р°РµС‚СЃСЏ РѕРґРёРЅ СЂР°Р· РїСЂРё СЃР±РѕСЂРєРµ.</summary>
        /// <summary>Множитель прорастания пропа по его BirthTime: 0 сразу после
        /// появления, 1 — вырос. Ничего не делает для старых сборок (BirthTime 0).</summary>
        private static float DecorBirthFade(GroundDecorInstance instance, float fadeSeconds)
        {
            if (fadeSeconds <= 0f || instance.BirthTime <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((Time.time - instance.BirthTime) / fadeSeconds);
        }

        private static Matrix4x4 DecorLocalMatrix(GroundDecorInstance instance, GroundDecorLayer layer, Vector3 up)
        {
            return DecorLocalMatrix(instance, layer, up, 1f);
        }

        /// <summary>С множителем прорастания: fade 0..1 — проп вылезает из-под
        /// земли, а не появляется мгновенно (BirthTime ставит сборка).</summary>
        private static Matrix4x4 DecorLocalMatrix(
            GroundDecorInstance instance, GroundDecorLayer layer, Vector3 up, float fade)
        {
            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
            Vector3 reference = Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, up).normalized;
            Quaternion rotation;
            if (layer.FlatOnGround)
            {
                // РџР»Р°С€РјСЏ, Р±РµР· СЃР»СѓС‡Р°Р№РЅРѕРіРѕ РїРѕРІРѕСЂРѕС‚Р°: В«РїСЏС‚Р°С‡РѕРєВ» Р»РµР¶РёС‚
                // СЂРѕРІРЅРѕ (Р»РѕРєР°Р»СЊРЅС‹Р№ Z РјРµС€Р° = РЅРѕСЂРјР°Р»СЊ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё).
                rotation = Quaternion.LookRotation(up, tangent);
            }
            else
            {
                Vector3 forward = Vector3.Cross(up, tangent);
                Quaternion align = Quaternion.LookRotation(forward, up);
                rotation = Quaternion.AngleAxis(instance.Yaw * 57.29578f, up) * align;
                if (instance.LeanDegrees > 0.001f)
                {
                    Vector3 windForward = rotation * Vector3.forward;
                    Vector3 leanAxis = Vector3.Cross(up, windForward);
                    if (leanAxis.sqrMagnitude > 1e-8f)
                    {
                        rotation = Quaternion.AngleAxis(instance.LeanDegrees, leanAxis.normalized) * rotation;
                    }
                }
            }

            return Matrix4x4.TRS(position, rotation, new Vector3(instance.Scale, instance.Scale, instance.Scale) * Mathf.Clamp01(fade));
        }

        /// <summary>РњРёСЂРѕРІРѕРµ РЅР°РїСЂР°РІР»РµРЅРёРµ РІРµС‚СЂР° РёРЅСЃС‚Р°РЅСЃР°: С‚РѕС‚ Р¶Рµ yaw-Р±Р°Р·РёСЃ, С‡С‚Рѕ Сѓ
        /// Р±Р»РёР¶РЅРµРіРѕ LOD (yaw РІРѕРєСЂСѓРі РЅРѕСЂРјР°Р»Рё РѕС‚ РѕСЃРё В«СЃРµРІРµСЂВ»), РїРѕРІС‘СЂРЅСѓС‚С‹Р№ С‡Р°РЅРєРѕРј.</summary>
        private static Vector3 WindLeanDirection(GroundDecorInstance instance, Matrix4x4 chunkMatrix)
        {
            Vector3 up = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
            if (up.sqrMagnitude < 1e-6f)
            {
                up = Vector3.up;
            }
            else
            {
                up.Normalize();
            }

            Vector3 reference = Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, up).normalized;
            Vector3 forward = Vector3.Cross(up, tangent);
            Vector3 yawForward = Quaternion.AngleAxis(instance.Yaw * 57.29578f, up) * forward;
            return chunkMatrix.MultiplyVector(yawForward).normalized;
        }

        /// <summary>
        /// РќР°СЂРёСЃРѕРІР°С‚СЊ РґРµРєРѕСЂ РІРёРґРёРјС‹С… С‡Р°РЅРєРѕРІ: near вЂ” solid-РјРµС€, РґР°Р»СЊС€Рµ вЂ”
        /// Р±РёР»Р»Р±РѕСЂРґ РґРѕ MaxDistance. РњР°С‚СЂРёС†С‹ вЂ” РјРёСЂРѕРІРѕР№ С‚СЂР°РЅСЃС„РѕСЂРј С‡Р°РЅРєР° Г—
        /// Р»РѕРєР°Р»СЊРЅС‹Р№ TRS РёРЅСЃС‚Р°РЅСЃР° (РїРµСЂРµСЃС‡С‘С‚ РєР°Р¶РґС‹Р№ РєР°РґСЂ: floating origin/СЃРїРёРЅ).
        /// </summary>
        private void DrawDecor(Vector3 cameraPosition)
        {
            if (decorProfile == null)
            {
                return;
            }

            // РЎС‚СЂР°С…РѕРІРєР°: РµСЃР»Рё РїСЂРѕС€Р»С‹Р№ РєР°РґСЂ Р·Р°РІРµСЂС€РёР»СЃСЏ РёСЃРєР»СЋС‡РµРЅРёРµРј РґРѕ Flush,
            // РґРѕРІРѕРґРёРј Р·Р°РїР»Р°РЅРёСЂРѕРІР°РЅРЅС‹Рµ РґР¶РѕР±С‹, РёРЅР°С‡Рµ РЅРѕРІС‹Рµ РїРёСЃР°Р»Рё Р±С‹ РІ С‚Рµ Р¶Рµ
            // РјР°СЃСЃРёРІС‹ РјР°С‚СЂРёС† РїР°СЂР°Р»Р»РµР»СЊРЅРѕ.
            if (burstDecorDraws.Count != 0)
            {
                FlushBurstDecor();
            }

            // Р”РёСЃС‚Р°РЅС†РёСЏ РґРµРєРѕСЂР° вЂ” С‚Р°РЅРіРµРЅС†РёР°Р»СЊРЅР°СЏ (РїРѕ РєР°СЃР°С‚РµР»СЊРЅРѕР№ Рє РїРѕРІРµСЂС…РЅРѕСЃС‚Рё):
            // РІС‹СЃРѕС‚Р° РїРѕР»С‘С‚Р° РќР• РІС‹РєР»СЋС‡Р°РµС‚ РІРµСЃСЊ РґРµРєРѕСЂ СЂР°Р·РѕРј. РРЅР°С‡Рµ РЅР° РЅР°Р±РѕСЂРµ
            // РІС‹СЃРѕС‚С‹ ~MaxDistance РІСЃС‘ РёСЃС‡РµР·Р°Р»Рѕ РѕРґРЅРѕР№ РіСЂР°РЅРёС†РµР№ (РґРµСЂРµРІСЊСЏ ~3 РєРј).
            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            // Р¤СЂСѓСЃС‚СѓРј-РєСѓР»Р»РёРЅРі РґРµРєРѕСЂР°: Сѓ С‚СЂР°РІС‹ РґРµСЃСЏС‚РєРё С‚С‹СЃСЏС‡ РёРЅСЃС‚Р°РЅСЃРѕРІ, Рё
            // СЃС‡РёС‚Р°С‚СЊ РјР°С‚СЂРёС†С‹ С‡Р°РЅРєР°Рј Р·Р° СЃРїРёРЅРѕР№ РЅРµР·Р°С‡РµРј. РЎР»РѕРё СЃ С‚РµРЅСЏРјРё (РґРµСЂРµРІСЊСЏ)
            // РЅРµ СЂРµР¶РµРј вЂ” РёС… С‚РµРЅСЊ РјРѕР¶РµС‚ РїРѕРїР°РґР°С‚СЊ РІ РєР°РґСЂ РёР·-Р·Р° СЃРїРёРЅС‹.
            Camera decorCamera = Camera.main;
            if (decorCamera != null)
            {
                if (decorFrustumPlanes == null)
                {
                    decorFrustumPlanes = new Plane[6];
                }

                GeometryUtility.CalculateFrustumPlanes(decorCamera, decorFrustumPlanes);
            }
            else
            {
                decorFrustumPlanes = null;
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.Decor.Count == 0)
                {
                    continue;
                }

                Matrix4x4 chunkMatrix = chunk.Go.transform.localToWorldMatrix;
                float distance = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
                // Р“СЂР°РЅРёС†С‹ вЂ” РїРѕ С„Р°РєС‚РёС‡РµСЃРєРѕРјСѓ СЂРµР»СЊРµС„Сѓ (|M|В·extents, Р·Р°РїР°СЃ РЅР°
                // С‚СЂР°РІСѓ): Рё РґР»СЏ С„СЂСѓСЃС‚СѓРј-С‚РµСЃС‚Р°, Рё РґР»СЏ РєСѓР»Р»РёРЅРіР° Р±Р°С‚С‡РµР№
                // RenderMeshInstanced (С‚Р°Рј СЂР°РЅСЊС€Рµ Р±С‹Р» Р±РѕРєСЃ РІРѕРєСЂСѓРі РїРѕР·РёС†РёРё С‡Р°РЅРєР°
                // РЅР° СѓСЂРѕРІРЅРµ РјРѕСЂСЏ вЂ” С‚СЂР°РІР° РІРЅРµ РЅРµРіРѕ РїСЂРѕРїР°РґР°Р»Р° В«РїРѕ СѓРіР»Сѓ РєР°РјРµСЂС‹В»).
                Bounds chunkWorldBounds = ChunkWorldBounds(chunk);
                bool chunkInFrustum = true;
                if (decorFrustumPlanes != null)
                {
                    chunkInFrustum = GeometryUtility.TestPlanesAABB(decorFrustumPlanes, chunkWorldBounds);
                }

                for (int i = 0; i < chunk.Decor.Count; i++)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    GroundDecorLayer layer = runtime.Profile;
                    if (distance > layer.MaxDistanceMeters || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    if (!chunkInFrustum && !layer.CastShadows)
                    {
                        continue;
                    }

                    // РЎР»РѕР№ Р±РµР· billboard-РјРµС€Р° (РґРµСЂРµРІСЊСЏ) РІСЃРµРіРґР° РёРґС‘С‚ 3D-РїСѓС‚С‘Рј:                    // РёРЅР°С‡Рµ РµРіРѕ СЂРёСЃРѕРІР°Р»Рѕ Р±С‹ РєР°РјРµСЂРѕ-РѕСЂРёРµРЅС‚РёСЂРѕРІР°РЅРЅС‹Рј Рё РѕРЅ РєСЂСѓС‚РёР»СЃСЏ
                    // Р±С‹ Р·Р° РёРіСЂРѕРєРѕРј.
                    bool near = distance <= layer.NearDistanceMeters || layer.FarBillboardMesh == null;
                    Material material = near ? layer.NearMaterial : layer.FarMaterial;
                    if (material == null)
                    {
                        material = layer.NearMaterial;
                    }

                    if (material == null)
                    {
                        continue;
                    }

                    int count = runtime.Instances.Length;
                    if (layer.PerInstanceDensity && runtime.WorldMatrices.IsCreated
                        && runtime.WorldMatrices.Length == count)
                    {
                        // РњР°С‚СЂРёС†С‹ вЂ” Burst-РґР¶РѕР±С‹: РїР»Р°РЅРёСЂСѓРµРј РІСЃРµ С‡Р°РЅРєРё СЂР°Р·РѕРј Рё
                        // СЃС‡РёС‚Р°РµРј РёС… РџРђР РђР›Р›Р•Р›Р¬РќРћ, РѕР¶РёРґР°РЅРёРµ вЂ” РѕРґРЅРёРј РїСЂРѕС…РѕРґРѕРј.
                        ScheduleBurstDecor(
                            chunk, runtime, layer, chunkMatrix, cameraPosition, near, material, chunkWorldBounds);
                        continue;
                    }

                    if (near)
                    {
                        // Managed-матрицы у травы не аллоцируются (идёт Burst-путь);
                        // сюда попадаем только у слоёв без PerInstanceDensity, но
                        // подстрахуемся, чтобы не поймать null при смене режима.
                        if (runtime.Matrices == null || runtime.Matrices.Length != count)
                        {
                            runtime.Matrices = new Matrix4x4[count];
                        }

                        // Тень слоя в shadow map: трава — только в радиусе
                        // (ShadowCastDistanceMeters), деревья/камни/кактусы — целиком.
                        ShadowCastingMode decorShadow = layer.CastShadows
                            ? ShadowCastingMode.On
                            : ShadowCastingMode.Off;

                        for (int k = 0; k < count; k++)
                        {
                            GroundDecorInstance instance = runtime.Instances[k];
                            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);

                            // РћСЂРёРµРЅС‚Р°С†РёСЏ РїРѕ РќРћР РњРђР›Р РїРѕРІРµСЂС…РЅРѕСЃС‚Рё: Р»РѕРєР°Р»СЊРЅС‹Р№ +Y РјРµС€Р°
                            // (РІРµСЂС… РєР°СЂС‚РѕС‡РєРё/РґРµСЂРµРІР°) СЃРјРѕС‚СЂРёС‚ РІРґРѕР»СЊ СЂР°РґРёР°Р»Рё, yaw вЂ”
                            // РїРѕРІРѕСЂРѕС‚ РІРѕРєСЂСѓРі РЅРµС‘. Р Р°РЅСЊС€Рµ Р±С‹Р» yaw РІРѕРєСЂСѓРі РјРёСЂРѕРІРѕР№ Y:
                            // РЅР° С€РёСЂРѕС‚Р°С… РєР°СЂС‚РѕС‡РєРё Р»РѕР¶РёР»РёСЃСЊ РїР»Р°С€РјСЏ Рё С‚РѕРЅСѓР»Рё РІ Р·РµРјР»Рµ.
                            Vector3 up = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                            if (up.sqrMagnitude < 1e-6f)
                            {
                                up = Vector3.up;
                            }
                            else
                            {
                                up.Normalize();
                            }

                            float fade = DecorBirthFade(instance, DecorPropFadeSeconds);
                            runtime.Matrices[k] = chunkMatrix * DecorLocalMatrix(instance, layer, up, fade);
                        }

                        if (layer.CastShadows && layer.ShadowCastDistanceMeters > 0f
                            && layer.NearMeshes.Length == 1 && layer.NearMeshes[0] != null)
                        {
                            // Р›РёРјРёС‚ РєР°СЃС‚Р° РїРѕ СЂР°РґРёСѓСЃСѓ (С‚СЂР°РІР°): РёРЅСЃС‚Р°РЅСЃС‹ РІ СЂР°РґРёСѓСЃРµ
                            // РёРґСѓС‚ РІ shadow map, РѕСЃС‚Р°Р»СЊРЅС‹Рµ вЂ” С‚РѕР»СЊРєРѕ С„РѕСЂРІР°СЂРґ.
                            EnsureDecorShadowScratch(count);
                            EnsureDecorBlobScratch(count);
                            Vector3 sunDirWS = GetSunDirectionWS();
                            float limitSq = layer.ShadowCastDistanceMeters * layer.ShadowCastDistanceMeters;
                            int castCount = 0;
                            int restCount = 0;
                            for (int k = 0; k < count; k++)
                            {
                                Matrix4x4 grassMatrix = runtime.Matrices[k];
                                Vector3 worldPosition = grassMatrix.GetPosition();
                                if ((worldPosition - cameraPosition).sqrMagnitude <= limitSq)
                                {
                                    decorCastScratch[castCount++] = grassMatrix;
                                    continue;
                                }

                                // Р”Р°Р»СЊС€Рµ СЂР°РґРёСѓСЃР° РЅР°СЃС‚РѕСЏС‰РµРіРѕ РєР°СЃС‚Р° вЂ” РїСЂРѕСЃС‚Р°СЏ С‚РµРЅСЊ:
                                // РїСЏС‚РЅРѕ, СЃРјРµС‰С‘РЅРЅРѕРµ РћРў СЃРѕР»РЅС†Р° (РёРЅР°С‡Рµ СЃРїСЂСЏС‚Р°РЅРѕ РїРѕРґ
                                // СЃР°РјРёРј РєСѓСЃС‚РѕРј). Р’РЅСѓС‚СЂРё СЂР°РґРёСѓСЃР° РїСЏС‚РЅР° РЅРµС‚ вЂ” С‚Р°Рј
                                // СѓР¶Рµ РЅР°СЃС‚РѕСЏС‰Р°СЏ С‚РµРЅСЊ.
                                Vector3 groundUp = grassMatrix.rotation * Vector3.up;
                                float blobScale = runtime.Instances[k].Scale * BlobScaleFactor
                                    * DecorBirthFade(runtime.Instances[k], DecorPropFadeSeconds);
                                Vector3 sunTangent = sunDirWS - (groundUp * Vector3.Dot(sunDirWS, groundUp));
                                if (sunTangent.sqrMagnitude > 1e-6f)
                                {
                                    sunTangent.Normalize();
                                }
                                else
                                {
                                    sunTangent = Vector3.zero;
                                }

                                decorBlobScratch[restCount] = Matrix4x4.TRS(
                                    worldPosition + (groundUp * 0.12f) - (sunTangent * (blobScale * BlobSunOffset)),
                                    grassMatrix.rotation,
                                    new Vector3(blobScale, 1f, blobScale));
                                decorRestScratch[restCount++] = grassMatrix;
                            }

                            if (castCount > 0)
                            {
                                DrawDecorBatches(
                                    layer.NearMeshes[0], material, decorCastScratch, castCount, ShadowCastingMode.On);
                            }

                            if (restCount > 0)
                            {
                                DrawDecorBatches(
                                    layer.NearMeshes[0], material, decorRestScratch, restCount, ShadowCastingMode.Off);
                            }

                            Material blobMat = GetBlobMaterial();
                            if (blobMat != null)
                            {
                                if (!blobDiagLogged)
                                {
                                    blobDiagLogged = true;
                                    Debug.Log(string.Format(
                                        "[Blob] layer={0} count={1} queue={2} instancing={3} shader={4} sunDir=({5:F3},{6:F3},{7:F3}) color={8}",
                                        layer.Name, restCount, blobMat.renderQueue, blobMat.enableInstancing,
                                        blobMat.shader.name, sunDirWS.x, sunDirWS.y, sunDirWS.z, BlobShadowColor));
                                }

                                blobMat.SetColor("_Color", BlobShadowColor);
                                DrawDecorBatches(
                                    GetBlobMesh(), blobMat, decorBlobScratch, restCount, ShadowCastingMode.Off);
                            }
                        }
                        else if (layer.NearMeshes.Length > 1)
                        {
                            EnsureDecorScratch(count);
                            for (int m = 0; m < layer.NearMeshes.Length; m++)
                            {
                                Mesh variant = layer.NearMeshes[m];
                                if (variant == null)
                                {
                                    continue;
                                }

                                int written = 0;
                                for (int k = 0; k < count; k++)
                                {
                                    if (runtime.Instances[k].MeshIndex == m)
                                    {
                                        decorPerMeshScratch[written++] = runtime.Matrices[k];
                                    }
                                }

                                if (written > 0)
                                {
                                    DrawDecorBatches(
                                        variant, material, decorPerMeshScratch, written, decorShadow);
                                }
                            }
                        }
                        else if (layer.NearMeshes[0] != null)
                        {
                            DrawDecorBatches(
                                layer.NearMeshes[0], material, runtime.Matrices, count, decorShadow);
                        }
                    }
                    else
                    {
                        Mesh billboard = layer.FarBillboardMesh != null ? layer.FarBillboardMesh : layer.NearMeshes[0];
                        if (billboard == null)
                        {
                            continue;
                        }

                        // Р‘РёР»Р»Р±РѕСЂРґ РІ РњРР РћР’Р«РҐ РєРѕРѕСЂРґРёРЅР°С‚Р°С…: Р»РѕРєР°Р»СЊРЅС‹Р№ +Y РёРЅСЃС‚Р°РЅСЃР° =
                        // РЅРѕСЂРјР°Р»СЊ РїРѕРІРµСЂС…РЅРѕСЃС‚Рё, +Z = РІР·РіР»СЏРґ РєР°РјРµСЂС‹ РІ РєР°СЃР°С‚РµР»СЊРЅРѕР№
                        // РїР»РѕСЃРєРѕСЃС‚Рё. Р’СЃСЏ РѕСЂРёРµРЅС‚Р°С†РёСЏ Р·РґРµСЃСЊ, С€РµР№РґРµСЂ вЂ” passthrough.
                        EnsureDecorBlobScratch(count);
                        Vector3 sunDirWS = GetSunDirectionWS();
                        for (int k = 0; k < count; k++)
                        {
                            GroundDecorInstance instance = runtime.Instances[k];
                            Vector3 localPosition = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
                            Vector3 localUp = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                            if (localUp.sqrMagnitude < 1e-6f)
                            {
                                localUp = Vector3.up;
                            }
                            else
                            {
                                localUp.Normalize();
                            }

                            Vector3 worldPosition = chunkMatrix.MultiplyPoint3x4(localPosition);
                            Vector3 worldUp = chunkMatrix.MultiplyVector(localUp).normalized;

                            Vector3 facing = Vector3.ProjectOnPlane(cameraPosition - worldPosition, worldUp);
                            if (facing.sqrMagnitude < 1e-6f)
                            {
                                facing = Vector3.ProjectOnPlane(Vector3.forward, worldUp);
                                if (facing.sqrMagnitude < 1e-6f)
                                {
                                    facing = Vector3.ProjectOnPlane(Vector3.right, worldUp);
                                }
                            }

                            // Р”Р°Р»СЊРЅРёРµ Р±РёР»Р»Р±РѕСЂРґС‹ С‡СѓС‚СЊ РєСЂСѓРїРЅРµРµ: РїСЂРё С‚РѕР№ Р¶Рµ
                            // РїР»РѕС‚РЅРѕСЃС‚Рё РёРЅСЃС‚Р°РЅСЃРѕРІ РѕСЃС‚СЂРѕРІР° С‡РёС‚Р°СЋС‚СЃСЏ РїР»РѕС‚РЅРµРµ Рё
                            // РЅРµ В«РєСЂРѕС€Р°С‚СЃСЏВ» РІ С‚РѕС‡РєРё РЅР° РіРѕСЂРёР·РѕРЅС‚Рµ.
                            float farBlend = Mathf.Clamp01(
                                (distance - layer.NearDistanceMeters)
                                / Mathf.Max(1f, layer.MaxDistanceMeters - layer.NearDistanceMeters));
                            float size = instance.Scale * Mathf.Lerp(1.1f, 1.9f, farBlend)
                                * DecorBirthFade(instance, DecorPropFadeSeconds);
                            Vector3 billboardUp = worldUp;

                            // РќР°РєР»РѕРЅ РїРѕ РІРµС‚СЂСѓ Сѓ РґР°Р»СЊРЅРµРіРѕ LOD: Р±РёР»Р»Р±РѕСЂРґ РЅРµ РјРѕР¶РµС‚
                            // РЅР°РєР»РѕРЅРёС‚СЊСЃСЏ В«РІ РіР»СѓР±РёРЅСѓВ», РЅРѕ РІ СЃРІРѕРµР№ РїР»РѕСЃРєРѕСЃС‚Рё
                            // РЅР°РєР»РѕРЅСЏРµС‚СЃСЏ Рє РїСЂРѕРµРєС†РёРё РЅР°РїСЂР°РІР»РµРЅРёСЏ РІРµС‚СЂР° вЂ” РёРЅР°С‡Рµ
                            // Р·РѕРЅС‹ РІРµС‚СЂР° С‡РёС‚Р°Р»РёСЃСЊ Р±С‹ С‚РѕР»СЊРєРѕ РІР±Р»РёР·Рё.
                            if (instance.LeanDegrees > 0.001f && layer.WindZoneFrequency > 0d)
                            {
                                Vector3 leanDir = WindLeanDirection(instance, chunkMatrix);
                                Vector3 facingNormalized = facing.normalized;
                                Vector3 planeLean = leanDir - (facingNormalized * Vector3.Dot(leanDir, facingNormalized));
                                if (planeLean.sqrMagnitude > 1e-8f)
                                {
                                    float leanRad = instance.LeanDegrees * Mathf.Deg2Rad;
                                    billboardUp = ((worldUp * Mathf.Cos(leanRad))
                                        + (planeLean.normalized * Mathf.Sin(leanRad))).normalized;
                                }
                            }

                            Quaternion groundRotation = Quaternion.LookRotation(facing.normalized, worldUp);
                            Quaternion rotation = Quaternion.LookRotation(facing.normalized, billboardUp);

                            // РџСЂРѕСЃС‚Р°СЏ С‚РµРЅСЊ: С‚Рѕ Р¶Рµ СЃРјРµС‰С‘РЅРЅРѕРµ РѕС‚ СЃРѕР»РЅС†Р° РїСЏС‚РЅРѕ, С‡С‚Рѕ
                            // Рё Сѓ Р±Р»РёР¶РЅРµРіРѕ LOD, вЂ” С‚РµРЅСЊ С‚СЂР°РІС‹ Р¶РёРІС‘С‚ РґРѕ MaxDistance.
                            Vector3 sunTangent = sunDirWS - (worldUp * Vector3.Dot(sunDirWS, worldUp));
                            if (sunTangent.sqrMagnitude > 1e-6f)
                            {
                                sunTangent.Normalize();
                            }
                            else
                            {
                                sunTangent = Vector3.zero;
                            }

                            float blobSize = instance.Scale * BlobScaleFactor
                                * DecorBirthFade(instance, DecorPropFadeSeconds);
                            decorBlobScratch[k] = Matrix4x4.TRS(
                                worldPosition + (worldUp * 0.12f) - (sunTangent * (blobSize * BlobSunOffset)),
                                groundRotation,
                                new Vector3(blobSize, 1f, blobSize));

                            // РљРІР°Рґ С†РµРЅС‚СЂРёСЂРѕРІР°РЅ РїРѕ РІС‹СЃРѕС‚Рµ: РїРѕРґРЅРёРјР°РµРј, С‡С‚РѕР±С‹ РѕСЃРЅРѕРІР°РЅРёРµ
                            // СЃС‚РѕСЏР»Рѕ РЅР° Р·РµРјР»Рµ.
                            worldPosition += worldUp * (0.5f * size);
                            runtime.Matrices[k] = Matrix4x4.TRS(
                                worldPosition, rotation, new Vector3(size, size, size));
                        }

                        DrawDecorBatches(billboard, material, runtime.Matrices, count, ShadowCastingMode.Off);

                        Material blobMatFar = GetBlobMaterial();
                        if (blobMatFar != null)
                        {
                            blobMatFar.SetColor("_Color", BlobShadowColor);
                            DrawDecorBatches(
                                GetBlobMesh(), blobMatFar, decorBlobScratch, count, ShadowCastingMode.Off);
                        }
                    }
                }
            }

            FlushBurstDecor();
        }

        private void EnsureDecorScratch(int size)
        {
            if (decorPerMeshScratch == null || decorPerMeshScratch.Length < size)
            {
                decorPerMeshScratch = new Matrix4x4[size];
            }
        }

        private void EnsureDecorShadowScratch(int size)
        {
            if (decorCastScratch == null || decorCastScratch.Length < size)
            {
                decorCastScratch = new Matrix4x4[size];
            }

            if (decorRestScratch == null || decorRestScratch.Length < size)
            {
                decorRestScratch = new Matrix4x4[size];
            }
        }

        private void EnsureDecorBlobScratch(int size)
        {
            if (decorBlobScratch == null || decorBlobScratch.Length < size)
            {
                decorBlobScratch = new Matrix4x4[size];
            }
        }

        /// <summary>РќР°РїСЂР°РІР»РµРЅРёРµ РќРђ СЃРѕР»РЅС†Рµ РёР· РіР»РѕР±Р°Р»РѕРІ (SunBillboard/SkyEnvironment).</summary>
        private static Vector3 GetSunDirectionWS()
        {
            Vector3 sun = Shader.GetGlobalVector("_TerrainSunDir");
            if (sun.sqrMagnitude < 1e-6f)
            {
                sun = Shader.GetGlobalVector("_SkySunDir");
            }

            return sun;
        }

        /// <summary>РўРѕС‡РєР° РЅР° Р Р•РќР”Р•Р -РјРµС€Рµ С‡Р°РЅРєР° РїРѕРґ (u,v) РєР°РЅРґРёРґР°С‚Р°: РёРЅС‚РµСЂРїРѕР»СЏС†РёСЏ
        /// РїРѕР·РёС†РёР№ РІРµСЂС€РёРЅ СЂРѕРІРЅРѕ РїРѕ С‚СЂРµСѓРіРѕР»СЊРЅРёРєР°Рј BuildChunk (a,c,b / b,c,d).</summary>
        private static Vector3 MeshSurfacePoint(Vector3[] meshVertices, int coreN, double gridU, double gridV)
        {
            gridU = System.Math.Max(0d, System.Math.Min(gridU, coreN - 1d));
            gridV = System.Math.Max(0d, System.Math.Min(gridV, coreN - 1d));
            int uIndex = System.Math.Min((int)System.Math.Floor(gridU), coreN - 2);
            int vIndex = System.Math.Min((int)System.Math.Floor(gridV), coreN - 2);
            float fu = (float)(gridU - uIndex);
            float fv = (float)(gridV - vIndex);

            Vector3 p00 = meshVertices[(uIndex * coreN) + vIndex];
            Vector3 p10 = meshVertices[((uIndex + 1) * coreN) + vIndex];
            Vector3 p01 = meshVertices[(uIndex * coreN) + vIndex + 1];
            Vector3 p11 = meshVertices[((uIndex + 1) * coreN) + vIndex + 1];

            if (fu + fv <= 1f)
            {
                return p00 + (fu * (p10 - p00)) + (fv * (p01 - p00));
            }

            return p11 + ((1f - fu) * (p01 - p11)) + ((1f - fv) * (p10 - p11));
        }

        /// <summary>РќРћР”: stride РґР»СЏ РѕР±С…РѕРґР°-РїРµСЂРµСЃС‚Р°РЅРѕРІРєРё РѕР±СЏР·Р°РЅ Р±С‹С‚СЊ РІР·Р°РёРјРЅРѕ РїСЂРѕСЃС‚ СЃ count.</summary>
        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }

            return a < 0 ? -a : a;
        }

        /// <summary>РљРІР°Рґ РІ XZ-РїР»РѕСЃРєРѕСЃС‚Рё (С†РµРЅС‚СЂ РІ РЅР°С‡Р°Р»Рµ): РёРЅСЃС‚Р°РЅСЃ-РјР°С‚СЂРёС†Р° РєР»Р°РґС‘С‚
        /// РµРіРѕ РїР»Р°С€РјСЏ РЅР° РїРѕРІРµСЂС…РЅРѕСЃС‚СЊ вЂ” Р»РѕРєР°Р»СЊРЅС‹Р№ +Y РјРµС€Р° = РЅРѕСЂРјР°Р»СЊ.</summary>
        private Mesh GetBlobMesh()
        {
            if (blobMesh == null)
            {
                blobMesh = new Mesh { name = "GroundDecorBlobQuad" };
                blobMesh.vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f),
                    new Vector3(0.5f, 0f, 0.5f)
                };
                blobMesh.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                };
                blobMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
                blobMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                blobMesh.RecalculateBounds();
            }

            return blobMesh;
        }

        private Material GetBlobMaterial()
        {
            if (blobMaterial == null)
            {
                Shader shader = Shader.Find("Galilego/GroundDecorBlob");
                if (shader == null)
                {
                    if (!blobDiagLogged)
                    {
                        blobDiagLogged = true;
                        Debug.LogWarning("[Blob] С€РµР№РґРµСЂ Galilego/GroundDecorBlob РЅРµ РЅР°Р№РґРµРЅ вЂ” РїСЂРѕСЃС‚РѕР№ С‚РµРЅРё РЅРµ Р±СѓРґРµС‚.");
                    }

                    return null;
                }

                blobMaterial = new Material(shader);
                blobMaterial.enableInstancing = true;
            }

            return blobMaterial;
        }

        /// <summary>РћРґРЅР° РѕС‚Р»РѕР¶РµРЅРЅР°СЏ РѕС‚СЂРёСЃРѕРІРєР° СЃР»РѕСЏ СЃ PerInstanceDensity: РґР¶РѕР±Р°
        /// РјР°С‚СЂРёС† РїР»Р°РЅРёСЂСѓРµС‚СЃСЏ СЃСЂР°Р·Сѓ, РѕР¶РёРґР°РЅРёРµ Рё RenderMeshInstanced вЂ” РѕР±С‰РёРј
        /// РїСЂРѕС…РѕРґРѕРј, РїРѕСЌС‚РѕРјСѓ С‡Р°РЅРєРё СЃС‡РёС‚Р°СЋС‚СЃСЏ РїР°СЂР°Р»Р»РµР»СЊРЅРѕ РЅР° РІСЃРµС… РІРѕСЂРєРµСЂР°С….</summary>
        private struct BurstDecorDraw
        {
            public Chunk Chunk;
            public DecorLayerRuntime Runtime;
            public Mesh Mesh;
            public Material Material;
            public ShadowCastingMode Shadow;
            public Bounds WorldBounds;
            public JobHandle Handle;
        }

        /// <summary>РџР»Р°РЅРёСЂСѓРµС‚ Burst-РґР¶РѕР±Сѓ РјР°С‚СЂРёС† СЃР»РѕСЏ (Р±РµР· РѕР¶РёРґР°РЅРёСЏ).</summary>
        private void ScheduleBurstDecor(
            Chunk chunk, DecorLayerRuntime runtime, GroundDecorLayer layer,
            Matrix4x4 chunkMatrix, Vector3 cameraPosition, bool near, Material material,
            Bounds worldBounds)
        {
            int count = runtime.Instances.Length;

            Mesh mesh = near
                ? layer.NearMeshes[0]
                : (layer.FarBillboardMesh != null ? layer.FarBillboardMesh : layer.NearMeshes[0]);
            if (mesh == null)
            {
                return;
            }

            // РџРёРІРѕС‚ РјРµС€Р°: Сѓ РєР»РёРЅРєР° С‚СЂР°РІС‹ РѕРЅ РІ РѕСЃРЅРѕРІР°РЅРёРё (0), Сѓ С†РµРЅС‚СЂРёСЂРѕРІР°РЅРЅРѕРіРѕ
            // РєРІР°РґР° вЂ” 0.5. Р‘РёР»Р»Р±РѕСЂРґ РїРѕРґРЅРёРјР°РµРј РЅР° СЌС‚Сѓ РґРѕР»СЋ РІС‹СЃРѕС‚С‹, РёРЅР°С‡Рµ РґР°Р»СЊРЅРёР№
            // LOD СЃ РїРёРІРѕС‚РѕРј РІ РѕСЃРЅРѕРІР°РЅРёРё РІРёСЃРµР» Р±С‹ РЅР°Рґ Р·РµРјР»С‘Р№.
            Bounds meshBounds = mesh.bounds;
            float meshHeight = Mathf.Max(1e-4f, meshBounds.size.y);
            float pivotFraction = Mathf.Clamp01(-meshBounds.min.y / meshHeight);

            var job = new GroundDecorMatrixJob
            {
                Instances = runtime.Instances,
                WorldMatrices = runtime.WorldMatrices,
                ChunkToWorld = ToFloat4x4(chunkMatrix),
                CameraWorld = new float3(cameraPosition.x, cameraPosition.y, cameraPosition.z),
                Billboard = !near,
                FlatOnGround = layer.FlatOnGround,
                NearDistance = layer.NearDistanceMeters,
                FarDistance = Mathf.Max(layer.NearDistanceMeters + 1f, layer.MaxDistanceMeters),
                BillboardNearScale = 1f,
                BillboardFarScale = 1.9f,
                BillboardPivotFraction = pivotFraction,
                Now = Time.time,
                FadeSeconds = DecorBladeFadeSeconds
            };

            ShadowCastingMode shadow = near && layer.CastShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;

            burstDecorDraws.Add(new BurstDecorDraw
            {
                Chunk = chunk,
                Runtime = runtime,
                Mesh = mesh,
                Material = material,
                Shadow = shadow,
                WorldBounds = worldBounds,
                Handle = job.Schedule(count, 256, default(JobHandle))
            });
        }

        /// <summary>Р”РѕР¶РёРґР°РµС‚СЃСЏ РІСЃРµС… Р·Р°РїР»Р°РЅРёСЂРѕРІР°РЅРЅС‹С… РґР¶РѕР± Рё СЂРёСЃСѓРµС‚ СЃР»РѕРё.</summary>
        private void FlushBurstDecor()
        {
            if (burstDecorDraws.Count == 0)
            {
                return;
            }

            for (int i = 0; i < burstDecorDraws.Count; i++)
            {
                BurstDecorDraw draw = burstDecorDraws[i];
                draw.Handle.Complete();
            }

            for (int i = 0; i < burstDecorDraws.Count; i++)
            {
                BurstDecorDraw draw = burstDecorDraws[i];
                DrawDecorInstanced(
                    draw.Mesh, draw.Material, draw.Runtime.WorldMatrices,
                    draw.Runtime.Instances.Length, draw.Shadow, draw.WorldBounds);
            }

            burstDecorDraws.Clear();
        }

        private void DrawDecorInstanced(
            Mesh mesh, Material material, NativeArray<Matrix4x4> matrices, int count,
            ShadowCastingMode shadowMode, Bounds worldBounds)
        {
            if (!material.enableInstancing)
            {
                material.enableInstancing = true;
            }

            const int batchSize = 1023;
            // Р“СЂР°РЅРёС†С‹ вЂ” С„Р°РєС‚РёС‡РµСЃРєРёРµ (СЂРµР»СЊРµС„ + Р·Р°РїР°СЃ): Unity РєСѓР»Р»РёС‚ Р±Р°С‚С‡ РїРѕ РЅРёРј,
            // Рё Р±РѕРєСЃ РІРѕРєСЂСѓРі РїРѕР·РёС†РёРё С‡Р°РЅРєР° РЅР° СѓСЂРѕРІРЅРµ РјРѕСЂСЏ СЂРѕРЅСЏР» С‚СЂР°РІСѓ С†РµР»РёРєРѕРј
            // РЅР° РІРѕР·РІС‹С€РµРЅРЅРѕСЃС‚СЏС… РїСЂРё РѕРїСЂРµРґРµР»С‘РЅРЅРѕРј СѓРіР»Рµ РєР°РјРµСЂС‹.
            RenderParams renderParams = new RenderParams(material)
            {
                worldBounds = worldBounds,
                shadowCastingMode = shadowMode,
                receiveShadows = false,
                layer = gameObject.layer
            };

            for (int start = 0; start < count; start += batchSize)
            {
                int n = System.Math.Min(batchSize, count - start);
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Graphics.RenderMeshInstanced(renderParams, mesh, sub, matrices, n, start);
                }
            }
        }

        private static float4x4 ToFloat4x4(Matrix4x4 m)
        {
            return new float4x4(
                new float4(m.m00, m.m10, m.m20, m.m30),
                new float4(m.m01, m.m11, m.m21, m.m31),
                new float4(m.m02, m.m12, m.m22, m.m32),
                new float4(m.m03, m.m13, m.m23, m.m33));
        }

        private void DrawDecorBatches(
            Mesh mesh, Material material, Matrix4x4[] matrices, int count,
            ShadowCastingMode shadowMode)
        {
            const int batchSize = 1023;
            if (!material.enableInstancing)
            {
                // DrawMeshInstanced С‚СЂРµР±СѓРµС‚ РІРєР»СЋС‡С‘РЅРЅРѕРіРѕ РёРЅСЃС‚Р°РЅСЃРёРЅРіР° Сѓ РјР°С‚РµСЂРёР°Р»Р°.
                material.enableInstancing = true;
            }

            int start = 0;
            while (start < count)
            {
                int n = System.Math.Min(batchSize, count - start);
                Matrix4x4[] batch = matrices;
                if (start > 0)
                {
                    if (decorBatchScratch == null || decorBatchScratch.Length < batchSize)
                    {
                        decorBatchScratch = new Matrix4x4[batchSize];
                    }

                    System.Array.Copy(matrices, start, decorBatchScratch, 0, n);
                    batch = decorBatchScratch;
                }

                // Р’СЃРµ СЃР°Р±РјРµС€Рё: Сѓ FBX СЃС‚РІРѕР» Рё Р»РёСЃС‚РІР° РјРѕРіСѓС‚ Р±С‹С‚СЊ РѕРґРЅРёРј РјРµС€РµРј
                // СЃ РґРІСѓРјСЏ РјР°С‚РµСЂРёР°Р»Р°РјРё, РёРЅР°С‡Рµ РґРµСЂРµРІРѕ СЂРёСЃСѓРµС‚СЃСЏ Р±РµР· РєСЂРѕРЅС‹.
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Graphics.DrawMeshInstanced(
                        mesh, sub, material, batch, n, null,
                        shadowMode, false, gameObject.layer);
                }

                start += n;
            }
        }

        private void EvictIfNeeded()
        {
            if (chunks.Count <= MaxCachedChunks)
            {
                return;
            }

            toEvict.Clear();
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (!keep.Contains(kv.Key))
                {
                    toEvict.Add(kv.Key);
                }
            }

            for (int i = 0; i < toEvict.Count && chunks.Count > MaxCachedChunks; i++)
            {
                long id = toEvict[i];
                Chunk chunk = chunks[id];
                DisposeDecor(chunk);
                if (chunk.Go != null)
                {
                    Destroy(chunk.Go);
                }

                chunks.Remove(id);
            }
        }

        private void SetAllInvisible()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Visible)
                {
                    kv.Value.Go.SetActive(false);
                    kv.Value.Visible = false;
                }
            }
        }

        private Material GetMaterial()
        {
            if (materialCache == null)
            {
                Shader shader = Shader.Find("Galilego/PlanetSurface");
                materialCache = new Material(shader);
                ApplyTerrainGlobals();
            }

            return materialCache;
        }

        /// <summary>
        /// РџР°СЂР°РјРµС‚СЂС‹ per-pixel РїР°Р»РёС‚СЂС‹/С€СѓРјР°/С‚РµРєСЃС‚СѓСЂ СЂРµР»СЊРµС„Р° вЂ” РІ Р“Р›РћР‘РђР›Р« С€РµР№РґРµСЂР°,
        /// Р° РЅРµ РІ РјР°С‚РµСЂРёР°Р»: РїРµСЂ-РјР°С‚РµСЂРёР°Р»СЊРЅС‹Рµ Р·РЅР°С‡РµРЅРёСЏ, РЅРµ РѕР±СЉСЏРІР»РµРЅРЅС‹Рµ РІ Properties,
        /// РЅРµ РґРѕРµР·Р¶Р°Р»Рё РґРѕ РјР°С‚РµСЂРёР°Р»Р° (РІ СЃРІРµР¶РёС… СЃРµСЃСЃРёСЏС… РјР°С‚РµСЂРёР°Р» РѕРєР°Р·С‹РІР°Р»СЃСЏ СЃ РЅСѓР»СЏРјРё,
        /// `_SteepBlendStart == _SteepBlendEnd == 0` в†’ rock-С‚РµРєСЃС‚СѓСЂР° РЅР° РІСЃРµР№ Р·РµРјР»Рµ,
        /// СЂРµР»СЊРµС„ С‡С‘СЂРЅС‹Р№). Р“Р»РѕР±Р°Р»С‹ СЃС‚Р°РІРёС‚ РґРѕРјРёРЅР°РЅС‚РЅРѕРµ С‚РµР»Рѕ РєР°Р¶РґС‹Р№ РєР°РґСЂ, СЃРѕСЃС‚РѕСЏРЅРёРµ
        /// РјР°С‚РµСЂРёР°Р»Р° СЂРѕР»Рё РЅРµ РёРіСЂР°РµС‚. Р¦РІРµС‚Р° вЂ” float4: РѕРЅРё СѓР¶Рµ Р»РёРЅРµР№РЅС‹Рµ.
        /// </summary>
        private void ApplyTerrainGlobals()
        {
            TerrainPaletteData palette = terrain.Palette ?? new TerrainPaletteData();
            Shader.SetGlobalFloat("_TerrainAmplitude", (float)System.Math.Max(1d, terrain.AmplitudeMeters));
            Shader.SetGlobalFloat("_TerrainSeaLevel", (float)System.Math.Max(terrain.SeaLevelMeters, -1e30d));
            Shader.SetGlobalFloat("_BeachHeightMeters", (float)System.Math.Max(0d, terrain.BeachHeightMeters));
            Shader.SetGlobalFloat("_TerrainSeed", terrain.Seed);
            Shader.SetGlobalFloat("_TerrainGain", (float)TerrainNoise.EffectiveGain(terrain.Gain));
            Shader.SetGlobalFloat("_TerrainLacunarity", (float)TerrainNoise.EffectiveLacunarity(terrain.Lacunarity));
            Shader.SetGlobalFloat("_TerrainRockSlopeTan", (float)terrain.ColorRockSlopeTan);
            Shader.SetGlobalFloat("_TerrainRockSlopeWidth", (float)terrain.ColorRockSlopeWidth);
            Shader.SetGlobalFloat("_TerrainRockHeightMin", (float)terrain.ColorRockHeightMin);
            Shader.SetGlobalFloat("_TerrainSnowSlopeTan", (float)terrain.ColorSnowSlopeTan);
            Shader.SetGlobalFloat("_ColorNoiseFrequency", (float)terrain.ColorNoiseFrequency);
            Shader.SetGlobalFloat("_ColorNoiseOctaves", terrain.ColorNoiseOctaves);
            Shader.SetGlobalFloat("_ColorNoiseStrength", (float)terrain.ColorNoiseStrength);
            Shader.SetGlobalFloat("_ColorDetailFrequency", (float)terrain.ColorDetailFrequency);
            Shader.SetGlobalFloat("_ColorDetailOctaves", terrain.ColorDetailOctaves);
            Shader.SetGlobalFloat("_ColorDetailStrength", (float)terrain.ColorDetailStrength);

            Shader.SetGlobalVector("_ColSand", ToVec(palette.SandLinear));
            Shader.SetGlobalVector("_ColDesert", ToVec(palette.DesertLinear));
            Shader.SetGlobalVector("_ColDryGrass", ToVec(palette.DryGrassLinear));
            Shader.SetGlobalVector("_ColGrass", ToVec(palette.GrassLinear));
            Shader.SetGlobalVector("_ColForest", ToVec(palette.ForestLinear));
            Shader.SetGlobalVector("_ColTundra", ToVec(palette.TundraLinear));
            Shader.SetGlobalVector("_ColRock", ToVec(palette.RockLinear));
            Shader.SetGlobalVector("_ColSnow", ToVec(palette.SnowLinear));
            Shader.SetGlobalVector("_ColSea", ToVec(palette.SeaLinear));
            Shader.SetGlobalVector("_ColSoil", ToVec(palette.SoilLinear));
            Shader.SetGlobalVector("_ColLush", ToVec(palette.LushLinear));

            // РўРµРєСЃС‚СѓСЂС‹ СЂРµР»СЊРµС„Р°: С‚СЂРёРїР»Р°РЅР°СЂРЅС‹Р№ Р±Р»РµРЅРґРёРЅРі РїРѕ РІС‹СЃРѕС‚Рµ
            // Рё СЃРєР»РѕРЅСѓ. Р’СЃРµ С‡РµС‚С‹СЂРµ Р°Р»СЊР±РµРґРѕ РѕР±СЏР·Р°С‚РµР»СЊРЅС‹, РёРЅР°С‡Рµ вЂ” РїСЂРѕС†РµРґСѓСЂРЅР°СЏ РїР°Р»РёС‚СЂР°.
            Texture2D texLow = terrain.TextureLow;
            Texture2D texMid = terrain.TextureMid;
            Texture2D texHigh = terrain.TextureHigh;
            Texture2D texSteep = terrain.TextureSteep;
            bool useTextures = texLow != null && texMid != null && texHigh != null && texSteep != null;
            Shader.SetGlobalFloat("_TerrainUseTextures", useTextures ? 1f : 0f);
            if (useTextures)
            {
                Shader.SetGlobalTexture("_TexLow", texLow);
                Shader.SetGlobalTexture("_TexMid", texMid);
                Shader.SetGlobalTexture("_TexHigh", texHigh);
                Shader.SetGlobalTexture("_TexSteep", texSteep);
                Shader.SetGlobalTexture("_TexOcclusion", terrain.TextureOcclusion != null ? terrain.TextureOcclusion : Texture2D.whiteTexture);
                Shader.SetGlobalFloat("_TerrainTextureScale", (float)terrain.TextureScale);
                Shader.SetGlobalFloat("_LowMidBlendStart", (float)terrain.LowMidBlendStart);
                Shader.SetGlobalFloat("_LowMidBlendEnd", (float)terrain.LowMidBlendEnd);
                Shader.SetGlobalFloat("_MidHighBlendStart", (float)terrain.MidHighBlendStart);
                Shader.SetGlobalFloat("_MidHighBlendEnd", (float)terrain.MidHighBlendEnd);
                Shader.SetGlobalFloat("_SteepBlendStart", (float)terrain.SteepBlendStart);
                Shader.SetGlobalFloat("_SteepBlendEnd", (float)terrain.SteepBlendEnd);
            }

            colorStateCache = CaptureColorState();
        }

        private static Vector4 ToVec(Color c)
        {
            return new Vector4(c.r, c.g, c.b, c.a);
        }

        private static long NodeId(int face, int depth, int ix, int iy)
        {
            return (long)face
                | ((long)depth << 3)
                | ((long)(ix & 0xFFFFF) << 8)
                | ((long)(iy & 0xFFFFF) << 28);
        }

        private static long NodeId(Node node)
        {
            return NodeId(node.Face, node.Depth, node.Ix, node.Iy);
        }
    }
}
