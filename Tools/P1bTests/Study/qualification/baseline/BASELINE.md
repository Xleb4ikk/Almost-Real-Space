# BASELINE: Test564_MethodQualification (TASK.md v4 / Приложение C, C8)

Актуальный эталон сравнения — **`baseline/20261114T173334Z_142824a/`**. Это новый
`full` baseline после порта WH-BM14A; он **не меняет прежние задачи**: `--compare`
против `baseline/20261114T093336Z_f899097/` даёт 0 изменений статусов, ошибок и цен.
Предыдущий baseline сохранён в `baseline/20261114T093336Z_f899097/`, его описание —
`BASELINE_20261114T093336Z.md` (там же детали слоя B5).

| поле | значение |
|---|---|
| дата (UTC) | 2026-11-14 17:33:34 |
| run | `20261114T173334Z_142824a` |
| commit | `142824a` (`Q-C8`: решение владельца A, старт C8) |
| ветка | `integrator-port/wh-bm14a` |
| версия TASK.md | 4, SHA256 `7779706cf649a4c7dfe1a12b34fcd9aee97084fe600c3c0ea44440d487db1b16` |
| профиль | `full` |
| `dirty` | **clean** |
| задач / методов | 47 / 15 |
| ячеек `matrix.csv` | 2925 (`PASS=1800 FAIL=194 SKIP=376 N/A=555 ERROR=0`) |
| строк `runs.csv` | 9452 |
| oracle | 47 `VALID`, 0 `INVALID` |
| `--compare` с `20261114T093336Z_f899097` | 0 статусов, 0 новых ячеек, 0 ошибок, 0 изменений цены > 10 % |
| время прогона | 3 333 847 мс (~55.6 мин) |

## Что это значит

Новый baseline — это тот же `full`-каталог, что и `20261114T093336Z_f899097`, но
снятый после `Q-C8` и на TASK.md v4. Поскольку `--compare` дал «No changes: statuses,
errors and prices are identical within 10 %», прежние задачи K/T/R/L/S/P/F/H и их
эталоны не сдвинулись. Менять числа в старом baseline не нужно: новый является
актуальной точкой регрессии для C8/C9.

## Команда воспроизведения

```powershell
$env:P1B564_PROFILE='full'
$env:P1B564_COMPARE='Tools\P1bTests\Study\qualification\baseline\20261114T093336Z_f899097'
Tools\P1bTests\bin\Release\net8.0\P1bTests.exe --exact Test564_MethodQualification
```

Для нового прогона без сравнения:
`$env:P1B564_COMPARE` не задавать; профиль — `full`.
