# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T225110Z_22f94b7/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 22:51:10 |
| commit | `22f94b7` (Q-FIX-17: `COST_TABLE.md` + решение по эталону R3) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T225110Z_22f94b7/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, все задачи `VALID` |
| ячейки | `PASS=480 FAIL=68 SKIP=112 N/A=105 ERROR=0` |
| строк `runs.csv` | 3888 (из них `order_obs` заполнен в 1850) |
| oracle | 17 `VALID` |
| L1 | `VALID`: эталон — локальный сертифицированный DOP853, floor 1.824e-04 м; golden — кросс-проверка (F8) |
| R1 | тестовый фикстур `StudyRealSolar7` (7 тел), эталон — два dd-Taylor прогона Test553, floor 5.882e-2 м |
| R2 | DE441 10 тел, 1PN, эталон — фикс. DOP853 5 уровней, floor 1.969 м; A3 = SKIP по floor |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 702 199 мс (≈11.7 мин) |
| контрольный прогон | против `20261111T203415Z_becd26a` — `No changes` (0 статусов / 0 ошибок / 0 цен) |

Этот эталон отличается от `20261111T203415Z_becd26a` только добавленным
`COST_TABLE.md`: матрица, `runs.csv`, ошибки и цены совпадают (`--compare` — `No changes`).
Предыдущие baseline сохранены: `20261111T171630Z_4df81a2`, `20261111T183727Z_c979ab0`,
`20261111T192603Z_4270a7f`, `20261111T200320Z_2a36227`, `20261111T203415Z_becd26a`.

## COST_TABLE.md

Генерируется из `matrix.csv` и `runs.csv` (никаких ручных чисел):

* **255 строк** «метод × задача»: цена достижения A1/A2/A3 (`price_at_tier`), ошибка
  `err_max` на самом мелком выполненном (`ok`) control и сам control;
* N/A/SKIP/FAIL заменены одним словом: `n/a`, `floor`, `budget`, `order`, `level`,
  `finite`, `invariant`, `rev`, `error`;
* отдельная таблица **«Invariants by problem»** — `max |ΔE|/E` и `max |ΔL|/L` по каждому
  методу и задаче (не усреднение по методу);
* всё считается из CSV при каждом прогоне, файл пишется рядом с
  `METHOD_QUALIFICATION.md` и `RECOMMENDATIONS.md`.

## G2 в этом эталоне

* **G2-FAIL (16 ячеек):**
  * `GJ8`: K2 (9.27), K3 (7.08), T2 (8.83), R4 (10.59), L2 (10.71) — эффект драйвера
    (`FOUND.md` F6);
  * `RK4`: R2 A1/A2 (4.94) — доказанный наклон наблюдаемой ветки (`FOUND.md` F6);
  * `RK4`: R3 A1 (4.96) — новый факт после подъёма бюджета; диагностический resume
    с шагом 1.41 с дал 4.99 (`FOUND.md` F6).
* **K5 — PASS:** `GJ8 K5` даёт 9.70 при ожидаемых 10.00 (`|Δ| = 0.295 ≤ 0.5`).
* **`n/a` по бюджету/floor:** `RK4 L2` — `n/a (few points)`; `Leapfrog L2` —
  `n/a (budget ...)`; `DOPRI5-fixed R2` — `n/a (floor-limited window)` в smoke.

## L1 (после решения владельца)

* Oracle — локальный сертифицированный DOP853 (`LocalReferenceOnly = true`), floor
  **1.824e-04 м**. Golden — кросс-проверка; расхождение 8.522 м / 9.657e-3 м/с записано
  в `oracle_audit.md` как frame difference (`FOUND.md` F8).
* Ячейки L1: **17 PASS, 2 FAIL, 26 SKIP**. A1: 14 PASS + 1 SKIP; A2: 3 PASS +
  2 FAIL (`RKN12(10)`, `IAS15` — level not reached) + 10 SKIP; A3: 15 SKIP по floor.

## R3 A2

R3 A2 остаётся `SKIP (oracle floor 2.544e-3 > 0.1×A2 = 6.771e-4)`. Попытки более
точного эталона (6/7-уровневый DOP853, IAS15 `1e-14/1e-15`, TaylorShip, сетка
`stepsPerTau`) не дали устойчивого floor ниже порога; детали и решение — `PROGRESS.md`
(раздел «R3 A2») и `FOUND.md` F9. Пороги/окно/TASK.md не менялись.

## Гейты в этом эталоне

* **G2-FAIL:** 16 ячеек (список выше); `K5` PASS.
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
| `COST_TABLE.md` | 255 строк цен/ошибок + 255 строк инвариантов по задачам |
| `compare_vs_previous_baseline.md` | `--compare` против `20261111T203415Z_becd26a`: `No changes` |
| `run.log` | полный лог прогона (включая строки `order <method> <problem>: ...`) |

`run.log` добавлен в git принудительно (`git add -f`): корневой `.gitignore` игнорирует
`*.log`.

## Как сравнивать

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification --compare Tools\P1bTests\Study\qualification\baseline\20261111T225110Z_22f94b7
```

или fallback через `P1B564_COMPARE`. Отчёт пишется в `compare.md` нового прогона:
изменения статусов, ошибок > 10 % и цен > 10 %.

## Что НЕ входит в эталон

* R1 считается на 7-тельном тестовом фикстуре (по указанию владельца), а не на DE441
  10 тел; DE441 10 тел остаётся в R2 (1PN). Отличие записано в `INVENTORY.md`.
* R2 сертифицирован до A2; A3 = SKIP по floor 1.969 м (решение владельца: шкалу не
  подгонять).
* R3 A2 = SKIP по floor 2.544e-3 м (`FOUND.md` F9).
* §15 трактуется как «без Unity»: `StarSystem` — обычный headless-класс, сцены и
  `VesselStep` нет (формулировка поправлена владельцем).
* G2 — проверка порядка, а не бюджета/floor: см. правила в `PROGRESS.md` (FIX-11/12).
