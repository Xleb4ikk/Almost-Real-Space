# INVENTORY: Приложение D (динамический выбор интегратора)

Файл ведёт агент. Раздел «D0» — этап D0 (TASK_D.md §7). Разделы следующих этапов дописываются по мере
выполнения. Все ссылки «файл:строка» проверены чтением рабочей копии на commit `0b63345`
(ветка `integrator-port/dynamic-policy`), дата 2026-11-14.

---

## D0. Ритуал и базовая линия

### D0.1. Ветка и база

| что | значение |
|---|---|
| ветка задания | `integrator-port/dynamic-policy` |
| базовый коммит | `0b6334553cc4e0f7908d8fce5314cd3685eb3691` (`Q-C8` на `integrator-port/wh-bm14a`) |
| `TASK_D.md` (v5) SHA256 | `388f70f83fb28daba07c9b71cb0706795f0ec22f83db7d31908ace6471e07ffa` |
| `TASK.md` (v4, папка Q) SHA256 | `7779706cf649a4c7dfe1a12b34fcd9aee97084fe600c3c0ea44440d487db1b16` |
| код игры в D0 | не менялся (`git diff` по `*.cs`, `*.csproj`, `*.props` пуст) |

### D0.2. Перепроверка таблицы §2 TASK_D.md по коду

Каждая строка — «элемент → файл:строка → что подтверждено».

| элемент §2 | файл:строка | подтверждено в коде |
|---|---|---|
| `IntegratorPolicy.Select(in IntegratorContext)` — чистая функция | `Assets/_Project/Scripts/Spacecraft/IntegratorPolicy.cs:131` | `public static SpacecraftIntegrator Select(in IntegratorContext context)`; только чтение полей контекста, без глобалов и времени |
| `IntegratorContext` — 10 полей | `IntegratorPolicy.cs:14-81` | `ChunkSeconds, FlightModelActive, SunDominant, SunCoastAllowed, CanCoast, AltitudeMeters, VerticalSpeedMps, CentralMu, Eccentricity, PortedAllowed`; `readonly struct` |
| Ветка Surface → DOPRI5 | `IntegratorPolicy.cs:99-100`, `:133-141` | `SurfaceChunkSeconds=0.05`, `SurfaceAltitudeMeters=10 000`; условие `FlightModelActive && ChunkSeconds <= 0.05+1e-9 && AltitudeMeters < 10 000`, исключение `VerticalSpeedMps < 0 && AltitudeMeters < TouchdownGuardMeters` (`TouchdownGuardMeters=2 000`, `:115`) |
| Ветка Sun-coast → DOPRI5 | `IntegratorPolicy.cs:108`, `:147-153` | `SunDominant && SunCoastAllowed && CanCoast && AltitudeMeters >= MinSunDistanceMeters`, `MinSunDistanceMeters=1e10` |
| Ветка Ported → WH-BM14A | `IntegratorPolicy.cs:123-129`, `:158-170` | `PortedAllowed && CanCoast && !SunDominant && !FlightModelActive && ChunkSeconds >= 600 && 200 000 <= AltitudeMeters <= 2 000 000 && CentralMu > 0 && 0 <= Eccentricity < 0.05` |
| Порядок ветвей | `IntegratorPolicy.cs:131-173` | Surface → Sun-coast → Ported → `Dop853` (default). Как в §3 DECISION_TABLE.md |
| Применение политики на каждый `Propagate`/`LongWarp` | `Assets/_Project/Scripts/Simulation/VesselStep.cs:213` (long warp), `:256` (живой чанк) | `ApplyIntegratorPolicy(OrbitIntegrator.DefaultMaxStepSize, false)` перед `Driver.AdvanceToTarget`; `ApplyIntegratorPolicy(chunk, flightOn)` перед `Propagator.Propagate` |
| Ранний выход при выключенных флагах | `VesselStep.cs:330-337` | `if (!EnableRegimeIntegratorPolicy && !EnablePortedIntegratorPolicy) { Propagator.Integrator = Dop853; LastSelectedIntegrator = Dop853; return; }` — без lookup'ов. Следствие: `EnableSunCoastPolicy=true` в одиночку ничего не включает |
| Сборка `IntegratorContext` в игровом пути | `VesselStep.cs:359-384` (геометрия), `:386-408` (CanCoast), `:410-420` (контекст) | высота/вертикальная скорость/`CentralMu`/`e` считаются от доминирующего тела (`FindDominantBody`), `e` — из `OrbitalElements.FromState`, `CanCoast` — из `Driver.CanEnterLongWarp` только для sun/ported-кандидатов |
| `LastSelectedIntegrator` — наблюдаемость | `VesselStep.cs:80`, `:335`, `:422` | `public SpacecraftIntegrator LastSelectedIntegrator { get; private set; } = SpacecraftIntegrator.Dop853;` |
| `UseDenseRefinement=false` только для WH-BM14A | `VesselStep.cs:424-426` | `Propagator.UseDenseRefinement = selected != SpacecraftIntegrator.WhBm14a;` |
| Флаги в Inspector | `Assets/_Project/Scripts/Simulation/SimulationRunner.cs:65,68,71` | `public bool EnableRegimeIntegratorPolicy;` / `EnableSunCoastPolicy;` / `EnablePortedIntegratorPolicy;` — без инициализаторов, значение по умолчанию `false` |
| Копирование флагов в `VesselStep` в `Awake` | `SimulationRunner.cs:574-578` | `step.Enable* = Enable*;` сразу после создания `VesselStep` |
| `HardHorizonSeconds` | `SimulationRunner.cs:568` | `propagator.HardHorizonSeconds = SystemState.BakedEndSeconds();` (горизонт D2, §6 TASK_D.md) |
| `SpacecraftIntegrator` | `Assets/_Project/Scripts/Spacecraft/SpacecraftPhysics.cs:8-13` | `Dop853, Dopri5, WhBm14a` (новое значение в конце; существующие не сдвинуты) |
| DOP853 по умолчанию | `SpacecraftPhysics.cs:31` | `public SpacecraftIntegrator Integrator = SpacecraftIntegrator.Dop853;` |
| Ветка WH-BM14A в физике | `SpacecraftPhysics.cs:197-201` (локальный кадр), `:271-287` (абсолютный путь) | локальный кадр — `WhBm14aIntegrator.StepForward`, абсолютный — безопасный откат на DOP853 |
| Порт WH-BM14A | `Assets/_Project/Scripts/Core/Integration/WhBm14aIntegrator.cs` (6 215 Б), `KeplerUniversalDrift.cs` (9 934 Б) | файлы на месте |
| Лимит шага игры | `Core/Integration/OrbitIntegrator.cs:16-17` | `DefaultMaxStepSize = 600.0`, `DefaultMinStepSize = 1e-6` |
| Чанк полёта | `VesselStep.cs:89` | `FlightChunkSeconds = 0.05d` |

