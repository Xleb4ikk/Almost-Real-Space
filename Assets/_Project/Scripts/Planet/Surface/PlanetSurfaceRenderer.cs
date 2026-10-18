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
    /// Planetary surface renderer: cube-sphere with quadtree LOD.
    ///
    /// Geometry: 6 cube faces → unit directions (CubeSphere) → height/mask from a
    /// SINGLE TerrainNoise (same Burst job as physics) → body-fixed positions
    /// (Radius + h). Chunks are children of a common root that inherits the body
    /// transform (BodyView) but has a compensated scale, so vertices live in
    /// metres from the centre.
    ///
    /// LOD: a node splits while camera distance < node size × SplitFactor
    /// (and depth < MaxDepth). Built meshes are cached by node id (terrain is
    /// static in the body-fixed frame); the visible set comes from back-face
    /// culling. Skirts along the edges hide cracks between different LODs.
    /// Ocean is two layers: chunk geometry goes down to the RAW seabed (no clamping; the
    /// shader darkens it by depth), and a separate near-opaque water mesh sits on top
    /// (Galilego/WaterSurface: waves, sun glint, foam, underwater tint). Seabed/depth
    /// physics: WaterQuery. LateUpdate runs after BodyView.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-70)]
    public sealed class PlanetSurfaceRenderer : MonoBehaviour
    {
        // ===== [GRAPHICS] Terrain quality and draw distance =====
        // TileResolution / MaxDepth / SplitFactor / BuildsPerFrame / MaxNodes /
        // MaxCachedChunks is a candidate for future graphics presets (min to max quality).
        // Draw distance is set by the camera (FirstPersonCamera.UpdateClipPlanes, physical
        // horizon with margin for peaks); terrain here is built over the whole visible
        // hemisphere with no altitude threshold. Frame antialiasing (SMAA/TAA) is set on the
        // Main Camera in the scene.
        // OutdoorsScene: HDAdditionalCameraData (antialiasing / SMAAQuality / TAA*).
        // Search by tag: [GRAPHICS].

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

        /// <summary>
        /// Water skirt depth as a fraction of the TERRAIN skirt. Must be SMALL:
        /// this is required by transparency, not just caution.
        ///
        /// The skirt is a vertical wall down from the chunk edge (see
        /// SurfaceChunkBuilder: Position = core - inward*WaterSkirtDepth) with the
        /// normal taken from the core boundary, so it reads not as a wall but as a
        /// horizontal cut that looks like a hole in the terrain.
        ///
        /// Water is transparent: at Extinction 0.08 about 30 m is visible
        /// (e^(-0.08*30) ≈ 0.09), so a longer skirt reads as a cut going underwater.
        ///
        /// The water plane is FLAT, unlike terrain, so its skirt only hides the
        /// tessellation gap between LODs — a thin horizontal strip. Metres are enough.
        /// </summary>
        [Tooltip("Глубина юбок ВОДЫ, доля от юбки РЕЛЬЕФА. Держите МАЛЕНЬКИМ (0.02-0.06): вода прозрачна, и при Extinction 0.08 видно ~30 м вглубь, поэтому юбка длиннее этого читается как вертикальный срез под водой. 1 = глубина рельефа, неверно. 0 = полоса вернётся.")]
        [Range(0f, 0.3f)]
        public float WaterSkirtFactor = 0.03f;

        [Tooltip("Ширина градиента «мокрый песок → сухой песок» над уровнем моря (м). " +
                 "У самого уреза цвет гаснет и уходит в холодный (красный первым), выше — " +
                 "плавно к обычному. Полоса ограничена пляжной зоной (_BeachHeightMeters), " +
                 "так что мокрым остаётся только низ берега, а не весь пляж. " +
                 "Геометрию, уровень моря и воду не трогает — только вид суши у кромки.")]
        [Range(0.5f, 20f)]
        public float ShoreWetMeters = 3f;

        [Tooltip("Цвет множителя мокрого песка у кромки: темнее и холоднее сухого. " +
                 "У старого плоского затемнения 45 % был одинаковый по всем каналам — " +
                 "получалось просто «темнее», без перехода.")]
        public Color ShoreWetTint = new Color(0.42f, 0.52f, 0.56f, 1f);

        [Tooltip("РЎРєРѕР»СЊРєРѕ С‡Р°РЅРєРѕРІ СЃС‚СЂРѕРёС‚СЊ Р·Р° РєР°РґСЂ (РїРµСЂРІС‹Р№ РєР°РґСЂ вЂ” Р±РµР· Р»РёРјРёС‚Р°).")]
        [Range(1, 64)]
        public int BuildsPerFrame = 16;

        [Tooltip("РЎРєРѕР»СЊРєРѕ СЃР±РѕСЂРѕРє СЃР»РѕС‘РІ РґРµРєРѕСЂР° Р·Р° РєР°РґСЂ (Р»РµРЅРёРІР°СЏ РіРµРЅРµСЂР°С†РёСЏ: Р±Р»РёР¶РЅРёРµ РїРµСЂРІС‹РјРё).")]
        [Range(1, 16)]
        public int DecorBuildsPerFrame = 8;

        [Tooltip("Р‘СЋРґР¶РµС‚ РіР»Р°РІРЅРѕРіРѕ РїРѕС‚РѕРєР° РЅР° С€Р°Рі Р°СЃРёРЅС…СЂРѕРЅРЅС‹С… СЃР±РѕСЂРѕРє РґРµРєРѕСЂР° (РјСЃ/РєР°РґСЂ): " +
                 "С‚СЏР¶С‘Р»С‹Рµ СЃР±РѕСЂРєРё РёРґСѓС‚ РїРѕСЂС†РёСЏРјРё Рё РЅРµ Р±Р»РѕРєРёСЂСѓСЋС‚ РєР°РґСЂ С†РµР»РёРєРѕРј.")]
        [Range(0.5f, 8f)]
        public float DecorBuildBudgetMs = 4f;

        // [PHASE 4] Pause after a full scan that found nothing.
        //
        //
        // The pause is deliberately short: within it the camera can reach a new chunk,
        // so decor still appears without visible delay.
        [Tooltip("How long to stop re-scanning after a full scan found nothing to build.")]
        [Range(0.02f, 2f)]
        public float DecorRescanCooldownSeconds = 0.2f;

        private float decorScanBlockedUntil;

        [Tooltip("РњР°РєСЃРёРјСѓРј РѕРґРЅРѕРІСЂРµРјРµРЅРЅС‹С… Р°СЃРёРЅС…СЂРѕРЅРЅС‹С… СЃР±РѕСЂРѕРє РґРµРєРѕСЂР° (РїР°РјСЏС‚СЊ/Р»Р°С‚РµРЅС‚РЅРѕСЃС‚СЊ).")]
        [Range(1, 32)]
        public int MaxDecorBuildSessions = 8;

        [Header("Р”РёР°РіРЅРѕСЃС‚РёРєР° РґРµРєРѕСЂР°")]
        [Tooltip("Р Р°Р· РІ СЃРµРєСѓРЅРґСѓ РїРёСЃР°С‚СЊ РІ Р»РѕРі СЂР°РґРёСѓСЃ РѕР±СЂРµР·РєРё Рё С‡РёСЃР»Рѕ С‚СЂР°РІРёРЅРѕРє РїСѓР»Р° " +
                 "РґР»СЏ С‡Р°РЅРєР° Сѓ РёРіСЂРѕРєР° вЂ” РїРѕ РЅРµРјСѓ РІРёРґРЅРѕ, РєСѓРґР° СЃРµР»Р° РіСЂР°РЅРёС†Р°.")]
        public bool LogDecorAllocation;

        [Tooltip("РћРіСЂР°РЅРёС‡РёС‚РµР»СЊ Р°РєС‚РёРІРЅС‹С… СѓР·Р»РѕРІ (Р·Р°С‰РёС‚Р° РѕС‚ Р»Р°РІРёРЅС‹).")]
        [Min(64)]
        public int MaxNodes = 6000;
        [Tooltip("РњР°РєСЃРёРјСѓРј Р·Р°РєСЌС€РёСЂРѕРІР°РЅРЅС‹С… РјРµС€РµР№ (Р»РёС€РЅРёРµ РІС‹С‚РµСЃРЅСЏСЋС‚СЃСЏ).")]
        [Min(64)]
        public int MaxCachedChunks = 1024;

        [Tooltip("Сколько чанков выгружать за кадр. Destroy пачкой — сам по себе " +
                 "фриз: освобождение меша и GameObject синхронно, и 40 таких за " +
                 "кадр неотличимы от постройки 40 новых. По 2-4 в кадр кэш " +
                 "подчищается за доли секунды и не видно.")]
        [Range(1, 64)]
        public int MaxEvictionsPerFrame = 4;

        [Tooltip("На сколько верхних уровней глубины рельеф оставляет тени. " +
                 "3 = тени только у самых ближних чанков (MaxDepth-2…MaxDepth). " +
                 "Дальше флаг лишь экономит отсечение в проходах теней: за " +
                 "пределом тени в HDRP эти чанки всё равно не отбрасываются по " +
                 "результату, а попадают в тест. 99 = тени у всех чанков, как " +
                 "было до этого флага.")]
        [Range(0, 99)]
        public int TerrainShadowDepthBand = 3;

        [Header("Фаза 2: асинхронная постройка чанков")]
        [Tooltip("Строить чанки в Burst-джобах, а не на главном потоке. " +
                 "В Editor-замере даёт 0.9-1.2 мс на кадр против 42.75 мс у " +
                 "синхронного пути. Изначально был выключен, потому что очередь " +
                 "не дренировалась (3400-5200 записей при 6 джобах в полёте и " +
                 "бюджете 1.5 мс) и наблюдались кадры, где видимых чанков 0 — " +
                 "то есть рельеф пропадал. С тех пор добавлены Фаза 3 (тиры " +
                 "качества по скорости), префетч вперёд по вектору скорости и " +
                 "сброс устаревших записей; в OutdoorsScene флаг включён. " +
                 "Следить за SurfacePerf.QueuedBuilds и VisibleChunks: очередь " +
                 "не должна расти, а дыр в видимых чанках быть не должно. " +
                 "Старый синхронный путь оставлен как эталон: с ним картинку " +
                 "можно сравнить побитово.")]
        public bool AsyncChunkBuild = true;

        [Tooltip("Сколько джоб постройки может висеть одновременно. Больше — " +
                 "ровнее сглаживание, но больше одновременной нагрузки на " +
                 "процессор и больше живой нативной памяти.")]
        [Range(1, 16)]
        public int MaxChunkJobsInFlight = 12;

        [Tooltip("Мс на кадр, которые можно потратить на ЗАЛИВКУ готовых " +
                 "чанков в меши. Заливка копирует память без вычислений и " +
                 "стоит порядка сотни микросекунд на чанк, поэтому 1.5 мс — " +
                 "это единицы чанков. Всё, что не уложилось, ждёт следующего " +
                 "кадра: лучше отстать от LOD, чем встать.")]
        [Range(0.25f, 8f)]
        public float ChunkFinalizeBudgetMs = 3f;

        [Tooltip("Сколько кадров запись очереди может ждать, прежде чем будет " +
                 "признана устаревшей и выброшена. Узел, ушедший за спину " +
                 "камере, в desired больше не попадает, но из очереди сам не " +
                 "исчезал: на 2 км/с она копила тысячи записей, и планировщик " +
                 "каждый кадр ходил по ним. Не ноль: узел на грани LOD бывает в " +
                 "desired через кадр.")]
        [Range(1, 64)]
        public int MaxChunkQueueAgeFrames = 6;

        [Header("Фаза 3: тиры качества по скорости")]
        [Tooltip("Понижать глубину LOD и плотность разбиения на большой " +
                 "скорости. На 2 км/с строить листья по 440 м бессмысленно: " +
                 "игрок проезжает поле быстрее, чем оно достраивается. " +
                 "Понижение НЕ трогает ResolutionForDepth — он остаётся на " +
                 "сериализованном MaxDepth, иначе у уже построенных узлов " +
                 "менялась бы сетка и они перестраивались бы каждый кадр.")]
        public bool EnableQualityTiers = true;

        [Tooltip("М/с, выше которых включается тир Fast (глубина минус один). " +
                 "Порог намеренно высокой (1 км/с, а не 250): смена тира меняет " +
                 "EffectiveMaxDepth, а декор привязан к узлу чанка, поэтому при " +
                 "возврате в Normal все видимые пулы становятся «чужими» для новых " +
                 "узлов и декор пропадает целиком, а потом вырастает заново. " +
                 "Замер в Editor: при 250 м/с переключение Normal→Fast→Normal " +
                 "роняло DecorPools со 214 до 76 и возвращало к 214. Ниже " +
                 "1 км/с LOD и так успевает, поэтому тир там не нужен.")]
        public float TierFastSpeed = 1000f;

        [Tooltip("М/с, выше которых включается тир Extreme (глубина минус два).")]
        public float TierExtremeSpeed = 1200f;

        [Tooltip("С, сколько ждать перед ВНИЗ возвратом тира. Вверх переключаемся " +
                 "сразу, вниз — с задержкой: иначе скорость шла бы около порога " +
                 "и LOD флапал каждый кадр.")]
        public float TierDownDelaySeconds = 1.5f;

        [Tooltip("На сколько уровней глубины срезать в тире Fast.")]
        public int TierDepthDropFast = 1;

        [Tooltip("На сколько уровней глубины срезать в тире Extreme.")]
        public int TierDepthDropExtreme = 2;

        [Tooltip("Множитель SplitFactor в тире Fast. Ниже — реже делим квад, " +
                 "то есть поднимаем порог дистанции до листьев.")]
        public float TierSplitScaleFast = 0.9f;

        [Tooltip("Множитель SplitFactor в тире Extreme.")]
        public float TierSplitScaleExtreme = 0.8f;

        [Tooltip("Поднимать тир по времени кадра, а не только по скорости. " +
                 "Страховка для слабых машин. По умолчанию выключено: без " +
                 "замеров в билде регулятор способен сам ухудшить картинку.")]
        public bool TierAutoRaise;

        [Tooltip("Целевое время кадра для регулятора, мс. Регулятор следит за " +
                 "скользящим окном и держит его не выше этой цифры.")]
        public float TierTargetFrameMs = 22f;

        [Tooltip("Строить чанки ВПЕРЁД по вектору скорости, а не только там, " +
                 "где камера уже стоит. На скорости узел нужен через долю " +
                 "секунды, а к моменту, когда камера доедет, он уже готов.")]
        public bool PrefetchEnabled = true;

        [Tooltip("С, на сколько вперёд заглядывать. 1.5 с — примерно 3 км на " +
                 "2 км/с.")]
        [Range(0f, 3f)]
        public float PrefetchSeconds = 1.5f;

        [Tooltip("С, за которые сглаженная скорость догоняет настоящую. Короткое " +
                 "окно заставляет префетч метаться на ускорениях, длинное " +
                 "заставляет опаздывать на торможении.")]
        [Range(0.05f, 1f)]
        public float PrefetchVelSmoothSeconds = 0.15f;

        [Tooltip("М, минимальный упрелёж. На малой скорости PrefetchSeconds даёт " +
                 "смещение в метры, и префетчить нечего — узлы не успевают " +
                 "понадобиться.")]
        public float PrefetchMinLeadMeters = 400f;

        [Tooltip("Потолок узлов в префетч-обходе. Он идёт поверх основного, и " +
                 "без отдельного лимита съел бы весь MaxNodes.")]
        [Min(16)]
        public int MaxPrefetchNodes = 512;

        [Tooltip("Сколько префетч-узлов максимум ждут в очереди на постройку.")]
        [Min(0)]
        public int MaxPrefetchQueued = 256;

        [Tooltip("В тирах Fast/Extreme строить чанк вчетверо реже (res/2) и " +
                 "достраивать до полного, когда очередь опустеет. Экономит " +
                 "в четыре раза вершины на чанк, но без прогрессивного " +
                 "уточнения видно мыло — поэтому за флагом.")]
        public bool TierHalveResolution;

        [Header("Декор: высота и скорость")]
        [Tooltip("М/с, выше которых новые сборки декора не запускаются. На " +
                 "такой скорости игрок проезжает поле быстрее, чем декор успевает " +
                 "вырасти, — пересборка только жрёт кадр. Уже начатые сессии " +
                 "доживают до конца.")]
        public float DecorPauseSpeed = 120f;

        [Tooltip("М, выше которых у декора выключаются тени. На высоте тени от " +
                 "дерева не попадают в картинку (пятно под ним — субпиксельное), " +
                 "а стоят целого прохода отсечения.")]
        public float DecorShadowMaxAltitude = 300f;

        [Tooltip("С, за которые сглаженная скорость догоняет настоящую. Короткое " +
                 "окно дёргает включения-выключения декора, длинное — оставляет " +
                 "декор включённым лишние секунды после остановки.")]
        public float DecorSpeedSmoothSeconds = 0.25f;

        [Tooltip("М, выше которых коллайдеры декора не создаются и уже " +
                 "созданные освобождаются. В ноуклипе игрок ни с чем не " +
                 "столкается, а обход пулов — это O(чанки × слои × инстансы) " +
                 "каждый кадр. У земли (высота ~2 м) поведение прежнее.")]
        public float DecorCollisionMaxAltitude = 150f;

        [Tooltip("М/с, выше которых между проходами декора держится минимальный " +
                 "интервал по времени: за кадр поле смещается на десятки " +
                 "метров, и сканировать чаще всё равно бесполезно.")]
        public float DecorScanSpeedGate = 150f;

        [Tooltip("С, минимальный интервал между проходами декора на скорости " +
                 "выше DecorScanSpeedGate.")]
        public float DecorScanMinInterval = 0.05f;

        [Header("Бюджет геометрии: горизонт и плотность сетки")]
        [Tooltip("Отбрасывать узлы, чья БЛИЖАЙШАЯ точка дальше предельной дальности " +
                 "видимости (горизонт планеты при максимальном рельефе). Такой узел " +
                 "не может закрыть ни одного пикселя, но без проверки он рисуется " +
                 "каждый кадр: на depth 2-6 это половина всех чанков.")]
        public bool CullBeyondHorizon = true;

        [Tooltip("Запас к предельной дальности (м). 1 = без запаса, 1.15 = +15 %.")]
        [Range(1f, 2f)]
        public float HorizonMargin = 1.15f;

        [Tooltip("Нижняя граница разрешения чанка. 9 = 8 квадов по стороне " +
                 "(это только дальняя полоса у горизонта, до неё горизонт не доходит).")]
        [Range(5, 65)]
        public int MinTileResolution = 9;

        [Tooltip("Через сколько уровней глубины делить разрешение сетки пополам. " +
                 "4 = ближние 4 уровня (MaxDepth-3…MaxDepth, при SplitFactor 3.5 это " +
                 "весь радиус 162 км вокруг игрока) на полном TileResolution без " +
                 "изменений, дальше вдвое каждые 4 уровня. " +
                 "99 = огрубление выключено, все чанки на полном разрешении.")]
        [Range(1, 99)]
        public int ResolutionHalveEveryLevels = 4;

        /// <summary>
        /// Periodic geometry budget report: chunks actually visible, their triangle
        /// count and depth distribution (twice a second, to the console).
        /// </summary>
        private void LogGeometryBudget()
        {
            if (!LogGeometryStats || Time.unscaledTime < nextGeometryDiagTime)
            {
                return;
            }

            nextGeometryDiagTime = Time.unscaledTime + 0.5f;

            int visible = 0;
            int waterMeshes = 0;
            int perDepthTris = 0;
            var trisByDepth = new int[MaxDepth + 1];
            for (int i = 0; i < desired.Count; i++)
            {
                if (!chunks.ContainsKey(NodeId(desired[i])))
                {
                    continue;
                }

                visible++;
                int res = ResolutionForDepth(desired[i].Depth);
                trisByDepth[desired[i].Depth] += (2 * res * res) + (8 * (res - 1));
            }

            for (int d = 0; d <= MaxDepth; d++)
            {
                perDepthTris += trisByDepth[d];
            }

            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.WaterMesh != null)
                {
                    waterMeshes++;
                }
            }

            string bands = string.Empty;
            for (int d = 0; d <= MaxDepth; d++)
            {
                if (trisByDepth[d] > 0)
                {
                    bands += " d" + d + "=" + (trisByDepth[d] / 1000) + "k";
                }
            }

            Debug.Log(string.Format(
                "[PlanetSurface] видно чанков: {0} (в кэше {1}, из них водных {2}), " +
                "треугольников рельефа: {3:N1}M, горизонт: {4:N0} км |{5}",
                visible, chunks.Count, waterMeshes, perDepthTris / 1000000f,
                maxVisibleDistance / 1000f, bands));
        }

        /// <summary>
        /// Far limit of visibility for the current frame (m). Computed in
        /// LateUpdate from the camera radius and the max terrain height; read by Traverse.</summary>
        private double maxVisibleDistance;

        [Tooltip("Печатать в консоль бюджет геометрии раз в 0.5 с (видно чанки, " +
                 "треугольники, горизонт). Для замера эффекта оптимизации.")]
        public bool LogGeometryStats = false;

        private float nextGeometryDiagTime;

        [Header("РџСЂРѕСЃС‚Р°СЏ С‚РµРЅСЊ С‚СЂР°РІС‹ (Р±Р»РѕР±)")]
        [Tooltip("Р¦РІРµС‚ РїСЏС‚РЅР° РїСЂРѕСЃС‚РѕР№ С‚РµРЅРё (opaque alpha-test; Р°Р»СЊС„Р° РЅРµ РёСЃРїРѕР»СЊР·СѓРµС‚СЃСЏ).")]
        public Color BlobShadowColor = new Color(0.05f, 0.08f, 0.04f, 1f);

        [Tooltip("Р Р°Р·РјРµСЂ РїСЏС‚РЅР° РѕС‚РЅРѕСЃРёС‚РµР»СЊРЅРѕ РјР°СЃС€С‚Р°Р±Р° РёРЅСЃС‚Р°РЅСЃР° С‚СЂР°РІС‹.")]
        [Range(0.2f, 2f)]
        public float BlobScaleFactor = 1.5f;

        [Tooltip("РЎРјРµС‰РµРЅРёРµ РїСЏС‚РЅР° РѕС‚ СЃРѕР»РЅС†Р° (РґРѕР»СЏ СЂР°Р·РјРµСЂР°): С‚РµРЅСЊ СѓС…РѕРґРёС‚ РёР·-РїРѕРґ РєСѓСЃС‚Р° Рё СЃС‚Р°РЅРѕРІРёС‚СЃСЏ РІРёРґРЅР°, РєР°Рє РЅР°СЃС‚РѕСЏС‰РёР№ РєР°СЃС‚.")]
        [Range(0f, 2f)]
        public float BlobSunOffset = 0.6f;

        [Header("Water — красивое море")]
        [Tooltip("Normal ripple strength (0 = dead flat water). Спокойная HOWTO-вода: 0.35 — живая зыбь в упор, без молочной заливки.")]
        [Range(0f, 1f)]
        public float WaterWaveStrength = 0.35f;

        [Tooltip("Base wave frequency (same scale as _WaveScale in shader). Красивое море: 0.18 — длинная океанская зыбь.")]
        public float WaterWaveScale = 0.18f;

        [Tooltip("Geometric wave amplitude in Vert, meters along radial normal. Visual only - physics/landing stays on flat sea level via WaterQuery.")]
        [Range(0f, 2f)]
        public float WaterAmplitudeMeters = 0.25f;

        [Tooltip("HF sparkle octave strength (specular only, diffuse/foam untouched).")]
        [Range(0f, 1f)]
        public float WaterSparkleStrength = 0.35f;

        [Tooltip("Sparkle frequency multiplier relative to base wave scale.")]
        public float WaterSparkleScale = 8f;

        [Tooltip("How much stronger wave crests foam (0 = as before). Лёгкие барашки на гребнях.")]
        [Range(0f, 1f)]
        public float WaterFoamCrest = 0.03f;

        [Tooltip("Shading fade floor: macro normal/foam never fade below this (0 = old dead-flat far water).")]
        [Range(0f, 1f)]
        public float WaterShadingFadeFloor = 0.85f;

        [Tooltip("Detail fade floor for HF sparkle/ripple. Keep 0: HF must reach true zero far away or it aliases to grain.")]
        [Range(0f, 1f)]
        public float WaterDetailFadeFloor = 0f;

        [Tooltip("Fine chop octave strength: goes into main normal (diffuse/fresnel/skyRef), not just specular.")]
        [Range(0f, 1f)]
        public float WaterRippleStrength = 0.45f;

        [Tooltip("Ripple frequency multiplier relative to base wave scale.")]
        public float WaterRippleScale = 10f;

        [Tooltip("Ripple animation speed (rad/s).")]
        public float WaterRippleSpeed = 2.2f;

        [Tooltip("Widens the foam band on steep underwater slopes via screen-space depth gradient (0 = fixed band). На скользящем взгляде fwidth глубины взрывается и пена заливает весь шельф — держим 0.")]
        public float WaterFoamSlopeGain = 0f;

        [Tooltip("Ширина полосы пены у уреза (м). HOWTO: пена только у берега, не по всему мелководью.")]
        [Min(0.01f)]
        public float WaterFoamDepthMeters = 1.5f;

        [Tooltip("Depth-buffer shore softening distance in meters (Phase 2).")]
        public float WaterShoreFadeMeters = 8f;

        [Tooltip("Минимальная непрозрачность воды. Блендинга нет (AlphaTest-очередь), " +
                 "прозрачность сделана screen-door'ом (clip(alpha - ign)), поэтому всё, " +
                 "что меньше 1, показывает СЫРОЕ дно. На пологом шельфе это давало " +
                 "шахматную полосу цвета пляжа с резкой прямой границей. 0.93 = у воды " +
                 "есть только тонкая мокрая кромка, дно просвечивает лишь у самого уреза.")]
        [Range(0.3f, 1f)]
        public float WaterMinAlpha = 0.93f;

        [Tooltip("Ширина полосы частичной прозрачности у уреза, В МЕТРАХ глубины. " +
                 "Раньше стоялло 2 м: на пологом дне эти 2 м растягивались на сотни " +
                 "метров, и screen-door рисовал дно шахматкой через всю мель. " +
                 "0.35 м = дно видно только там, где по колено.")]
        [Range(0.05f, 3f)]
        public float WaterShallowAlphaMeters = 0.35f;

        [Tooltip("Глубина бирюзового мелководья (м): экспонента Beer-Lambert для перехода shallow→deep.")]
        [Min(0.5f)]
        public float WaterAbsorbShallow = 4.5f;

        [Tooltip("Глубина глухого цвета (м): дальше этой глубины вода полностью _WaterDeep.")]
        [Min(1f)]
        public float WaterAbsorbDeep = 28f;

        [Tooltip("Видимость воды у самой кромки: ~0.5 — дно просвечивает сквозь screen-door (прозрачное мелководье), 1 — сплошная текстура.")]
        [Range(0f, 1f)]
        public float WaterShallowAlpha = 0.5f;

        [Tooltip("Сила бегущих линий прибоя (0 = только статичная полоса пены). Контурные линии по глубине на скользящем взгляде дают молочную зебру — держим 0.")]
        [Range(0f, 2f)]
        public float WaterSurfStrength = 0f;

        [Tooltip("Скорость набегания прибоя (рад/с).")]
        [Min(0f)]
        public float WaterSurfSpeed = 2.0f;

        [Tooltip("Сила зеркала неба по Френелю-Шлику (F0 воды 0.06 для красивого моря): вдаль море зеркалит небо. 0 = без отражений.")]
        [Range(0f, 1.2f)]
        public float WaterFresnelStrength = 0.7f;

        [Tooltip("Яркость солнечного блика (HOWTO: Reflection/refraction distort + spec). 1.0 выжигал шельф в молоко.")]
        [Range(0f, 1.5f)]
        public float WaterSpecularIntensity = 0.65f;

        [Tooltip("Фокус солнечного блика: больше = уже и острее дорожка.")]
        [Min(8f)]
        public float WaterSpecularPower = 600f;

        [Tooltip("Текстурная рябь HOWTO-Water (две normal map в противоход). Пусто — автозагрузка Resources/Water/water_normal_{a,b} (бейкер: Tools/Galilego/Bake water normal maps). Нет текстур — чисто процедурные нормали.")]
        public UnityEngine.Texture2D WaterNormalA;

        [Tooltip("Вторая normal map (мельче первой). Пусто — автозагрузка из Resources.")]
        public UnityEngine.Texture2D WaterNormalB;

        [Tooltip("Сила текстурной ряби (0 = только процедурные нормали).")]
        [Range(0f, 2f)]
        public float WaterNormalStrength = 0.7f;

        [Header("Water — штатный HDRP-патч (новая вода)")]
        [Tooltip("Автосоздавать локальный патч штатной HDRP Water у игрока (Ocean/Sea/Lake, Quad) при старте. ВЫКЛ по умолчанию: две воды (патч + чанки) дают шов, двойной берег и разный цвет вблизи — одна чанковая вода везде.")]
        public bool UseHdrpWaterPatch = false;

        [Tooltip("Размер HDRP-патча (м). 512 м: просадка края от кривизны ~3 см.")]
        [Min(64f)]
        public float HdrpWaterPatchSizeMeters = 512f;

        private GameObject hdrpWaterPatchGo;

        [Header("Water — красивое видимое море")]
        [Tooltip("Рисовать воду штатным HDRP/Unlit в художественном аквамарине (видно всегда, без пересвета от физических 80k люкс; мелководье светится сквозь прозрачность над песком). Выкл = вернуться к Galilego/WaterSurface (новый WIP-шейдер с волнами/пеной, сейчас не даёт пикселей — чинится отдельно).")]
        public bool UseLitWaterFallback = false;

        [Tooltip("Цвет красивого моря (linear): аквамарин тропического мелководья.")]
        public Color LitWaterColor = new Color(0.06f, 0.42f, 0.62f, 0.82f);

        [Tooltip("Зарезервировано под WaterSurface (пока не используется).")]
        [Range(0f, 1f)]
        public float LitWaterSmoothness = 0.9f;

        [Header("Water — живая зыбь (без шейдеров)")]
        [Tooltip("ДЕРЖАТЬ 0: сдвиг считается от центра чанка — соседние чанки расходятся до ±2×амплитуды и вода РВЁТСЯ на стыках (+ступень на границе WaterBobRadius). Живое дыхание даёт вершинная волна шейдера (WaterAmplitudeMeters) — она непрерывна по мировым координатам.")]
        [Range(0f, 1f)]
        public float WaterBobAmplitude = 0f;

        [Tooltip("Длина бегущей волны зыби (м).")]
        [Min(1f)]
        public float WaterBobLength = 28f;

        [Tooltip("Скорость бега волны (рад/с).")]
        [Min(0f)]
        public float WaterBobSpeed = 1.1f;

        [Tooltip("Радиус живой воды вокруг камеры (м): дальше — плоский штиль (экономия).")]
        [Min(50f)]
        public float WaterBobRadius = 700f;

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
            /// <summary>Transparent water surface of the chunk (sea-level plane,
            /// same LOD grid). null — the chunk has no water. Child of the chunk
            /// Go: visibility and eviction follow the parent.</summary>
            public GameObject WaterGo;
            public Mesh WaterMesh;
            /// <summary>Renderer of WaterGo. Cached because SetChunkVisible runs on
            /// every visibility change and GetComponent walks the hierarchy.</summary>
            public MeshRenderer WaterRenderer;
            public bool Visible;
            public int DecorBuiltMask;
            /// <summary>Build-time camera local to the chunk per layer (even when a layer
            /// produced 0 instances): it decides whether the density cloud is rebuilt on
            /// approach.</summary>
            public Vector3[] DecorBuildCameraLocal;
            public Node Node;
            public int Depth;
            public Vector3d CenterAstro;
            public float BoundsRadius;
            /// <summary>Grid resolution the mesh was actually built with. Needed for
            /// progressive refinement: in the fast tier a chunk is built at a quarter
            /// resolution, and when the queue drains, nodes with Res below
            /// ResolutionForDepth(Depth) are scheduled for rebuild. Without it there
            /// is no way to tell a fully built chunk from a coarsely built one.</summary>
            public int Res;
            /// <summary>Time (Time.unscaledTime) the chunk was last in desired or
            /// keep. LRU eviction uses it to pick the first to remove. Time, not a
            /// frame number: at 10 fps the last 90 frames span nine seconds, not
            /// 1.5, so eviction would lag exactly when it is needed most.</summary>
            public float LastKeepTime;
            public readonly List<DecorLayerRuntime> Decor = new List<DecorLayerRuntime>();
        }

        /// <summary>Built instances of one decor layer in a chunk (chunk-local).</summary>
        private sealed class DecorLayerRuntime
        {
            public GroundDecorLayer Profile;
            public int LayerIndex;
            /// <summary>Camera position at build time: for layers with density falloff
            /// the density cloud is anchored to it and must follow the player.</summary>
            public Vector3 BuildCameraRenderPos;
            public Vector3 BuildCameraLocalPosition;
            public NativeArray<GroundDecorInstance> Instances;
            public Matrix4x4[] Matrices;
            /// <summary>Cell layout table of the pool (the same one the allocator
            /// uses): the next rebuild needs it to carry over already grown blades
            /// (GroundDecorMergeKeptJob) with their BirthTime.</summary>
            public NativeArray<int> CellOffsets;
            public NativeArray<int> CellCounts;
            /// <summary>World instance matrices, computed by a Burst job every frame
            /// (for layers with PerInstanceDensity) — the main thread spends no time
            /// on per-blade TRS.</summary>
            public NativeArray<Matrix4x4> WorldMatrices;

            /// <summary>How many instances at the start of Instances are alive. The
            /// tail after them holds blades dropped from the new selection during a
            /// pool rebuild: they still draw but shrink by scale at their DeathTime
            /// (sink under ground). 0 = no tail.</summary>
            public int AliveCount;

            /// <summary>Time (Time.time) after which the Instances tail has faded
            /// out and can be truncated. 0 = no tail.</summary>
            public float RetireDeadline;

            /// <summary>The whole pool sinks under ground (layers without
            /// PerInstanceDensity — trees/rocks/cacti/daisies: they have no per-instance
            /// markings). Such a runtime still draws but shrinks by scale; see
            /// RetireStart/RetireFadeSeconds. Pool rebuilds and chunks leaving the
            /// radius never remove it instantly, so trees do not pop across the field
            /// on the first movement.</summary>
            public bool Retiring;

            /// <summary>Start time and duration of the pool fade-out (s).</summary>
            public float RetireStart;
            public float RetireFadeSeconds;

            /// <summary>Blade sink-under-ground duration for this pool (s). Derived
            /// from the frame rate at rebuild time (DecorFadeSeconds) and stored here
            /// so the matrix job and RetireDeadline share one transition formula.</summary>
            public float SinkFadeSeconds;

            /// <summary>Grass indirect path: instance matrices (chunk frame) in a
            /// buffer + draw arguments. Computed ONCE at build time; the world part
            /// goes through _DecorChunkToWorld. One draw per submesh instead of
            /// count/1023 batches.</summary>
            public GraphicsBuffer MatrixBuffer;
            public GraphicsBuffer ArgsBuffer;
            public Mesh DrawMesh;
        }

        /// <summary>
        /// Asynchronous build of a decor layer in a chunk. Stages run in slices sized
        /// by the frame budget: jitter hashes by cursor, the Burst candidate filter in
        /// the background (polled via IsCompleted), tuft expansion by cursor,
        /// finalization by swapping in the finished layer. The math and hash order are
        /// the same as the synchronous path.
        /// </summary>
        private sealed class DecorBuildSession
        {
            public long ChunkId;
            public Chunk Chunk;
            public int LayerIndex;
            public GroundDecorLayer Layer;
            public Node Node;

            public Vector3 CameraPosition;
            /// <summary>Anchor centre of the density cloud (camera + speed lead).</summary>
            public Vector3 CameraLocal;
            /// <summary>Raw local camera — used to check layer staleness (the speed lead is
            /// excluded, otherwise turns/jerks would age the layer).</summary>
            public Vector3 BuildCameraLocal;
            /// <summary>A build at the player (rebuild/visible zone) — such sessions are not
            /// limited by the slot reserve for distant first builds.</summary>
            public bool NearPlayer;
            /// <summary>Previous layer of the same chunk: its blades away from the new
            /// core are preserved (accumulation instead of full replacement), so grass
            /// does not appear anew on already crossed ground.</summary>
            public DecorLayerRuntime Previous;
            public NativeArray<GroundDecorInstance> KeptOld;
            public int KeptOldCount;
            /// <summary>How many records the new build may add (minus the kept old ones).</summary>
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

            /// <summary>Burst path (layers with PerInstanceDensity): mesh vertices in
            /// native memory, blade counters, write slots.</summary>
            public NativeArray<Vector3> MeshVerticesNative;
            /// <summary>Blade count of the cell from the Burst weight (allocator input).</summary>
            public NativeArray<int> SubsFor;
            /// <summary>How many blades the cell actually writes after cap clamping.</summary>
            public NativeArray<int> WriteCounts;
            public NativeArray<float> Distances;
            /// <summary>[0] — selection radius, [1] — edge band share, [2] — width.</summary>
            public NativeArray<float> Selection;

            /// <summary>Cell order by distance buckets (allocator scratch).</summary>
            public NativeArray<int> AllocCellOrder;

            /// <summary>Build lag diagnostics: queue time and placement distance.</summary>
            public float QueueTimeSeconds;
            public float QueueTangentialDistance;
            public NativeArray<int> WriteOffsets;
            /// <summary>[0] — blades written, [1] — clip radius (m).</summary>
            public NativeArray<float> WriteTotal;

            public JobHandle CandidateJob;
            public bool CandidateScheduled;
            public bool CancelRequested;
            public bool Finished;

            /// <summary>0 — candidate prep, 1 — waiting for the Burst job,
            /// 2 — accepted/weight pass, 3 — tuft expansion, 4 — finalization.</summary>
            public int Stage;
            public int Cursor;
            public int AcceptedCount;
            public double WeightSum;
            /// <summary>Σ weight×effective tufts — expected instance count (weight already
            /// includes falloff; distant tufts are clamped to one).</summary>
            public double ExpectedWeight;
            public double CapScale = 1d;
            public int SubPerCell = 1;
            public int Take;
            public int Stride = 1;
            public int Write;

            public int K;
            public int SubIndex;
            /// <summary>Tuft count for the CURRENT cell: full nearby, 1 far away
            /// (density there is negligible).</summary>
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
        private Material waterMaterialCache;
        private int waterChunkCount;
        private bool waterDiagLogged;
        private BodyAuthoring bodyAuthoring;
        private GroundDecorProfile decorProfile;
        private Matrix4x4[] decorBatchScratch;
        private Matrix4x4[] decorPerMeshScratch;
        private Matrix4x4[] decorCastScratch;
        private Matrix4x4[] decorRestScratch;
        private Matrix4x4[] decorBlobScratch;
        private Plane[] decorFrustumPlanes;
        private Mesh blobMesh;
        private readonly Dictionary<GroundDecorLayer, Mesh> treeProxyMeshes = new Dictionary<GroundDecorLayer, Mesh>();
        /// <summary>Simplified proxy (no trunk, 4-sided crown) for high altitudes.
        /// Keyed on altitude, not chunk distance, so that at the step boundary all
        /// chunks in frame share the same mesh and can batch into one buffer instead
        /// of scattering into individual draw calls.</summary>
        private readonly Dictionary<GroundDecorLayer, Mesh> treeProxyMeshesLow = new Dictionary<GroundDecorLayer, Mesh>();
        private Material blobMaterial;

        private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
        private readonly List<Node> desired = new List<Node>();
        private readonly HashSet<long> keep = new HashSet<long>();
        private readonly HashSet<long> splitNodes = new HashSet<long>();
        private readonly HashSet<long> splitNext = new HashSet<long>();
        private readonly List<long> toEvict = new List<long>();
        /// <summary>Chunks currently drawn. Maintained by SetChunkVisible so the
        /// transform pass walks the active list instead of the whole dictionary:
        /// in flight the cache holds thousands of nodes, of which hundreds show.</summary>
        private readonly List<Chunk> visibleChunks = new List<Chunk>();

        // ===== [PHASE 2] Asynchronous chunk building =====
        // Queue of nodes awaiting build; requests already sent to jobs; a set of
        // ids for deduplication (without it a node would be queued every frame
        // until built, growing the queue faster than flight).
        private readonly List<ChunkQueueItem> chunkBuildQueue = new List<ChunkQueueItem>();
        private readonly List<ChunkBuildInFlight> chunkBuildsInFlight = new List<ChunkBuildInFlight>();
        private readonly HashSet<long> queuedChunkIds = new HashSet<long>();

        private struct ChunkQueueItem
        {
            public Node Node;
            public long Id;
            public float Priority;
            public float Distance;

            /// <summary>Frame the entry was queued. Used to drop stale entries: a node
            /// the player has left behind no longer enters desired and would otherwise
            /// linger in the queue.</summary>
            public int EnqueuedFrame;
        }

        private struct ChunkBuildInFlight
        {
            public long Id;
            public ChunkBuildRequest Request;
        }

        /// <summary>Terrain parameter version. Incremented when noise changes: results
        /// computed from old parameters are discarded at finalization, otherwise two
        /// different terrains would mix in the cache.</summary>
        private int paramsVersion;

        /// <summary>Parameters are alive (ClearChunkCache is not running). While false,
        /// any in-flight result is stale and is disposed instead of uploaded to a mesh.</summary>
        private bool paramsAlive;

        private Vector3 lastCameraPosition;

        // ===== [PHASE 3] Quality tiers and prefetch =====
        private enum MotionTier
        {
            Normal,
            Fast,
            Extreme,
        }

        private MotionTier motionTier;
        private float motionTierSince;
        private Vector3 smoothedSurfaceVelocity;
        private Vector3 predictedCamera;
        private float predictedLeadMeters;

        /// <summary>Separate result of the prefetch traversal. Own list and own pair of
        /// hysteresis sets: writing into the shared ones would shift the main traversal's
        /// decisions by a frame and make nodes flicker at the LOD boundary.</summary>
        private readonly List<Node> prefetchDesired = new List<Node>();
        private readonly HashSet<long> prefetchSplitNodes = new HashSet<long>();
        private readonly HashSet<long> prefetchSplitNext = new HashSet<long>();
        private readonly HashSet<long> prefetchKeep = new HashSet<long>();

        /// <summary>Depth ceiling for the current tier. Must NEVER be passed into
        /// ResolutionForDepth: that computes stepsBelowFinest from MaxDepth, and if the
        /// tier substituted MaxDepth there, a built node's grid would change and it
        /// would rebuild endlessly.</summary>
        private int EffectiveMaxDepth
        {
            get
            {
                if (!EnableQualityTiers)
                {
                    return MaxDepth;
                }

                int drop = motionTier == MotionTier.Extreme
                    ? TierDepthDropExtreme
                    : (motionTier == MotionTier.Fast ? TierDepthDropFast : 0);
                return System.Math.Max(1, MaxDepth - drop);
            }
        }

        private float EffectiveSplitFactor
        {
            get
            {
                if (!EnableQualityTiers)
                {
                    return SplitFactor;
                }

                float scale = motionTier == MotionTier.Extreme
                    ? TierSplitScaleExtreme
                    : (motionTier == MotionTier.Fast ? TierSplitScaleFast : 1f);
                return SplitFactor * scale;
            }
        }
        private readonly List<DecorBuildSession> decorBuildSessions = new List<DecorBuildSession>();
        private readonly List<DecorCandidate> decorCandidates = new List<DecorCandidate>(256);
        private readonly List<BurstDecorDraw> burstDecorDraws = new List<BurstDecorDraw>();
        /// <summary>Shared matrix buffer for distant trees. One draw for hundreds of
        /// chunks instead of one per chunk: the main thread stops issuing hundreds of
        /// RenderMeshInstanced calls and copies. Only for shadowless layers — they
        /// share one mesh and material, so they can batch.</summary>
        private const int ProxySharedCapacity = 131072; // 8 MB
        private NativeArray<Matrix4x4> proxyShared;
        private JobHandle proxySharedHandle;
        private int proxySharedUsed;
        private Mesh proxySharedMesh;
        private Material proxySharedMaterial;
        private Bounds proxySharedBounds;
        private MaterialPropertyBlock decorDrawPropertyBlock;
        private bool decorIndirectDiagLogged;
        private static readonly int DecorChunkToWorldId = Shader.PropertyToID("_DecorChunkToWorld");
        private static readonly int DecorMatricesId = Shader.PropertyToID("_DecorMatrices");
        private const int DecorScanItemsPerStep = 4096;
        private const int DecorExpandSubsPerStep = 2048;
        /// <summary>How many session slots are always reserved for work at the player
        /// (cloud rebuilds and visible first builds).</summary>
        private const int PlayerReservedSessions = 2;
        /// <summary>Below this falloff the cell's tufts are not expanded (one instance
        /// per cell): density that far out is negligible and the build gets several
        /// times cheaper.</summary>
        /// <summary>Grass cloud lead by player speed: a fraction of a second of travel
        /// so the dense zone is prepared AHEAD of the player (grass meets the player
        /// rather than chasing them). Capped by the flat core share, otherwise the
        /// player ends up inside the falloff zone and a density step shows ahead.</summary>
        private const float GrassLeadSeconds = 0.5f;
        private const float GrassLeadMaxMeters = 60f;
        private const float GrassLeadCoreShare = 0.5f;
        /// <summary>Share of the grass pool budget given to soft falloff at the clip
        /// edge: otherwise the pool edge shows as a straight line on the ground.</summary>
        private const float DecorCutFadeShare = 0.25f;
        /// <summary>Density falloff band before the pool radius (m).</summary>
        private const float DecorPoolRadiusFadeMeters = 60f;
        /// <summary>Seconds for a blade to sprout (scale 0→1).</summary>
        private const float DecorBladeFadeSeconds = 0.6f;
        /// <summary>Spawn-time spread across blades (s): new ones appear one by one
        /// within this window, not as one batch.</summary>
        private const float DecorBladeStaggerSeconds = 1.2f;
        /// <summary>Seconds for a blade to sink UNDER ground (scale 1→0): the mirror
        /// of DecorBladeFadeSeconds. Blades dropped from a new pool selection do not
        /// vanish instantly but sink into the ground.</summary>
        private const float DecorBladeSinkSeconds = 0.9f;
        /// <summary>Fade-start spread (s): dying blades fade out of sync, otherwise
        /// the field edge would blink in one wave.</summary>
        private const float DecorBladeSinkStaggerSeconds = 1f;
        /// <summary>Seconds for a prop (tree/rock/cactus/daisy) to sink under ground
        /// entirely: the layer pool is not PerInstanceDensity and has no per-instance
        /// markings, so the whole pool fades at once (as softly as it appears).</summary>
        private const float DecorPropSinkSeconds = 1.4f;
        /// <summary>Minimum number of frames any decor appear/disappear transition
        /// must span. At low FPS a fixed 0.6-0.9 s is 0-1 frames — a visually instant
        /// pop; this floor stretches the duration to N frames.</summary>
        private const int DecorFadeMinFrames = 5;

        /// <summary>
        /// Transition duration adjusted for frame rate: no shorter than
        /// DecorFadeMinFrames frames, otherwise the base value. Computed ONCE per pool
        /// rebuild and stored in the runtime so every transition frame uses the same
        /// formula.
        /// </summary>
        private static float DecorFadeSeconds(float baseSeconds)
        {
            float dt = Time.deltaTime;
            float minSeconds = dt > 0f ? DecorFadeMinFrames * dt : 0f;
            return baseSeconds > minSeconds ? baseSeconds : minSeconds;
        }
        /// <summary>Prop sprouting (trees/rocks/cacti/daisies): appear duration and
        /// spread. Large objects grow slower than blades.</summary>
        private const float DecorPropFadeSeconds = 1.0f;
        private const float DecorPropStaggerSeconds = 2.0f;
        /// <summary>A chunk's grass pool is released beyond MaxDistance + this margin:
        /// otherwise passed chunks pile up full pools, the budget drifts away from the
        /// player (and the cloud near them thins out).</summary>
        private const float DistantDecorTrimMargin = 80f;
        /// <summary>Normalization of the decor cell metric: density ∝ square.
        /// 1.0 = pure metres (real spacing between plants = SpacingMeters).
        /// 1.3 = keep the temperate-latitude look (costlier in frame time where
        /// grass is added to the candidates at face centres).</summary>
        private const double DecorMetricNorm = 1.0d;
        private int firstFrameGuard = -1;
        private int diagnosticChunksLogged;
        private float decorAllocLogTime;
        private readonly float[] decorProbeNext = new float[16];
        private float decorAltitude;
        private float decorSpeed;

        /// <summary>Frame threshold above which [Hitch] is logged. 40 ms = 25 fps.</summary>
        [SerializeField]
        private float HitchLogThresholdMs = 0.04f;

        /// <summary>Managed counter snapshot taken at the start of this frame's LateUpdate.</summary>
        private long lateUpdateAllocBefore;
        private bool blobDiagLogged;
        private Vector3 bodyRenderPosition;
        /// <summary>Player speed relative to the surface in body-fixed axes (same as the
        /// chunk local axes): planet rotation excluded.</summary>
        private Vector3 surfaceVelocityLocal;
        private Vector3 lastSurfaceLocalPosition;
        private bool hasSurfaceLocalPosition;
        private Vector3 lastDecorScanCamera;
        private bool hasDecorScanCamera;
        private float nextDecorScanTime;
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
                // Smooth sphere or body not found — leave the old visual untouched.
                enabled = false;
                return;
            }

            noiseParams = TerrainNoiseParams.FromTerrain(terrain);
            // The body's primitive sphere is only a placeholder: its radius equals the
            // mean (sea) level, so with chunks on it is coplanar with the ocean and
            // flickers — hence it is disabled permanently. Planet opacity at any
            // distance is provided by the terrain itself (cube-sphere renders in space
            // too, with no altitude threshold).
            MeshRenderer sphereRenderer = GetComponent<MeshRenderer>();
            if (sphereRenderer != null)
            {
                sphereRenderer.enabled = false;
            }


            if (Runner.SystemView == null)
            {
                Runner.SystemView = new BodyTransformRegistry();
            }

            // Identity root for chunks: they are NOT under the body transform (otherwise
            // their coordinates would be ~planet radius and float-jitter). Each chunk's
            // position/rotation is set every frame via FloatingOrigin.
            surfaceRoot = new GameObject("PlanetSurfaceChunks").transform;
            surfaceRoot.position = Vector3.zero;
            surfaceRoot.rotation = Quaternion.identity;
            surfaceRoot.localScale = Vector3.one;

            bodyAuthoring = GetComponent<BodyAuthoring>();
            decorProfile = bodyAuthoring != null && bodyAuthoring.GroundDecorPreset != null
                ? bodyAuthoring.GroundDecorPreset.Profile
                : null;
            // Clean state on every Play: with Enter Play Mode Options (no Reload
            // Scene/Domain) private component fields can survive the previous run while
            // their chunks are already destroyed.
            ClearChunkCache();
            paramsAlive = true;
            EnsureHdrpWaterPatch();
        }

        /// <summary>
        /// New water with no manual steps: on start an object with HDRP Water Surface +
        /// WaterPatchFollower is created under surfaceRoot (Ocean/Sea/Lake, Quad at the
        /// player). Disabled via the UseHdrpWaterPatch flag in the inspector.
        /// </summary>
        private void EnsureHdrpWaterPatch()
        {
            if (!UseHdrpWaterPatch || body == null || !WaterQuery.HasOcean(body))
            {
                DestroyHdrpWaterPatch();
                return;
            }

            if (hdrpWaterPatchGo != null)
            {
                return;
            }

            hdrpWaterPatchGo = new GameObject("HdrpWaterPatch (auto)");
            hdrpWaterPatchGo.transform.SetParent(surfaceRoot, false);
            hdrpWaterPatchGo.AddComponent<UnityEngine.Rendering.HighDefinition.WaterSurface>();
            WaterPatchFollower follower = hdrpWaterPatchGo.AddComponent<WaterPatchFollower>();
            follower.Runner = Runner;
            follower.PatchSizeMeters = Mathf.Max(64f, HdrpWaterPatchSizeMeters);
            Debug.Log("[PlanetSurface] HDRP-патч воды создан автоматически (Ocean/Sea/Lake, Quad 512 м). " +
                "Старая чанковая вода — дальний фон. Отключить: UseHdrpWaterPatch.");
        }

        private void DestroyHdrpWaterPatch()
        {
            if (hdrpWaterPatchGo != null)
            {
                Destroy(hdrpWaterPatchGo);
                hdrpWaterPatchGo = null;
            }
        }

        private void OnDestroy()
        {
            CancelAllDecorBuilds();

            // [PHASE 1] Chunks are cleared BEFORE Destroy(surfaceRoot.gameObject) and
            // with the same DestroyChunkResources used for eviction: after the root is
            // destroyed, child GameObject references are already dead to Unity checks,
            // and the mesh balance counter would come out wrong.
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DestroyChunkResources(kv.Value);
            }

            chunks.Clear();

            if (surfaceRoot != null)
            {
                Destroy(surfaceRoot.gameObject);
            }

            DestroyHdrpWaterPatch();

            if (materialCache != null)
            {
                Destroy(materialCache);
            }

            if (waterMaterialCache != null)
            {
                Destroy(waterMaterialCache);
            }

            if (blobMaterial != null)
            {
                Destroy(blobMaterial);
            }

            if (blobMesh != null)
            {
                Destroy(blobMesh);
            }

            foreach (KeyValuePair<GroundDecorLayer, Mesh> proxy in treeProxyMeshes)
            {
                if (proxy.Value != null)
                {
                    Destroy(proxy.Value);
                }
            }

            treeProxyMeshes.Clear();

            foreach (KeyValuePair<GroundDecorLayer, Mesh> proxy in treeProxyMeshesLow)
            {
                if (proxy.Value != null)
                {
                    Destroy(proxy.Value);
                }
            }

            treeProxyMeshesLow.Clear();

            // The Persistent shared decor matrix buffer lives for the whole session:
            // drain it here, otherwise it survives a domain reload.
            proxySharedHandle.Complete();
            if (proxyShared.IsCreated)
            {
                proxyShared.Dispose();
            }

            proxySharedUsed = 0;
            proxySharedHandle = default(JobHandle);

            // Teardown: drain the decor buffer pool (otherwise the Persistent arrays
            // leak on exiting play or on domain reload). The same required step applies
            // to the chunk pool and the shared index buffers.
            DecorArrayPool.ClearAll();
            CancelAllChunkBuilds();
            SurfaceChunkPool.ClearAll();
        }

        /// <summary>Reset the mesh cache (after a live change of terrain parameters).</summary>
        [ContextMenu("Clear chunk cache")]
        public void ClearChunkCache()
        {
            CancelAllDecorBuilds();
            // [PHASE 2] Order matters: first clear the intents, then paramsAlive, and
            // only then reset the cache. Jobs already in flight are awaited and return
            // their arrays to the pool; their results are already marked stale.
            CancelAllChunkBuilds();
            paramsVersion++;
            paramsAlive = false;
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                DestroyChunkResources(kv.Value);
            }

            chunks.Clear();
            splitNodes.Clear();
            visibleChunks.Clear();

            // The water material is recreated on every Play: with Enter Play Mode without
            // Reload Domain the old material (different shader/colours) would survive the
            // run and switching UseLitWaterFallback/shader would have no effect.
            if (waterMaterialCache != null)
            {
                Destroy(waterMaterialCache);
                waterMaterialCache = null;
            }
        }

        /// <summary>Tear down all unfinished decor builds: finish the jobs and free the
        /// Persistent arrays, otherwise they leak.</summary>
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
        /// Live tuning in Play: copy terrain parameters from the neighbouring
        /// BodyAuthoring into the live HeightfieldTerrain, refresh the noise snapshot
        /// and rebuild all chunks. Outside Play it only changes serialized fields.
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
        /// Live tuning: when terrain fields in BodyAuthoring change in Play, they are
        /// copied into the live terrain and all chunks rebuild automatically — otherwise
        /// Inspector edits would not affect already built geometry.
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

            // Decor exclusion zones: collected from site objects (every 0.5 s, with a
            // content check inside). Runs after ApplyToTerrain because ApplyToTerrain can
            // rebuild the terrain tables, and the zones live on the same HeightfieldTerrain.
            DecorExclusionTable.Apply(terrain, body.Radius, body.Name);

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
                // COLOUR-ONLY changes (mottling strength/frequency, rock/snow thresholds)
                // do not alter geometry — no need to rebuild chunks, just re-upload the
                // uniforms to the globals.
                ApplyTerrainGlobals();
            }
        }

        /// <summary>Snapshot of colour uniforms (no geometry) for live tuning.</summary>
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
                && a.BeachShelfWidthMaxScale == b.BeachShelfWidthMaxScale
                && a.BeachShelfWidthNoiseFrequency == b.BeachShelfWidthNoiseFrequency
                && a.BeachShelfWidthNoiseOctaves == b.BeachShelfWidthNoiseOctaves
                && a.BeachShelfSeawardWidth == b.BeachShelfSeawardWidth
                && a.ColorNoiseFrequency == b.ColorNoiseFrequency
                && a.ColorNoiseOctaves == b.ColorNoiseOctaves
                && a.ColorNoiseSeedOffset == b.ColorNoiseSeedOffset
                && a.ColorDetailFrequency == b.ColorDetailFrequency
                && a.ColorDetailOctaves == b.ColorDetailOctaves
                && a.ColorDetailSeedOffset == b.ColorDetailSeedOffset
                && a.ComputeMask == b.ComputeMask
                && a.ComputeDetail == b.ComputeDetail
                // Sites must participate in the comparison: otherwise adding or moving a
                // base changes physics but not the chunk cache, silently leaving visual vs
                // physics out of sync. Compare table CONTENTS, not descriptors:
                // NativeArray == compares pointers, so an identical set from two builds
                // would look different.
                && TerrainModifiers.TableEqual(a.Mods, b.Mods)
                // Decor exclusion zones follow the same rule: rebuilding them must rebuild
                // decor chunks, otherwise a zone silently drifts from the table contents.
                && DecorExclusionTable.TableEqual(a.DecorExclusions, b.DecorExclusions);
        }

        private void LateUpdate()
        {
            // [PHASE 0] Surface render frame accounting. Opened BEFORE everything and
            // closed at every exit point: managed allocation is measured across the whole
            // LateUpdate, not just its interesting part.
            SurfacePerf.BeginFrame();
            long perfAllocBefore = System.GC.GetAllocatedBytesForCurrentThread();
            lateUpdateAllocBefore = perfAllocBefore;

            // Water must always stay alive: wave time and camera position are set BEFORE
            // all early-outs (no Ship / foreign DominantBody / no MainCamera). Otherwise
            // _WaterTime freezes and the ocean goes static: no waves, no ripples, no surf.
            Shader.SetGlobalFloat("_WaterTime", Time.time);
            Camera cameraEarly = Camera.main;
            if (cameraEarly != null)
            {
                Shader.SetGlobalVector("_PlanetCameraPos", cameraEarly.transform.position);
            }

            ApplyWaterParams();
            if (body == null || terrain == null || Runner.Ship == null)
            {
                SurfacePerf.EndFrame(perfAllocBefore);
                return;
            }
            if (Runner.DominantBody != body)
            {
                SetAllInvisible();
                SurfacePerf.EndFrame(perfAllocBefore);
                return;
            }

            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPosition, out _);

            // [GRAPHICS] Antialiasing (SMAA/TAA) lives on the same Main Camera:
            // HDAdditionalCameraData in the scene (antialiasing / SMAAQuality / TAA*).
            Camera camera = Camera.main;
            if (camera == null)
            {
                SurfacePerf.EndFrame(perfAllocBefore);
                return;
            }

            Vector3 cameraPosition = camera.transform.position;
            lastCameraPosition = cameraPosition;
            UpdateUnderwaterCameraDepth(cameraPosition);
            if (!waterDiagLogged && Time.frameCount > 30)
            {
                waterDiagLogged = true;
                Debug.Log("[PlanetSurface] чанков: " + chunks.Count + ", водных мешей построено: " + waterChunkCount);
            }

            // LOD traversal — every frame. It is cheap (meshes are cached), and under the
            // floating origin the render-space camera position stays ~0: hysteresis on it
            // would freeze the node set at spawn and let distant chunks go stale.
            bodyRenderPosition = FloatingOrigin.ToRender(bodyPosition);
            currentBodyPosition = bodyPosition;
            currentBodyOrientation = body.GetVisualOrientation(Runner.TimeSeconds);
            currentBodyRotation = FloatingOrigin.RenderRotation(currentBodyOrientation);

            // Player speed relative to the SURFACE (body-fixed, chunk axes): planet rotation
            // is subtracted, otherwise the grass cloud lead would drift even when the player
            // stands still. Computed in double from astro positions: float differences at
            // ~1e6 m are noisy at metres per second.
            Vector3 surfaceLocal = AstroFrame.ToSimulation(
                currentBodyOrientation.Conjugated.Rotate(Runner.PlayerPosition - currentBodyPosition));
            if (hasSurfaceLocalPosition)
            {
                surfaceVelocityLocal = (surfaceLocal - lastSurfaceLocalPosition) / Mathf.Max(1e-4f, Time.deltaTime);
            }

            lastSurfaceLocalPosition = surfaceLocal;
            hasSurfaceLocalPosition = true;
            UpdateTerrainTextureOrigin();
            UpdateWaterAnchor();
            ApplyTerrainGlobals();

            {
                // Body-fixed direction for the current frame.
                // [PHASE 2] The “params alive” flag is raised BEFORE enqueueing:
                // ClearChunkCache lowers it, and without this, after any cache rebuild
                // (noise change, ApplyAuthoringAndRebuild) in-flight results would forever
                // look stale and never be merged into meshes — the cache would stay empty
                // for good. In-flight requests are already drained in CancelAllChunkBuilds,
                // so nothing stale can be here.
                paramsAlive = true;
                desired.Clear();
                splitNext.Clear();

                // Far limit of terrain visibility from this point.
                //
                // Camera at radius r1, terrain up to height h. Beyond the tangent point of
                // the (R+h)·r1 circle the surface is hidden behind the planet's curvature:
                // d = sqrt((R+h)² - R²). h is terrain.AmplitudeMeters, the upper bound of
                // height above sea level, so the cull never eats visible terrain even on a
                // peak or at high altitude (the camera radius is in the formula, so the
                // horizon moves out in atmosphere/orbit).
                maxVisibleDistance = double.MaxValue;
                if (CullBeyondHorizon && terrain != null)
                {
                    double camRadius = (cameraPosition - bodyRenderPosition).magnitude;
                    double height = System.Math.Max(1d, terrain.AmplitudeMeters)
                                    + System.Math.Max(0d, camRadius - body.Radius);
                    double tangentRadius = body.Radius + height;
                    maxVisibleDistance = System.Math.Sqrt(
                        (tangentRadius * tangentRadius) - (body.Radius * body.Radius))
                        * System.Math.Max(1d, HorizonMargin);
                }

                using (SurfacePerf.TraverseMarker.Auto())
                {
                    for (int face = 0; face < CubeSphere.FaceCount; face++)
                    {
                        Traverse(face, 0, 0, 0, cameraPosition);
                    }
                }

                SurfacePerf.DesiredNodes = desired.Count;

                // [PHASE 3] The speed tier and the predicted point are computed BEFORE the
                // traversal: EffectiveMaxDepth and EffectiveSplitFactor are read directly in
                // Traverse, and prefetch runs as a second pass over the predicted camera.
                UpdateMotionTier(surfaceVelocityLocal.magnitude);
                UpdatePredictedCamera(cameraPosition);

                // Freeze this frame's subdivision — next frame it is the hysteresis base in
                // Traverse, so a node does not split/merge every frame.
                splitNodes.Clear();
                splitNodes.UnionWith(splitNext);

                // First BUILD/SHOW the desired nodes and only then compute keep from the
                // actual frame state. Otherwise, on the frame a child appears, keep would
                // still include the parent (the child was a cache miss) — coarse and detailed
                // chunks show together: z-fighting/pop on every transition.
            int budget = firstFrameGuard < 0 ? int.MaxValue : BuildsPerFrame;
            bool firstFrame = firstFrameGuard < 0;
            firstFrameGuard = 0;
            int built = 0;
            int enqueued = 0;
            // [PHASE 0] Build time is measured with Stopwatch, not ProfilerMarker: a counter
            // is needed here, not just a profiler entry.
            //
            // [PHASE 2] The budget is ChunkFinalizeBudgetMs (milliseconds, not a chunk
            // count): the build itself runs in jobs, and on the main thread only the cheap
            // upload of the finished buffer remains.
            var buildClock = System.Diagnostics.Stopwatch.StartNew();
            int visibleNow = 0;
            int terrainTrisNow = 0;
            for (int i = 0; i < desired.Count; i++)
            {
                Node wanted = desired[i];
                long id = NodeId(wanted);
                if (chunks.TryGetValue(id, out Chunk cached))
                {
                    if (!cached.Visible)
                    {
                        SetChunkVisible(cached, true);
                    }

                    visibleNow++;
                    terrainTrisNow += ChunkTriangleCount(wanted.Depth);
                    continue;
                }

                if (firstFrame || !AsyncChunkBuild)
                {
                    // The first frame and the build-synchronously mode stay synchronous:
                    // smoothness does not matter there, but the world must be ready by the
                    // first shown frame. The async path would fill the cache over seconds and
                    // the player would fly off into holes.
                    if (built >= budget)
                    {
                        continue;
                    }

                    BuildChunk(wanted);
                    built++;
                }
                else
                {
                    EnqueueChunkBuild(wanted);
                    enqueued++;
                }

                visibleNow++;
                terrainTrisNow += ChunkTriangleCount(wanted.Depth);
            }

                PumpChunkBuilds();
                EnqueuePrefetchChunks(cameraPosition);

            buildClock.Stop();
            SurfacePerf.ChunksBuilt = built;
            SurfacePerf.BuildChunkMs = (float)buildClock.Elapsed.TotalMilliseconds;
            SurfacePerf.VisibleChunks = visibleNow;
            SurfacePerf.TerrainTriangles = terrainTrisNow;
            SurfacePerf.CachedChunks = chunks.Count;
            SurfacePerf.ChunksEnqueued = enqueued;
            SurfacePerf.QueuedBuilds = chunkBuildQueue.Count;
            SurfacePerf.InFlightBuilds = chunkBuildsInFlight.Count;

                // keep = built desired leaves + parents of desired nodes not yet built in
                // this frame (hole plugs at LOD switches).
                keep.Clear();
                for (int i = 0; i < desired.Count; i++)
                {
                    if (chunks.ContainsKey(NodeId(desired[i])))
                    {
                        keep.Add(NodeId(desired[i]));
                    }
                }

                int activeNow = 0;

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

                        // [PHASE 1] A parent is needed as a hole plug exactly until all four
                        // of its children are built: once a node is replaced by its children
                        // it must leave keep, otherwise it keeps being drawn on top of them.
                        //
                        //
                        // The “has an unbuilt child” condition is sufficient and safe: when
                        // the last fourth child is built, all children are already in desired
                        // and visible in the same frame, so no hole appears.
                        if (!HasAllBuiltChildren(face, depth, ix, iy))
                        {
                            keep.Add(NodeId(face, depth, ix, iy));
                        }
                    }
                }

                // Hide everything visible that is not in keep.
                foreach (KeyValuePair<long, Chunk> kv in chunks)
                {
                    if (keep.Contains(kv.Key))
                    {
                        // The LRU stamp is set right here, avoiding a second pass: walking
                        // the whole cache every frame is not free.
                        kv.Value.LastKeepTime = Time.unscaledTime;
                        if (kv.Value.Visible)
                        {
                            activeNow++;
                        }

                        continue;
                    }

                    if (kv.Value.Visible)
                    {
                        SetChunkVisible(kv.Value, false);
                    }
                }

                for (int i = 0; i < desired.Count; i++)
                {
                    if (chunks.TryGetValue(NodeId(desired[i]), out Chunk fresh))
                    {
                        fresh.LastKeepTime = Time.unscaledTime;
                    }
                }

                // [PHASE 2.5] keep size and DesiredInKeep are computed HERE in one pass, not
                // inside EvictIfNeeded: when eviction exits early (cache under the limit) the
                // counters would stay zero.
                //
                // By construction keep ⊇ desired∩chunks, so DesiredInKeep must equal
                // VisibleChunks for the same frame. If it does not, the cache changed between
                // these points and the screen really has holes.
                int desiredInKeep = 0;
                for (int i = 0; i < desired.Count; i++)
                {
                    if (keep.Contains(NodeId(desired[i])))
                    {
                        desiredInKeep++;
                    }
                }

                SurfacePerf.KeepCount = keep.Count;
                SurfacePerf.DesiredInKeep = desiredInKeep;

                // [PHASE 0] Active chunks usually outnumber desired nodes: the difference is
                // parents left visible as hole plugs. A large difference means holes are
                // covered too broadly and extra geometry is drawn.
                SurfacePerf.ActiveChunks = activeNow;

                EvictIfNeeded();
                LogGeometryBudget();
            }

            // Transforms of visible chunks — every frame, but only over the active list. One
            // SetPositionAndRotation call replaces two setters: one transform write instead of
            // two, and the matrix recomputes once instead of once per channel.
            using (SurfacePerf.ChunkTransformsMarker.Auto())
            {
                for (int i = 0; i < visibleChunks.Count; i++)
                {
                    Chunk c = visibleChunks[i];
                    if (c.Go == null)
                    {
                        continue;
                    }

                    c.Go.transform.SetPositionAndRotation(
                        NodeRenderPosition(c.CenterAstro), currentBodyRotation);
                }
            }

            UpdateWaterBob(cameraPosition);

            // Decor after the chunk transforms are placed: instance matrices take the chunk
            // world transform of the current frame.
            Shader.SetGlobalFloat("_GroundDecorTime", Time.time);
            UpdateDecorMotionState(cameraPosition);
            // Chunk passes (stale/unbuilt search, distant eviction) run not every frame: they
            // are O(chunks×layers). Enough on camera displacement > 0.25 m or once per
            // 6 frames (keeps the cloud seated when standing).
            // Decor scans trigger on camera displacement; the threshold is proportional to
            // altitude, because up high the field is subpixel and a frequent scan gives no
            // gain.
            float decorScanThreshold = Mathf.Max(0.25f, decorAltitude * 0.02f);
            float decorScanThresholdSq = decorScanThreshold * decorScanThreshold;
            bool decorScanDue = !hasDecorScanCamera
                || (cameraPosition - lastDecorScanCamera).sqrMagnitude > decorScanThresholdSq
                || (++decorScanTick % 6) == 0;
            // [PHASE 1] The camera displacement threshold grows with altitude (a fixed
            // 0.25 m would be exceeded every frame at 2 km). At speed a minimum time interval
            // is also added: the field moves tens of metres per frame, so scanning more often
            // is pointless.
            if (decorScanDue && decorSpeed > DecorScanSpeedGate
                && Time.unscaledTime < nextDecorScanTime)
            {
                decorScanDue = false;
            }

            if (decorScanDue)
            {
                // Cloud rebuilds at the player come first: they have a guaranteed session
                // slot, otherwise distant first builds would occupy the queue.
                RefreshMovingDecor(cameraPosition);
                EnsureVisibleDecor(cameraPosition);
                TrimDistantDecor(cameraPosition);
                lastDecorScanCamera = cameraPosition;
                hasDecorScanCamera = true;
                nextDecorScanTime = Time.unscaledTime + DecorScanMinInterval;
            }

            StepDecorBuilds();
            UpdateDecorCollision();
            DrawDecor(cameraPosition);

            LogFrameHitch();

            // [PHASE 0] Close the frame. Absolute counters run once per 2 s and only behind a
            // flag: FindObjectsOfTypeAll<Mesh> over 1000+ meshes causes a hitch and would
            // spoil exactly what is being measured.
            LogFrameHitch();
            SurfacePerf.MaybeScanSlowDiagnostics(LogGeometryStats);
            SurfacePerf.EndFrame(perfAllocBefore);
        }

        /// <summary>Hitch logger. State snapshot of the frames where the whole frame
        /// missed the threshold: the threshold is measured over the full frame, not only
        /// LateUpdate, because LOD switches and decor drawing happen in the render part
        /// of the PlayerLoop and are invisible to our LateUpdate.
        ///
        /// allocLU is managed bytes accrued during LateUpdate. If it is comparable to the
        /// hitch length, our managed allocation is to blame (e.g. Mesh.vertices when
        /// placing a session). If it is small while the hitch is big, it is foreign code
        /// in the frame or unmanaged work such as chunk finalization — that never shows
        /// up in the managed counter.</summary>
        private void LogFrameHitch()
        {
            if (Time.unscaledDeltaTime <= HitchLogThresholdMs)
            {
                return;
            }

            long allocNow = System.GC.GetAllocatedBytesForCurrentThread();
            Debug.Log($"[Hitch] frame={Time.unscaledDeltaTime * 1000f:F0}ms allocLU={allocNow - lateUpdateAllocBefore} "
                      + $"speed={decorSpeed:F0} alt={decorAltitude:F0} tier={SurfacePerf.MotionTier} "
                      + $"enq={SurfacePerf.ChunksEnqueued} fin={SurfacePerf.ChunksFinalized} "
                      + $"queued={SurfacePerf.QueuedBuilds} inflight={SurfacePerf.InFlightBuilds} "
                      + $"vis={SurfacePerf.VisibleChunks} desired={SurfacePerf.DesiredNodes} "
                      + $"keep={SurfacePerf.KeepCount} evict={SurfacePerf.EvictedLast} "
                      + $"cached={SurfacePerf.CachedChunks} "
                      + $"pools={SurfacePerf.DecorPools} inst={SurfacePerf.DecorInstances} "
                      + $"sessions={decorBuildSessions.Count} candidates={decorCandidates.Count} "
                      + $"live={SurfacePerf.LiveMeshes} tris={SurfacePerf.TerrainTriangles}");
        }

        /// <summary>
        /// Triplanar terrain texture UVs — camera-relative. Absolute coordinates from the
        /// planet centre (~1.14e6 m) quantize in float with ~6 cm steps (~2.5 texels at
        /// TextureScale 0.04), which produced moiré (stripes moving together with the
        /// origin). The shader uses the position delta from the camera (small exact numbers
        /// under the floating origin) rotated into body-fixed axes by _TerrainWorldToBody;
        /// the texture phase (UV fractions in double) comes from globals — the pattern
        /// sticks to the ground.
        /// </summary>
        private void UpdateUnderwaterCameraDepth(Vector3 cameraPosition)
        {
            Shader.SetGlobalFloat("_UnderwaterCameraDepth", 0f);
            if (Runner == null || Runner.DominantBody == null)
            {
                return;
            }

            Vector3d observer = FloatingOrigin.Anchor + AstroFrame.ToAstro(cameraPosition);

            // SubmersionDepthAt, NOT IsSubmergedAt. IsSubmergedAt additionally requires the
            // column under the camera to be WATER (IsWaterAt: raw seabed below sea level).
            // The water shader needs only “is the camera below the surface?”, and the extra
            // condition can lie: near the shore, over shallows or over the water mesh skirt
            // the camera is visually underwater while the column below is land, leaving
            // _UnderwaterCameraDepth at 0. The shader then draws the ceiling with its upper
            // branch: surf foam and screen-door dither seen from below.
            double depthMeters = WaterQuery.SubmersionDepthAt(Runner.DominantBody, observer, Runner.TimeSeconds);
            if (!double.IsNaN(depthMeters) && depthMeters > 0d)
            {
                Shader.SetGlobalFloat("_UnderwaterCameraDepth", (float)depthMeters);
            }
        }

        private void UpdateTerrainTextureOrigin()
        {
            if (terrain == null || !(terrain.TextureScale > 0d))
            {
                return;
            }

            double scale = terrain.TextureScale;
            Vector3d relative = Runner.PlayerPosition - currentBodyPosition;
            Vector3d bodyAstro = currentBodyOrientation.Conjugated.Rotate(relative);

            // Phases are in the shader's sim axes (positionWS): _TerrainWorldToBody gives the
            // delta in sim body axes, while the astro vector here is Z-up. Same bridge as
            // AstroFrame.ToSimulation: sim = (x, z, −y). Without the conversion the phase and
            // the delta live in different frames and the texture slides when moving.
            double sx = bodyAstro.X;
            double sy = bodyAstro.Z;
            double sz = -bodyAstro.Y;

            // World → body-fixed (mesh object axes): inverse of the chunk rotation.
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
        /// Water pattern anchor in body-fixed axes. Same issue as terrain textures
        /// (see UpdateTerrainTextureOrigin): the shader only sees render space, whose
        /// zero is FloatingOrigin.Anchor = the player position, so it rides along with
        /// the player. The shader takes the delta from the camera (small exact numbers)
        /// and rotates it into body-fixed axes with _WaterWorldToBody; the absolute
        /// phase (the player's body-fixed position in double) comes as the
        /// _WaterBodyAnchor global — the pattern is glued to the sea, not to the
        /// player. Float quantization at ~1.1e6 m radius is the same ~6 cm as terrain.
        ///
        /// The pattern is NOT passed as an absolute float32 anchor: an anchor of
        /// ~1.25e6 m gives 6-12 cm steps — every water position snapped to a grid and
        /// the ripple phase jumped by ~1 rad. Instead the shader receives a tangent
        /// basis (east/north) and a cumulative uvOffset, both in double and both modulo
        /// the period P = 1024 m. The shader takes uv = dot(relBody, east/north) +
        /// uvOffset, where relBody is the small delta from the camera, giving ~0.1 mm
        /// precision.
        /// </summary>
        private void UpdateWaterAnchor()
        {
            if (body == null || Runner == null)
            {
                return;
            }

            Vector3d relative = Runner.PlayerPosition - currentBodyPosition;
            Vector3d bodyAstro = currentBodyOrientation.Conjugated.Rotate(relative);

            // Pattern period 51 200 m = 1024 * 50. Waves use INTEGER vectors K with the
            // base 2*pi/1024 m; macro waves in p-space are scaled by _WaveScale. A
            // 51 200 m shift adds an integer number of 2*pi to any wave while _WaveScale
            // is a multiple of 0.02 (see ApplyWaterParams). The remainder is taken in
            // double BEFORE the cast to float: an absolute ~6e6 m anchor gives float32
            // steps up to 0.5 m and snaps the pattern onto a grid (blocks on the ceiling).
            const double patternPeriod = 51200.0;
            Vector3 anchorSim = new Vector3(
                (float)(bodyAstro.X % patternPeriod),
                (float)(bodyAstro.Z % patternPeriod),
                (float)((-bodyAstro.Y) % patternPeriod));

            Shader.SetGlobalMatrix("_WaterWorldToBody", Matrix4x4.Rotate(Quaternion.Inverse(currentBodyRotation)));
            Shader.SetGlobalVector("_WaterBodyAnchor", anchorSim);
        }

        /// <summary>
        /// Refresh trunk cylinder colliders (layers with Collides) for player physics:
        /// instances only from chunks within ~60 m of the player, positions in the inertial
        /// astro frame (like PlayerPosition).
        /// </summary>
        /// <summary>Wrapper for profiling: at speed this pass is O(chunks × layers)
        /// every frame, and without its own counter its share of the frame is invisible.</summary>
        private void UpdateMotionTier(float speed)
        {
            if (!EnableQualityTiers)
            {
                motionTier = MotionTier.Normal;
                SurfacePerf.MotionTier = 0;
                return;
            }

            MotionTier want = speed > TierExtremeSpeed
                ? MotionTier.Extreme
                : (speed > TierFastSpeed ? MotionTier.Fast : MotionTier.Normal);

            // Up — immediately: the faster you fly, the worse it gets without this.
            // Down — only after a delay, otherwise speed jitters around the threshold and
            // the LOD flaps every frame.
            if (want > motionTier)
            {
                motionTier = want;
                motionTierSince = Time.unscaledTime;
            }
            else if (want < motionTier
                && Time.unscaledTime - motionTierSince > TierDownDelaySeconds)
            {
                motionTier = want;
                motionTierSince = Time.unscaledTime;
            }

            SurfacePerf.MotionTier = (int)motionTier;
        }

        /// <summary>
        /// Predicted camera position PrefetchSeconds ahead.
        ///
        /// surfaceVelocityLocal lives in the body-fixed frame while the camera is in the
        /// render frame, so the velocity is rotated by the body rotation. Without it the
        /// prefetch would look the wrong way — kilometres off on a self-rotating planet.
        ///
        /// The speed is smoothed: a single one-frame spike at 2 km/s is 40 m of travel,
        /// and the “prefetch over there” decision would thrash on such an outlier.
        /// </summary>
        private void UpdatePredictedCamera(Vector3 cameraPosition)
        {
            float smooth = Mathf.Max(0.01f, PrefetchVelSmoothSeconds);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / smooth);
            smoothedSurfaceVelocity = Vector3.Lerp(
                smoothedSurfaceVelocity, surfaceVelocityLocal, blend);

            float leadSeconds = Mathf.Clamp(PrefetchSeconds, 0f, 3f);
            Vector3 lead = currentBodyRotation * (smoothedSurfaceVelocity * leadSeconds);

            // At low speed prefetch is useless: the offset is metres and the nodes are not
            // needed in time. The floor keeps prefetch from burning the node budget while
            // standing still.
            if (lead.magnitude < PrefetchMinLeadMeters)
            {
                lead = Vector3.zero;
            }

            predictedLeadMeters = lead.magnitude;
            predictedCamera = cameraPosition + lead;
        }

        /// <summary>
        /// Second traversal — prefetch. Walks from the predicted point, writes to its own
        /// list and does NOT touch desired: what is not visible now must not affect
        /// rendering.
        /// </summary>
        private void UpdatePrefetchPass()
        {
            if (!PrefetchEnabled || predictedLeadMeters <= 0f)
            {
                prefetchDesired.Clear();
                return;
            }

            prefetchDesired.Clear();
            prefetchSplitNext.Clear();
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                TraverseInto(
                    face, 0, 0, 0, predictedCamera,
                    prefetchDesired, prefetchSplitNodes, prefetchSplitNext,
                    MaxPrefetchNodes, EffectiveMaxDepth, EffectiveSplitFactor);
            }

            prefetchSplitNodes.Clear();
            prefetchSplitNodes.UnionWith(prefetchSplitNext);

            // Hysteresis is committed AFTER the traversal — merges lag by one frame, with
            // or without prefetch (see the splitNodes commit after Traverse).
            prefetchSplitNext.Clear();
        }

        private void UpdateDecorCollision()
        {
            using (SurfacePerf.UpdateDecorCollisionMarker.Auto())
            {
                UpdateDecorCollisionCore();
            }
        }

        /// <summary>
        /// Camera height above terrain and smoothed surface speed. Computed once per frame
        /// before all decor decisions, because builds, shadows and colliders all depend on
        /// these two numbers.
        ///
        /// Speed is smoothed exponentially, not averaged per frame: at 2 km/s a one-frame
        /// spike is 30 m of travel, so the “no decor needed” decision must rest on a stable
        /// signal, not a single outlier.
        ///
        /// Height uses the same formula as the camera near plane in
        /// FirstPersonCamera.UpdateClipPlanes: distance to the body centre minus Radius
        /// minus terrain height under the observer. A separate height query here would mean
        /// a second noise sample per frame.
        /// </summary>
        private void UpdateDecorMotionState(Vector3 cameraPosition)
        {
            float smooth = Mathf.Max(0.01f, DecorSpeedSmoothSeconds);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / smooth);
            decorSpeed = Mathf.Lerp(decorSpeed, surfaceVelocityLocal.magnitude, blend);

            double groundHeight = 0d;
            if (terrain != null)
            {
                groundHeight = CurrentTerrainHeightMeters();
            }

            double radius = (cameraPosition - bodyRenderPosition).magnitude;
            decorAltitude = (float)System.Math.Max(0d, radius - (body != null ? body.Radius : 0d) - groundHeight);

            SurfacePerf.DecorAltitude = decorAltitude;
            SurfacePerf.DecorSpeed = decorSpeed;
            SurfacePerf.TickDecorAccum(Time.unscaledDeltaTime);
        }

        /// <summary>Current terrain height under the player, m. Null if there is no body
        /// or terrain.</summary>
        private double CurrentTerrainHeightMeters()
        {
            if (Runner == null || body == null)
            {
                return 0d;
            }

            body.SurfaceLatLonAt(Runner.PlayerPosition, Runner.TimeSeconds, out double latDeg, out double lonDeg);
            return body.Terrain != null
                ? body.Terrain.GetHeightMeters(
                    body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d))
                : 0d;
        }

        /// <summary>Decor not needed: either too fast or already above the radius of the
        /// farthest layer. Called from EnsureVisibleDecor and RefreshMovingDecor;
        /// TrimDistantDecor deliberately does NOT call it — memory must be freed even when
        /// new builds are paused.
        ///
        /// includeSpeed=false lifts only the speed pause; the height check remains. This
        /// lets EnsureVisibleDecor finish cheap layers (trees, rocks, cacti) at speed:
        /// their session is far smaller than grass, and without them decor vanished
        /// entirely above DecorPauseSpeed — the new chunk was empty and the hidden parent
        /// was not drawn.</summary>
        private bool DecorBuildsSuspended(bool includeSpeed = true)
        {
            if (includeSpeed && decorSpeed > DecorPauseSpeed)
            {
                return true;
            }

            float maxLayerDistance = 0f;
            if (decorProfile != null && decorProfile.Layers != null)
            {
                for (int i = 0; i < decorProfile.Layers.Count; i++)
                {
                    GroundDecorLayer layer = decorProfile.Layers[i];
                    if (layer != null && layer.Enabled)
                    {
                        // EffectiveMaxDistance, not the raw MaxDistanceMeters: on a small
                        // body the layer radius hits the camera horizon, and builds must be
                        // suspended by it — otherwise at high altitude chunks beyond the
                        // horizon build for nothing.
                        maxLayerDistance = Mathf.Max(maxLayerDistance, EffectiveMaxDistance(layer));
                    }
                }
            }

            return maxLayerDistance > 0f && decorAltitude > maxLayerDistance;
        }

        /// <summary>Decor shadows make sense only close to the ground: at altitude a tree's
        /// shadow spot is subpixel while the shadow culling pass is real cost.</summary>
        private ShadowCastingMode DecorShadowMode(GroundDecorLayer layer)
        {
            return layer != null && layer.CastShadows && decorAltitude < DecorShadowMaxAltitude
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
        }

        private void UpdateDecorCollisionCore()
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

            // [PHASE 1] Above DecorCollisionMaxAltitude decor colliders are not needed:
            // nothing collides in noclip, and the pool walk is O(chunks × layers ×
            // instances) EVERY frame; at altitude it checks nothing since the player is
            // outside collisionRange. Return an empty registry, otherwise it would keep
            // earlier frames' entries.
            if (decorAltitude > DecorCollisionMaxAltitude)
            {
                GroundDecorCollisionRegistry.Begin(body.Name);
                return;
            }

            GroundDecorCollisionRegistry.Begin(body.Name);
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
                // Colliders are needed regardless of visibility: during an LOD transition a
                // nearby chunk may be hidden while its trunks are physically there.
                if (chunkDistance > collisionRange)
                {
                    continue;
                }

                Matrix4x4 chunkMatrix = chunk.Go.transform.localToWorldMatrix;
                for (int i = 0; i < chunk.Decor.Count; i++)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    GroundDecorLayer layer = runtime.Profile;
                    // A pool sinking under ground does not take part in collisions.
                    if (layer == null || !layer.Collides || runtime.Retiring
                        || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    int count = runtime.Instances.Length;
                    for (int k = 0; k < count; k++)
                    {
                        GroundDecorInstance instance = runtime.Instances[k];
                        // Sinking instances (the grass pool tail) do not take part in
                        // collisions — they are no longer on the surface.
                        if (instance.DeathTime > 0f)
                        {
                            continue;
                        }

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

        /// <summary>
        /// Quadtree traversal for DRAWING. A thin wrapper over the generic traversal:
        /// writes into desired, reads last frame's hysteresis and uses the current
        /// quality tier.
        /// </summary>
        private void Traverse(int face, int depth, int ix, int iy, Vector3 cameraPosition)
        {
            TraverseInto(
                face, depth, ix, iy, cameraPosition,
                desired, splitNodes, splitNext, MaxNodes, EffectiveMaxDepth, EffectiveSplitFactor);
        }

        /// <summary>
        /// Generic traversal. The prefetch pass differs from the main one only in start
        /// point, result set, hysteresis set pair, depth ceiling and node budget — hence it
        /// runs through the same code, not a copy of it.
        ///
        /// Hysteresis: splitRead is LAST frame's, splitWrite the current one. The
        /// “write, then commit” order delays merges by one frame, which is what damps LOD
        /// flapping. Prefetch must keep its own pair of sets, otherwise it would disturb
        /// the main traversal's decisions.
        /// </summary>
        private void TraverseInto(
            int face, int depth, int ix, int iy, Vector3 cameraPosition,
            List<Node> output, HashSet<long> splitRead, HashSet<long> splitWrite,
            int nodeBudget, int maxDepth, float splitFactor)
        {
            Vector3d dir = CubeSphere.NodeCenterDirection(face, depth, ix, iy);
            Vector3 worldCenter = NodeRenderPosition(dir * body.Radius);

            Vector3 bodyToCam = cameraPosition - bodyRenderPosition;
            Vector3 bodyToNode = worldCenter - bodyRenderPosition;
            if (bodyToNode.sqrMagnitude > 1e-6f && bodyToCam.sqrMagnitude > 1e-6f)
            {
                // The back hemisphere is not built.
                if (Vector3.Dot(bodyToNode.normalized, bodyToCam.normalized) < -0.15f)
                {
                    return;
                }
            }

            double nodeSize = body.Radius * 1.5707963267948966d / (1 << depth);
            float distance = Vector3.Distance(cameraPosition, worldCenter);
            // Distance to the CLOSEST point of the node, not to its centre: for a large
            // node the centre can sit far beyond the threshold while its edge is visible
            // right next to the camera. With centre-based distance such a node never split,
            // and when the camera turned, its edge "suddenly" needed detail — LOD pop/flicker.
            // Patch half-diagonal ≈ nodeSize·√2/2.
            float closestDistance = Mathf.Max(0f, distance - (float)(nodeSize * 0.70710678d));

            // Horizon culling. Checked BEFORE expanding: children lie inside the parent
            // face, so if the parent's closest point is behind the horizon, so are all
            // children. One such test cuts the whole far dome of the planet (Earth preset:
            // depth 2-6 is ~51 % of chunks, beyond 549 km at a ~341 km visibility limit).
            if (closestDistance > maxVisibleDistance)
            {
                return;
            }

            bool split;
            if (depth < maxDepth && output.Count + 4 < nodeBudget)
            {
                // Hysteresis: split at splitDistance, merge only once the node went
                // noticeably farther (×MergeHysteresis). Between the thresholds the
                // previous level is kept — otherwise the node "splits/merges" every
                // frame (LOD flapping, more noticeable when moving fast).
                double splitDistance = nodeSize * splitFactor;
                split = splitRead.Contains(NodeId(face, depth, ix, iy))
                    ? closestDistance < splitDistance * MergeHysteresis
                    : closestDistance < splitDistance;
            }
            else
            {
                split = false;
            }

            if (split)
            {
                splitWrite.Add(NodeId(face, depth, ix, iy));
                TraverseInto(face, depth + 1, (ix * 2) + 0, (iy * 2) + 0, cameraPosition,
                    output, splitRead, splitWrite, nodeBudget, maxDepth, splitFactor);
                TraverseInto(face, depth + 1, (ix * 2) + 1, (iy * 2) + 0, cameraPosition,
                    output, splitRead, splitWrite, nodeBudget, maxDepth, splitFactor);
                TraverseInto(face, depth + 1, (ix * 2) + 0, (iy * 2) + 1, cameraPosition,
                    output, splitRead, splitWrite, nodeBudget, maxDepth, splitFactor);
                TraverseInto(face, depth + 1, (ix * 2) + 1, (iy * 2) + 1, cameraPosition,
                    output, splitRead, splitWrite, nodeBudget, maxDepth, splitFactor);
                return;
            }

            output.Add(new Node { Face = face, Depth = depth, Ix = ix, Iy = iy });
        }

        /// <summary>
        /// Render position of a point given by a body-fixed vector from the body centre.
        /// Computed in DOUBLE (bodyPos + orientation·local − anchor), then cast to float:
        /// otherwise the sum of two large float values (~radius) loses precision and
        /// the chunk/camera jitters by tens of centimetres.
        /// </summary>
        private Vector3 NodeRenderPosition(Vector3d centerBodyFixed)
        {
            Vector3d absolute = currentBodyPosition + currentBodyOrientation.Rotate(centerBodyFixed);
            return FloatingOrigin.ToRender(absolute);
        }

        /// <summary>
        /// Whether all four children of the node are built. Only then can the parent be
        /// released: until that it plugs the hole of an unfinished quad. Four hash checks
        /// are cheaper than one extra active renderer.
        /// </summary>
        private bool HasAllBuiltChildren(int face, int depth, int ix, int iy)
        {
            int cx = ix * 2;
            int cy = iy * 2;
            int childDepth = depth + 1;
            return chunks.ContainsKey(NodeId(face, childDepth, cx, cy))
                && chunks.ContainsKey(NodeId(face, childDepth, cx + 1, cy))
                && chunks.ContainsKey(NodeId(face, childDepth, cx, cy + 1))
                && chunks.ContainsKey(NodeId(face, childDepth, cx + 1, cy + 1));
        }

        /// <summary>
        /// Chunk grid resolution for a given LOD depth: near levels
        /// (MaxDepth-ResolutionHalveEveryLevels+1 … MaxDepth) keep the full TileResolution,
        /// far ones halve every ResolutionHalveEveryLevels levels down to MinTileResolution.
        ///
        /// Why far-level halving is invisible. A quadtree leaf's closest point lies about
        /// SplitFactor·nodeSize from the centre, and the quad side = nodeSize/(res-1). The
        /// quad's angular size on screen = 1/(SplitFactor·(res-1)) and does NOT depend on
        /// depth or distance: resolution sets screen-space grid density, not scale. The
        /// full grid (res 65, SplitFactor 3.5) gives ~6.1 px per quad at FOV 60° and 1440p;
        /// halving raises it to ~12.7 px.
        ///
        /// With ResolutionHalveEveryLevels = 4 the full resolution holds for levels
        /// MaxDepth-3…MaxDepth — at SplitFactor 3.5 the whole 162 km radius around the
        /// player, i.e. almost everything visible from the surface with the naked eye.
        /// Only the far cone 162-393 km is roughened, collapsed at the horizon into a thin
        /// band where terrain is smooth and the grid step is unreadable.
        /// ResolutionHalveEveryLevels = 99 disables halving entirely.
        /// </summary>
        private int ResolutionForDepth(int depth)
        {
            int full = System.Math.Max(2, TileResolution);
            if (ResolutionHalveEveryLevels >= 99)
            {
                return full;
            }

            int stepsBelowFinest = MaxDepth - depth;
            if (stepsBelowFinest <= 0)
            {
                return full;
            }

            int shift = stepsBelowFinest / ResolutionHalveEveryLevels;
            int minRes = System.Math.Min(full, System.Math.Max(2, MinTileResolution));
            return System.Math.Max(minRes, full >> shift);
        }

        /// <summary>
        /// Triangles in a chunk mesh at this depth: core grid 2·res² plus four skirts of
        /// res quads. Computed with the same formula BuildChunk uses for indices, so the
        /// HUD counter matches what actually goes to the GPU.
        /// </summary>
        private int ChunkTriangleCount(int depth)
        {
            int res = ResolutionForDepth(depth);
            return (2 * res * res) + (8 * res);
        }

        /// <summary>
        /// Marker-only wrapper: the build body lives in BuildChunkCore. The marker shows
        /// how many milliseconds per frame chunk building costs, not just the count.
        /// </summary>
        /// <summary>
        /// Enqueues a node for building. No synchronous work here: only planning and
        /// priority.
        ///
        /// Priority — a “hole” outranks a leaf: an empty node is visible to the player
        /// immediately, while one half a kilometre away is not. Within a group, closer to
        /// the camera first.
        ///
        /// Dedup via queuedChunkIds is mandatory: otherwise the same node would be
        /// enqueued every frame until built, and at speed the queue would grow faster than
        /// flight. The set covers both queued and in-flight nodes: an id is removed only on
        /// finalization, so a separate “is it in flight” check would be the same search twice.
        /// </summary>
        private void EnqueueChunkBuild(Node node)
        {
            long id = NodeId(node);
            if (chunks.ContainsKey(id) || !queuedChunkIds.Add(id))
            {
                return;
            }

            chunkBuildQueue.Add(new ChunkQueueItem
            {
                Node = node,
                Id = id,
                Priority = HasBuiltAncestor(node) ? 1f : 0f,
                Distance = (float)ChunkDistanceFromCamera(node),
                EnqueuedFrame = Time.frameCount
            });
        }

        /// <summary>Whether at least one built parent exists. Without a parent the node
        /// gapes as a hole in the terrain, and that is the only thing the player sees right
        /// now.</summary>
        private bool HasBuiltAncestor(Node node)
        {
            int face = node.Face;
            int depth = node.Depth;
            int ix = node.Ix;
            int iy = node.Iy;
            while (depth > 0)
            {
                depth--;
                ix >>= 1;
                iy >>= 1;
                if (chunks.ContainsKey(NodeId(face, depth, ix, iy)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Distance to the CLOSEST point of the node — the same metric Traverse
        /// uses to decide splitting. Mixing “to centre” and “to edge” is not allowed: then
        /// a near large node would rank farther than a far small one and the queue order
        /// would break.</summary>
        private double ChunkDistanceFromCamera(Node node)
        {
            Vector3d dir = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy);
            double nodeSize = body.Radius * 1.5707963267948966d / (1 << node.Depth);
            Vector3 worldCenter = NodeRenderPosition(dir * body.Radius);
            float distance = Vector3.Distance(lastCameraPosition, worldCenter);
            return System.Math.Max(0d, distance - (nodeSize * 0.70710678d));
        }

        /// <summary>
        /// Queues prefetch nodes. Priority is WORSE than the leaf and worse than the hole:
        /// prefetch is an optimization for the future, not what is visible now.
        ///
        /// Their ids go into prefetchKeep, not keep: keep defines VISIBILITY, so
        /// prefetch nodes must not become visible. Nor can they be unloaded while
        /// queued — otherwise they are recycled and rebuilt in a loop.
        ///
        /// Back-of-camera culling (FOV+30° cone) applies ONLY here. A node must not enter
        /// desired under any condition: an off-screen node still draws if it is in keep.
        /// </summary>
        private void EnqueuePrefetchChunks(Vector3 cameraPosition)
        {
            prefetchKeep.Clear();
            if (!PrefetchEnabled || prefetchDesired.Count == 0)
            {
                SurfacePerf.PrefetchQueued = 0;
                return;
            }

            Vector3 forward = cameraPosition - bodyRenderPosition;
            float backConeCos = BuildBackConeCos();

            int queued = 0;
            for (int i = 0; i < prefetchDesired.Count; i++)
            {
                Node node = prefetchDesired[i];
                long id = NodeId(node);
                if (chunks.ContainsKey(id))
                {
                    continue;
                }

                prefetchKeep.Add(id);
                if (queued >= MaxPrefetchQueued)
                {
                    continue;
                }

                if (backConeCos > -1f)
                {
                    Vector3d dir = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy);
                    Vector3 toNode = NodeRenderPosition(dir * body.Radius) - cameraPosition;
                    if (toNode.sqrMagnitude > 1e-6f && forward.sqrMagnitude > 1e-6f
                        && Vector3.Dot(toNode.normalized, forward.normalized) < backConeCos)
                    {
                        // Back cone: the camera will not look there within the lead time,
                        // nothing to build.
                        continue;
                    }
                }

                EnqueuePrefetchChunk(node);
                queued++;
            }

            SurfacePerf.PrefetchQueued = queued;
            SurfacePerf.PrefetchNodes = prefetchDesired.Count;
        }

        /// <summary>Cosine of the “no building behind the back” angle. The camera FOV is
        /// vertical while the cone is 3D, so half the diagonal is used:
        /// tan(diag/2) = tan(v/2)·sqrt(1 + (h/v)²), plus a 30° margin. No camera — no cull
        /// (cosine -1 = “never”).</summary>
        private float BuildBackConeCos()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return -1f;
            }

            float halfV = cam.fieldOfView * 0.5f * (Mathf.PI / 180f);
            float aspect = cam.aspect > 0.01f ? cam.aspect : 1.7778f;
            float halfDiag = Mathf.Atan(Mathf.Tan(halfV) * Mathf.Sqrt(1f + aspect * aspect));
            halfDiag += 30f * (Mathf.PI / 180f);
            return Mathf.Cos(Mathf.Clamp(halfDiag, 0f, 3.14159f));
        }

        private void EnqueuePrefetchChunk(Node node)
        {
            long id = NodeId(node);
            if (chunks.ContainsKey(id) || !queuedChunkIds.Add(id))
            {
                return;
            }

            chunkBuildQueue.Add(new ChunkQueueItem
            {
                Node = node,
                Id = id,
                // 2 — worse than a leaf (1) and worse than a hole (0).
                Priority = 2f,
                Distance = (float)ChunkDistanceFromCamera(node),
                EnqueuedFrame = Time.frameCount
            });
        }

        /// <summary>
        /// Build scheduler.
        ///
        /// The budget is in milliseconds: per-frame work is waiting for finished jobs
        /// (usually 0) plus uploading vertices to the mesh. Upload costs on the order of a
        /// hundred microseconds per chunk, so at the normal 1.5 ms pace that is a few chunks
        /// per frame. If the queue still grows (it will at 2 km/s), the fix is not the budget
        /// but the quality tier in PHASE 3: build less and more rarely, not upload faster.
        ///
        /// The exception from FinalizeChunk is caught DELIBERATELY: one bad chunk must not
        /// take down the whole LateUpdate (EvictIfNeeded, counters, decor and frame end
        /// would be skipped and a half-filled chunk left in the cache with an empty mesh).
        /// One bad node must not take the frame with it.
        /// </summary>
        private void PumpChunkBuilds()
        {
            if (!AsyncChunkBuild)
            {
                return;
            }

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();

            // Stale entry filtering. A node that entered desired a couple of frames ago and
            // has since moved behind the camera is pointless to build: by the time its turn
            // comes it will be off-screen anyway. The threshold is a few frames rather than
            // zero: a node is still needed while it flickers in desired across frames at an
            // LOD boundary.
            int staleBefore = Time.frameCount - MaxChunkQueueAgeFrames;
            int dropped = 0;
            for (int i = chunkBuildQueue.Count - 1; i >= 0; i--)
            {
                if (chunkBuildQueue[i].EnqueuedFrame >= staleBefore)
                {
                    continue;
                }

                queuedChunkIds.Remove(chunkBuildQueue[i].Id);
                chunkBuildQueue.RemoveAt(i);
                dropped++;
            }

            SurfacePerf.QueueDropped = dropped;

            for (int i = chunkBuildsInFlight.Count - 1; i >= 0; i--)
            {
                ChunkBuildInFlight entry = chunkBuildsInFlight[i];
                if (!entry.Request.Handle.IsCompleted)
                {
                    continue;
                }

                if (ElapsedMs(startTicks) > ChunkFinalizeBudgetMs)
                {
                    break;
                }

                entry.Request.Handle.Complete();
                chunkBuildsInFlight.RemoveAt(i);
                queuedChunkIds.Remove(entry.Id);

                if (entry.Request.ParamsVersion != paramsVersion || !paramsAlive)
                {
                    SurfaceChunkPool.Return(entry.Request);
                    continue;
                }

                // Isolate finalize: any failure here must cost one chunk, not the whole
                // frame.
                bool finalized = false;
                try
                {
                    FinalizeChunk(entry);
                    finalized = true;
                }
                catch (System.Exception exception)
                {
                    SurfacePerf.FinalizeErrors++;
                    Debug.LogError("[PlanetSurfaceRenderer] чанк " + entry.Id
                        + " не удалось залить в меш: " + exception);
                }

                if (!finalized)
                {
                    AbandonChunk(entry.Id);
                }

                SurfaceChunkPool.Return(entry.Request);
            }

            // Keep slots occupied: while free ones exist, jobs run overlapped with
            // rendering. They pile up only under a hard limit, and then they wait for the
            // CPU by themselves.
            while (chunkBuildsInFlight.Count < MaxChunkJobsInFlight
                && TryDequeueBest(out ChunkQueueItem item))
            {
                ScheduleChunkBuild(item);
            }

            JobHandle.ScheduleBatchedJobs();
        }

        /// <summary>Removes from the cache a chunk that failed to upload: it would stay
        /// there with an empty mesh and draw as a grey hole. The slot in queuedChunkIds is
        /// freed so the node can be rebuilt on the next frame.</summary>
        private void AbandonChunk(long id)
        {
            queuedChunkIds.Remove(id);
            if (!chunks.TryGetValue(id, out Chunk chunk))
            {
                return;
            }

            SetChunkVisible(chunk, false);
            DestroyChunkResources(chunk);
            chunks.Remove(id);
            visibleChunks.Remove(chunk);
        }

        /// <summary>Pops the highest-priority node, closest first within a priority.
        /// Linear selection, not sorting: sorting every frame for one element costs more
        /// than walking the list.</summary>
        private bool TryDequeueBest(out ChunkQueueItem best)
        {
            best = default;
            int bestIndex = -1;
            for (int i = 0; i < chunkBuildQueue.Count; i++)
            {
                if (bestIndex < 0)
                {
                    bestIndex = i;
                    continue;
                }

                ChunkQueueItem candidate = chunkBuildQueue[i];
                ChunkQueueItem current = chunkBuildQueue[bestIndex];
                if (candidate.Priority < current.Priority
                    || (candidate.Priority == current.Priority && candidate.Distance < current.Distance))
                {
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return false;
            }

            best = chunkBuildQueue[bestIndex];
            chunkBuildQueue.RemoveAt(bestIndex);
            return true;
        }

        private void ScheduleChunkBuild(ChunkQueueItem item)
        {
            Node node = item.Node;
            int res = ResolutionForDepth(node.Depth);
            int n = res + 1;
            int grid = n + 2;
            int totalVerts = (n * n) + (4 * n);

            ChunkBuildRequest request = SurfaceChunkPool.Rent();
            SurfaceChunkPool.Prepare(request, res, n, grid, totalVerts);
            request.Plan = new ChunkNodePlan { Face = node.Face, Depth = node.Depth, Ix = node.Ix, Iy = node.Iy };
            request.ParamsVersion = paramsVersion;
            request.Priority = item.Priority;

            double sizeUv = 1d / (1 << node.Depth);
            double u0 = node.Ix * sizeUv;
            double v0 = node.Iy * sizeUv;
            double stepUv = sizeUv / (n - 1);

            Vector3d centerAstro = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy) * body.Radius;
            double amplitude = System.Math.Max(1d, terrain.AmplitudeMeters);
            double skirtDepth = body.Radius * 1.5707963267948966d / (1 << node.Depth) * SkirtFactor;

            // Chain of three jobs. Dependencies via JobHandle; no Complete() on the main
            // thread — it only collects finished results within the budget.
            JobHandle fill = new FillDirectionsJob
            {
                Face = node.Face,
                Grid = grid,
                U0 = u0,
                V0 = v0,
                StepUv = stepUv,
                Dirs = request.Dirs
            }.Schedule(grid * grid, 64, new JobHandle());

            JobHandle tile = new TerrainTileJob
            {
                Params = noiseParams,
                Directions = request.Dirs,
                Heights = request.Heights,
                ColorMasks = request.Masks,
                ColorDetails = request.Details
            }.Schedule(grid * grid, 64, fill);

            request.Handle = new AssembleChunkMeshJob
            {
                Dirs = request.Dirs,
                Heights = request.Heights,
                Masks = request.Masks,
                Details = request.Details,
                Res = res,
                N = n,
                Grid = grid,
                CoreCount = n * n,
                TotalVerts = totalVerts,
                Amplitude = amplitude,
                Radius = body.Radius,
                SeaLevel = terrain.SeaLevelMeters,
                SkirtDepth = skirtDepth,
                WaterSkirtDepth = skirtDepth * WaterSkirtFactor,
                CenterAstro = new double3(centerAstro.X, centerAstro.Y, centerAstro.Z),
                Positions = request.Positions,
                Vertices = request.Vertices,
                WaterVertices = request.WaterVertices,
                WaterFlags = request.WaterFlags,
                BoundsMinMax = request.BoundsMinMax
            }.Schedule(tile);

            request.CenterAstro = centerAstro;
            request.SkirtDepth = skirtDepth;
            chunkBuildsInFlight.Add(new ChunkBuildInFlight { Id = item.Id, Request = request });
        }

        private static double ElapsedMs(long startTicks)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000d
                / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>
        /// Upload of the finished buffer into the mesh. The only expensive operation on the
        /// main thread, and it copies memory without computing.
        ///
        /// Indices come from a shared cache by res: the topology is identical for all
        /// chunks of the same grid, so building a 51840-element int[] per chunk meant both
        /// an allocation and extra work for Unity.
        /// </summary>
        private void FinalizeChunk(ChunkBuildInFlight entry)
        {
            ChunkBuildRequest request = entry.Request;
            Node node = new Node
            {
                Face = request.Plan.Face,
                Depth = request.Plan.Depth,
                Ix = request.Plan.Ix,
                Iy = request.Plan.Iy
            };

            long id = NodeId(node);
            Chunk chunk = chunks.TryGetValue(id, out Chunk existing) ? existing : CreateChunkShell(node);

            // The chunk enters the cache LAST. Registering before upload would leave a node
            // with an empty mesh in the cache on any failure: it would become visible at
            // once and draw as a grey hole, and would not be rebuilt since “it is in chunks”.
            chunk.CenterAstro = request.CenterAstro;
            chunk.Node = node;
            chunk.Depth = node.Depth;
            chunk.Res = request.Res;
            chunk.Go.transform.position = NodeRenderPosition(request.CenterAstro);
            chunk.Go.transform.rotation = currentBodyRotation;

            Mesh mesh = chunk.Mesh;
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertexBufferParams(
                request.TotalVerts,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
            mesh.SetVertexBufferData(
                request.Vertices, 0, 0, request.TotalVerts, 0,
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            // The shared buffer matches the required length exactly, so it is uploaded whole
            // from scratch. In Unity 6 SetIndexBufferData has five parameters (no
            // startIndex), hence the call shape.
            NativeArray<ushort> indices = SurfaceChunkPool.GetSharedIndices(request.Res);
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(
                indices, 0, 0, indices.Length,
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(
                0, new SubMeshDescriptor(0, indices.Length),
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            // Same bounds slack as in the synchronous path: the skirt is inside the mesh
            // already, RecalculateBounds is replaced by a job-side compute, so Expand starts
            // from the same AABB.
            Bounds bounds = BoundsFrom(request.BoundsMinMax[0], request.BoundsMinMax[1]);
            bounds.Expand((float)(request.SkirtDepth * 1.5d) + (bounds.size.magnitude * 0.001f));
            mesh.bounds = bounds;

            if (request.HasWater)
            {
                BuildAsyncWaterMesh(chunk, request);
            }

            // The chunk is intact — register and show it.
            chunks[id] = chunk;
            if (!chunk.Visible)
            {
                SetChunkVisible(chunk, true);
            }

            SurfacePerf.ChunksFinalized++;
        }

        private static Bounds BoundsFrom(float3 min, float3 max)
        {
            var center = new Vector3(
                (min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, (min.z + max.z) * 0.5f);
            var size = new Vector3(max.x - min.x, max.y - min.y, max.z - min.z);
            return new Bounds(center, size);
        }

        private void BuildAsyncWaterMesh(Chunk chunk, ChunkBuildRequest request)
        {
            Material waterMaterial = GetWaterMaterial();
            if (waterMaterial == null)
            {
                return;
            }

            if (chunk.WaterMesh == null)
            {
                chunk.WaterMesh = new Mesh();
                SurfacePerf.NoteMeshCreated();
            }

            if (chunk.WaterGo == null)
            {
                chunk.WaterGo = new GameObject("Water");
                chunk.WaterGo.transform.SetParent(chunk.Go.transform, false);
                chunk.WaterGo.transform.localPosition = Vector3.zero;
                chunk.WaterGo.transform.localRotation = Quaternion.identity;
                chunk.WaterGo.transform.localScale = Vector3.one;
                chunk.WaterGo.AddComponent<MeshFilter>().sharedMesh = chunk.WaterMesh;
                MeshRenderer waterRenderer = chunk.WaterGo.AddComponent<MeshRenderer>();
                waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
                waterRenderer.receiveShadows = false;
                waterRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                waterRenderer.lightProbeUsage = LightProbeUsage.Off;
                waterRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                waterRenderer.allowOcclusionWhenDynamic = false;
                waterRenderer.enabled = chunk.Visible;
                chunk.WaterRenderer = waterRenderer;
                waterChunkCount++;
            }

            chunk.WaterRenderer.sharedMaterial = waterMaterial;

            Mesh mesh = chunk.WaterMesh;
            mesh.Clear();
            mesh.indexFormat = IndexFormat.UInt16;
            mesh.SetVertexBufferParams(
                request.TotalVerts,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4));
            mesh.SetVertexBufferData(
                request.WaterVertices, 0, 0, request.TotalVerts, 0,
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            NativeArray<ushort> indices = SurfaceChunkPool.GetSharedIndices(request.Res);
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt16);
            mesh.SetIndexBufferData(
                indices, 0, 0, indices.Length,
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(
                0, new SubMeshDescriptor(0, indices.Length),
                MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds);

            Bounds bounds = BoundsFrom(request.BoundsMinMax[0], request.BoundsMinMax[1]);
            bounds.Expand(chunk.BoundsRadius);
            mesh.bounds = bounds;
        }

        /// <summary>Chunk skeleton: the object, mesh and renderer are created once per node
        /// and reused on rebuild.</summary>
        private Chunk CreateChunkShell(Node node)
        {
            var chunk = new Chunk
            {
                Go = new GameObject("PlanetChunk_" + node.Face + "_" + node.Depth + "_" + node.Ix + "_" + node.Iy),
                Mesh = new Mesh()
            };
            SurfacePerf.NoteMeshCreated();
            chunk.Go.transform.SetParent(surfaceRoot, false);
            chunk.Go.transform.localScale = Vector3.one;
            chunk.Node = node;
            chunk.Depth = node.Depth;
            // The synchronous path always builds at the full resolution for the depth.
            chunk.Res = ResolutionForDepth(node.Depth);
            chunk.BoundsRadius = (float)(body.Radius * 1.5707963267948966d / (1 << node.Depth) * 0.70710678d);
            MeshFilter filter = chunk.Go.AddComponent<MeshFilter>();
            chunk.Renderer = chunk.Go.AddComponent<MeshRenderer>();
            chunk.Renderer.sharedMaterial = GetMaterial();
            chunk.Renderer.enabled = false;
            filter.sharedMesh = chunk.Mesh;
            ApplyChunkRenderFlags(chunk, node.Depth);
            chunk.Visible = false;
            return chunk;
        }

        /// <summary>Wait for and release all in-flight requests. Required before
        /// ClearChunkCache and OnDestroy: persistent rent arrays and shared index buffers
        /// would otherwise live until domain reload and Unity would report a native memory
        /// leak.</summary>
        private void CancelAllChunkBuilds()
        {
            for (int i = chunkBuildsInFlight.Count - 1; i >= 0; i--)
            {
                ChunkBuildInFlight entry = chunkBuildsInFlight[i];
                entry.Request.Handle.Complete();
                SurfaceChunkPool.Return(entry.Request);
            }

            chunkBuildsInFlight.Clear();
            chunkBuildQueue.Clear();
            queuedChunkIds.Clear();
        }

        private void BuildChunk(Node node)
        {
            using (SurfacePerf.BuildChunkMarker.Auto())
            {
                BuildChunkCore(node);
            }
        }

        private void BuildChunkCore(Node node)
        {
            int res = ResolutionForDepth(node.Depth);
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

            // Node centre (body-fixed, double): mesh vertices are stored LOCALLY relative to
            // it. Otherwise float at planet radius (~1.1e6 m) gives ulp ~0.06 m → camera and
            // terrain jitter; huge vertices also break camera-relative rendering (body
            // transform at anchor ~0).
            Vector3d centerAstro = CubeSphere.NodeCenterDirection(node.Face, node.Depth, node.Ix, node.Iy) * body.Radius;
            Vector3 centerUnity = AstroFrame.ToSimulation(centerAstro);

            double amplitude = System.Math.Max(1d, terrain.AmplitudeMeters);
            double seaLevel = terrain.SeaLevelMeters;
            // Sea-level tolerance — the same as in PlanetSurface.shader (SeaEpsilon):
            // ABSOLUTE metres, almost independent of amplitude. Kept consistent with the
            // shader because seaEps is used in the diagnostic log below, and otherwise the
            // log would lie about the land fraction.
            double seaEps = System.Math.Max(0.05d, amplitude * 1e-6d);

            // Tiles with a 1-vertex overlap — normals from central differences.
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

            // Mask/detail colour is now computed per pixel in the shader — the tile job no
            // longer needs those streams. The edit is made on a LOCAL copy so ParamsEqual
            // against the cached noiseParams is not broken.
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
                    // The ocean floor is NOT clamped to sea level: geometry goes to the raw
                    // floor (floor physics — WaterQuery, visuals — darkening in the shader),
                    // with a separate transparent water mesh on top (BuildWaterMesh). Ships
                    // still land on the clamped height (GetHeightMeters) — as on water.
                    double rawHeight = jobHeights[vi] * amplitude;
                    Vector3d absAstro = new Vector3d(d.x, d.y, d.z) * (body.Radius + rawHeight);
                    positions[vi] = AstroFrame.ToSimulation(absAstro - centerAstro);
                }
            }

            // dirs/jobHeights are still needed for the per-pixel attributes below.
            var vertices = new Vector3[totalVerts];
            var normals = new Vector3[totalVerts];
            // Albedo: fragment shader interpolates per-vertex data (vertex color on coarse LOD
            // gave blocky patch borders). We pass what geometry cannot derive: body-fixed direction,
            // raw height (pre-sea-clamp), slope cosine (rock/snow bands) AND the CPU color mask +
            // detail — the same pattern the decor placement reads (parity paint/placement).
            var surfaceDirs = new Vector3[totalVerts];
            var surfaceExtra = new Vector4[totalVerts];
            // Chunk water plane: the same core vertices projected onto sea level (water
            // physics is flat; waves only in shader normals). Per-vertex depth drives
            // colour/foam/alpha.
            var waterVerts = new Vector3[coreCount];
            var waterNormals = new Vector3[coreCount];
            var waterExtra = new Vector4[coreCount];
            bool hasWater = seaLevel > -1e29d;

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

                    if (hasWater)
                    {
                        // One water quad per chunk (the shoreline is cut by the
                        // terrain itself); over land depth is 0 — no foam there.
                        Vector3d absWater = new Vector3d(d.x, d.y, d.z) * (body.Radius + seaLevel);
                        waterVerts[index] = AstroFrame.ToSimulation(absWater - centerAstro);
                        waterNormals[index] = (waterVerts[index] + centerUnity).normalized;
                        waterExtra[index] = new Vector4((float)System.Math.Max(0d, seaLevel - rawHeight), 0f, 0f, 0f);
                    }
                }
            }

            // The whole chunk is over the sea — no water is built.
            if (hasWater)
            {
                hasWater = false;
                for (int k = 0; k < coreCount; k++)
                {
                    if (waterExtra[k].x > 0f)
                    {
                        hasWater = true;
                        break;
                    }
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

            // Skirts: duplicate edges sunk toward the planet centre — they cover the
            // cracks between adjacent LOD levels.
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
            // [PHASE 0] Live-mesh counter. A Mesh does not belong to a GameObject:
            // Destroy(chunk.Go) does NOT remove it, so the leak is invisible in the
            // hierarchy and in GameObject memory — only in this counter.
            SurfacePerf.NoteMeshCreated();
            // No MarkDynamic(): a chunk mesh is built ONCE and never changes
            // (only its transform moves). MarkDynamic() keeps it on the dynamic VB
            // path without format optimization and re-uploads it instead of caching —
            // on 1000+ chunks that is extra memory and extra work every frame.
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
            ApplyChunkRenderFlags(chunk, node.Depth);

            chunk.Mesh.Clear();
            chunk.Mesh.vertices = vertices;
            chunk.Mesh.normals = normals;
            chunk.Mesh.SetUVs(1, new List<Vector3>(surfaceDirs));
            chunk.Mesh.SetUVs(2, new List<Vector4>(surfaceExtra));
            chunk.Mesh.triangles = triangles;
            chunk.Mesh.RecalculateBounds();
            // Bounds margin. RecalculateBounds already gives the true mesh AABB
            // (camera-relative coordinates, skirt inside the mesh). Do not inflate
            // by BoundsRadius: that is the node's BOUNDING-SPHERE RADIUS, while the
            // cube face is flat (half a node) and Bounds.Expand grows along all six
            // axes. Keep the skirt plus a relative tolerance for float jitter when
            // the chunk position changes (floating origin / camera movement).
            Bounds paddedBounds = chunk.Mesh.bounds;
            paddedBounds.Expand(skirtDepth * 1.5f + (paddedBounds.size.magnitude * 0.001f));
            chunk.Mesh.bounds = paddedBounds;

            if (hasWater)

            {
                BuildWaterMesh(chunk, waterVerts, waterNormals, waterExtra, n, triangles, skirtDepth * WaterSkirtFactor, centerUnity);
            }

            chunk.Visible = true;
            chunk.Renderer.enabled = true;
            visibleChunks.Add(chunk);
            chunks[id] = chunk;
        }

        /// <summary>
        /// Chunk water surface: core grid at sea level + skirts down
        /// (like terrain, they hide cracks between neighbouring LODs at seams,
        /// otherwise a "staircase" of gaps down to the seabed/sky shows at the shore),
        /// per-vertex depth in UV2.x for the shader's color/foam/alpha. Child of
        /// the chunk — visibility and eviction inherit from the parent. No shadow
        /// casting or receiving: water is lit by its own shader (sun + sky).
        /// </summary>
        /// <summary>
        /// Cheap chunk renderer flags. None of them change the on-screen
        /// image, but they remove work that adds up across 1000+ chunks.
        ///
        /// motionVectorGenerationMode: a chunk moves by transform every frame
        /// (floating origin + camera movement), so without this flag Unity computes
        /// motion vectors for it that never reach the image.
        ///
        /// Motion vectors are disabled in the active HDRP asset (supportMotionVectors: 0),
        /// so this flag currently saves nothing; it guards against future enablement.
        ///
        /// probe usage off: terrain is not dynamic and receives no light or
        /// reflections — probe values are not read from it.
        ///
        /// shadowCastingMode: terrain shadows are only needed near the observer.
        /// Far levels lie beyond the shadow range anyway, but with the flag On
        /// they still run a culling test in every shadow pass. Depths are counted
        /// from the CURRENT MaxDepth, not the speed tier's EffectiveMaxDepth:
        /// otherwise ground shadows would flicker along with LOD rebuilds.
        /// </summary>
        private void ApplyChunkRenderFlags(Chunk chunk, int depth)
        {
            Renderer renderer = chunk.Renderer;
            if (renderer == null)
            {
                return;
            }

            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.shadowCastingMode = depth >= MaxDepth - TerrainShadowDepthBand
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
        }

        private void BuildWaterMesh(Chunk chunk, Vector3[] coreVerts, Vector3[] coreNormals, Vector4[] coreExtra, int n, int[] coreTriangles, float skirtDepth, Vector3 centerUnity)
        {
            int res = n - 1;
            int perEdge = n;
            int coreCount = n * n;
            int totalVerts = coreCount + (4 * perEdge);
            int coreTriCount = res * res * 6;
            int totalTris = coreTriCount + (4 * res * 6);

            var waterVerts = new Vector3[totalVerts];
            var waterNormals = new Vector3[totalVerts];
            var waterExtra = new Vector4[totalVerts];
            System.Array.Copy(coreVerts, waterVerts, coreCount);
            System.Array.Copy(coreNormals, waterNormals, coreCount);
            System.Array.Copy(coreExtra, waterExtra, coreCount);

            // Skirts: duplicate the edges and sink them toward the planet centre — as for terrain.
            // Depth/normal as at the edge: the shader draws the edge per-pixel
            // from the depth buffer, so no staircase from the coarse grid.
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

                    Vector3 p = waterVerts[coreIndex];
                    Vector3 inward = (p + centerUnity).normalized;
                    waterVerts[baseIndex + k] = p - (inward * skirtDepth);
                    waterNormals[baseIndex + k] = waterNormals[coreIndex];
                    waterExtra[baseIndex + k] = waterExtra[coreIndex];
                }
            }

            var waterTris = new int[totalTris];
            System.Array.Copy(coreTriangles, waterTris, coreTriCount);
            int t = coreTriCount;
            for (int edge = 0; edge < 4; edge++)
            {
                int baseIndex = coreCount + (edge * perEdge);
                for (int k = 0; k < res; k++)
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
                    waterTris[t++] = a;
                    waterTris[t++] = c;
                    waterTris[t++] = b;
                    waterTris[t++] = b;
                    waterTris[t++] = c;
                    waterTris[t++] = d;
                }
            }

            chunk.WaterMesh = new Mesh();
            SurfacePerf.NoteMeshCreated();
            chunk.WaterMesh.vertices = waterVerts;
            chunk.WaterMesh.normals = waterNormals;
            chunk.WaterMesh.SetUVs(2, new System.Collections.Generic.List<Vector4>(waterExtra));
            chunk.WaterMesh.triangles = waterTris;
            chunk.WaterMesh.RecalculateBounds();
            Bounds padded = chunk.WaterMesh.bounds;
            padded.Expand(chunk.BoundsRadius);
            chunk.WaterMesh.bounds = padded;

            chunk.WaterGo = new GameObject("Water");
            chunk.WaterGo.transform.SetParent(chunk.Go.transform, false);
            chunk.WaterGo.transform.localPosition = Vector3.zero;
            chunk.WaterGo.transform.localRotation = Quaternion.identity;
            chunk.WaterGo.transform.localScale = Vector3.one;
            MeshFilter filter = chunk.WaterGo.AddComponent<MeshFilter>();
            filter.sharedMesh = chunk.WaterMesh;
            MeshRenderer renderer = chunk.WaterGo.AddComponent<MeshRenderer>();
            Material waterMaterial = GetWaterMaterial();
            if (waterMaterial == null)
            {
                Destroy(chunk.WaterGo);
                Destroy(chunk.WaterMesh);
                SurfacePerf.NoteMeshDestroyed();
                chunk.WaterGo = null;
                chunk.WaterMesh = null;
                return;
            }

            renderer.sharedMaterial = waterMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // [PHASE 1] Water needs no probes for the same reason as terrain.
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            chunk.WaterRenderer = renderer;
            waterChunkCount++;
        }

        private Material GetWaterMaterial()
        {
            if (waterMaterialCache == null)
            {
                // Visible stylised sea: built-in HDRP/Unlit aquamarine
                // is always drawn and does not blow out
                // from the physical 80k lux (unlike Lit); transparency gives a depth
                // gradient over the seabed. The custom WaterSurface with waves/foam is
                // WIP and currently emits no pixels (fixed separately).
                if (UseLitWaterFallback)
                {
                    Shader unlit = Shader.Find("HDRP/Unlit");
                    if (unlit == null)
                    {
                        Debug.LogError("[PlanetSurface] шейдер HDRP/Unlit не найден — водные меши нечем рисовать.");
                        return null;
                    }

                    waterMaterialCache = new Material(unlit);
                    waterMaterialCache.SetFloat("_SurfaceType", 1f);
                    ApplyWaterParams();
                    return waterMaterialCache;
                }

                Shader shader = Shader.Find("Galilego/WaterSurface");
                if (shader == null)
                {
                    Debug.LogError("[PlanetSurface] шейдер Galilego/WaterSurface не найден — водные меши нечем рисовать.");
                    return null;
                }

                waterMaterialCache = new Material(shader);
                ApplyWaterParams();
            }

            return waterMaterialCache;
        }

        /// <summary>
        /// Water diagnostics: which material the chunks actually use, whether its
        /// shader compiled and whether the renderers draw it at all. Without this,
        /// "water invisible" and "water visible but grey" look the same.
        /// </summary>
        [ContextMenu("Galilego/Water probe")]
        private void WaterProbe()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== WATER PROBE ===");
            sb.AppendLine("UseLitWaterFallback = " + UseLitWaterFallback);

            Material m = waterMaterialCache;
            if (m == null)
            {
                sb.AppendLine("waterMaterialCache == null");
                Debug.Log(sb.ToString());
                return;
            }

            Shader sh = m.shader;
            sb.AppendLine("material shader = " + (sh != null ? sh.name : "null")
                + ", isSupported = " + (sh != null && sh.isSupported));
            sb.AppendLine("renderQueue = " + m.renderQueue + ", passCount = " + m.passCount);
            for (int i = 0; i < m.passCount; i++)
            {
                string pn = m.GetPassName(i);
                sb.AppendLine("  pass " + i + " = '" + pn + "', enabled = " + m.GetShaderPassEnabled(pn));
            }

            if (m.HasProperty("_UnderwaterDebugMode"))
            {
                sb.AppendLine("_UnderwaterDebugMode = " + m.GetFloat("_UnderwaterDebugMode"));
            }
            else
            {
                sb.AppendLine("материал НЕ имеет _UnderwaterDebugMode (это не WaterSurface)");
            }

#if UNITY_EDITOR
            if (sh != null)
            {
                var msgs = UnityEditor.ShaderUtil.GetShaderMessages(sh);
                sb.AppendLine("shader messages = " + msgs.Length);
                for (int i = 0; i < msgs.Length && i < 12; i++)
                {
                    sb.AppendLine("  [" + msgs[i].severity + "] " + msgs[i].message
                        + " (" + msgs[i].file + ":" + msgs[i].line + ")");
                }
            }
#endif

            Camera cam = Camera.main != null ? Camera.main : FindObjectOfType<Camera>();
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            int total = 0, enabledCount = 0, containing = 0, forcedOff = 0, wrongLayer = 0, visible = 0;
            foreach (MeshRenderer r in FindObjectsOfType<MeshRenderer>())
            {
                if (!ReferenceEquals(r.sharedMaterial, m))
                {
                    continue;
                }

                total++;
                if (r.enabled && r.gameObject.activeInHierarchy)
                {
                    enabledCount++;
                }

                if (r.forceRenderingOff)
                {
                    forcedOff++;
                }

                if (cam != null && (cam.cullingMask & (1 << r.gameObject.layer)) == 0)
                {
                    wrongLayer++;
                }

                if (r.bounds.Contains(camPos))
                {
                    containing++;
                }

                if (r.isVisible)
                {
                    visible++;
                }
            }

            sb.AppendLine("renderers с этим материалом: total=" + total + " enabled=" + enabledCount
                + " containCamera=" + containing + " forcedOff=" + forcedOff
                + " wrongLayer=" + wrongLayer + " isVisible=" + visible);

            sb.AppendLine("_UnderwaterCameraDepth = " + Shader.GetGlobalFloat("_UnderwaterCameraDepth"));
            sb.AppendLine("_UwCamAltitude = " + Shader.GetGlobalFloat("_UwCamAltitude"));
            Debug.Log(sb.ToString());
        }

        [ContextMenu("Galilego/Water magenta ON")]
        private void WaterMagentaOn()
        {
            if (waterMaterialCache != null)
            {
                waterMaterialCache.SetFloat("_UnderwaterDebugMode", 99f);
            }
        }

        [ContextMenu("Galilego/Water magenta OFF")]
        private void WaterMagentaOff()
        {
            if (waterMaterialCache != null)
            {
                waterMaterialCache.SetFloat("_UnderwaterDebugMode", 0f);
            }
        }

        /// <summary>
        /// Push water tuning fields into the shared water material.
        /// Called on creation + every LateUpdate so Inspector edits apply live
        /// to the already cached material (no chunk rebuild needed).
        /// </summary>
        private void ApplyWaterParams()
        {
            if (waterMaterialCache == null)
            {
                return;
            }

            // Single radial water centre (prevents a brightness step at LOD seams).
            // Use bodyRenderPosition: fresh while chunks are visible, before the dominance gate.
            Shader.SetGlobalVector("_PlanetWaterCenter", bodyRenderPosition);

            // Stylised sea: aquamarine in every Unlit color slot
            // (color + _UnlitColor + _BaseColor), transparency for the depth
            // gradient. WaterSurface waves do not apply — calm water.
            if (UseLitWaterFallback)
            {
                waterMaterialCache.color = LitWaterColor;
                waterMaterialCache.SetColor("_UnlitColor", LitWaterColor);
                waterMaterialCache.SetColor("_BaseColor", LitWaterColor);
                return;
            }

            waterMaterialCache.SetFloat("_WaveStrength", WaterWaveStrength);
            // Multiple of 0.02 for exact pattern periodicity over 51 200 m (see UpdateWaterAnchor).
            waterMaterialCache.SetFloat("_WaveScale", Mathf.Max(0.02f, Mathf.Round(WaterWaveScale * 50f) / 50f));
            waterMaterialCache.SetFloat("_WaveAmplitude", WaterAmplitudeMeters);
            waterMaterialCache.SetFloat("_SparkleStrength", WaterSparkleStrength);
            waterMaterialCache.SetFloat("_SparkleScale", WaterSparkleScale);
            waterMaterialCache.SetFloat("_FoamCrestStrength", WaterFoamCrest);
            waterMaterialCache.SetFloat("_ShadingFadeFloor", WaterShadingFadeFloor);
            waterMaterialCache.SetFloat("_DetailFadeFloor", WaterDetailFadeFloor);
            waterMaterialCache.SetFloat("_RippleStrength", WaterRippleStrength);
            waterMaterialCache.SetFloat("_RippleScale", WaterRippleScale);
            waterMaterialCache.SetFloat("_RippleSpeed", WaterRippleSpeed);
            waterMaterialCache.SetFloat("_FoamSlopeGain", WaterFoamSlopeGain);
            waterMaterialCache.SetFloat("_FoamDepthMeters", Mathf.Max(0.5f, WaterFoamDepthMeters));
            waterMaterialCache.SetFloat("_ShoreFadeMeters", WaterShoreFadeMeters);
            waterMaterialCache.SetFloat("_AbsorbShallow", WaterAbsorbShallow);
            waterMaterialCache.SetFloat("_AbsorbDeep", WaterAbsorbDeep);
            waterMaterialCache.SetFloat("_ShallowAlpha", WaterShallowAlpha);
            waterMaterialCache.SetFloat("_MinAlpha", WaterMinAlpha);
            waterMaterialCache.SetFloat("_ShallowAlphaMeters", WaterShallowAlphaMeters);
            waterMaterialCache.SetFloat("_SurfStrength", WaterSurfStrength);
            waterMaterialCache.SetFloat("_SurfSpeed", WaterSurfSpeed);
            waterMaterialCache.SetFloat("_FresnelStrength", WaterFresnelStrength);
            waterMaterialCache.SetFloat("_SpecularIntensity", WaterSpecularIntensity);
            waterMaterialCache.SetFloat("_SpecularPower", Mathf.Max(8f, WaterSpecularPower));
            ApplyWaterNormalMaps();
            // Chunks hold the sharedMaterial captured at build time. If the cache is
            // recreated (ClearChunkCache / shader change), old chunks point at a dead
            // material and live tuning misses them. Reassigning fixes it.
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (chunk.WaterGo == null)
                {
                    continue;
                }

                MeshRenderer renderer = chunk.WaterGo.GetComponent<MeshRenderer>();
                if (renderer != null && !ReferenceEquals(renderer.sharedMaterial, waterMaterialCache))
                {
                    renderer.sharedMaterial = waterMaterialCache;
                }
            }
        }

        private static UnityEngine.Texture2D cachedWaterNormalA;
        private static UnityEngine.Texture2D cachedWaterNormalB;
        private static bool waterNormalsLoadAttempted;

        /// <summary>
        /// HOWTO-Water texture ripple: an inspector assignment takes priority,
        /// otherwise the baked textures under Resources/Water/ are loaded once.
        /// With neither texture the shader stays on procedural normals.
        /// </summary>
        private void ApplyWaterNormalMaps()
        {
            // The baker may have run after the first attempt (cached null) —
            // retry loading until both textures exist.
            if (!waterNormalsLoadAttempted || cachedWaterNormalA == null || cachedWaterNormalB == null)
            {
                waterNormalsLoadAttempted = true;
                if (cachedWaterNormalA == null)
                {
                    cachedWaterNormalA = UnityEngine.Resources.Load<UnityEngine.Texture2D>("Water/water_normal_a");
                }

                if (cachedWaterNormalB == null)
                {
                    cachedWaterNormalB = UnityEngine.Resources.Load<UnityEngine.Texture2D>("Water/water_normal_b");
                }
            }

            UnityEngine.Texture2D normalA = WaterNormalA != null ? WaterNormalA : cachedWaterNormalA;
            UnityEngine.Texture2D normalB = WaterNormalB != null ? WaterNormalB : cachedWaterNormalB;
            if (normalA != null)
            {
                waterMaterialCache.SetTexture("_NormalMap0", normalA);
            }

            if (normalB != null)
            {
                waterMaterialCache.SetTexture("_NormalMap1", normalB);
            }

            bool useMaps = normalA != null && normalB != null && WaterNormalStrength > 0f;
            waterMaterialCache.SetFloat("_UseNormalMap", useMaps ? 1f : 0f);
            waterMaterialCache.SetFloat("_NormalStrength", WaterNormalStrength);
        }

        /// <summary>
        /// Living sea without shaders: near water meshes ride a slow travelling
        /// swell (rigid per-chunk offset along the local radial, sampled from a
        /// shared world-space wave function so neighbours stay continuous).
        /// Visible as waterline lapping on the beach + gentle breathing offshore.
        /// Far water stays flat (perf). Runs every LateUpdate after chunk transforms.
        /// </summary>
        private void UpdateWaterBob(Vector3 cameraPosition)
        {
            if (WaterBobAmplitude <= 0f || WaterBobLength <= 0f)
            {
                return;
            }

            float t = Time.time * WaterBobSpeed;
            float k = (Mathf.PI * 2f) / WaterBobLength;
            float radiusSqr = WaterBobRadius * WaterBobRadius;
            // Fixed diagonal swell direction (world XZ): cheap, stable, no params.
            const float dirX = 0.8f;
            const float dirZ = 0.6f;
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.WaterGo == null)
                {
                    continue;
                }

                Vector3 center = chunk.Go.transform.position;
                Vector3 toChunk = center - cameraPosition;
                if (toChunk.sqrMagnitude > radiusSqr)
                {
                    continue;
                }

                float phase = (center.x * dirX + center.z * dirZ) * k - t;
                Vector3 radial = center - bodyRenderPosition;
                float radialLen = radial.magnitude;
                if (radialLen < 1e-3f)
                {
                    continue;
                }

                float bob = Mathf.Sin(phase) * WaterBobAmplitude;
                chunk.WaterGo.transform.position = center + (radial / radialLen) * bob;
            }
        }

        /// <summary>
        /// Queues an asynchronous decor layer build for a chunk. Heavy work runs
        /// in per-frame budget slices (StepDecorBuilds); the result appears after
        /// a few frames. Returns false if a build is already running or the
        /// concurrent-build limit is exhausted.
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
                // Reserve slots for the player: far-away first builds (trees/rocks
                // behind the player) must not occupy the queue while grass is needed nearby.
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
                // A pool that is sinking underground is not the "previous" one — it must not
                // be carried into a new build (otherwise a copy of the pool grows underground).
                if (chunk.Decor[i].LayerIndex == layerIndex && !chunk.Decor[i].Retiring)
                {
                    previous = chunk.Decor[i];
                    break;
                }
            }

            // Snapshot of the chunk's RENDER mesh: decor bases are seated on it, not on
            // the analytic height — at coarse LOD (vertex step ~7 m) the mesh differs
            // by tens of centimetres, so grass would hang and sink relative to the
            // visible ground. For grass (Burst path) — a native copy without managed allocation.
            // The mesh grid row MUST match what the chunk was actually built with:
            // BuildChunk uses ResolutionForDepth(node.Depth), not TileResolution;
            // MeshSurfacePoint indexes assume that exact resolution.
            int coreN = ResolutionForDepth(chunk.Node.Depth) + 1;
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

            // Cell metric on the cube-sphere: cell area ∝ (1+a²+b²)^(-3/2),
            // where a,b are the chunk centre coordinates in [-1,1] on the face.
            // Candidate density per m² is inverse to that area, so cells_axis is kept
            // proportional to the linear size = DecorMetricNorm·R·sizeUv·f.
            // 1.0 = "pure metres" (actual step = SpacingMeters).
            double ca = ((u0 + (sizeUv * 0.5d)) * 2d) - 1d;
            double cb = ((v0 + (sizeUv * 0.5d)) * 2d) - 1d;
            double chunkArc = body.Radius * sizeUv * 2d * DecorMetricNorm
                / System.Math.Pow(1d + (ca * ca) + (cb * cb), 0.75d);

            double spacing = System.Math.Max(0.25d, layer.SpacingMeters);
            int cells = (int)System.Math.Ceiling(chunkArc / spacing);
            cells = System.Math.Max(1, System.Math.Min(System.Math.Max(1, layer.MaxCellsPerAxis), cells));
            int count = cells * cells;

            if (layer.PerInstanceDensity)
            {
                // Coarse LODs (huge patches) cannot represent grass: their grid is too
                // coarse, and "distance 0" kept them in the queue forever and starved
                // builds near the player. Mark them as done.
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
                // One channel (not atan2 of two): the angle is a smooth field, without
                // direction "swirl" at points where both channels are near zero.
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

            // Cloud lead along the player's velocity RELATIVE TO THE SURFACE:
            // while running, the dense zone is prepared AHEAD and grass meets
            // the player instead of being chased by them. Planet spin is not
            // included here (standing still the player "does not move"), so the cloud
            // does not drift while standing.
            // surfaceVelocityLocal is already in body-fixed axes matching the
            // chunk's local axes, so removing the normal component is enough.
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
                // The subtuft grid is exactly the layer step (gridN×gridN), not "as many
                // as fit": otherwise gaps remained between subtufts.
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
                // All buffers come from the pool (DecorArrayPool): a grass rebuild must not
                // allocate tens of MB of native arrays and free them immediately, or
                // finalization causes a hitch.
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

            if (LogDecorAllocation && nearPlayer
                && Time.realtimeSinceStartup >= decorProbeNext[layerIndex & 15])
            {
                decorProbeNext[layerIndex & 15] = Time.realtimeSinceStartup + 0.5f;
                double cx = chunk.CenterAstro.X, cy = chunk.CenterAstro.Y, cz = chunk.CenterAstro.Z;
                double lat = System.Math.Asin(cz / System.Math.Sqrt((cx * cx) + (cy * cy) + (cz * cz))) * 57.29578d;
                double lon = System.Math.Atan2(cy, cx) * 57.29578d;
                Vector3 bs = chunk.Mesh.bounds.size;
                float real = Mathf.Max(bs.x, Mathf.Max(bs.y, bs.z));
                Debug.Log($"[Probe] {layer.Name} lat={lat:F1} lon={lon:F1} f={chunk.Node.Face} d={chunk.Node.Depth} " +
                          $"ix={chunk.Node.Ix} iy={chunk.Node.Iy} nomArc={chunkArc:F0} realSize={real:F0} " +
                          $"want={(int)System.Math.Ceiling(chunkArc / spacing)} cells={cells}/{layer.MaxCellsPerAxis} " +
                          $"sub={subPerCell}/{layer.SubInstancesPerCell} realSpacing={real / cells:F2}");
            }

            if (session.Selection.IsCreated)
            {
                // The sampling radius is the layer's whole visibility zone; the cap is cut by
                // the "nearest first" allocator with a smooth edge, so the cut boundary
                // itself lies beyond the dense zone.
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

        /// <summary>Advances all active builds within the frame budget. Sessions
        /// go round-robin while there is progress and budget remains.</summary>
        /// <summary>Marker-only wrapper: the sessions share DecorBuildBudgetMs,
        /// so this is the only place that shows how much of it reached the frame.</summary>
        private void StepDecorBuilds()
        {
            using (SurfacePerf.StepDecorBuildsMarker.Auto())
            {
                StepDecorBuildsCore();
            }
        }

        private void StepDecorBuildsCore()
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
                        // Chunk evicted / LOD changed — cancel the build.
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

                    // An exception in one build must not bring down the whole LateUpdate
                    // (otherwise DrawDecor does not run and the world disappears).
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

        /// <summary>Build cancellation: the job may still be running on workers —
        /// then the arrays are freed on the next step, otherwise memory corruption.</summary>
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

        /// <summary>Return the array to the pool and null the OWNER's reference (ref):
        /// otherwise the parameter copy is nulled, the field stays "created", and a
        /// repeated return hands the same buffer to the pool twice.</summary>
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

        /// <summary>Computes grass matrices in the chunk frame ONCE and packs
        /// them into a ComputeBuffer + indirect-draw args. The world part is
        /// supplied at draw time via _DecorChunkToWorld (MPB), so per-frame
        /// matrix jobs are no longer needed.</summary>
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

        /// <summary>One indirect draw per submesh: matrices come from the buffer,
        /// the world part from _DecorChunkToWorld.</summary>
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
                // SRP path: one indirect draw per submesh (unlike legacy
                // DrawMeshInstancedIndirect, which HDRP ignores).
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
                        // Grass: weights/subtufts are computed by Burst, not the main thread.
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
                    // The grass count is now set by the DENSITY PROFILE (flat
                    // core + falloff), and the cap is cut by the allocator by distance.
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

                    // Preserved old blades (far from the new core) go into the
                    // pool tail: density near the player is unaffected, and the world does not
                    // "appear anew" in places already visited.
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

        /// <summary>Stage 5: Burst chain "falloff weight → grass counters".</summary>
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

        /// <summary>Stage 6: write slots (nearest first, smooth edge)
        /// + Burst expansion of exactly the planned number of blades.</summary>
        private void ScheduleDecorOffsetAndExpand(DecorBuildSession session)
        {
            // Distance buckets: ~2 m wide (the cut radius resolution),
            // so the cap boundary is a ring around the player, not a grid row.
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

            // Incremental: blades from the previous pool that remain
            // in the new selection are carried over with their BirthTime — already grown
            // ones do not sprout again. Only additions get a fresh BirthTime.
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
                // 13 is coprime with 36: the sparse order covers the 6×6 grid
                // entirely, and the first k subtufts are scattered over the whole cell.
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

        /// <summary>Stage 0: jitter candidate grid and UV scratch — entirely in
        /// a Burst job; tens of thousands of cells, each with seven hashes and double math.</summary>
        private bool AdvanceDecorPrep(DecorBuildSession session)
        {
            Node node = session.Node;
            JobHandle prep = new GroundDecorPrepJob
            {
                Face = node.Face,
                Depth = node.Depth,
                Ix = node.Ix,
                Iy = node.Iy,
                Cells = session.Cells,
                LayerIndex = session.LayerIndex,
                U0 = session.U0,
                V0 = session.V0,
                SizeUv = session.SizeUv,
                Dirs = session.Dirs,
                Uvs = session.Uvs,
                Randoms = session.Randoms,
                MeshPicks = session.MeshPicks,
                BuryRandoms = session.BuryRandoms,
                LeanRandoms = session.LeanRandoms
            }.Schedule(session.Count, 256);

            ScheduleDecorCandidateJob(session, prep);
            JobHandle.ScheduleBatchedJobs();
            session.Cursor = session.Count;
            session.Stage = 1;
            return true;
        }

        private void ScheduleDecorCandidateJob(DecorBuildSession session, JobHandle dependency = default(JobHandle))
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
            session.CandidateJob = job.Schedule(session.Count, 64, dependency);
            session.CandidateScheduled = true;
        }

        /// <summary>Stage 2: count accepted candidates and total weight — with a cursor.</summary>
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

        /// <summary>Stage 2 result: subtufts, cap and expansion — same order and
        /// hashes as the synchronous build.</summary>
        private void FinishDecorScan(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;

            if (layer.PerInstanceDensity && session.ExpectedWeight > 0d)
            {
                double expected = session.ExpectedWeight * layer.Density;
                if (expected > layer.MaxInstancesPerChunk)
                {
                    // Keep headroom for random-selection variance so the hard
                    // tail limit is not hit and no repeating stripes appear from the cell traversal order.
                    session.CapScale = (layer.MaxInstancesPerChunk * 0.9d) / expected;
                }
            }

            int capacity = session.AcceptedCount * session.SubPerCell;
            if (layer.PerInstanceDensity)
            {
                // Grass pool — "N nearest by density profile": the cap is the layer's
                // nominal limit, and the boundary is cut by the distance allocator (nearest
                // first) with smooth falloff at the edge.
                capacity = System.Math.Max(0, layer.MaxInstancesPerChunk);
            }
            else
            {
                capacity = System.Math.Min(capacity, layer.MaxInstancesPerChunk);
            }

            session.Take = capacity;

            if (LogDecorAllocation && session.NearPlayer)
            {
                Debug.Log($"[Probe] {layer.Name} f={session.Node.Face} d={session.Node.Depth} ix={session.Node.Ix} iy={session.Node.Iy} " +
                          $"accepted={session.AcceptedCount}/{session.Count} take={capacity}/{layer.MaxInstancesPerChunk} " +
                          $"cap={session.CapScale:F3}");
            }

            // The limit does not "cut the chunk's tail" (that produced a decor stripe
            // on one edge of the grid); it thins uniformly: traverse by the permutation
            // i = (k·stride) mod count with the golden-ratio stride, coprime with
            // count. Any prefix is uniform.
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

            // Candidate arrays are no longer needed — freed immediately.
            DisposeIfCreated(ref session.Dirs);
            DisposeIfCreated(ref session.Randoms);
            DisposeIfCreated(ref session.MeshPicks);
            DisposeIfCreated(ref session.BuryRandoms);
            DisposeIfCreated(ref session.LeanRandoms);
        }

        /// <summary>Carries blades FAR from the new core over from the previous
        /// pool (accumulation): they stay in place, and the new build adds a dense
        /// core near the player within the remaining budget.</summary>
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

        /// <summary>Tangential distance from the instance to the cloud centre (the same
        /// metric as Falloff in the jobs).</summary>
        private static int CountBirthsNearCameraLocal(
            Unity.Collections.NativeArray<GroundDecorInstance> stored, int count, Vector3 cameraLocal)
        {
            if (!stored.IsCreated || count <= 0)
            {
                return 0;
            }

            int n = Mathf.Min(count, stored.Length);
            float radiusSq = SurfacePerf.DecorBirthProbeMeters * SurfacePerf.DecorBirthProbeMeters;
            int births = 0;
            for (int i = 0; i < n; i++)
            {
                float3 p = stored[i].Position;
                float dx = p.x - cameraLocal.x;
                float dy = p.y - cameraLocal.y;
                float dz = p.z - cameraLocal.z;
                if (((dx * dx) + (dy * dy) + (dz * dz)) <= radiusSq)
                {
                    births++;
                }
            }

            return births;
        }

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

        /// <summary>Stage 3: expand subtufts into a persistent array —
        /// with a cursor over cells/subtufts; the result is deterministic.</summary>
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
                    // Sprouting from underground: each prop (tree/rock/
                    // cactus/daisy) has its own appearance time offset by hash —
                    // they grow one by one, not in a batch. Grass uses the Burst path.
                    sub.BirthTime = DecorPropStaggerSeconds > 0f
                        ? Time.time + (float)(GroundDecorDistribution.Hash01(
                            subIndex, session.Node.Face, i + session.Node.Ix + 1997,
                            session.Node.Iy, 42) * DecorPropStaggerSeconds)
                        : 0f;

                    double finalU;
                    double finalV;
                    if (session.CurrentSubs == 1)
                    {
                        // The candidate is already jittered on the grid — take its UV.
                        finalU = session.Uvs[i].x;
                        finalV = session.Uvs[i].y;
                    }
                    else
                    {
                        // Subtufts inside the cell: hash-jittered positions
                        // (a fixed 3×3 grid produced a repeating pattern),
                        // other parameters are per blade.
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
                        // The subtuft moved away from the verified cell-centre TryEvaluate:
                        // the hard filters (water/sand/mountains/biome) are rechecked at
                        // its own direction. Without this, the cell edge at coarse LOD
                        // planted decor in water and on the beach.
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

                    // Snap guard: the coarse chunk mesh deviates from the analytic height
                    // by metres — the point itself must be above water, otherwise trees/rocks
                    // "sink" where the analytic height sees land.
                    if (!GroundDecorDistribution.IsMeshPointAboveWater(
                        session.Placement, new float3(meshPoint.x, meshPoint.y, meshPoint.z)))
                    {
                        continue;
                    }

                    if (layer.PerInstanceDensity)
                    {
                        // Per-blade gain. Falloff is by tangential distance to the camera
                        // IN THE CHUNK'S MESH FRAME: meshPoint and cameraLocal share one space,
                        // so the cloud really follows the player.
                        Vector3 toCamera = meshPoint - session.CameraLocal;
                        float alongNormal = Vector3.Dot(toCamera, normal);
                        Vector3 tangential = toCamera - (normal * alongNormal);
                        double accept = layer.Density * sub.DensityWeight * session.CapScale
                            * GroundDecorDistribution.Falloff(session.Placement, tangential.magnitude);
                        if (session.CurrentSubs == 1 && subPerCell > 1)
                        {
                            // Far away one subtuft stands in for the grid — the probability is
                            // raised by the same factor: the expected blade count per cell
                            // does not drop, and the grass "tail" out to MaxDistance remains.
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

        /// <summary>Stage 4: the finished layer is installed into the chunk (for a
        /// rebuild — replacing the layer with the same identity; the old one draws until then).</summary>
        private void FinalizeDecorBuild(DecorBuildSession session)
        {
            using (SurfacePerf.FinalizeDecorBuildMarker.Auto())
            {
                FinalizeDecorBuildCore(session);
            }
        }

        /// <summary>Stage 4: the finished layer is installed into the chunk (for a
        /// rebuild — into the replaceable layer of the same cell; the old one draws until the clock runs out).</summary>
        private void FinalizeDecorBuildCore(DecorBuildSession session)
        {
            GroundDecorLayer layer = session.Layer;
            int write = session.Write;

            NativeArray<GroundDecorInstance> stored = session.Stored;
            if (stored.IsCreated && write < stored.Length)
            {
                if (write > 0)
                {
                    // Exact size comes from the pool (it usually holds a buffer of that
                    // length): only a copy remains, no alloc/free per frame.
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

            // Blades that dropped out of the new selection are not discarded: they are
            // appended to the new pool tail with DeathTime, and the matrix job
            // fades their scale (they sink underground). Instances carried by
            // MergeKept stay in both the new selection and the tail (same matrix).
            int aliveCount = write;
            float retireDeadline = 0f;
            float sinkFadeSeconds = DecorFadeSeconds(DecorBladeSinkSeconds);
            if (layer.PerInstanceDensity && write > 0)
            {
                retireDeadline = AppendRetiringInstances(session, ref stored, ref write, sinkFadeSeconds);
            }

            // Find the previous layer of this identity (if this is a rebuild):
            // replace it with the finished one, or remove it if the result is empty;
            // otherwise the old layer would stay "stale" forever and rebuild endlessly.
            int existingIndex = -1;
            for (int i = 0; i < session.Chunk.Decor.Count; i++)
            {
                if (session.Chunk.Decor[i].LayerIndex == session.LayerIndex
                    && !session.Chunk.Decor[i].Retiring)
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
                    // Grass is drawn with Burst matrices (WorldMatrices); managed
                    // Matrices is not needed for it.
                    Matrices = layer.PerInstanceDensity ? null : new Matrix4x4[write],
                    SinkFadeSeconds = sinkFadeSeconds
                };

                // "Hole underfoot" in numbers: how many blades in the fresh
                // build were born near the camera. Walking rebuilds run in
                // waves; launched too late, those instances would appear in a
                // zone where the player already sees full density and grow
                // in-frame. Counted here, where the instances are already prepared.
                if (layer.PerInstanceDensity)
                {
                    SurfacePerf.DecorBirthsNearCameraThisFrame += CountBirthsNearCameraLocal(
                        session.Stored,
                        write,
                        session.BuildCameraLocal);
                }

                if (layer.PerInstanceDensity)
                {
                    runtime.WorldMatrices = DecorArrayPool.Rent<Matrix4x4>(write);
                    runtime.AliveCount = aliveCount;
                    runtime.RetireDeadline = retireDeadline;
                    // The pool's cell layout table goes to the runtime:
                    // the next rebuild uses it to carry over already grown blades.
                    runtime.CellOffsets = session.WriteOffsets;
                    runtime.CellCounts = session.WriteCounts;
                    session.WriteOffsets = default;
                    session.WriteCounts = default;
                }

                if (existingIndex >= 0)
                {
                    // The old pool is not deleted but sent underground (for layers without
                    // PerInstanceDensity there are many instances and they are not individually
                    // marked like grass: the whole pool fades). For grass the tail is
                    // already collected in the new pool, so the old one can be freed at once.
                    if (layer.PerInstanceDensity)
                    {
                        DisposeDecorLayerRuntime(session.Chunk.Decor[existingIndex]);
                        session.Chunk.Decor[existingIndex] = runtime;
                    }
                    else
                    {
                        RetireDecorLayerRuntime(session.Chunk.Decor[existingIndex]);
                        session.Chunk.Decor.Add(runtime);
                    }
                }
                else
                {
                    session.Chunk.Decor.Add(runtime);
                }
            }
            else if (existingIndex >= 0)
            {
                // The new build is empty (the player moved onto rock/sand): the old pool is
                // sent underground instead of deleted — otherwise trees at the zone edge
                // would vanish in one frame. RetireFinishedDecor removes it.
                RetireDecorLayerRuntime(session.Chunk.Decor[existingIndex]);
            }

            session.Chunk.DecorBuiltMask |= 1 << session.LayerIndex;
            if (layer.DensityFalloffMeters > 0f)
            {
                // Record the build position even for an empty result: the layer rebuilds
                // there when the player comes closer (otherwise a chunk built empty
                // from afar would stay without grass forever).
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

        /// <summary>Lazy decor layer build: a chunk is decorated per layer when it
        /// first enters the layer's MaxDistance+SpawnMargin. This search queues it
        /// (nearest first); the build itself runs in frame-budget slices
        /// (TryQueueDecorBuild/StepDecorBuilds) without hitches.</summary>
        /// <summary>Marker-only wrapper: two passes "all chunks × all
        /// layers" per run, and it runs almost every frame.</summary>
        private void EnsureVisibleDecor(Vector3 cameraPosition)
        {
            using (SurfacePerf.EnsureVisibleDecorMarker.Auto())
            {
                EnsureVisibleDecorCore(cameraPosition);
            }
        }

        /// <summary>A candidate for the decor build queue, collected by a single
        /// pass over chunks and layers.</summary>
        private struct DecorCandidate
        {
            public Chunk Chunk;
            public int LayerIndex;
            public float Distance;
            public bool Grass;
        }

        /// <summary>Grass (per-instance) first; within a group, nearest to the camera.</summary>
        private static readonly System.Comparison<DecorCandidate> DecorCandidateOrder = CompareDecorCandidates;

        private static int CompareDecorCandidates(DecorCandidate x, DecorCandidate y)
        {
            if (x.Grass != y.Grass)
            {
                return x.Grass ? -1 : 1;
            }

            return x.Distance.CompareTo(y.Distance);
        }

        private void EnsureVisibleDecorCore(Vector3 cameraPosition)
        {
            if (decorProfile == null || decorProfile.Layers == null || decorProfile.Layers.Count == 0)
            {
                return;
            }

            // [PHASE 1] No new pools high up or at speed. TrimDistantDecor
            // keeps running: otherwise at high altitude we would also
            // accumulate memory for decor that is no longer visible.
            if (DecorBuildsSuspended(false))
            {
                return;
            }

            int budget = DecorBuildsPerFrame;
            if (budget <= 0)
            {
                return;
            }

            // [PHASE 4] The previous full scan found nothing to build. Until the
            // pause expires, another scan is guaranteed to find nothing: the chunk
            // set and visibility do not change enough in 0.2 s to justify
            // another full pass.
            if (Time.unscaledTime < decorScanBlockedUntil)
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

            // One pass instead of N: the order is set by sorting —
            // grass (per-instance) first, within a group the nearer one — so
            // queueing DecorBuildsPerFrame candidates does not require
            // DecorBuildsPerFrame full "all chunks × all layers" passes.
            decorCandidates.Clear();
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.Go == null)
                {
                    continue;
                }

                long chunkId = NodeId(chunk.Node);
                float distance = ChunkDecorDistance3D(chunk, cameraPosition, cameraUp);
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

                    if (layer.PerInstanceDensity && decorSpeed > DecorPauseSpeed)
                    {
                        continue;
                    }

                    if (distance > EffectiveMaxDistance(layer) + layer.SpawnMarginMeters)
                    {
                        continue;
                    }

                    if (HasDecorBuildSession(chunkId, layerIndex))
                    {
                        continue;
                    }

                    decorCandidates.Add(new DecorCandidate
                    {
                        Chunk = chunk,
                        LayerIndex = layerIndex,
                        Distance = distance,
                        Grass = layer.PerInstanceDensity
                    });
                }
            }

            if (decorCandidates.Count == 0)
            {
                decorScanBlockedUntil = Time.unscaledTime
                    + Mathf.Max(0.02f, DecorRescanCooldownSeconds);
                return;
            }

            decorCandidates.Sort(DecorCandidateOrder);

            int queued = 0;
            for (int i = 0; i < decorCandidates.Count && queued < budget; i++)
            {
                DecorCandidate candidate = decorCandidates[i];
                bool nearPlayer = candidate.Distance
                    <= decorProfile.Layers[candidate.LayerIndex].MaxDistanceMeters;
                if (TryQueueDecorBuild(candidate.Chunk, candidate.LayerIndex, cameraPosition, nearPlayer))
                {
                    queued++;
                    continue;
                }

                if (decorBuildSessions.Count >= MaxDecorBuildSessions)
                {
                    break;
                }
            }
        }

        /// <summary>Tangential distance to a chunk (as in DrawDecor): flight
        /// altitude does not switch decor off all at once.</summary>
        private static float ChunkTangentialDistance(Chunk chunk, Vector3 cameraPosition, Vector3 cameraUp)
        {
            Vector3 toChunk = chunk.Go.transform.position - cameraPosition;
            Vector3 tangential = toChunk - (Vector3.Dot(toChunk, cameraUp) * cameraUp);
            return Mathf.Max(0f, tangential.magnitude - chunk.BoundsRadius);
        }

        /// <summary>
        /// Distance to a chunk including the observer's altitude.
        ///
        /// Distance is the hypotenuse: sqrt(tangential² + altitude²). The smooth
        /// transition at the boundary comes from edgeFade at the layer edge
        /// (see ChunkDecorEdgeFade), not from this cull.
        ///
        /// </summary>
        private float ChunkDecorDistance3D(Chunk chunk, Vector3 cameraPosition, Vector3 cameraUp)
        {
            float tangential = ChunkTangentialDistance(chunk, cameraPosition, cameraUp);
            float height = decorAltitude;
            return Mathf.Sqrt((tangential * tangential) + (height * height));
        }

        /// <summary>
        /// Smooth falloff at the layer radius edge. Returns 1 inside and 0 beyond
        /// the boundary, with a band 20 % of the radius wide: without it, the
        /// transition under altitude gain would read as a click instead of a fade.
        /// </summary>
        private static float ChunkDecorEdgeFade(float distance3D, float maxDistanceMeters)
        {
            if (maxDistanceMeters <= 0f)
            {
                return 0f;
            }

            float band = 0.2f * maxDistanceMeters;
            return Mathf.Clamp01((maxDistanceMeters - distance3D) / band);
        }

        /// <summary>World bounds of a chunk's decor: the RENDER mesh AABB (with
        /// BoundsRadius margin) correctly rotated (|M|·extents) plus height
        /// margin for blades/billboards. The chunk position is at sea level while
        /// grass lies on the terrain, so the bounds must follow the terrain, not the chunk origin.</summary>
        private static Bounds ChunkWorldBounds(Chunk chunk)
        {
            // Safe margin for grass (up to ~2.6 m), billboards and wind.
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
        /// The density cloud of layers with DensityFalloffMeters is baked at the
        /// camera position during the build. As the player moves away it stays
        /// behind. When tangential displacement exceeds a threshold (a fraction of
        /// the falloff scale), the nearest stale layer is queued for rebuild. The
        /// build is asynchronous: the old layer draws until the new one is ready.
        /// Fast flight does not rebuild — chunks there are new anyway.
        /// </summary>
        /// <summary>Marker-only wrapper: rebuilds decor pools for "stale"
        /// chunks — at high speed it runs every frame.</summary>
        private void RefreshMovingDecor(Vector3 cameraPosition)
        {
            using (SurfacePerf.RefreshMovingDecorMarker.Auto())
            {
                RefreshMovingDecorCore(cameraPosition);
            }
        }

        private void RefreshMovingDecorCore(Vector3 cameraPosition)
        {
            if (decorProfile == null || decorProfile.Layers == null)
            {
                return;
            }

            // [PHASE 1] Pool rebuild is the most expensive decor operation. At speed
            // the field moves out from under the built pool faster than it can be
            // rebuilt, and every frame would go into a full rebuild with no visible result.
            if (DecorBuildsSuspended())
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
            // Take the OLDEST layer: with equal distances (large chunks
            // overlap the player) the "nearest" choice always pointed at the same
            // chunk while neighbours accumulated cloud lag and grass disappeared there.
            // A rebuild resets staleness, so the queue cycles through chunks by itself.
            float bestStale = -1f;
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (!chunk.Visible || chunk.Go == null || chunk.DecorBuiltMask == 0)
                {
                    continue;
                }

                long chunkId = NodeId(chunk.Node);
                float chunkDistance = ChunkDecorDistance3D(chunk, cameraPosition, cameraUp);
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

                    // Build position: from the runtime for a non-empty layer, from the value
                    // recorded at finalization for an empty one (otherwise a layer built empty
                    // from afar would never rebuild closer).
                    DecorLayerRuntime runtime = null;
                    for (int i = 0; i < chunk.Decor.Count; i++)
                    {
                        if (chunk.Decor[i].LayerIndex == layerIndex && !chunk.Decor[i].Retiring)
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

                    // Staleness threshold: density inside the flat core (DensityCoreMeters)
                    // does not change with centre displacement, so the player must stay
                    // INSIDE the core — then a rebuild only refines the far edge and the grass
                    // does not "breathe". A larger threshold means rarer rebuilds (they are
                    // expensive: candidate pass + pool expansion).
                    float core = (float)System.Math.Max(0d, layer.DensityCoreMeters);
                    float threshold;
                    if (layer.DecorLookAheadMeters > 0f)
                    {
                        float floor = Mathf.Max(1f, layer.DecorMinRebuildMeters);
                        threshold = Mathf.Clamp(
                            core - layer.DecorLookAheadMeters,
                            floor,
                            Mathf.Max(floor, layer.MaxDistanceMeters * 0.3f));
                    }
                    else
                    {
                        threshold = surfaceVelocityLocal.magnitude < 0.5f
                            ? 8f
                            : Mathf.Clamp(core * 0.6f, 16f, Mathf.Max(16f, layer.MaxDistanceMeters * 0.3f));
                    }
                    Vector3 buildDelta = currentCameraLocal - buildCameraLocal;
                    if (localUp.sqrMagnitude > 1e-6f)
                    {
                        // Tangential displacement only: rising/descending does not age the cloud.
                        buildDelta -= localUp * Vector3.Dot(buildDelta, localUp);
                    }

                    float moved = buildDelta.sqrMagnitude;
                    if (moved <= threshold * threshold)
                    {
                        continue;
                    }

                    // Urgency: how badly this chunk needs a rebuild, scaled by proximity.
                    // With MaxDistanceMeters = 200 there are many more distant
                    // chunks than before, and they compete in the same queue —
                    // a plain "most moved" could keep the chunk under the
                    // player's feet waiting behind a far one. Only for
                    // PerInstanceDensity layers (grass): trees/rocks keep the
                    // old ordering, their rebuilds are rare and scheduled by
                    // their own budget.
                    float urgency = layer.PerInstanceDensity
                        ? moved / (1f + (chunkDistance / Mathf.Max(1f, layer.NearDistanceMeters)))
                        : moved;
                    if (urgency > bestStale)
                    {
                        bestStale = urgency;
                        bestChunk = chunk;
                        bestLayerIndex = layerIndex;
                    }
                }
            }

            if (bestChunk == null)
            {
                return;
            }

            // Rebuild cost per second: one queueing costs some milliseconds of the main
            // thread. The frequency (times per second) is written to PerfLog so a
            // walking route shows what the even carpet costs.
            var rebuildClock = System.Diagnostics.Stopwatch.StartNew();
            TryQueueDecorBuild(bestChunk, bestLayerIndex, cameraPosition, true);
            rebuildClock.Stop();
            SurfacePerf.DecorRebuildsThisFrame++;
            SurfacePerf.DecorRebuildMsThisFrame += (float)rebuildClock.Elapsed.TotalMilliseconds;
        }

        /// <summary>Unloads grass pools far beyond visibility: otherwise visited
        /// chunks accumulate full pools (cap per chunk), the instance budget
        /// drifts away from the player and the visible zone empties. When the player
        /// returns, the layer rebuilds (the mask is cleared).</summary>
        /// <summary>Marker-only wrapper: decor memory release must continue even
        /// when new builds are stopped by altitude or speed, so this pass must not be
        /// suppressed.</summary>
        private void TrimDistantDecor(Vector3 cameraPosition)
        {
            using (SurfacePerf.TrimDistantDecorMarker.Auto())
            {
                TrimDistantDecorCore(cameraPosition);
            }
        }

        private void TrimDistantDecorCore(Vector3 cameraPosition)
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

                    // Only beyond the REFILL ZONE (MaxDistance + spawn margin):
                    // otherwise an unloaded chunk is queued again immediately and clogs
                    // sessions, while grass near the player is not rebuilt.
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
                            // Grass: the pool is already all tail/beyond the radius, remove it
                            // at once. Other layers (trees etc.) go underground,
                            // so nothing vanishes instantly at the edge.
                            if (chunk.Decor[i].Retiring
                                || decorProfile.Layers[layerIndex].PerInstanceDensity)
                            {
                                DisposeDecorLayerRuntime(chunk.Decor[i]);
                                chunk.Decor.RemoveAt(i);
                            }
                            else
                            {
                                RetireDecorLayerRuntime(chunk.Decor[i]);
                            }
                        }
                    }

                    chunk.DecorBuiltMask &= ~(1 << layerIndex);
                }
            }
        }

        /// <summary>Instance matrix local to the chunk: position/normal/yaw/
        /// tilt/scale. Camera-independent — computed once at build time.</summary>
        /// <summary>
        /// Appends to the end of the new pool those instances from the OLD pool that
        /// are no longer in the new selection and sets their DeathTime: they still
        /// draw, shrinking and sinking underground (see GroundDecorMatrixJob).
        ///
        /// Both pools share the same cell layout (same grid and SubPerCell), so
        /// "kept in the new selection" = the first min(prev, new) subtufts of a
        /// cell — exactly what GroundDecorMergeKeptJob carries over. Dropped ones
        /// are the tail of each cell plus cells that got no slot in the new budget.
        ///
        /// Returns the moment when the tail is fully faded (0 = no tail).
        /// </summary>
        private static float AppendRetiringInstances(
            DecorBuildSession session, ref NativeArray<GroundDecorInstance> stored, ref int write,
            float sinkFadeSeconds)
        {
            DecorLayerRuntime previous = session.Previous;
            if (previous == null || !previous.Instances.IsCreated
                || !previous.CellOffsets.IsCreated || !previous.CellCounts.IsCreated)
            {
                return 0f;
            }

            NativeArray<GroundDecorInstance> prev = previous.Instances;
            NativeArray<int> prevOffsets = previous.CellOffsets;
            NativeArray<int> prevCounts = previous.CellCounts;
            NativeArray<int> newCounts = session.WriteCounts;
            if (!newCounts.IsCreated)
            {
                return 0f;
            }

            int cells = System.Math.Min(prevCounts.Length, newCounts.Length);
            int dropped = 0;
            for (int i = 0; i < cells; i++)
            {
                int prevCount = prevCounts[i];
                if (prevCount <= 0)
                {
                    continue;
                }

                int offset = prevOffsets[i];
                if (offset == int.MaxValue)
                {
                    continue;
                }

                int newCount = newCounts[i];
                int keep = newCount < prevCount ? newCount : prevCount;
                for (int s = keep; s < prevCount; s++)
                {
                    int from = offset + s;
                    if (from >= 0 && from < prev.Length && prev[from].Scale > 0f)
                    {
                        dropped++;
                    }
                }
            }

            if (dropped <= 0)
            {
                return 0f;
            }

            // The already fading tail of the PREVIOUS pool is carried forward: otherwise
            // a half-faded blade would vanish abruptly on the next rebuild.
            // Fully faded ones (DeathTime + duration < now) are not carried.
            int carried = 0;
            int prevAlive = previous.AliveCount;
            for (int i = prevAlive; i < prev.Length; i++)
            {
                GroundDecorInstance dying = prev[i];
                if (dying.Scale > 0f && dying.DeathTime > 0f
                    && dying.DeathTime + sinkFadeSeconds > Time.time)
                {
                    carried++;
                }
            }

            // New buffer: live part + departing tail (both halves from the pool).
            int total = write + dropped + carried;
            NativeArray<GroundDecorInstance> grown = DecorArrayPool.Rent<GroundDecorInstance>(total);
            if (write > 0)
            {
                NativeArray<GroundDecorInstance>.Copy(stored, grown, write);
            }

            DecorArrayPool.Return(ref stored);
            stored = grown;

            float now = Time.time;
            float latest = 0f;
            int dst = write;
            for (int i = 0; i < cells; i++)
            {
                int prevCount = prevCounts[i];
                if (prevCount <= 0)
                {
                    continue;
                }

                int offset = prevOffsets[i];
                if (offset == int.MaxValue)
                {
                    continue;
                }

                int newCount = newCounts[i];
                int keep = newCount < prevCount ? newCount : prevCount;
                for (int s = keep; s < prevCount; s++)
                {
                    int from = offset + s;
                    if (from < 0 || from >= prev.Length)
                    {
                        continue;
                    }

                    GroundDecorInstance dying = prev[from];
                    if (dying.Scale <= 0f)
                    {
                        continue;
                    }

                    // Spread by slot hash: departing blades do not fade in one wave.
                    dying.DeathTime = now + (DecorBladeSinkStaggerSeconds > 0f
                        ? (float)(GroundDecorDistribution.Hash01(from, session.LayerIndex, i, session.Node.Ix, 46)
                            * DecorBladeSinkStaggerSeconds)
                        : 0f);
                    latest = System.Math.Max(latest, dying.DeathTime);
                    stored[dst++] = dying;
                }
            }

            // Append the previous pool's fading tail with its original DeathTime:
            // the blade keeps leaving instead of restarting or snapping.
            for (int i = prevAlive; i < prev.Length; i++)
            {
                GroundDecorInstance dying = prev[i];
                if (dying.Scale <= 0f || dying.DeathTime <= 0f
                    || dying.DeathTime + sinkFadeSeconds <= now)
                {
                    continue;
                }

                latest = System.Math.Max(latest, dying.DeathTime);
                stored[dst++] = dying;
            }

            write = dst;
            return latest + sinkFadeSeconds;
        }

        /// <summary>
        /// Cuts off a faded pool tail (RetireDeadline passed): Instances and
        /// WorldMatrices are shortened to AliveCount. Called from drawing —
        /// the only consumer of these arrays.
        /// </summary>
        private static void CompactRetiredDecor(DecorLayerRuntime runtime)
        {
            int alive = runtime.AliveCount;
            if (alive <= 0 || alive >= runtime.Instances.Length)
            {
                runtime.RetireDeadline = 0f;
                return;
            }

            NativeArray<GroundDecorInstance> instances = DecorArrayPool.Rent<GroundDecorInstance>(alive);
            NativeArray<GroundDecorInstance>.Copy(runtime.Instances, instances, alive);
            DecorArrayPool.Return(ref runtime.Instances);
            runtime.Instances = instances;

            if (runtime.WorldMatrices.IsCreated)
            {
                NativeArray<Matrix4x4> matrices = DecorArrayPool.Rent<Matrix4x4>(alive);
                NativeArray<Matrix4x4>.Copy(runtime.WorldMatrices, matrices, alive);
                DecorArrayPool.Return(ref runtime.WorldMatrices);
                runtime.WorldMatrices = matrices;
            }

            runtime.AliveCount = 0;
            runtime.RetireDeadline = 0f;
        }

        /// <summary>
        /// Frees pools that have sunk underground (Retiring) and faded out.
        /// Called from DrawDecor before drawing: the only consumer of their arrays.
        /// Iterates all chunks, not just visible ones: a retired pool may remain in
        /// an invisible chunk.
        /// </summary>
        private void RetireFinishedDecor()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                Chunk chunk = kv.Value;
                if (chunk.Decor.Count == 0)
                {
                    continue;
                }

                for (int i = chunk.Decor.Count - 1; i >= 0; i--)
                {
                    DecorLayerRuntime runtime = chunk.Decor[i];
                    if (!runtime.Retiring)
                    {
                        continue;
                    }

                    if (Time.time < runtime.RetireStart + runtime.RetireFadeSeconds)
                    {
                        continue;
                    }

                    DisposeDecorLayerRuntime(runtime);
                    chunk.Decor.RemoveAt(i);
                }
            }
        }

        /// <summary>Prop sprout multiplier from its BirthTime: 0 right after
        /// appearance, 1 when grown. No effect for old builds (BirthTime 0).</summary>
        private static float DecorBirthFade(GroundDecorInstance instance, float fadeSeconds)
        {
            if (fadeSeconds <= 0f || instance.BirthTime <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((Time.time - instance.BirthTime) / fadeSeconds);
        }

        /// <summary>Pool sink multiplier: 1 — still standing, 0 — gone.
        /// Mirror of DecorBirthFade, but for the whole layer (trees and other props).</summary>
        private static float DecorRetireFade(DecorLayerRuntime runtime)
        {
            if (runtime == null || !runtime.Retiring || runtime.RetireFadeSeconds <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp01((runtime.RetireStart + runtime.RetireFadeSeconds - Time.time)
                / runtime.RetireFadeSeconds);
        }

        /// <summary>Marks the pool as sinking underground instead of deleting it.
        /// RetireFinishedDecor frees it (see DrawDecor).</summary>
        private static void RetireDecorLayerRuntime(DecorLayerRuntime runtime)
        {
            if (runtime == null || runtime.Retiring)
            {
                return;
            }

            runtime.Retiring = true;
            runtime.RetireStart = Time.time;
            runtime.RetireFadeSeconds = DecorFadeSeconds(DecorPropSinkSeconds);
        }

        private static Matrix4x4 DecorLocalMatrix(GroundDecorInstance instance, GroundDecorLayer layer, Vector3 up)
        {
            return DecorLocalMatrix(instance, layer, up, 1f);
        }

        /// <summary>Multiplied by sprout fade: fade 0..1 — the prop rises out of
        /// the ground instead of appearing instantly (BirthTime is set by the build).</summary>
        private static Matrix4x4 DecorLocalMatrix(
            GroundDecorInstance instance, GroundDecorLayer layer, Vector3 up, float fade)
        {
            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);
            Vector3 reference = Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right;
            Vector3 tangent = Vector3.Cross(reference, up).normalized;
            Quaternion rotation;
            if (layer.FlatOnGround)
            {
                // Flat, with no random rotation: the "patch" lies
                // flat (mesh local Z = surface normal).
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

        /// <summary>World wind direction of the instance: the same yaw basis as
        /// the near LOD (yaw around the normal from "north"), rotated by the chunk.</summary>
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
        /// Draw decor of visible chunks: near — solid mesh, farther —
        /// billboard up to MaxDistance. Matrices are the chunk's world transform ×
        /// the instance's local TRS (recomputed every frame: floating origin/spin).
        /// </summary>
        /// <summary>Marker-only wrapper: for layers without PerInstanceDensity
        /// (trees/rocks/cacti) matrices are rebuilt every frame, the most
        /// expensive part of decor at high altitude and speed.</summary>
        private void DrawDecor(Vector3 cameraPosition)
        {
            using (SurfacePerf.DrawDecorMarker.Auto())
            {
                DrawDecorCore(cameraPosition);
            }
        }

        private void DrawDecorCore(Vector3 cameraPosition)
        {
            if (decorProfile == null)
            {
                return;
            }

            // Free pools that faded out after sinking underground (trees/rocks/
            // cacti on rebuild and when a chunk leaves the radius).
            RetireFinishedDecor();

            // Safety: if the previous frame ended with an exception before Flush,
            // complete the scheduled jobs, otherwise new ones would write to the same
            // matrix arrays in parallel.
            if (burstDecorDraws.Count != 0 || proxySharedUsed != 0)
            {
                FlushBurstDecor();
            }

            // Decor distance is tangential (along the surface): flight
            // altitude does NOT switch all decor off at once. Otherwise at altitude
            // ~MaxDistance everything would disappear at one boundary (trees ~3 km).
            Vector3 cameraUp = cameraPosition - bodyRenderPosition;
            if (cameraUp.sqrMagnitude < 1e-6f)
            {
                cameraUp = Vector3.up;
            }
            else
            {
                cameraUp.Normalize();
            }

            // Decor frustum culling: grass has tens of thousands of instances, and
            // computing matrices for chunks behind the player is wasted. Shadow-casting
            // layers (trees) are not culled — their shadow can reach the frame from behind.
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
                float distance = ChunkDecorDistance3D(chunk, cameraPosition, cameraUp);
                // Bounds follow the actual terrain (|M|·extents, grass margin): both for the
                // frustum test and for RenderMeshInstanced batch culling (a sea-level box
                // around the chunk origin dropped grass outside it "by camera angle").
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
                    float layerMaxDistance = EffectiveMaxDistance(layer);
                    if (distance > layerMaxDistance || runtime.Instances.Length == 0)
                    {
                        continue;
                    }

                    // [PHASE 1] Smooth falloff at the layer radius edge. Without it the
                    // "gained altitude — decor faded" transition would read as a click across
                    // the whole image; the tangential metric was chosen for smooth altitude
                    // gain, so it must be removed smoothly, not with a threshold.
                    float edgeFade = ChunkDecorEdgeFade(distance, layerMaxDistance);
                    if (edgeFade <= 0f)
                    {
                        continue;
                    }

                    // [PHASE 0] Amount of decor actually reaching the draw: without these
                    // numbers one cannot tell "many chunks" from "many instances in each"
                    // at high altitude.
                    SurfacePerf.DecorPools++;
                    SurfacePerf.DecorInstances += runtime.Instances.Length;

                    // Faded pool tail (grass dropped from the new selection on rebuild) is
                    // removed from the arrays: from this frame only live instances are drawn.
                    if (runtime.RetireDeadline > 0f && Time.time >= runtime.RetireDeadline)
                    {
                        CompactRetiredDecor(runtime);
                    }

                    // Distant proxy (trees): the whole chunk is beyond ProxyFromMeters —
                    // cheap mesh, Burst matrices, frustum culling. Shadows follow the layer's
                    // rule (DecorShadowMode); otherwise near forest would look washed out
                    // against the shaded one.
                    if (layer.FarProxy && !layer.PerInstanceDensity && distance > layer.ProxyFromMeters)
                    {
                        DrawTreeProxy(chunk, runtime, layer, chunkMatrix, cameraPosition, edgeFade, chunkWorldBounds, distance);
                        continue;
                    }

                    if (!chunkInFrustum && !layer.CastShadows)
                    {
                        continue;
                    }

                    // A layer without a billboard mesh (trees) always uses the 3D path:
                    // otherwise it would be drawn camera-facing and spin with the player.
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
                        // Matrices via Burst jobs: schedule all chunks at once and compute them
                        // IN PARALLEL, waiting in one pass.
                        ScheduleBurstDecor(
                            chunk, runtime, layer, chunkMatrix, cameraPosition, near, material, chunkWorldBounds);
                        continue;
                    }

                    if (near)
                    {
                        // Managed matrices are not allocated for grass (it uses the Burst path);
                        // this path only runs for layers without PerInstanceDensity, but guard
                        // against null when the mode changes.
                        if (runtime.Matrices == null || runtime.Matrices.Length != count)
                        {
                            runtime.Matrices = new Matrix4x4[count];
                        }

                        // Layer shadow in the shadow map: grass only within the radius
                        // (ShadowCastDistanceMeters), trees/rocks/cacti entirely.
                        // [PHASE 1] And only near the ground: at altitude a tree's
                        // shadow patch is unreadable while the shadow culling pass is real.
                        ShadowCastingMode decorShadow = DecorShadowMode(layer);

                        // The departing pool fades as a whole: otherwise trees vanished
                        // frame by frame on rebuild and when a chunk left the radius.
                        float retireFade = DecorRetireFade(runtime);
                        float growFade = DecorFadeSeconds(DecorPropFadeSeconds);

                        for (int k = 0; k < count; k++)
                        {
                            GroundDecorInstance instance = runtime.Instances[k];
                            Vector3 position = new Vector3(instance.Position.x, instance.Position.y, instance.Position.z);

                            // Orientation by the surface NORMAL: the mesh's local +Y
                            // (top of the card/tree) points along the radial, yaw is
                            // rotation around it. A yaw around world Y would lay cards flat
                            // at non-equatorial latitudes and sink them into the ground.
                            Vector3 up = new Vector3(instance.Normal.x, instance.Normal.y, instance.Normal.z);
                            if (up.sqrMagnitude < 1e-6f)
                            {
                                up = Vector3.up;
                            }
                            else
                            {
                                up.Normalize();
                            }

                            // [PHASE 1] edgeFade is multiplied into the layer's shared fade:
                            // decor does not disappear at the radius boundary but fades
                            // together with the other transitions (growth, sinking
                            // underground). For non-Burst layers (trees, rocks,
                            // cacti) this is the only softness needed.
                            float fade = DecorBirthFade(instance, growFade) * retireFade * edgeFade;
                            runtime.Matrices[k] = chunkMatrix * DecorLocalMatrix(instance, layer, up, fade);
                        }

                        if (layer.CastShadows && layer.ShadowCastDistanceMeters > 0f
                            && layer.NearMeshes.Length == 1 && layer.NearMeshes[0] != null)
                        {
                            // Shadow-cast radius limit (grass): instances within the radius
                            // go into the shadow map, the rest only forward.
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

                                // Beyond the real cast radius — a simple shadow:
                                // a patch offset AWAY from the sun (otherwise hidden under
                                // the bush itself). Inside the radius there is no patch — real shadow there.
                                Vector3 groundUp = grassMatrix.rotation * Vector3.up;
                                float blobScale = runtime.Instances[k].Scale * BlobScaleFactor
                                    * DecorBirthFade(runtime.Instances[k], growFade) * retireFade;
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

                        // Billboard in WORLD coordinates: the instance's local +Y =
                        // surface normal, +Z = camera view in the tangent
                        // plane. All orientation is here; the shader is passthrough.
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

                            // Distant billboards are slightly larger: at the same instance
                            // density islands read denser and do not "crumble" into dots
                            // on the horizon.
                            float farBlend = Mathf.Clamp01(
                                (distance - layer.NearDistanceMeters)
                                / Mathf.Max(1f, layer.MaxDistanceMeters - layer.NearDistanceMeters));
                            float size = instance.Scale * Mathf.Lerp(1.1f, 1.9f, farBlend)
                                * DecorBirthFade(instance, DecorPropFadeSeconds);
                            Vector3 billboardUp = worldUp;

                            // Wind tilt at the far LOD: a billboard cannot tilt "in depth",
                            // but in its own plane it tilts toward the wind direction projection —
                            // otherwise wind zones would only read up close.
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

                            // Simple shadow: the same sun-offset patch as at the near LOD —
                            // grass shadow lives out to MaxDistance.
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

                            // The quad is centred by height: lift it so its base
                            // stands on the ground.
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

        /// <summary>Direction TO the sun from globals (SunBillboard/SkyEnvironment).</summary>
        private static Vector3 GetSunDirectionWS()
        {
            Vector3 sun = Shader.GetGlobalVector("_TerrainSunDir");
            if (sun.sqrMagnitude < 1e-6f)
            {
                sun = Shader.GetGlobalVector("_SkySunDir");
            }

            return sun;
        }

        /// <summary>Point on the chunk's RENDER mesh under the candidate's (u,v):
        /// vertex position interpolation exactly over BuildChunk triangles (a,c,b / b,c,d).</summary>
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

        /// <summary>GCD: the traversal permutation stride must be coprime with count.</summary>
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

        /// <summary>Quad in the XZ plane (centred at origin): the instance matrix lays
        /// it flat on the surface — mesh local +Y = normal.</summary>
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

        /// <summary>One deferred draw of a layer with PerInstanceDensity: the matrix
        /// job is scheduled immediately; waiting and RenderMeshInstanced share one
        /// pass, so chunks are computed in parallel on all workers.</summary>
        private struct BurstDecorDraw
        {
            public Chunk Chunk;
            public DecorLayerRuntime Runtime;
            public Mesh Mesh;
            public Material Material;
            public ShadowCastingMode Shadow;
            public Bounds WorldBounds;
            public JobHandle Handle;
            /// <summary>How many instances the job actually computed. With thinning
            /// (Stride) this is less than runtime.Instances.Length, and exactly that many
            /// must be drawn — the matrix buffer tail holds the previous frame.</summary>
            public int Count;
        }

        /// <summary>Schedules the layer's Burst matrix job (without waiting).</summary>
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

            // Mesh pivot: 0 at the base for a grass blade, 0.5 for a centred
            // quad. The billboard is lifted by that fraction of its height; otherwise
            // a far LOD with a base pivot would hang above the ground.
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
                // Coverage law: size ∝ d from the core edge, clamped to FarScale.
                // At DensityCoreMeters = 25 and MaxDistanceMeters = 200 the far boundary
                // needs ≈8x, otherwise density ∝ (R/d)² is not covered and the zone stays bald.
                BillboardFarScale = 10f,
                DistanceCore = Mathf.Max(1f, layer.DensityCoreMeters),
                BillboardPivotFraction = pivotFraction,
                Now = Time.time,
                FadeSeconds = DecorFadeSeconds(DecorBladeFadeSeconds),
                SinkSeconds = runtime.Retiring
                    ? 0f
                    : (runtime.RetireDeadline > 0f ? runtime.SinkFadeSeconds : DecorFadeSeconds(DecorBladeSinkSeconds))
            };

            ShadowCastingMode shadow = near ? DecorShadowMode(layer) : ShadowCastingMode.Off;

            burstDecorDraws.Add(new BurstDecorDraw
            {
                Chunk = chunk,
                Runtime = runtime,
                Mesh = mesh,
                Material = material,
                Shadow = shadow,
                WorldBounds = worldBounds,
                Count = count,
                Handle = job.Schedule(count, 256, default(JobHandle))
            });
        }

        /// <summary>Layer range including the physical horizon
        /// (HorizonObjectHeightMeters): the camera cannot see past the sum of its two
        /// horizons, so a layer with tall objects (trees) stops at the
        /// physical limit rather than MaxDistanceMeters.</summary>
        private float EffectiveMaxDistance(GroundDecorLayer layer)
        {
            float max = layer.MaxDistanceMeters;
            if (layer.HorizonObjectHeightMeters <= 0f || body == null)
            {
                return max;
            }

            double horizon = SkyPhysics.MaxSightDistance(body.Radius, decorAltitude, layer.HorizonObjectHeightMeters);
            return Mathf.Clamp((float)horizon, layer.NearDistanceMeters + 1f, max);
        }

        /// <summary>Distant tree proxy: a chunk entirely beyond ProxyFromMeters draws
        /// a cheap mesh (trunk + double crown pyramid) instead of the full one. A Burst job
        /// computes matrices, one draw per chunk, shadows as at the near branch
        /// (otherwise forest beyond ProxyFromMeters loses shading and looks washed out).</summary>
        private void DrawTreeProxy(
            Chunk chunk, DecorLayerRuntime runtime, GroundDecorLayer layer,
            Matrix4x4 chunkMatrix, Vector3 cameraPosition, float edgeFade, Bounds chunkBounds,
            float distance)
        {
            int count = runtime.Instances.Length;
            Mesh mesh = GetTreeProxyMesh(layer, decorAltitude > 4000f);
            Material material = layer.NearMaterial;
            if (count == 0 || mesh == null || material == null)
            {
                return;
            }

            // ChunkWorldBounds leaves only 8 m of margin, but the proxy is taller: widen
            // by the layer's worst tree computed from the PROXY itself (mesh height ×
            // instance MaxScale × size boost). A fixed margin here would
            // drop a chunk at the screen edge while its crowns are still visible.
            Bounds bounds = chunkBounds;
            float proxyReach = mesh.bounds.size.y
                * Mathf.Max(1f, (float)layer.MaxScale)
                * Mathf.Max(1f, layer.ProxyScaleBoost);
            bounds.Expand(proxyReach * 2f);
            if (decorFrustumPlanes != null && !GeometryUtility.TestPlanesAABB(decorFrustumPlanes, bounds))
            {
                return;
            }

            // Distance thinning. At 10 km a 20 m tree covers 1-2 pixels,
            // so every 2nd/4th is replaced by size: the forest's total visual "weight"
            // stays almost the same while instances and matrices drop severalfold.
            int stride = distance > 6000f ? 4 : (distance > 3000f ? 2 : 1);
            int drawCount = (count + stride - 1) / stride;
            float strideGrow = Mathf.Pow(stride, 0.35f);

            // Batching into a shared buffer: only when shadows are off (then all
            // distant chunks share one mesh and material — otherwise they cannot be
            // combined into one draw) and there is room. The first chunk sets mesh/material;
            // later ones must match, otherwise fall back to the separate path.
            ShadowCastingMode shadow = DecorShadowMode(layer);
            bool shared = shadow == ShadowCastingMode.Off
                && proxySharedUsed + drawCount <= ProxySharedCapacity
                && (proxySharedUsed == 0
                    || (proxySharedMesh == mesh && proxySharedMaterial == material));

            NativeSlice<Matrix4x4> target;
            if (shared)
            {
                if (!proxyShared.IsCreated)
                {
                    proxyShared = new NativeArray<Matrix4x4>(
                        ProxySharedCapacity, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }

                target = new NativeSlice<Matrix4x4>(proxyShared, proxySharedUsed, drawCount);
            }
            else
            {
                if (!runtime.WorldMatrices.IsCreated || runtime.WorldMatrices.Length != count)
                {
                    DecorArrayPool.Return(ref runtime.WorldMatrices);
                    runtime.WorldMatrices = DecorArrayPool.Rent<Matrix4x4>(count);
                }

                target = runtime.WorldMatrices;
            }

            var job = new GroundDecorMatrixJob
            {
                Instances = runtime.Instances,
                WorldMatrices = target,
                ChunkToWorld = ToFloat4x4(chunkMatrix),
                CameraWorld = new float3(cameraPosition.x, cameraPosition.y, cameraPosition.z),
                Billboard = false,
                FlatOnGround = false,
                ApplyGlobalScale = true,
                GlobalScale = DecorRetireFade(runtime) * edgeFade * strideGrow,
                Stride = stride,
                ScaleBoost = layer.ProxyScaleBoost,
                BoostStart = layer.ProxyBoostStartMeters,
                BoostSpan = Mathf.Max(1f, layer.MaxDistanceMeters - layer.ProxyBoostStartMeters),
                Now = Time.time,
                FadeSeconds = DecorFadeSeconds(DecorPropFadeSeconds),
                SinkSeconds = 0f
            };

            if (shared)
            {
                JobHandle handle = job.Schedule(drawCount, 64, default(JobHandle));
                proxySharedHandle = JobHandle.CombineDependencies(proxySharedHandle, handle);
                if (proxySharedUsed == 0)
                {
                    proxySharedMesh = mesh;
                    proxySharedMaterial = material;
                    proxySharedBounds = bounds;
                }
                else
                {
                    proxySharedBounds.Encapsulate(bounds);
                }

                proxySharedUsed += drawCount;
                return;
            }

            burstDecorDraws.Add(new BurstDecorDraw
            {
                Chunk = chunk,
                Runtime = runtime,
                Mesh = mesh,
                Material = material,
                Shadow = shadow,
                WorldBounds = bounds,
                Count = drawCount,
                Handle = job.Schedule(drawCount, 64, default(JobHandle))
            });
        }

        /// <summary>Low-poly tree (trunk + double crown pyramid, ~26 triangles) built
        /// from the layer's averaged NearMeshes dimensions. Colors in vertex colors, as in
        /// full meshes (TreeModelSetup), alpha 0 — no wind.</summary>
        private Mesh GetTreeProxyMesh(GroundDecorLayer layer, bool low)
        {
            Dictionary<GroundDecorLayer, Mesh> cache = low ? treeProxyMeshesLow : treeProxyMeshes;
            if (cache.TryGetValue(layer, out Mesh cached) && cached != null)
            {
                return cached;
            }

            float height = 0f;
            float radius = 0f;
            int sources = 0;
            for (int i = 0; layer.NearMeshes != null && i < layer.NearMeshes.Length; i++)
            {
                Mesh source = layer.NearMeshes[i];
                if (source == null)
                {
                    continue;
                }

                Vector3 size = source.bounds.size;
                height += size.y;
                radius += Mathf.Max(size.x, size.z) * 0.5f;
                sources++;
            }

            if (sources == 0)
            {
                return null;
            }

            height /= sources;
            radius = Mathf.Clamp(radius / sources, height * 0.2f, height * 0.45f);

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            Color trunk = new Color(0.55f, 0.18f, 0.03f, 0f);
            Color leafDark = new Color(0.008f, 0.05f, 0.014f, 0f);
            Color leaf = new Color(0.01034f, 0.06838f, 0.01848f, 0f);

            // Vertex ring; returns the index of the first.
            int Ring(int segments, float y, float r, Color color, float phase)
            {
                int first = vertices.Count;
                for (int s = 0; s < segments; s++)
                {
                    float a = phase + (s * Mathf.PI * 2f / segments);
                    vertices.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    colors.Add(color);
                }

                return first;
            }

            // Side strip between rings (same segment count), normals facing out.
            void Band(int lower, int upper, int segments)
            {
                for (int s = 0; s < segments; s++)
                {
                    int n = (s + 1) % segments;
                    triangles.Add(lower + s); triangles.Add(upper + s); triangles.Add(upper + n);
                    triangles.Add(lower + s); triangles.Add(upper + n); triangles.Add(lower + n);
                }
            }

            // A trunk at 10 km is 0 pixels; the simplified proxy omits it.
            if (!low)
            {
                int trunkBottom = Ring(4, 0f, height * 0.04f, trunk, 0f);
                int trunkTop = Ring(4, height * 0.35f, height * 0.03f, trunk, 0f);
                Band(trunkBottom, trunkTop, 4);
            }

            int crownSegments = low ? 4 : 6;
            int crownLow = Ring(crownSegments, height * 0.22f, radius * 0.7f, leafDark, 0f);
            int crownMid = Ring(crownSegments, height * 0.55f, radius, leaf, 0f);
            Band(crownLow, crownMid, crownSegments);

            int apex = vertices.Count;
            vertices.Add(new Vector3(0f, height, 0f));
            colors.Add(leaf);
            for (int s = 0; s < crownSegments; s++)
            {
                int n = (s + 1) % crownSegments;
                triangles.Add(crownMid + s); triangles.Add(apex); triangles.Add(crownMid + n);
            }

            var proxy = new Mesh
            {
                name = layer.Name + (low ? "_FarProxyLow" : "_FarProxy"),
                hideFlags = HideFlags.HideAndDontSave
            };
            proxy.SetVertices(vertices);
            proxy.SetColors(colors);
            proxy.SetTriangles(triangles, 0);
            proxy.RecalculateNormals();
            proxy.RecalculateBounds();
            cache[layer] = proxy;
            return proxy;
        }

        /// <summary>Calls all scheduled jobs and draws the layers.</summary>
        private void FlushBurstDecor()
        {
            // Shared distant-tree buffer — before the list check: it also
            // needs Complete and drawing even when burstDecorDraws is empty.
            if (proxySharedUsed > 0)
            {
                proxySharedHandle.Complete();
                DrawProxyShared();
                proxySharedUsed = 0;
                proxySharedHandle = default(JobHandle);
            }

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
                    draw.Count, draw.Shadow, draw.WorldBounds);
            }

            burstDecorDraws.Clear();
        }

        /// <summary>Shared distant-tree buffer in one set of draw calls.
        /// Mesh and material are the same for all batched chunks (otherwise they
        /// would not combine in DrawTreeProxy), shadows off.</summary>
        private void DrawProxyShared()
        {
            if (!proxySharedMaterial.enableInstancing)
            {
                proxySharedMaterial.enableInstancing = true;
            }

            var renderParams = new RenderParams(proxySharedMaterial)
            {
                worldBounds = proxySharedBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                layer = gameObject.layer
            };

            const int batchSize = 1023;
            for (int start = 0; start < proxySharedUsed; start += batchSize)
            {
                int n = System.Math.Min(batchSize, proxySharedUsed - start);
                Graphics.RenderMeshInstanced(renderParams, proxySharedMesh, 0, proxyShared, n, start);
            }
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
            // Bounds are actual (terrain + margin): Unity culls the batch by them;
            // a sea-level box around the chunk origin dropped grass entirely
            // on elevations at certain camera angles.
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
                // DrawMeshInstanced requires instancing enabled on the material.
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

                // All submeshes: in an FBX the trunk and foliage may be one mesh
                // with two materials; otherwise the tree draws without its crown.
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    Graphics.DrawMeshInstanced(
                        mesh, sub, material, batch, n, null,
                        shadowMode, false, gameObject.layer);
                }

                start += n;
            }
        }

        /// <summary>Marker-only wrapper: chunk unloading runs in batches and can
        /// itself cause a hitch if Destroy is called without a limit.</summary>
        private void EvictIfNeeded()
        {
            using (SurfacePerf.EvictMarker.Auto())
            {
                EvictIfNeededCore();
            }
        }

        private void EvictIfNeededCore()
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

            SurfacePerf.EvictCandidates = toEvict.Count;
            if (toEvict.Count == 0)
            {
                // Everything is in keep, nothing to clean up. The candidate counter
                // is exposed in the HUD so this state is visible.
                return;
            }

            // LRU: the longer a chunk has been unneeded, the sooner it unloads. Dictionary
            // traversal order means nothing, and batch Destroy in one
            // frame is itself a hitch, so cut one at a time.
            toEvict.Sort(GetEvictLruComparer());

            int removed = 0;
            for (int i = 0; i < toEvict.Count && chunks.Count > MaxCachedChunks; i++)
            {
                if (removed >= MaxEvictionsPerFrame)
                {
                    break;
                }

                long id = toEvict[i];
                if (!chunks.TryGetValue(id, out Chunk chunk))
                {
                    continue;
                }

                DestroyChunkResources(chunk);
                chunks.Remove(id);
                // Safety: the chunk being unloaded is already hidden by the pass above, but
                // if the order changes, a dead reference would remain in the active list and
                // the transform pass would walk a destroyed object.
                visibleChunks.Remove(chunk);
                removed++;
            }

            SurfacePerf.EvictedLast = removed;
        }

        /// <summary>Cached comparison for List.Sort. The closure is created ONCE
        /// per component, not per sort call: otherwise every frame with an eviction
        /// would allocate a closure object — exactly the frame that hits the GC
        /// budget. A captureless lambda is impossible here: it needs the chunk dictionary.</summary>
        private System.Comparison<long> evictLruComparer;

        private System.Comparison<long> GetEvictLruComparer()
        {
            if (evictLruComparer == null)
            {
                evictLruComparer = (a, b) =>
                {
                    float ta = chunks.TryGetValue(a, out Chunk ca) ? ca.LastKeepTime : float.NegativeInfinity;
                    float tb = chunks.TryGetValue(b, out Chunk cb) ? cb.LastKeepTime : float.NegativeInfinity;
                    return ta.CompareTo(tb);
                };
            }

            return evictLruComparer;
        }

        private static void DestroyChunkResources(Chunk chunk)
        {
            DisposeDecor(chunk);
            chunk.WaterRenderer = null;
            if (chunk.WaterMesh != null)
            {
                Destroy(chunk.WaterMesh);
                SurfacePerf.NoteMeshDestroyed();
                chunk.WaterMesh = null;
            }

            // Mesh does NOT belong to the GameObject: Destroy(chunk.Go) does not remove it.
            if (chunk.Mesh != null)
            {
                Destroy(chunk.Mesh);
                SurfacePerf.NoteMeshDestroyed();
                chunk.Mesh = null;
            }

            if (chunk.Go != null)
            {
                Destroy(chunk.Go);
                chunk.Go = null;
            }
        }

        private void SetAllInvisible()
        {
            foreach (KeyValuePair<long, Chunk> kv in chunks)
            {
                if (kv.Value.Visible)
                {
                    SetChunkVisible(kv.Value, false);
                }
            }

            visibleChunks.Clear();
        }

        /// <summary>
        /// Shows/hides a chunk and its water mesh next to it.
        ///
        /// Via Renderer.enabled, not GameObject.SetActive. SetActive
        /// walks the whole hierarchy and triggers OnEnable/OnDisable on all
        /// components and children; with 1000+ chunks toggled every
        /// frame at LOD boundaries that is noticeable wasted work. The renderer is
        /// culled exactly the same way, while colliders and components on the object
        /// stay alive — which is required: terrain physics and player rays must not
        /// depend on whether the node was drawn this frame.
        ///
        /// visibleChunks is maintained here too: the transform pass iterates
        /// it instead of the whole dictionary, where thousands of
        /// invisible nodes sit at speed.
        /// </summary>
        private void SetChunkVisible(Chunk chunk, bool visible)
        {
            if (chunk == null || chunk.Go == null)
            {
                return;
            }

            if (chunk.Visible == visible)
            {
                return;
            }

            chunk.Visible = visible;
            if (chunk.Renderer != null)
            {
                chunk.Renderer.enabled = visible;
            }

            // Water is a separate child object with its own MeshRenderer; the reference
            // is cached at build time.
            if (chunk.WaterRenderer != null)
            {
                chunk.WaterRenderer.enabled = visible;
            }

            if (visible)
            {
                visibleChunks.Add(chunk);
            }
            else
            {
                visibleChunks.Remove(chunk);
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
        /// Terrain per-pixel palette/noise/texture parameters live in shader
        /// GLOBALS, not in the material: per-material values not declared in
        /// Properties do not reach the material. The dominant body sets the globals
        /// every frame; the material state does not matter. Colors are float4: already linear.
        /// </summary>
        private void ApplyTerrainGlobals()
        {
            ApplyTerrainGlobals(terrain, ShoreWetMeters, ShoreWetTint);
            colorStateCache = CaptureColorState();
        }

        /// <summary>
        /// Terrain shader globals for any HeightfieldTerrain. Kept out of
        /// instance methods unchanged: the editor surface preview must get EXACTLY
        /// the same light, palette and textures as the game, otherwise authors
        /// tune the base against an image the game does not show. Single source of truth, do not duplicate.
        /// </summary>
        public static void ApplyTerrainGlobals(HeightfieldTerrain terrain, float shoreWetMeters, Vector4 shoreWetTint)
        {
            if (terrain == null)
            {
                return;
            }

            TerrainPaletteData palette = terrain.Palette ?? new TerrainPaletteData();
            Shader.SetGlobalFloat("_TerrainAmplitude", (float)System.Math.Max(1d, terrain.AmplitudeMeters));
            Shader.SetGlobalFloat("_TerrainSeaLevel", (float)System.Math.Max(terrain.SeaLevelMeters, -1e30d));
            Shader.SetGlobalFloat("_BeachHeightMeters", (float)System.Math.Max(0d, terrain.BeachHeightMeters));
            Shader.SetGlobalFloat("_ShoreWetMeters", shoreWetMeters);
            Shader.SetGlobalVector("_ShoreWetTint", shoreWetTint);
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

            // Terrain textures: triplanar blending by height
            // and slope. All four albedos are required, otherwise — procedural palette.
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
