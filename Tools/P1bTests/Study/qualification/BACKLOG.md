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
| B8 | Прочие гелиоцентрические сценарии Test559 (0.3/1/5/30 AU × e=0/0.5/0.9/0.99, кроме F02) | A5.4: один представитель класса уже в F02; остальные — отдельная задача владельца | `Study/qualification/` |
| B9 | Прочие пролёты Луны Test559 (vinf 50…10000 × alt 1…1000, кроме R4) | A5.2/A5.3: дубли класса R4 | `Study/qualification/` |
| B10 | Прочие орбиты/прыжки Луны Test559 (alt 1/10/100/1000, 10/100 витков; hop 50…1600 м/с) | A5.4: один представитель каждого класса уже в F04/F05 | `Study/qualification/` |
| B11 | Пролёты планет Test559 (Earth flyby 1.1/3/10 R × vinf) | A5.3: тот же класс и сила, что S15 (Earth + solar tide) | `Study/qualification/` |
| B12 | `Far flight 30 AU e 0.5 (normalized)` | A5.3: та же физика, что F01, в нормированных единицах | `Study/qualification/` |
| B13 | Отдельный проход уточнения порядка G2 для S/P/F (досчитать точки ниже последнего control'а) | Предсимптотическая ветка даёт ложные G2-FAIL RK4/DOPRI5 на S (`FOUND.md` F11); менять процедуру G2 без владельца нельзя | `Study/qualification/` |
| B14 | Расширение WH-BM14A на GTO/Молнию: perigee-guard (e≈0.745), J2, события, 2–7 суток, эталон лучше S05 floor | C6-класс GTO/Молния-only даёт 1.45× < 1.5; S05 1.12× floor; GEO нельзя захватывать max-altitude-условием | `Study/qualification/` + `IntegratorPolicy` |