Расхождений с таблицей §2 TASK_D.md **не найдено**.

### D0.3. Тесты политики (наличие и роль)

| тест | файл:строка | что покрывает |
|---|---|---|
| `ProductionUsesDop853ByDefault` | `tests/GalilegoSimTests/ProductionIntegratorDefaultTests.cs:9` | `new SpacecraftPhysics().Integrator == Dop853` |
| `IntegratorPolicyTests` (20 методов) | `tests/GalilegoSimTests/IntegratorPolicyTests.cs:11` | таблица `Select` по веткам Surface/Sun/Ported и их границам |
| `LiveChunkPolicyTests` (4 метода) | `tests/GalilegoSimTests/LiveChunkPolicyTests.cs:14` | живой чанк: стоимость и совпадение с DOP853 |
| `WhBm14aProductionTests` (8 методов) | `tests/GalilegoSimTests/WhBm14aProductionTests.cs:18` | WH-BM14A: абсолютный откат, dense, события, детерминизм, аллокации, J2 |
| `Test561_IntegratorPolicy` | `Tools/P1bTests/Tests/IntegratorPolicyRuntime.cs:16` | сквозной путь `VesselStep`: policy off == direct Step, поверхность, sun-coast, ported |

### D0.4. Базовая линия прогонов (эталон до этапов D1–D7)

Окружение: Windows 10.0.26200, AMD Ryzen 9 8940HX (16C/32T), .NET SDK 10.0.401, `net8.0`.

| проверка | результат |
|---|---|
| `dotnet build Tools/P1bTests -c Release` | 0 ошибок, 16 предупреждений, 7.59 с |
| `GalilegoSimTests` | **60 / 60**, 0 failed, 558 мс |
| `GalilegoPhysicsTests` | **283 / 283**, 0 failed, 9 с |
| `Test551` (`verify_test551.ps1`) | `lines=84 fails=0`, SHA256 `54ae98e23563b5bec8551f9188a2b83424b049cc6e4cd5deba43141402aec5e6` |
| `Test561` | `ALL PASS`, 16 проверок, 96 мс |

Хэши упорядоченных имён тестов (ordinal, LF, UTF-8 без BOM, завершающий LF):

| набор | тестов | SHA256 |
|---|---|---|
| `GalilegoSimTests` | 60 | `cf88d38e6d55bd8e232b72066e20569a7ad9f87483741855e226b8118794a86e` |
| `GalilegoPhysicsTests` | 283 | `f491a27b3504c173c91d3c7a7c49a93592da1ac5b8b703a244995745b3ee37f7` |
| вместе, один LF-поток (sim, затем phys) | 343 | `1e50e0fc76a79c48fd839a415eb870b8865ab97bda843524611409c766797886` |
| вместе, sim-блок + пустая строка + phys-блок | 343 | `a74221ba5c8e3ebfaf3fcec7b6ef103feea5754321431a4af6fa1b201ce4ae7a` |

### D0.5. Наличие эталонного production-прогона

