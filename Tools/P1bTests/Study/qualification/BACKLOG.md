# BACKLOG: идеи вне задания Test564

Правило TASK.md (раздел 14, п. 3): идея пишется сюда и **не реализуется** в рамках
Test564. Реализация — только после решения владельца.

| # | Идея | Зачем | Где могла бы жить |
|---|---|---|---|
| B1 | Вынести аналитические builder'ы из `StudySelfTest` в общий internal-класс | устранить дублирование формул oracle (см. `FOUND.md` F1) | `Study/framework/` |
| B2 | Общее ядро certified reference (5 уровней DOP853 + Richardson) с колбэком силы | применимость к любому `StudyProblem`, а не только к кораблю (F2) | `Study/integrators/` |
| B3 | Удалить/зарегистрировать мёртвый `Test560` | чистота реестра (F3) | `Study/tests/` |
| B4 | Флаг `--list-integrators` в `Program.cs` | сейчас список методов виден только в отчёте прогона | `Program.cs` |
| B5 | Отдельный `.gitignore`-шаблон для `qualification/results/` | прогоны не должны случайно попадать в индекс, но baseline-папка — должна | корневой `.gitignore` |
| B6 | DOP853 с фиксированным шагом как отдельная запись реестра | TASK.md (раздел 9) называет «certified DOP853 фикс. шаг», а в реестре только adaptive | `StudyRegistry.cs` |
| B7 | Экспорт `runs.csv`/`matrix.csv` в Parquet/JSON | удобнее для внешнего анализа | `Study/qualification/` |
