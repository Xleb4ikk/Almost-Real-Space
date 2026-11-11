# BASELINE: Test564_MethodQualification

Указатель на прогон-эталон (TASK.md §10, §12 M7). Копия результатов лежит рядом:
`baseline/20261111T164711Z_df7b7a0/`.

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-11 16:47:11 |
| commit | `df7b7a0aa39b3e0b581233106701a826197c99fb` (Q-M6) |
| ветка | `qualification/test564-method-qualification` |
| профиль | `full` |
| папка прогона | `results/20261111T164711Z_df7b7a0/` |
| задач / методов / уровней | 17 / 15 / 3 = 765 ячеек, из них L1 `INVALID` (F8) → 720 оценённых |
| ячейки | `PASS=448 FAIL=51 SKIP=116 N/A=105 ERROR=0` |
| oracle | 16 `VALID`, 1 `INVALID` (L1, документированная причина в `FOUND.md` F8) |
| .NET | 8.0.24 (net8.0), x64 |
| CPU | AMD Ryzen 9 8940HX, 32 логических CPU |
| время прогона | 158 701 мс |

## Файлы копии

| файл | содержание |
|---|---|
| `environment.txt` | дата, commit, .NET, CPU, профиль, SHA256 TASK.md, список методов |
| `oracle_audit.md` | G8 по каждой задаче |
| `runs.csv` | 3442 строки: (метод, задача, control) |
| `matrix.csv` | 720 ячеек: (метод, задача, уровень) |
| `METHOD_QUALIFICATION.md` | сертификаты методов и матрица `метод × задача` |
| `RECOMMENDATIONS.md` | рекомендации по тегам и уровням |
| `compare_vs_baseline.md` | контрольный `--compare` второго полного прогона против этого эталона: «No changes» |

## Как сравнивать

```
$env:P1B564_PROFILE = "full"
$env:P1B564_COMPARE = "Tools\P1bTests\Study\qualification\baseline\20261111T164711Z_df7b7a0"
dotnet run --project Tools/P1bTests -c Release -- --exact Test564_MethodQualification
```

Отчёт сравнения пишется в `compare.md` нового прогона: изменения статусов, ошибок > 10 %
и цен > 10 %. Контрольный прогон `20261111T165239Z_df7b7a0` дал 0 изменений.

## Что НЕ входит в эталон

* `L1` — oracle `INVALID` (`FOUND.md` F8): dd-golden и локальная модель расходятся на 8.522 м,
  поэтому методы по L1 не оцениваются.
* `run.log` — полный лог прогона (≈30 КБ текста) остаётся только в локальной папке прогона,
  в копию не включён.