`Tools/P1bTests/Study/qualification/results/20261114T151403Z_e860827/` — присутствует, 15 файлов:
`chunks.csv`, `compare.md`, `conditions.csv`, `CONDITIONS_REPORT.md`, `COST_TABLE.md`, `environment.txt`,
`game_control.csv`, `matrix.csv`, `METHOD_QUALIFICATION.md`, `oracle_audit.md`, `PRODUCTION_REPORT.md`,
`real_system.csv`, `RECOMMENDATIONS.md`, `run.log`, `runs.csv`.
Файлы не изменялись (каталог вне рабочей копии git-индекса, но не редактировался агентом).

### D0.6. Что дописывается на следующих этапах

D1 — сверка чисел `DECISION_TABLE.md` с CSV, ссылки «прогон/файл.csv: problem,method,goal[,колонка]»,
факты D1.d. D2/D3 — измерения G1/T1–T6 и причины выбора. D4/D5 — результаты `VesselStep`-прогонов
(`dynamic.csv`).

---

## D1. Таблица решений и доказательства

### D1.1. Чистый production-прогон

| поле | значение |
|---|---|
| папка | `qualification/results/20261114T193520Z_ff36340/` |
| `environment.txt` | `profile: production`, `commit: ff36340cd5cd921e1cfef9cd09d07b754fde264b`, `branch: integrator-port/dynamic-policy`, `dirty: clean`, 17 методов, .NET 8.0.24 |
| длительность | 948 031 мс |
| ячейки `matrix.csv` | 833: `PASS=746`, `FAIL=48`, `SKIP=39` |
| `game_control.csv` | 221 `ok`, 0 aborted/error |
| `chunks.csv` | 1105 `ok`, 0 error/aborted |
| ERROR | 0 |

Сравнение с `20261114T151403Z_e860827` (гейт §6): 833 общих ячейки, **0** различий статусов,
**0** цен с отклонением > 10 %. `runs.csv`/`matrix.csv` двух прогонов совпали побитово.
`game_control.csv` отличается для адаптивных study-методов: старый прогон был `dirty`, протокол
game-control появился после `e860827` (`QualificationChunkTest.cs`, коммит `7b3e7bb Q-C3`).
Все числа для включения в `DECISION_TABLE.md` взяты из нового прогона.

### D1.2. Аудит доказательств Surface/Sun-coast

| ветка | CSV-строки | игровые допуски | артефакты в репозитории | статус |
|---|---|---|---|---|
| Surface (R2) | нет: 13 production-задач без 6-DOF/тяги; `chunks.csv` 0.05 с — орбитальные чанки | нет | `LiveChunkPolicyTests.cs:41-65`; `IntegratorPolicyRuntime.cs:126-138`; `TouchdownEventIntegratorTests.cs` | `NO-EVIDENCE` |
| Sun-coast (R3) | нет: класса `heliocentric` в production нет (`PRODUCTION_REPORT.md`, вопрос 10) | нет | `LiveChunkPolicyTests.cs:16-38`; `IntegratorPolicyRuntime.cs:24-41,71-92` | `NO-EVIDENCE` |

Дополнительно: 600-с Sun-coast в репозитории не измерялся; `IntegratorPolicy.cs:93` ссылается на
«the 30-50 s live chunk and the 600 s stress chunk», но тест есть только на 30 с.

### D1.3. Факты D1.d (только чтение)

| вопрос | факт | ссылка |
|---|---|---|
| тела с `J2 == 0` | все: сцены `Sol` и `Terra` (`J2: 0`), остальные сцены без поля → default 0; `ExampleSystemPresets` J2 не задаёт | `OutdoorsScene.unity:426,953`; `ExampleSystemPresets.cs:22-59` |
| орбиты в диапазоне Ported | у тел — нет (орбиты тел: Terra a=5.519e10 м); корабль стартует на поверхности во всех 4 сценах | `OutdoorsScene.unity:1337-1350`; `ContactZoneSlice.unity:222-235`; `PlayerRoofSlice.unity:466-479`; `PlayerWalkSlice.unity:391-404` |
| Terra как тело для R1 | R=1 143 000 м, μ=1.28e13; 200/400/2000 км → v_circ 3087/2880/2018 м/с; v_esc 4733 м/с; SOI 1.64e8 м | `OutdoorsScene.unity:949-970`; формулы — `StarSystem.SphereOfInfluenceRadius` |
| гелиоцентрический coast ≥ 1e10 м | возможен во всех сценах после выхода из SOI Terra (1.64e8 м); требует обоих флагов и `CanCoast` | `SimulationRunner.cs:65-71`; `VesselStep.cs:347-357` |
| cislunar | недостижим: Луны в сценах нет (только Sol + Terra) | `ExampleSystemPresets.cs` используется только `Tools/P1bTests` |
| флаги в `.unity` | не сериализованы нигде (0 совпадений), действует default `false` | `grep` по `Assets/**/*.unity,*.prefab,*.asset` |
| горизонт одного `AdvanceToTarget` | `BakedEndSeconds() = +∞`: `StreamingAssets/Ephemerides` отсутствует | `SimulationRunner.cs:568`; `StarSystem.cs:711-724` |

