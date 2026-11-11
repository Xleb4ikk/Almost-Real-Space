# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T200320Z_2a36227/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 20:03:20 |
| commit | `2a36227` (Q-FIX-13: K5 и DoD; guard — Q-FIX-12) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T200320Z_2a36227/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, из них L1 `INVALID` (F8) → 720 оценённых |
| ячейки | `PASS=459 FAIL=46 SKIP=110 N/A=105 ERROR=0` |
| строк `runs.csv` | 3529 (из них `order_obs` заполнен в 1726) |
| oracle | 16 `VALID`, 1 `INVALID` (L1, `FOUND.md` F8) |
| R1 | тестовый фикстур `StudyRealSolar7` (7 тел), эталон — два dd-Taylor прогона Test553, floor 5.882e-2 м |
| R2 | DE441 10 тел, 1PN, эталон — фикс. DOP853 5 уровней, floor 1.969 м |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 202 578 мс |

Это эталон после `Q-FIX-12/13` (G2 guard для бюджета/floor + K5 в документации).
Предыдущие baseline сохранены и для сравнения с текущим не годятся:
`20261111T171630Z_4df81a2` (до исправлений G2), `20261111T183727Z_c979ab0` (первая версия
G2), `20261111T192603Z_4270a7f` (ветка от мелкой точки, ещё без guard'а).

## G2 в этом эталоне

* **G2-FAIL (15 ячеек):**
  * `GJ8`: K2 (9.27), K3 (7.08), T2 (8.83), R4 (10.59), L2 (10.71) — эффект драйвера
    (пересборка истории GJ8 на checkpoint'ах, `FOUND.md` F6);
  * `RK4`: R2 A1/A2 (4.94) — доказанный наклон наблюдаемой ветки (`FOUND.md` F6).
* **K5 — PASS:** `GJ8 K5` даёт 9.70 при ожидаемых 10.00 (`|Δ| = 0.295 ≤ 0.5`); в K5
  FAIL-ячеек только `Leapfrog A2/A3` («level not reached»), к G2 это не относится.
* **`n/a` по бюджету/floor:**
  * `RK4 L2` — `n/a (budget before the asymptotic branch)`: более мелкая точка
    (`h=1.0629e-3`) не посчитана (RHS budget 2e6, TASK §9 G7 = SKIP);
  * `DOPRI5-fixed R2` (smoke) — `n/a (floor-limited window)`: подоконная точка даёт 4.22
    против 6.02 ветки;
  * R3/L2 у DOPRI5-fixed/PEFRL/Leapfrog — `n/a (budget ...)`, их ячейки и так SKIP.

## Файлы копии

| файл | содержание |
|---|---|
| `environment.txt` | дата, commit, .NET, CPU, профиль, SHA256 TASK.md, список методов |
| `oracle_audit.md` | G8 по каждой задаче |
| `runs.csv` | 3529 строк: (метод, задача, control) |
| `matrix.csv` | 720 ячеек: (метод, задача, уровень) |
| `METHOD_QUALIFICATION.md` | сертификаты методов, матрица `метод × задача`, пометки снятых гейтов |
| `RECOMMENDATIONS.md` | рекомендации по тегам и уровням |
| `compare_vs_baseline.md` | контрольный `--compare` следующего полного прогона: «No changes» |
| `run.log` | полный лог прогона (включая строки `order <method> <problem>: ...`) |

`run.log` добавлен в git принудительно (`git add -f`): корневой `.gitignore` игнорирует
`*.log`.

## Как сравнивать

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification --compare Tools\P1bTests\Study\qualification\baseline\20261111T200320Z_2a36227
```

или fallback через `P1B564_COMPARE`. Отчёт пишется в `compare.md` нового прогона:
изменения статусов, ошибок > 10 % и цен > 10 %. Контрольный прогон
`20261111T200647Z_2a36227` дал 0 изменений.

## Что НЕ входит в эталон

* `L1` — oracle `INVALID` (`FOUND.md` F8): dd-golden и локальная модель расходятся на 8.522 м,
  поэтому методы по L1 не оцениваются. Статус вычисляется (`|golden − local| <= 0.1·A1`).
* R1 считается на 7-тельном тестовом фикстуре (по указанию владельца), а не на DE441 10 тел;
  DE441 10 тел остаётся в R2 (1PN). Отличие записано в `INVENTORY.md`.
* G3 снят по model drift **только на R4 A1/A2** (возмущённая задача: энергия и момент
  относительно Луны физически не сохраняются). На R3 A1 гейт применяется и проходит.
  Это видно в `matrix.csv` и `METHOD_QUALIFICATION.md` как `G3 skipped: model drift`.
* G2 — проверка порядка, а не бюджета/floor: см. правила в `PROGRESS.md` (FIX-11).
