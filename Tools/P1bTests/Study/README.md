# Карта стенда

Весь код стенда — `partial class P1bTests`, разложенный по папкам. Какой файл за что
отвечает:

## `core/`

`StudyConstants.cs` — `StudyYear`, `StudyAU`, `StudyShipT0`; общие типы
`StudyNBody`, `StudyRefResult`, `StudySample`.

## `fixtures/`

`StudyFixtures.cs` — `StudyBody`, `StudyRealSolar7`, `StudyCustomSystem`,
`StudyMakeNBody`, `StudyShiftSubtree`. Здесь задаются обе системы и их начальные
состояния (включая барицентрическую поправку поддеревьев, как в `EphemerisBaker`).

## `methods/`

По одному файлу на метод/семейство:

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

## `tests/`

| Файл | Тест |
|---|---|
| `Test500.cs` | `Test500_IntegratorStudyFast` — планеты 10 лет, 6 сценариев корабля, бейк, warp |
| `Test501.cs` | `Test501_IntegratorStudySlow` — 100 лет |
| `Test503.cs` | `Test503_ReferenceConvergence` — не в исходном реестре, доступен как `--reference` |

## `taylor/`

`TaylorReferenceTests.cs` — `Test504_TaylorReference` (Taylor vs Y6C/Y6/RK4/DOP853).

## `TaylorVsY6/`

| Файл | Что внутри |
|---|---|
| `Compare.cs` | CLI-режимы `--real/--custom/--ships/--kepler/--y6c/--dd-*`; Taylor vs Y6 |
| `TaylorDd.cs` | dd-джеты: старая реализация, арена/in-place (`DdView`/`DdArena`), self-check, сравнение реализаций |

## Как добавить новый метод/сценарий

1. Метод — новый `.cs` в `methods/`, любой приватный `static`-метод в
   `partial class P1bTests`. Сборка подхватывает `Study/**/*.cs` автоматически.
2. Эталон — в `references/`.
3. Сценарий — в `scenarios/`; вызвать из `tests/Test500.cs` или добавить новый тест
   и строку в `Runner.cs`.

## Движок

`Assets/_Project/Scripts/` — 114 файлов настоящего кода проекта (не копии).
Ядро интегратора: `Core/Integration`, `Spacecraft/Dynamics`, `Simulation/Events`,
`Simulation/Warp`, `Ephemeris`, `Universe`, `Core/Orbits`, `Core/Math`. Остальное —
compile-closure (террейн, декор, flight, solver). Менять их в пакете можно, но это
уже не «стенд», а реальный движок.
