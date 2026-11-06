# Карта стенда

Весь код стенда — `partial class P1bTests`, разложенный по папкам. Какой файл за что
отвечает:

## `core/`

`StudyConstants.cs` — `StudyYear`, `StudyAU`, `StudyShipT0`; общие типы
`StudyNBody`, `StudyRefResult`, `StudySample`.

## `fixtures/`

`StudyFixtures.cs` — `StudyBody`, `StudyRealSolar7`, `StudyCustomSystem`,
`StudyTightBinarySystem`, `StudyEccentricSystem`, `StudyHeliocentricSystem`,
`StudyMakeNBody`, `StudyShiftSubtree`. Здесь задаются системы и их начальные
состояния (включая барицентрическую поправку поддеревьев, как в `EphemerisBaker`).

## `framework/`

Независимое от движка ядро сравнения интеграторов (Test551–553):

| Файл | Что внутри |
|---|---|
| `StudyOde.cs` | `StudyProblem`, `StudyConfig`, `StudyRun`, `StudyContext`, сборка стоп-точек |
| `StudyIntegratorApi.cs` | `IStudyIntegrator`, базовые `FixedStepIntegrator` / `AdaptiveIntegrator` |
| `StudySteppers.cs` | RK4, DOPRI5, DOP853, PEFRL, Y6/Y6C, WH-Y6, leapfrog |
| `StudyRegistry.cs` | список методов и выбор по `P1B_INTEGRATORS` |
| `StudySweep.cs` | прогон work-precision, `StudyOptions`, подсчёт стоимости |
| `StudyReport.cs` | таблицы, ничьи, CSV и Markdown |
| `StudySelfTest.cs` | Test551: порядок, стоимость вызовов, бюджеты, смоук-свип |
| `StudyBridge.cs` | единственный файл, знающий движок; корабли и планеты для Test552/553 |
| `KeplerUniversal.cs`, `Dop853Coefficients.cs` | универсальный Кеплер и таблица DOP853 |

Стоимость в отчётах — вызовы RHS **реально выполненного** прогона, чья ошибка не выше
цели (без интерполяции). Вес одного Kepler-решения фиксирован (1 RHS-эквивалент), а не
измеряется по wall-clock, поэтому повторные прогоны сравнимы. Для корабля цена вызова
умножается на 3 (`P1B_RHS_MULTIPLIER` переопределяет это число); планеты считаются по 1.
Методы в пределах 10 % от лучшего помечаются `~` как ничья. Ничьи между одним
методом и его же cap-вариантом не считаются (это конфигурация, а не другой метод).

Профиль тяги в игре: `SpacecraftBuilder` всегда собирает 6-DOF `VesselFlightModel`
и вызывает `SimulationRunner.SetVesselFlight`, который заменяет прямой stage-thrust
на flight source. В `VesselFlightModel.Prepare` уровень двигателя подходит к цели
через `ThrottleRatePerS * dt` (у двигателей 1.5–10 /с), поэтому резкой отсечки
тяги в штатном полёте нет. Рамп 1 с в сценарии «unaligned burn» — консервативный
production-like режим; сценарий с `ramp = 0` был бы стресс-тестом разрыва, а не
сравнением порядка методов.

## `methods/`

Старый (Test549) набор по одному файлу на метод/семейство:

| Файл | Что внутри |
|---|---|
| `Rk4.cs` | `StudyRk4Step` — RK4 |
| `Dopri5.cs` | `StudyDopri5Fixed` — DOPRI5, фиксированный шаг |
| `Pefrl.cs` | `StudyPefrl` — PEFRL |
| `Yoshida6.cs` | веса Ёсиды, `StudyY6` и `StudyY6C` (compensated) |
| `Dop853Tableau.cs` | таблица DOP853 (Hairer/Wanner) |
| `Dop853Ship.cs` | `StudyShipDop853`, `StudyShipDop853Local` |
| `FixedShip.cs` | `StudyShipFixed`, `StudyShipFixedLocal` (Y6/PEFRL для корабля) |
| `WisdomHolman.cs` | `StudyKeplerDrift`, `StudyWhStep*`, `StudyShipWh*` |
| `GamePath.cs` | `StudyShipGame`, `StudyCloneBakedSystem` — реальный игровой путь |
| `StepDispatch.cs` | `StudyStep` — диспетчер методов по имени |

## `references/`

| Файл | Что внутри |
|---|---|
| `NBodyReference.cs` | `StudyAcc`, `StudyAccC` (compensated), `StudyAdd` |
| `RefRun.cs` | `StudyRefRun`, `StudyRel`, `StudyEnergy` |
| `DoubleDouble.cs` | `StudyDd`, `StudyDd3`, `StudyForceDd` — dd-арифметика |
| `ShipReferences.cs` | `StudyShipRefA`, `StudyShipRefLocal`, `StudyShipY6CDd` |

