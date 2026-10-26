# РљР°СЂС‚Р° СЃС‚РµРЅРґР°

Р’РµСЃСЊ РєРѕРґ СЃС‚РµРЅРґР° вЂ” `partial class P1bTests`, СЂР°Р·Р»РѕР¶РµРЅРЅС‹Р№ РїРѕ РїР°РїРєР°Рј. РљР°РєРѕР№ С„Р°Р№Р» Р·Р° С‡С‚Рѕ
РѕС‚РІРµС‡Р°РµС‚:

## `core/`

`StudyConstants.cs` вЂ” `StudyYear`, `StudyAU`, `StudyShipT0`; РѕР±С‰РёРµ С‚РёРїС‹
`StudyNBody`, `StudyRefResult`, `StudySample`.

## `fixtures/`

`StudyFixtures.cs` вЂ” `StudyBody`, `StudyRealSolar7`, `StudyCustomSystem`,
`StudyMakeNBody`, `StudyShiftSubtree`. Р—РґРµСЃСЊ Р·Р°РґР°СЋС‚СЃСЏ РѕР±Рµ СЃРёСЃС‚РµРјС‹ Рё РёС… РЅР°С‡Р°Р»СЊРЅС‹Рµ
СЃРѕСЃС‚РѕСЏРЅРёСЏ (РІРєР»СЋС‡Р°СЏ Р±Р°СЂРёС†РµРЅС‚СЂРёС‡РµСЃРєСѓСЋ РїРѕРїСЂР°РІРєСѓ РїРѕРґРґРµСЂРµРІСЊРµРІ, РєР°Рє РІ `EphemerisBaker`).

## `methods/`

РџРѕ РѕРґРЅРѕРјСѓ С„Р°Р№Р»Сѓ РЅР° РјРµС‚РѕРґ/СЃРµРјРµР№СЃС‚РІРѕ:

| Р¤Р°Р№Р» | Р§С‚Рѕ РІРЅСѓС‚СЂРё |
|---|---|
| `Rk4.cs` | `StudyRk4Step` вЂ” RK4 |
| `Dopri5.cs` | `StudyDopri5Fixed` вЂ” DOPRI5, С„РёРєСЃРёСЂРѕРІР°РЅРЅС‹Р№ С€Р°Рі |
| `Pefrl.cs` | `StudyPefrl` вЂ” PEFRL |
| `Yoshida6.cs` | РІРµСЃР° РЃСЃРёРґС‹, `StudyY6` Рё `StudyY6C` (compensated) |
| `Dop853Tableau.cs` | С‚Р°Р±Р»РёС†Р° DOP853 (Hairer/Wanner) |
| `Dop853Ship.cs` | `StudyShipDop853`, `StudyShipDop853Local` |
| `FixedShip.cs` | `StudyShipFixed`, `StudyShipFixedLocal` (Y6/PEFRL РґР»СЏ РєРѕСЂР°Р±Р»СЏ) |
| `WisdomHolman.cs` | `StudyKeplerDrift`, `StudyWhStep*`, `StudyShipWh*` |
| `GamePath.cs` | `StudyShipGame`, `StudyCloneBakedSystem` вЂ” СЂРµР°Р»СЊРЅС‹Р№ РёРіСЂРѕРІРѕР№ РїСѓС‚СЊ |
| `StepDispatch.cs` | `StudyStep` вЂ” РґРёСЃРїРµС‚С‡РµСЂ РјРµС‚РѕРґРѕРІ РїРѕ РёРјРµРЅРё |

## `references/`

| Р¤Р°Р№Р» | Р§С‚Рѕ РІРЅСѓС‚СЂРё |
|---|---|
| `NBodyReference.cs` | `StudyAcc`, `StudyAccC` (compensated), `StudyAdd` |
| `RefRun.cs` | `StudyRefRun`, `StudyRel`, `StudyEnergy` |
| `DoubleDouble.cs` | `StudyDd`, `StudyDd3`, `StudyForceDd` вЂ” dd-Р°СЂРёС„РјРµС‚РёРєР° |
| `ShipReferences.cs` | `StudyShipRefA`, `StudyShipRefLocal`, `StudyShipY6CDd` |

## `scenarios/`

