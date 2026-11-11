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