## `scenarios/`

| Файл | Что внутри |
|---|---|
| `Planets.cs` | `StudySystemTable`, `StudyPlanetTable`, `StudyBake`, `StudyBakeStep` |
| `Ships.cs` | `StudyShipOrbit`, `StudyLocalAcceleration*`, `StudyShipTable`, `PrintShipRow` |
| `Events.cs` | `StudyLiveCost`, `StudyEvent`, `StudyShipFixedStep` — warp/событие |
| `ReferenceCases.cs` | `StudyReferenceCase`, `StudyKeplerReference`, `StudyTimeReversal`, `StudyPlanetReference` |
| `KnownProblems.cs` | `Test505_KnownProblems` (Lagrange/figure-eight/Аренсторф, double+dd) и `Test506_KnownConvergence` (оценка K-порядка) |

## `tests/`

| Файл | Тест |
|---|---|
| `Test500.cs` | `Test500_IntegratorStudyFast` — планеты 10 лет, 6 сценариев корабля, бейк, warp |
| `Test501.cs` | `Test501_IntegratorStudySlow` — 100 лет |
| `Test503.cs` | `Test503_ReferenceConvergence` — не в исходном реестре, доступен как `--reference` |

## `taylor/`

`TaylorReferenceTests.cs` — `Test504_TaylorReference` (Taylor vs Y6C/Y6/RK4/DOP853),
`ForceVsProduction.cs` — `Test519_ForceVsProduction` (сила Taylor/dd/local против
производственной), плюс пробы бюджета dd и Chebyshev.

## `TaylorVsY6/`

| Файл | Что внутри |
|---|---|
| `Compare.cs` | CLI-режимы `--real/--custom/--ships/--kepler/--y6c/--dd-*`; Taylor vs Y6 |
| `TaylorDd.cs` | dd-джеты: старая реализация, арена/in-place (`DdView`/`DdArena`), self-check, сравнение реализаций |

## `integrators/` и `jpl/`

Старые матрицы Test549/550 (`SpacecraftOrbitMatrix`, `CertifiedReference`,
`JplRealism`, `FullComparison` и т. д.). Они по-прежнему используют собственный
cost-модель и собственные эталоны; новые сравнения делаются в `framework/`.
Поле `WorldChunkSeconds` относится только к production-пути Test549 (чанки
локального кадра); `framework/` его не читает, поэтому в Test552 у сценария
«Earth LEO 400 km, 30 d» больше нет пометки про world chunk.

## Как добавить новый метод

1. Написать stepper (`IFixedStepper` или `IAdaptiveStepper`) в `framework/StudySteppers.cs`
   либо в отдельном файле. Силу вызывать только через `ctx.Rhs(...)`.
2. Унаследоваться от `FixedStepIntegrator` / `AdaptiveIntegrator`, заполнить `Name`,
   `Family`, `ExpectedOrder` (fixed), `Supports(...)`, `CreateStepper(...)`.
3. Добавить экземпляр в `StudyRegistry.All()`.

Свип, стоимость, отчёты и CSV менять не нужно. Пример — `StudyExampleIntegrator.cs`.

## Как добавить сценарий

1. Для корабля — новый case в `StudyShipMatrixBuildCases` (`integrators/SpacecraftOrbitMatrix.cs`).
2. Для планет — фикстура в `fixtures/StudyFixtures.cs` и строка в
   `Test553_IntegratorFrameworkPlanetSweep` (`framework/StudyBridge.cs`).
3. Если нужен новый тип задачи (другая размерность/метрика), собрать `StudyProblem`
   в `StudyBridge.cs`: `Rhs`, `CheckpointTimes`, `CheckpointRef`, `Floor`, `CentralMu`,
   `ConservativeSecondOrder`, `ScaleLength`, `CharTime`.

## Запуск

```
dotnet build -c Release Tools/P1bTests/P1bTests.csproj
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test551_IntegratorFrameworkSelfTest
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test552_IntegratorFrameworkShipSweep
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test553_IntegratorFrameworkPlanetSweep
```

Фильтры: `P1B_SCENARIOS` (корабли), `P1B_PLANET_SCENARIOS` (планеты),
`P1B_INTEGRATORS` (список методов), `P1B_RHS_MULTIPLIER` (цена RHS).
`P1B_PLANET_YEARS` оставлен как быстрый шорткат: только реальная солнечная система
на указанном горизонте.

## Движок

`Assets/_Project/Scripts/` — настоящий код проекта (не копии). Ядро интегратора:
`Core/Integration`, `Spacecraft/Dynamics`, `Simulation/Events`, `Simulation/Warp`,
`Ephemeris`, `Universe`, `Core/Orbits`, `Core/Math`. Остальное — compile-closure
(террейн, декор, flight, solver). Менять их в пакете можно, но это уже не «стенд»,
а реальный движок.
