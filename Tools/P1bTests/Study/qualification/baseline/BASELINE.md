# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T203415Z_becd26a/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 20:34:15 |
| commit | `becd26a` (Q-FIX-15: бюджет full, локальный эталон L1, WH-примечание); документация — `98ffc1c` (Q-FIX-15b) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T203415Z_becd26a/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, все задачи `VALID` |
| ячейки | `PASS=480 FAIL=68 SKIP=112 N/A=105 ERROR=0` |
| строк `runs.csv` | 3888 (из них `order_obs` заполнен в 1850) |
| oracle | 17 `VALID` |
| L1 | `VALID`: эталон — локальный сертифицированный DOP853, floor 1.824e-04 м; golden — кросс-проверка (F8) |
| R1 | тестовый фикстур `StudyRealSolar7` (7 тел), эталон — два dd-Taylor прогона Test553, floor 5.882e-2 м |
| R2 | DE441 10 тел, 1PN, эталон — фикс. DOP853 5 уровней, floor 1.969 м; A3 = SKIP по floor |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 709 299 мс (≈11.8 мин) |
| контрольный прогон | `results/20261111T210516Z_98ffc1c/` — `No changes` |

Это эталон после `Q-FIX-15`: в `full` поднят бюджет RHS до `10_000_000`, L1 переведён
на локальный сертифицированный эталон, в отчёт добавлено примечание по WH/Encke.
Предыдущие baseline сохранены: `20261111T171630Z_4df81a2` (до исправлений G2),
`20261111T183727Z_c979ab0` (первая версия G2), `20261111T192603Z_4270a7f` (ветка
мелкий→крупный, без guard'а), `20261111T200320Z_2a36227` (guard Q-FIX-12, без решений
владельца).

## G2 в этом эталоне

* **G2-FAIL (16 ячеек):**
  * `GJ8`: K2 (9.27), K3 (7.08), T2 (8.83), R4 (10.59), L2 (10.71) — эффект драйвера
    (пересборка истории GJ8 на checkpoint'ах, `FOUND.md` F6);
  * `RK4`: R2 A1/A2 (4.94) — доказанный наклон наблюдаемой ветки (`FOUND.md` F6);
  * `RK4`: R3 A1 (4.96) — новый факт после подъёма бюджета; диагностический resume
    с шагом 1.41 с дал 4.99, то есть это не артефакт окна/floor (`FOUND.md` F6).
* **K5 — PASS:** `GJ8 K5` даёт 9.70 при ожидаемых 10.00 (`|Δ| = 0.295 ≤ 0.5`); в K5
  FAIL-ячеек только `Leapfrog A2/A3` («level not reached»), к G2 это не относится.
* **`n/a` по бюджету/floor:**
  * `RK4 L2` — `n/a (few points in the asymptotic branch)`: при бюджете 1e7 точка
    `h=2.099e-4` цензурирована, а две самые мелкие `ok`-точки (3.149e-4 и 4.724e-4)
    дают почти одинаковую ошибку, поэтому асимптотическая ветка короче 4 точек;
  * `Leapfrog L2` — `n/a (budget before the asymptotic branch)`;
  * `DOPRI5-fixed R2` — `n/a (floor-limited window)` в smoke; в full ветка даёт 4.88
    и проходит.

## L1 (после решения владельца)

* Oracle — локальный сертифицированный DOP853 (`LocalReferenceOnly = true`), floor
  **1.824e-04 м**. Golden `ship_1000_orbits_dd.json` остаётся кросс-проверкой: его
  расхождение 8.522 м / 9.657e-3 м/с записано в `oracle_audit.md` как frame difference
  (`FOUND.md` F8), в floor не входит.
* Ячейки L1: **17 PASS, 2 FAIL, 26 SKIP**.
  * A1: 14 PASS, 1 SKIP (`Leapfrog` — бюджет);
  * A2: 3 PASS, 2 FAIL (`RKN12(10)`, `IAS15` — «level not reached at the finest control»),
    10 SKIP по бюджету;
  * A3: 15 SKIP — `0.1×A3 = 6.771e-7 < 1.824e-4`.
* G3 на L1 снят по model drift только в трёх PASS-ячейках A2 (`DOP853-adaptive`, `WH-Y6`,
  `SRKN14A`): энергия/момент относительно Земли не сохраняются при возмущениях Луны/Солнца.

## Гейты в этом эталоне

* **G2-FAIL:** 16 ячеек (список выше). `K5` PASS.
* **G3:** реальных FAIL = 0; `G3 skipped: model drift` — R4 A1/A2 (27 ячеек) и L1 A2
  (3 ячейки).
* **G5:** 7 ячеек PASS — `T3 A1`, `R1 A1/A2/A3`, `R3 A1`, `L2 A1/A2`.
* **Флаги зависимости силы:** `K8` velocity=True, `R2` velocity=True,
  `R1/R3/R4` velocity=False; `VIOLATION` нет.
* **ERROR:** 0.

## Файлы копии

| файл | содержание |
|---|---|
| `environment.txt` | дата, commit, .NET, CPU, профиль, SHA256 TASK.md, список методов |
| `oracle_audit.md` | G8 по каждой задаче (17 VALID) |
| `runs.csv` | 3888 строк: (метод, задача, control) |
| `matrix.csv` | 765 ячеек: (метод, задача, уровень) |
| `METHOD_QUALIFICATION.md` | сертификаты методов, матрица `метод × задача`, пометки снятых гейтов |
| `RECOMMENDATIONS.md` | рекомендации по тегам и уровням, включая примечание `central-dominant` |
| `compare_vs_baseline.md` | контрольный `--compare` следующего полного прогона: «No changes» |
| `run.log` | полный лог прогона (включая строки `order <method> <problem>: ...`) |

`run.log` добавлен в git принудительно (`git add -f`): корневой `.gitignore` игнорирует
`*.log`.

## Как сравнивать

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification --compare Tools\P1bTests\Study\qualification\baseline\20261111T203415Z_becd26a
```

или fallback через `P1B564_COMPARE`. Отчёт пишется в `compare.md` нового прогона:
изменения статусов, ошибок > 10 % и цен > 10 %. Контрольный прогон
`20261111T210516Z_98ffc1c` дал 0 изменений.

## Что НЕ входит в эталон

* R1 считается на 7-тельном тестовом фикстуре (по указанию владельца), а не на DE441
  10 тел; DE441 10 тел остаётся в R2 (1PN). Отличие записано в `INVENTORY.md`.
* R2 сертифицирован до A2; A3 = SKIP по floor 1.969 м (решение владельца: шкалу не
  подгонять).
* §15 трактуется как «без Unity»: `StarSystem` — обычный headless-класс, сцены и
  `VesselStep` нет (формулировка поправлена владельцем).
* G2 — проверка порядка, а не бюджета/floor: см. правила в `PROGRESS.md` (FIX-11/12).
