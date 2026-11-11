# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T171630Z_4df81a2/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 17:16:30 |
| commit | `4df81a2f3633794149ea3cb33f574ff758c2db38` (Q-M8: R1 на тестовом фикстуре) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T171630Z_4df81a2/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, из них L1 `INVALID` (F8) → 720 оценённых |
| ячейки | `PASS=453 FAIL=52 SKIP=110 N/A=105 ERROR=0` |
| oracle | 16 `VALID`, 1 `INVALID` (L1, документированная причина в `FOUND.md` F8) |
| R1 | реальная солнечная система из тестового фикстура `StudyRealSolar7` (7 тел), эталон — два dd-Taylor прогона Test553, floor 5.882e-2 м |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 196 015 мс |

## Файлы копии

| файл | содержание |
|---|---|
| `environment.txt` | дата, commit, .NET, CPU, профиль, SHA256 TASK.md, список методов |
| `oracle_audit.md` | G8 по каждой задаче |
| `runs.csv` | 3442 строки: (метод, задача, control) |
| `matrix.csv` | 720 ячеек: (метод, задача, уровень) |
| `METHOD_QUALIFICATION.md` | сертификаты методов и матрица `метод × задача` |
| `RECOMMENDATIONS.md` | рекомендации по тегам и уровням |
| `compare_vs_baseline.md` | контрольный `--compare` следующего полного прогона: «No changes» |
| `run.log` | полный лог прогона |

## Как сравнивать

```
$env:P1B564_PROFILE = "full"
$env:P1B564_COMPARE = "Tools\P1bTests\Study\qualification\baseline\20261111T171630Z_4df81a2"
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification
```

Отчёт сравнения пишется в `compare.md` нового прогона: изменения статусов, ошибок > 10 %
и цен > 10 %. Контрольный прогон дал 0 изменений.

## Что НЕ входит в эталон

* `L1` — oracle `INVALID` (`FOUND.md` F8): dd-golden и локальная модель расходятся на 8.522 м,
  поэтому методы по L1 не оцениваются.
* R1 считается на 7-тельном тестовом фикстуре (по указанию владельца), а не на DE441 10 тел;
  DE441 10 тел остаётся в R2 (1PN). Отличие записано в `INVENTORY.md`.
