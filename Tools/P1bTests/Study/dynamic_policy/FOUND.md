# FOUND: найденные баги и странности (Приложение D)

Правило TASK_D.md §4/§7: найденное вне списка исправлений D3 записывается сюда, **не чинится**
в рамках Приложения D. Формат: путь, симптом, как воспроизвести, следствие.

| # | дата | путь | симптом | воспроизведение | следствие |
|---|---|---|---|---|---|
| F1 | 2026-11-14 | `Assets/_Project/Scripts/Spacecraft/IntegratorPolicy.cs:88-96,143-153` | комментарий политики ссылается на «the 30-50 s live chunk and the 600 s stress chunk» как на измеренные режимы Sun-coast, но теста/CSV на 600 с в репозитории нет: есть только 30 с (и 1/60 с) в `LiveChunkPolicyTests.cs:16-38` и `IntegratorPolicyRuntime.cs:24-41` | `grep -rn "600" tests/GalilegoSimTests/LiveChunkPolicyTests.cs Tools/P1bTests/Tests/IntegratorPolicyRuntime.cs` — ни одной Sun-coast проверки с чанком 600 с | D1.c: Sun-coast = `NO-EVIDENCE`; опереться на 600 с в D4 R3 нельзя, нужен новый прогон/тест. Не чиним: ветка выключена, §4 запрещает менять политику вне D3 |
| F2 | 2026-11-14 | `Tools/P1bTests/Study/qualification/results/20261114T151403Z_e860827/game_control.csv` | числа game-control для адаптивных study-методов сняты на `dirty`-дереве до протокола Q-C3 и не совпадают с чистым прогоном: IAS15 S04 `3.2975e-6` → `4.4209e-6` (PASS→FAIL), RKN12 S01 `2.371e-4` → `7.704e-5`, RKN12 wall S02 `0.0827` → `0.0082` мс | сравнить `game_control.csv`/`chunks.csv` прогонов `20261114T151403Z_e860827` и `20261114T193520Z_ff36340` (D1, скрипт `%TEMP%\d1_analysis.py`) | seed-числа `DECISION_TABLE.md` §5 для RKN12/IAS15/адаптивных устарели; в таблице §4 журнал, решения строк не меняются. Не чиним: старый прогон — исторический артефакт, переписывать results/ запрещено |
