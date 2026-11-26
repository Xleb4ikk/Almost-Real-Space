using System;
using System.Collections.Generic;
using System.Diagnostics;
using Galilego.Core;
using Galilego.Planning;

namespace Galilego.Tablet
{
    public enum PlannerMode { Transfer = 0, Flyby = 1 }

    public sealed class PlannerSettings
    {
        public PlannerMode Mode = PlannerMode.Transfer;
        public int To = -1, Via = -1;                        // индексы тел; старт — тело, в сфере которого корабль
        public bool UseCurrentOrbit = true;
        public double ParkAltitudeKm = 300d;
        public CaptureKind Capture = CaptureKind.Circular;
        public double ArrivalAltitudeKm = 300d, CaptureEccentricity = 0.5d;
        public double SearchSpanDays, MaxFlightDays;         // 0 — автоматически
    }

    /// <summary>
    /// Состояние и порядок работы планировщика (без Unity). Окна считаются синхронно (сетка Ламберта параллельна,
    /// доли секунды); n-body уточнение — корутина: Tick выполняет столько «выстрелов», сколько влезает в бюджет кадра.
    /// Весь доступ к эфемеридам и физике игры — из потока, вызывающего Tick (главный).
    /// </summary>
    public sealed class PlannerController
    {
        public readonly PlannerSettings Settings = new PlannerSettings();
        public IPlannerWorld World;
        public IPlannerPropagator Propagator;

        public string Status = "";
        public bool Busy { get; private set; }
        public int Shots { get; private set; }
        public int Version { get; private set; }

        /// <summary>Bumped whenever the window list itself changes (new search, selection, clear).</summary>
        public int WindowsVersion { get; private set; }

        public int FromBody { get; private set; } = -1;
        public PorkchopGrid Grid { get; private set; }
        public readonly List<TransferSolution> Windows = new List<TransferSolution>();
        public readonly List<FlybySolution> FlybyWindows = new List<FlybySolution>();
        public int Selected = -1;
        public PlanDraft Draft { get; private set; }
        public TransferSpec TransferSpec { get; private set; }
        public FlybySpec FlybySpec { get; private set; }
        public int PlannerId = 1;

        private TransferRequest tRq; private FlybyRequest fRq;
        private IEnumerator<ShootingCorrector.Shot> routine;
        private readonly List<int> order = new List<int>();

        public int WindowCount { get { return Settings.Mode == PlannerMode.Transfer ? Windows.Count : FlybyWindows.Count; } }

        public void Select(int i)
        {
            if (i < 0 || i >= WindowCount || i == Selected) { return; }
            Cancel();
            Selected = i; Draft = null; Version++; WindowsVersion++;
        }

        /// <summary>Сбрасывает найденные окна и черновик, сохраняя настройки поиска (смена цели/режима в окне).</summary>
        public void Clear(string status = "Окна не рассчитаны.")
        {
            Cancel();
            Windows.Clear(); FlybyWindows.Clear(); Grid = null; Draft = null;
            TransferSpec = null; FlybySpec = null; Selected = -1;
            Status = status;
            Version++; WindowsVersion++;
        }

        /// <summary>Сколько отлёту/прилёту до окна i (секунды от «сейчас»).</summary>
        public double DepartIn(int i) { return (Settings.Mode == PlannerMode.Transfer ? Windows[i].T0 : FlybyWindows[i].T0) - World.UniverseNow; }

        // -------------------------------------------------------------------------------------------- поиск окон