| Р¤Р°Р№Р» | Р§С‚Рѕ РІРЅСѓС‚СЂРё |
|---|---|
| `Planets.cs` | `StudySystemTable`, `StudyPlanetTable`, `StudyBake`, `StudyBakeStep` |
| `Ships.cs` | `StudyShipOrbit`, `StudyLocalAcceleration*`, `StudyShipTable`, `PrintShipRow` |
| `Events.cs` | `StudyLiveCost`, `StudyEvent`, `StudyShipFixedStep` вЂ” warp/СЃРѕР±С‹С‚РёРµ |
| `ReferenceCases.cs` | `StudyReferenceCase`, `StudyKeplerReference`, `StudyTimeReversal`, `StudyPlanetReference` |
| `KnownProblems.cs` | `Test505_KnownProblems` (Lagrange/figure-eight/Arenstorf, double+dd) и `Test506_KnownConvergence` (свип K×допуск) |

## `tests/`

| Р¤Р°Р№Р» | РўРµСЃС‚ |
|---|---|
| `Test500.cs` | `Test500_IntegratorStudyFast` вЂ” РїР»Р°РЅРµС‚С‹ 10 Р»РµС‚, 6 СЃС†РµРЅР°СЂРёРµРІ РєРѕСЂР°Р±Р»СЏ, Р±РµР№Рє, warp |
| `Test501.cs` | `Test501_IntegratorStudySlow` вЂ” 100 Р»РµС‚ |
| `Test503.cs` | `Test503_ReferenceConvergence` вЂ” РЅРµ РІ РёСЃС…РѕРґРЅРѕРј СЂРµРµСЃС‚СЂРµ, РґРѕСЃС‚СѓРїРµРЅ РєР°Рє `--reference` |

## `taylor/`

`TaylorReferenceTests.cs` вЂ” `Test504_TaylorReference` (Taylor vs Y6C/Y6/RK4/DOP853).

## `TaylorVsY6/`

| Р¤Р°Р№Р» | Р§С‚Рѕ РІРЅСѓС‚СЂРё |
|---|---|
| `Compare.cs` | CLI-СЂРµР¶РёРјС‹ `--real/--custom/--ships/--kepler/--y6c/--dd-*`; Taylor vs Y6 |
| `TaylorDd.cs` | dd-РґР¶РµС‚С‹: СЃС‚Р°СЂР°СЏ СЂРµР°Р»РёР·Р°С†РёСЏ, Р°СЂРµРЅР°/in-place (`DdView`/`DdArena`), self-check, СЃСЂР°РІРЅРµРЅРёРµ СЂРµР°Р»РёР·Р°С†РёР№ |

## РљР°Рє РґРѕР±Р°РІРёС‚СЊ РЅРѕРІС‹Р№ РјРµС‚РѕРґ/СЃС†РµРЅР°СЂРёР№

1. РњРµС‚РѕРґ вЂ” РЅРѕРІС‹Р№ `.cs` РІ `methods/`, Р»СЋР±РѕР№ РїСЂРёРІР°С‚РЅС‹Р№ `static`-РјРµС‚РѕРґ РІ
   `partial class P1bTests`. РЎР±РѕСЂРєР° РїРѕРґС…РІР°С‚С‹РІР°РµС‚ `Study/**/*.cs` Р°РІС‚РѕРјР°С‚РёС‡РµСЃРєРё.
2. Р­С‚Р°Р»РѕРЅ вЂ” РІ `references/`.
3. РЎС†РµРЅР°СЂРёР№ вЂ” РІ `scenarios/`; РІС‹Р·РІР°С‚СЊ РёР· `tests/Test500.cs` РёР»Рё РґРѕР±Р°РІРёС‚СЊ РЅРѕРІС‹Р№ С‚РµСЃС‚
   Рё СЃС‚СЂРѕРєСѓ РІ `Runner.cs`.

## Р”РІРёР¶РѕРє

`Assets/_Project/Scripts/` вЂ” 114 С„Р°Р№Р»РѕРІ РЅР°СЃС‚РѕСЏС‰РµРіРѕ РєРѕРґР° РїСЂРѕРµРєС‚Р° (РЅРµ РєРѕРїРёРё).
РЇРґСЂРѕ РёРЅС‚РµРіСЂР°С‚РѕСЂР°: `Core/Integration`, `Spacecraft/Dynamics`, `Simulation/Events`,
`Simulation/Warp`, `Ephemeris`, `Universe`, `Core/Orbits`, `Core/Math`. РћСЃС‚Р°Р»СЊРЅРѕРµ вЂ”
compile-closure (С‚РµСЂСЂРµР№РЅ, РґРµРєРѕСЂ, flight, solver). РњРµРЅСЏС‚СЊ РёС… РІ РїР°РєРµС‚Рµ РјРѕР¶РЅРѕ, РЅРѕ СЌС‚Рѕ
СѓР¶Рµ РЅРµ В«СЃС‚РµРЅРґВ», Р° СЂРµР°Р»СЊРЅС‹Р№ РґРІРёР¶РѕРє.

