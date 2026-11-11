# FOUND: найденные баги и странности (не чиним в рамках Test564)

Правило TASK.md (раздел 2): найденное записывается сюда, код не правится.
Правка — отдельная задача владельца.

## F1. Аналитические oracle самотеста недоступны для переиспользования

* Путь: `Tools/P1bTests/Study/framework/StudySelfTest.cs:81,119,124,1147,1186`.
* Симптом: `Analytic`, `AnalyticHyperbola`, `MakeKeplerProblemEcc`, `MakeDragKeplerProblem`,
  `MakeHarmonicProblem` объявлены `private` внутри класса `StudySelfTest`, а не в
  `partial class P1bTests`. Стенд в `Study/qualification/` (тот же partial-класс) не может
  их вызвать, хотя это ровно те задачи, что нужны каталогу K1–K8.
* Следствие: Test564 вынужден реализовать свои builder'ы K-тира. Дублируется **формула
  oracle**, а не интегратор и не sweep.
* Воспроизвести: попытка `StudySelfTest.MakeKeplerProblemEcc(...)` из другого файла
  partial-класса `P1bTests` — ошибка доступа `CS0122`.
* Предложение (не реализовывать здесь): вынести аналитические builder'ы в
  `internal static class StudyAnalyticProblems` в `Study/framework/`.

## F2. «Certified reference» жёстко привязан к корабельному кейсу

* Путь: `Tools/P1bTests/Study/integrators/CertifiedReference.cs:421`
  (`StudyShipCertifiedReference(StudyShipMatrixCase, ...)`).
* Симптом: TASK.md (раздел 4) указывает этот файл как общий «certified reference
  (DOP853 фикс. шаг, 5 уровней, Richardson/floor)». Фактически функция принимает
  `StudyShipMatrixCase`, строит локальный/запечённый кадр, опирается на
  `StudyShipMatrixRefs` и не применима к произвольному `StudyProblem` (N-body, Kepler).
* Следствие: для T1/T3/R1/R2 используется другой эталон (dd-Taylor / `StudyRefRun` /
  `JplDop853`), а не этот код. Это записано в `INVENTORY.md`.
* Предложение: выделить из функции чистое ядро «5 уровней фикс. DOP853 + Richardson»
  с колбэком силы.

## F3. `Test560` — мёртвый код (не зарегистрирован)

* Путь: `Tools/P1bTests/Study/tests/Test560.cs:14` (`Test560_IntegratorDecisionMatrix`).
* Симптом: метод нигде не вызывается и отсутствует в `TestRegistry.cs`; его заменил
  `Test560b`. Компилируется, но не запускается.
* На результат Test564 не влияет; трогать запрещено (раздел 2 TASK.md).

## F4. В Horizons-файле нет тела `Sun`

* Путь: `Tools/P1bTests/Study/jpl/horizons_real_2026_10y.json`.
* Симптом: `bodies` содержит 483 записи (планеты, Луна, спутники, астероиды, KBO), но не
  Солнце; состояния гелиоцентрические. Для R1/R2 (10 тел) Солнце восстанавливается
  барицентрической поправкой в `JplNBody` (`jpl/JplRealism.cs:37-60`).
* Следствие: при построении R1/R2 нельзя просто взять `Sun` из файла; проверить баланс
  импульса/барицентра на первом прогоне M5.

## F5. `StudySupport.Conservative` смешивает две разные причины отказа

* Путь: `Study/framework/StudyRegistry.cs:105-109`.
* Симптом: одно сообщение `force depends on velocity or mass (drag/thrust): splitting order
  is lost` для силы, зависящей от скорости, и для силы, зависящей от массы (переменная
  масса). Для K8/R2 это не мешает (обе причины дают N/A), но в отчёте причина неточная.
* Не чиним; при необходимости владелец разделит сообщения.

## F6. Наблюдаемый порядок GJ8 на задачах с несколькими checkpoint'ами занижен стартом

* Путь: `Study/framework/StudyAdvancedSteppers.cs` (GaussJacksonStepper), `StudySelfTest`
  («GJ8 survives a change of h at checkpoint boundaries»).
* Симптом: `GJ8` объявляет `ExpectedOrder = 10`, но на K3 (Kepler e=0.5) медианный наклон
  в окне G2 равен 7.08, на K2 — 9.27, на K4 — 8.14. Причина видна по ряду `runs.csv`:
  драйвер выравнивает шаг внутри каждого checkpoint-интервала (`h_eff = span/n`), поэтому
  на каждой из 4 границ история мультишага пересобирается стартовым IAS15, и его ошибка
  (~1e-12) становится полом, ниже которого порядок не наблюдается. На чистой кеплеровской
  задаче с одним checkpoint'ом самотест видит 10.12.
* Следствие: по правилу G2 (`|p_obs − p_exp| ≤ 0.5`) GJ8 получает FAIL там, где
  асимптотический префикс достаточно длинный; там, где точек <4, — `order: n/a` (не FAIL).
  Это честный результат для текущего драйвера, а не ошибка стенда.
* Не чиним (запрещено менять численную часть методов и драйвер).

## F7. `censored`-строка теряет фактическую работу

* Путь: `Study/framework/StudySweep.cs:1022-1027`.
* Симптом: при превышении `MaxCostRhs` строка получает `status=censored`, `Calls=MaxCostRhs`,
  но `Steps=0` и `Err=NaN`. Фактически выполненная работа (сколько шагов успело пройти)
  не сохраняется, поэтому по `runs.csv` нельзя отличить «упёрся в бюджет на 1% пути» от
  «упёрся на 99%».
* На выводы Test564 не влияет (SKIP по бюджету), но диагностика беднее.
* Не чиним: это файл фреймворка вне `qualification/`.

## F8. L1 (1000 витков LEO): golden и локальная модель расходятся на 8.5 м

* Путь: `Study/integrators/golden/ship_1000_orbits_dd.json` (эталон),
  `Study/integrators/SpacecraftOrbitMatrix.cs:4291-4298` (`UseTaylorDdReference = true`),
  локальная сила — `StudyShipMatrixLocalAcceleration`.
* Симптом: сертифицированный локальный эталон DOP853 и dd-golden (world-frame,
  `force=per-body-indirect v2`) расходятся на **8.522 м** и **9.657e-3 м/с** за 1000 витков
  (~64 сут). Это ровно значение из `StudyShipMatrixBaselines` для этого кейса. При этом
  WH-Y6 в стенде упирается в плато ~8.54 м при любом шаге (h от 1232 до 21 с), то есть
  расхождение не интеграторное, а модельное/кадровое.
* Следствие: при `ScaleLength = rp = 6.771e6 м` уровень A1 = 6.77 м ниже расхождения,
  поэтому L1 помечен `INVALID` и методы по нему не оцениваются (TASK.md раздел 9, G8).
  Результат записан в `oracle_audit.md`.
* Не чиним: это разные постановки силы/кадра в существующем коде (раздел 2 TASK.md).
  Вопрос владельцу — в `PROGRESS.md`.