        public bool Search()
        {
            Cancel();
            Windows.Clear(); FlybyWindows.Clear(); Grid = null; Draft = null; Selected = -1; Version++; WindowsVersion++;
            if (World == null) { Status = "Нет мира."; return false; }
            if (World is IPlannerShipState shipState && !shipState.ShipIsFreeFlying)
            {
                Status = "Корабль под тягой, на поверхности или разрушен: план строится из свободного полёта.";
                return false;
            }

            FromBody = World.ShipBody();
            if (FromBody < 0 || !World.TryShipState(out Vector3d sp, out Vector3d sv)) { Status = "Нет аппарата или он вне сферы тела."; return false; }
            if (Settings.To < 0 || Settings.To >= World.BodyCount) { Status = "Выберите цель."; return false; }
            double now = World.UniverseNow;
            World.TryBodyState(FromBody, now, out Vector3d bp, out _);
            double shipRadius = (sp - bp).Magnitude;
            const double Day = 86400d;

            // Планировщик строит отлётную гиперболу из ТЕКУЩЕЙ орбиты. Если корабль на поверхности/в атмосфере,
            // мгновенное состояние — не орбита, и такой план не имеет смысла: отсекаем это до поиска окон.
            double minShipRadius = World.Radius(FromBody) + World.SafeAltitude(FromBody);
            if (shipRadius < minShipRadius)
            {
                Status = "Корабль ниже безопасной высоты (" + ((shipRadius - World.Radius(FromBody)) / 1e3).ToString("F0")
                    + " км при минимуме " + (World.SafeAltitude(FromBody) / 1e3).ToString("F0") + " км). Поднимите орбиту.";
                return false;
            }

            // Высоты ниже SafeAltitude (атмосфера/корона) не годятся ни для парковки, ни для захвата.
            // Не меняем поля игрока, но в расчёт передаём безопасный минимум и сообщаем об этом в статусе.
            double parkKm = Settings.UseCurrentOrbit
                ? Settings.ParkAltitudeKm
                : Math.Max(Settings.ParkAltitudeKm, World.SafeAltitude(FromBody) / 1e3);
            double arrKm = Math.Max(Settings.ArrivalAltitudeKm, World.SafeAltitude(Settings.To) / 1e3);
            bool altClamped = (!Settings.UseCurrentOrbit && parkKm > Settings.ParkAltitudeKm + 1e-9d)
                || (arrKm > Settings.ArrivalAltitudeKm + 1e-9d);
            string altNote = altClamped ? "Высоты подняты до безопасных. " : "";

            if (Settings.Mode == PlannerMode.Transfer)
            {
                tRq = new TransferRequest
                {
                    From = FromBody, To = Settings.To, UseCurrentOrbit = Settings.UseCurrentOrbit, ParkAltitudeM = parkKm * 1e3,
                    Capture = Settings.Capture, ArrivalAltitudeM = arrKm * 1e3, CaptureEccentricity = Settings.CaptureEccentricity,
                    SearchSpanS = Settings.SearchSpanDays * Day, TofMaxS = Settings.MaxFlightDays * Day,
                };
                if (!PlannerEngine.TryBuildSpec(World, tRq, now, shipRadius, out TransferSpec spec, out string why)) { Status = why; return false; }
                TransferSpec = spec;
                PlannerEngine.DefaultWindow(World, tRq, now, out double start, out double span, out double tofMin, out double tofMax);
                if (Settings.MaxFlightDays > 0d && tofMax < tofMin)
                {
                    Status = "Максимальное время полёта " + Settings.MaxFlightDays.ToString("0.###") + " сут меньше минимального "
                        + (tofMin / Day).ToString("0.#") + " сут.";
                    return false;
                }

                if (tofMax <= tofMin) { tofMax = tofMin; }
                Windows.AddRange(PlannerEngine.FindWindows(spec, start, span, tofMin, tofMax, 120, out PorkchopGrid grid));
                Grid = grid;
            }
            else
            {
                if (Settings.Via < 0 || Settings.Via >= World.BodyCount) { Status = "Выберите тело гравитационного манёвра."; return false; }
                fRq = new FlybyRequest
                {
                    From = FromBody, Via = Settings.Via, To = Settings.To, Capture = Settings.Capture,
                    ArrivalAltitudeM = arrKm * 1e3, CaptureEccentricity = Settings.CaptureEccentricity,
                    SearchSpanS = Settings.SearchSpanDays * Day,
                };
                if (!FlybyPlanner.TryBuildSpec(World, fRq, now, shipRadius, out FlybySpec fs, out string why)) { Status = why; return false; }
                FlybySpec = fs;
                FlybyPlanner.DefaultWindows(World, fRq, now, out double a0, out double a1, out double b0, out double b1, out double c0, out double c1);
                if (Settings.MaxFlightDays > 0d) { c1 = Math.Min(c1, a0 + (Settings.MaxFlightDays * Day)); b1 = Math.Min(b1, c1); }
                if (b1 < b0 || c1 < c0)
                {
                    Status = "Максимальное время полёта " + Settings.MaxFlightDays.ToString("0.###") + " сут не оставляет окна у гиганта.";
                    return false;
                }

                FlybyWindows.AddRange(FlybyPlanner.FindWindows(fs, a0, a1, b0, b1, c0, c1));
            }

            Status = altNote + (WindowCount > 0 ? "Найдено окон: " + WindowCount : "Окон нет: расширьте поиск или измените цель.");
            if (WindowCount > 0) { Selected = 0; }
            Version++;
            return WindowCount > 0;
        }

