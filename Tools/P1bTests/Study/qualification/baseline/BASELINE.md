# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T183727Z_c979ab0/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 18:37:27 |
| commit | `c979ab0` (Q-FIX-8: документы и отчёты) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T183727Z_c979ab0/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, из них L1 `INVALID` (F8) → 720 оценённых |
| ячейки | `PASS=446 FAIL=59 SKIP=110 N/A=105 ERROR=0` |
| строк `runs.csv` | 3529 |
| oracle | 16 `VALID`, 1 `INVALID` (L1, `FOUND.md` F8) |
| R1 | тестовый фикстур `StudyRealSolar7` (7 тел), эталон — два dd-Taylor прогона Test553, floor 5.882e-2 м |
| R2 | DE441 10 тел, 1PN, эталон — фикс. DOP853 5 уровней, floor 1.969 м |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 205 693 мс |

Это первый baseline после исправлений `Q-FIX-1..Q-FIX-8` (G2, G3, G5, L1, wall time,
resume, `--compare`). Предыдущий baseline `20261111T171630Z_4df81a2` **сохранён** — он
относится к коду до исправлений и для сравнения с текущим не годится.

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

## Как сравнивать

```
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification --compare Tools\P1bTests\Study\qualification\baseline\20261111T183727Z_c979ab0
```

или fallback через `P1B564_COMPARE`. Отчёт пишется в `compare.md` нового прогона:
изменения статусов, ошибок > 10 % и цен > 10 %. Контрольный прогон
`20261111T184059Z_c979ab0` дал 0 изменений.

## Что НЕ входит в эталон

* `L1` — oracle `INVALID` (`FOUND.md` F8): dd-golden и локальная модель расходятся на 8.522 м,
  поэтому методы по L1 не оцениваются. Статус вычисляется (`|golden − local| <= 0.1·A1`).
* R1 считается на 7-тельном тестовом фикстуре (по указанию владельца), а не на DE441 10 тел;
  DE441 10 тел остаётся в R2 (1PN). Отличие записано в `INVENTORY.md`.
* G3 на R3/R4 снят по model drift (возмущённые задачи); это видно в `matrix.csv` и
  `METHOD_QUALIFICATION.md` как `G3 skipped: model drift`.