        // -------------------------------------------------------------------------------------------- черновик (n-body)

        public bool BeginDraft()
        {
            Cancel();
            if (Selected < 0 || Selected >= WindowCount) { Status = "Сначала выберите окно."; return false; }
            Draft = new PlanDraft();
            Shots = 0;
            IEnumerable<ShootingCorrector.Shot> co = Settings.Mode == PlannerMode.Transfer
                ? PlannerEngine.MakeTransferDraftCo(World, tRq, TransferSpec, Windows[Selected], Propagator, Draft)
                : FlybyPlanner.MakeDraftCo(World, fRq, FlybySpec, FlybyWindows[Selected], Propagator, Draft);
            routine = co.GetEnumerator();
            Busy = true;
            Status = "Уточнение в n-теле…";
            Version++;
            return true;
        }

        /// <summary>Выполняет запросы корутины, пока не выйдет бюджет кадра (мс). Вызывать каждый кадр, пока Busy.</summary>
        public void Tick(double budgetMs)
        {
            if (!Busy) { return; }
            var sw = Stopwatch.StartNew();
            try
            {
                do
                {
                    if (!routine.MoveNext()) { Finish(); return; }
                    ShootingCorrector.Shot s = routine.Current;
                    s.Ok = s.Model.TryShoot(s.Dv, s.Time, out s.RelPos);
                    Shots++;
                }
                while (sw.Elapsed.TotalMilliseconds < budgetMs);
            }
            catch (Exception e)
            {
                Busy = false; routine = null;
                if (Draft != null) { Draft.Valid = false; }
                Status = "Ошибка расчёта: " + e.Message;
            }

            Version++;
        }

        private void Finish()
        {
            Busy = false; routine = null;
            if (Draft.Valid)
            {
                Status = Draft.Preliminary ? "План готов (ОРИЕНТИРОВОЧНО: окно далеко)." : "План готов.";
                if (!string.IsNullOrEmpty(Draft.Note)) { Status += " " + Draft.Note; }
            }
            else { Status = Draft.Note.Length > 0 ? Draft.Note : "План построить не удалось."; }

            Version++;
        }

        public void Cancel()
        {
            if (!Busy) { return; }
            Busy = false; routine = null; Status = "Отменено."; Version++;
        }

        // -------------------------------------------------------------------------------------------- перенос в план

        public bool CanCommit { get { return !Busy && Draft != null && Draft.Valid; } }

        public int Commit(ManeuverPlan plan)
        {
            if (!CanCommit) { return 0; }
            int n = PlanExport.Apply(plan, Draft, PlannerId, World.UniverseNow);
            Status = "В плане манёвров: " + n;
            Version++;
            return n;
        }

        public int Remove(ManeuverPlan plan)
        {
            int n = plan.RemoveByPlanner(PlannerId);
            Status = n > 0 ? "Манёвры планировщика удалены." : Status;
            Version++;
            return n;
        }

        /// <summary>Родитель общего уровня (для автоматического выбора системы отсчёта карты), −1 если не определён.</summary>
        public int CommonParent
        {
            get { return World != null && FromBody >= 0 ? World.ParentOf(FromBody) : -1; }
        }
    }
}
