using System;
using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;

internal static partial class P1bTests
{
    private static int Test400_StageCoreConformance()
    {
        // Conformance of the pure staging model (stage 1, no Unity and PlacedPart):
        // cores are assembled by hand, all expectations are hand formulas.
        // Checks: thrust/flow == nameplate, cutoff == burnable, segments with mixed
        // engines, Commit accumulation, StagePropulsion semantics (ignite/jettison/
        // auto-ignite), segment detector, StageDeltaV == Tsiolkovsky.
        const double g0 = 9.80665d;
        EngineSpec rs25 = EngineSpecs.Rs25;
        EngineSpec aj10 = EngineSpecs.Aj10;
        DvEnvironment env = DvEnvironment.Default;

        double Rel(double a, double b)
        {
            return Math.Abs(a - b) / Math.Max(1e-12d, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        SpacecraftIntegrationState State(double mass)
        {
            return new SpacecraftIntegrationState { Mass = mass };
        }

        StageCore MakeCore(int index, EngineSpec engine, double extraDryKg,
            Propellant p1, double kg1, Propellant p2, double kg2)
        {
            var core = new StageCore { Index = index, DryMassKg = engine.MassKg + extraDryKg };
            core.GetOrAddPool(p1).StartKg = kg1;
            core.GetOrAddPool(p2).StartKg = kg2;
            core.AddEngine(engine, engine.DisplayName, engine.Mix, env.ThrustN(engine), env.MassFlowKgS(engine));
            core.FillToStart();
            return core;
        }

        // 1) Engine nameplate: F / (mdot·g0) == Isp; cutoff == burnable.
        StageCore single = MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d);
        double rsFlow = env.MassFlowKgS(rs25);
        double burnable = rs25.Mix.BurnableMassKg(3000d, 500d);
        Check(Math.Abs((single.ThrustN / (single.MassFlowKgS * g0)) - 452d) < 1e-9d,
            "T400 isp identity", string.Format("F/(mdot·g0)={0:R}, паспорт 452", single.ThrustN / (single.MassFlowKgS * g0)));
        Check(Rel(single.CutoffBurnKg(), burnable) < 1e-9d,
            "T400 cutoff == burnable", string.Format("отсечка {0:F3} кг, сжигаемое {1:F3} кг", single.CutoffBurnKg(), burnable));

        // 2) Accumulation: many small Commits == one large (pool state).
        StageCore one = MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d);
        StageCore many = MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d);
        const double consume = 1234.5d;
        double got = one.Commit(consume);
        double acc = 0d;
        var rnd = new Random(11);
        while (acc < consume - 1e-9d)
        {
            acc += many.Commit(Math.Min(consume - acc, 1d + (rnd.NextDouble() * 300d)));
        }

        bool poolsSame = true;
        for (int i = 0; i < one.Pools.Count; i++)
        {
            if (Math.Abs(one.Pools[i].Kg - many.Pools[i].Kg) > 1e-9d)
            {
                poolsSame = false;
            }
        }

        Check(Rel(got, consume) < 1e-9d && poolsSame,
            "T400 commit accumulation", string.Format("Commit={0:F6} (запрос {1:F6}); пулы совпали: {2}", got, consume, poolsSame));

        // 3) Mixed engines: segment boundary — RS-25 cuts out (LH2 gone), AJ10 continues.
        var mixed = new StageCore { Index = 0, DryMassKg = rs25.MassKg + aj10.MassKg };
        mixed.GetOrAddPool(Propellants.Lox).StartKg = 3000d;
        mixed.GetOrAddPool(Propellants.Lh2).StartKg = 400d;
        mixed.GetOrAddPool(Propellants.N2o4).StartKg = 1000d;
        mixed.GetOrAddPool(Propellants.Aerozine50).StartKg = 600d;
        mixed.AddEngine(rs25, rs25.DisplayName, rs25.Mix, env.ThrustN(rs25), env.MassFlowKgS(rs25));
        mixed.AddEngine(aj10, aj10.DisplayName, aj10.Mix, env.ThrustN(aj10), env.MassFlowKgS(aj10));
        mixed.FillToStart();

        double rsOx = rsFlow * rs25.Mix.OxidizerMassFraction;
        double rsFuel = rsFlow - rsOx;
        double ajFlow = env.MassFlowKgS(aj10);
        double ajOx = ajFlow * aj10.Mix.OxidizerMassFraction;
        double ajFuel = ajFlow - ajOx;
        double tSeg = 400d / rsFuel;                        // LH2 runs out here, RS-25 cuts out
        double seg = (rsFlow + ajFlow) * tSeg;
        // AJ10 also burned during the first segment, so its pools are smaller at the boundary:
        double n2o4Left = 1000d - (ajOx * tSeg);
        double az50Left = 600d - (ajFuel * tSeg);
        double ajBurn = aj10.Mix.BurnableMassKg(n2o4Left, az50Left);   // limited by N2O4
        double tAj = ajBurn / ajFlow;
        double loxLeft = 3000d - (rsOx * tSeg);
        double az50Final = az50Left - (ajFuel * tAj);
        Check(Rel(mixed.SegmentBurnKg(), seg) < 1e-9d && Rel(mixed.CutoffBurnKg(), seg + ajBurn) < 1e-9d,
            "T400 mixed segment/cutoff", string.Format("участок {0:F3} (эталон {1:F3}), отсечка {2:F3} (эталон {3:F3})",
                mixed.SegmentBurnKg(), seg, mixed.CutoffBurnKg(), seg + ajBurn));
        double segCommitted = mixed.Commit(seg);
        Check(Rel(segCommitted, seg) < 1e-6d
            && Rel(mixed.ThrustN, env.ThrustN(aj10)) < 1e-9d
            && Rel(mixed.MassFlowKgS, ajFlow) < 1e-9d
            && Rel(mixed.CutoffBurnKg(), ajBurn) < 1e-9d,
            "T400 mixed boundary", string.Format("списано {0:F3}; после границы F={1:F0} Н (AJ10 {2:F0}), mdot={3:F3}",
                segCommitted, mixed.ThrustN, env.ThrustN(aj10), mixed.MassFlowKgS));
        double rest = mixed.Commit(1e12);
        Check(mixed.ThrustN == 0d && Rel(segCommitted + rest, seg + ajBurn) < 1e-9d
            && Rel(mixed.PropellantKg, loxLeft + az50Final) < 1e-9d,
            "T400 full stop", string.Format("досписано {0:F3}; остаток пулов {1:F3} кг (непарные LOX {2:F3} + AZ50 {3:F3})",
                rest, mixed.PropellantKg, loxLeft, az50Final));

        // 4) StagePropulsion: ignite → jettison+ignite → the last stage can't be jettisoned.
        var model = new StagePropulsion(new List<StageCore>
        {
            MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
            MakeCore(1, aj10, 100d, Propellants.N2o4, 1000d, Propellants.Aerozine50, 600d),
        }, 42d, false);
        Check(model.ActiveStageIndex == -1 && model.MassFlowKgS == 0d,
            "T400 model idle", "до зажигания тяги нет");
        Check(model.Stage() == StagingResult.Ignited && model.ActiveStageIndex == 0,
            "T400 ignite", "первое нажатие зажигает нижнюю");
        Check(Rel(model.CommitBurn(1000d), 1000d) < 1e-9d, "T400 model commit", "списание 1000 кг принято");
        Check(model.Stage() == StagingResult.JettisonedAndIgnited && model.FirstAttached == 1 && model.IsJettisoned(0),
            "T400 jettison+ignite", "второе нажатие отделяет нижнюю и зажигает верхнюю");
        bool[] flags = model.JettisonedFlags();
        StageCore upperRef = MakeCore(1, aj10, 100d, Propellants.N2o4, 1000d, Propellants.Aerozine50, 600d);
        Check(flags[0] && !flags[1] && Rel(model.AttachedTotalKg(), upperRef.TotalMassKg) < 1e-12d,
            "T400 attached mass", string.Format("присоединено {0:F1} кг, эталон {1:F1} кг",
                model.AttachedTotalKg(), upperRef.TotalMassKg));
        Check(model.Stage() == StagingResult.Nothing, "T400 last stage stays", "последнюю ступень отделять нечем");

        // 5) Auto-ignite (debug): the next stage ignites on its own, without jettison.
        var auto = new StagePropulsion(new List<StageCore>
        {
            MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
            MakeCore(1, aj10, 100d, Propellants.N2o4, 1000d, Propellants.Aerozine50, 600d),
        }, 0d, true);
        auto.CommitBurn(1e12);
        Check(auto.ActiveStageIndex == 0 && auto.MassFlowKgS == 0d
            && auto.IgniteNextAttached() && auto.ActiveStageIndex == 1 && auto.FirstAttached == 0,
            "T400 auto-ignite", "нижняя выгорела — верхняя зажглась без отделения");

        // 6) Segment detector: falling crossing exactly at SegmentBurnKg.
        var detModel = new StagePropulsion(new List<StageCore>
        {
            MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
        }, 0d, false);
        detModel.Stage();
        var detector = new StageDepletionDetector();
        detector.Arm(detModel, 10000d);
        double detSeg = detModel.SegmentBurnKg;
        double atStart = detector.Evaluate(State(10000d), 0d);
        double mid = detector.Evaluate(State(10000d - (detSeg * 0.5d)), 0d);
        double past = detector.Evaluate(State(10000d - (detSeg * 1.1d)), 0d);
        Check(Rel(atStart, detSeg) < 1e-9d && mid > 0d && past < 0d,
            "T400 detector crossing", string.Format("g: старт {0:F1}, середина {1:F1}, за границей {2:F1}",
                atStart, mid, past));
        var idleModel = new StagePropulsion(new List<StageCore>
        {
            MakeCore(0, rs25, 500d, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
        }, 0d, false);
        detector.Arm(idleModel, 10000d);
        Check(detector.Evaluate(State(10000d), 0d) == 0d, "T400 detector idle zero", "без огня — ровно ноль, не ложное падение");

        // 7) StageDeltaV == hand Tsiolkovsky (one stage, full tank).
        var planCores = new List<StageCore>
        {
            MakeCore(0, rs25, 0d, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
        };
        DvResult plan = StageDeltaV.Evaluate(env, planCores, null, 0, false);
        double m0 = plan.Stages[0].StartMassKg;
        double hand = 452d * g0 * Math.Log(m0 / (m0 - burnable));
        Check(Rel(plan.Stages[0].DeltaVMs, hand) < 1e-9d,
            "T400 Evaluate == Tsiolkovsky", string.Format("ΔV {0:F3} м/с, эталон {1:F3} м/с", plan.Stages[0].DeltaVMs, hand));

        return 0;
    }

    private static int Test401_StagePropulsionFlight()
    {
        // Cross-checks of the runtime model and the panel computation (ported from
        // ConformanceReference): ignite/jettison, mass invariant with a crossing at
        // the event root, panel from state, detached mass.
        const double g0 = 9.80665d;
        EngineSpec rs25 = EngineSpecs.Rs25;
        EngineSpec aj10 = EngineSpecs.Aj10;
        DvEnvironment env = DvEnvironment.Default;

        double Rel(double a, double b)
        {
            return Math.Abs(a - b) / Math.Max(1e-12d, Math.Max(Math.Abs(a), Math.Abs(b)));
        }

        StageCore Make(int index, EngineSpec engine, Propellant p1, double kg1, Propellant p2, double kg2)
        {
            var core = new StageCore { Index = index, DryMassKg = engine.MassKg };
            core.GetOrAddPool(p1).StartKg = kg1;
            core.GetOrAddPool(p2).StartKg = kg2;
            core.AddEngine(engine, engine.DisplayName, engine.Mix, env.ThrustN(engine), env.MassFlowKgS(engine));
            core.FillToStart();
            return core;
        }

        // Three-stage stack: RS-25 | separator | AJ10 | separator | AJ10.
        var cores = new List<StageCore>
        {
            Make(0, rs25, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
            Make(1, aj10, Propellants.N2o4, 800d, Propellants.Aerozine50, 400d),
            Make(2, aj10, Propellants.N2o4, 400d, Propellants.Aerozine50, 200d),
        };
        var fresh = new List<StageCore>();
        for (int k = 0; k < cores.Count; k++)
        {
            fresh.Add(cores[k].Clone());
        }

        var prop = new StagePropulsion(cores, 0d, false);
        double shipMass = prop.AttachedTotalKg();

        DvResult Panel()
        {
            var pools = new List<StagePoolState>();
            prop.AppendState(pools);
            return StageDeltaV.EvaluateFromState(env, fresh, pools, prop.JettisonedFlags(), 0);
        }

        DvResult plan = StageDeltaV.Evaluate(env, fresh, null, 0, false);
        Check(prop.ThrustN == 0d && prop.ActiveStageIndex == -1, "T401 idle", "до зажигания тяги нет");
        Check(prop.Stage() == StagingResult.Ignited && prop.ActiveStageIndex == 0 && prop.ThrustN > 0d,
            "T401 ignite", "первое нажатие зажигает нижнюю");
        Check(Math.Abs((prop.ThrustN / (prop.MassFlowKgS * g0)) - 452d) < 1e-6d,
            "T401 flow isp", string.Format("F/(mdot·g0)={0:R}", prop.ThrustN / (prop.MassFlowKgS * g0)));

        // Burn "as physics would": segment + crossing at the event root
        // (≤ flow·1e-6 s), Commit clamps by pool; the invariant holds.
        int events = 0;
        double massBefore = shipMass;
        for (int g = 0; g < 20 && prop.MassFlowKgS > 0d; g++)
        {
            double seg = prop.SegmentBurnKg;
            double burned = seg + (prop.MassFlowKgS * 1e-6d);
            shipMass -= burned;
            prop.CommitBurn(burned);
            events++;
        }

        double burnedDv = 452d * g0 * Math.Log(massBefore / shipMass);
        Check(events == 1 && prop.ThrustN == 0d && prop.MassFlowKgS == 0d,
            "T401 stage1 one event", string.Format("событий {0} (одна пара — одно)", events));
        Check(Math.Abs(prop.AttachedTotalKg() - shipMass) / shipMass < 1e-6d,
            "T401 invariant after burn", string.Format("модель {0:F1}, корабль {1:F1}",
                prop.AttachedTotalKg(), shipMass));
        DvResult afterBurn = Panel();
        Check(afterBurn.Stages[0].IsSpent && afterBurn.AttachedSpentMassKg > 0d && afterBurn.Notes.Count == 1,
            "T401 spent attached note", string.Format("пометок {0}, неотделённая масса {1:F0} кг",
                afterBurn.Notes.Count, afterBurn.AttachedSpentMassKg));
        Check(Math.Abs(burnedDv - plan.Stages[0].DeltaVMs) < 0.01d,
            "T401 used+remaining == plan", string.Format("сожжено {0:F3}, план ступени {1:F3}",
                burnedDv, plan.Stages[0].DeltaVMs));

        // Jettison stage 1: mass is taken BEFORE Stage(); Ship.Mass is subtracted in
        // the same place as the call (as in game), so the invariant holds.
        double stageMass = prop.StageTotalKg(0);
        Check(prop.Stage() == StagingResult.JettisonedAndIgnited && prop.FirstAttached == 1 && prop.ActiveStageIndex == 1,
            "T401 jettison+ignite", "второе нажатие отделяет нижнюю и зажигает верхнюю");
        shipMass -= stageMass;
        Check(Math.Abs(prop.AttachedTotalKg() - shipMass) / shipMass < 1e-6d,
            "T401 invariant after jettison", string.Format("модель {0:F1}, корабль {1:F1}",
                prop.AttachedTotalKg(), shipMass));
        DvResult afterJet = Panel();
        Check(afterJet.Stages[0].IsJettisoned && afterJet.Notes.Count == 0
            && Math.Abs(afterJet.TotalDeltaVMs - afterBurn.TotalDeltaVMs) < 1e-9d,
            "T401 panel after jettison", "сброшенная помечена, пометка про неотделённую ушла, сумма не изменилась");
        Check(Math.Abs(afterJet.CurrentMassKg - prop.AttachedTotalKg()) < 1e-6d,
            "T401 panel mass", "масса панели == массе модели");

        // Half of stage 2, then jettison of the NON-empty stage 2.
        double half = 0.5d * cores[1].StartPropellantKg;
        shipMass -= prop.CommitBurn(half);
        DvResult halfPanel = Panel();
        Check(halfPanel.ActiveStageIndex == 1
            && halfPanel.Stages[1].DeltaVMs < plan.Stages[1].DeltaVMs && halfPanel.Stages[1].DeltaVMs > 0d,
            "T401 half burn", "остаток ступени 2 меньше плана и больше нуля");
        double before = halfPanel.TotalDeltaVMs;
        Check(prop.Stage() == StagingResult.JettisonedAndIgnited && prop.FirstAttached == 2,
            "T401 jettison non-empty", "непустую ступень тоже можно сбросить");
        DvResult dropped = Panel();
        Check(Math.Abs(dropped.TotalDeltaVMs - (before - halfPanel.Stages[1].DeltaVMs)) < 1e-6d,
            "T401 drop fuel loses its dv", string.Format("сумма {0:F3}, ожидание {1:F3}",
                dropped.TotalDeltaVMs, before - halfPanel.Stages[1].DeltaVMs));
        Check(prop.Stage() == StagingResult.Nothing && prop.FirstAttached == 2,
            "T401 last stays", "последнюю ступень отделять нечем");
        int rev = prop.Revision;
        prop.CommitBurn(10d);
        Check(prop.Revision == rev + 1, "T401 revision", "Revision растёт на списании");

        // Mixed stage (RS-25 + AJ10 in one): with the crossing exactly 2 events.
        var mixed = new StageCore { Index = 0, DryMassKg = rs25.MassKg + aj10.MassKg };
        mixed.GetOrAddPool(Propellants.Lox).StartKg = 3000d;
        mixed.GetOrAddPool(Propellants.Lh2).StartKg = 400d;
        mixed.GetOrAddPool(Propellants.N2o4).StartKg = 1000d;
        mixed.GetOrAddPool(Propellants.Aerozine50).StartKg = 600d;
        mixed.AddEngine(rs25, rs25.DisplayName, rs25.Mix, env.ThrustN(rs25), env.MassFlowKgS(rs25));
        mixed.AddEngine(aj10, aj10.DisplayName, aj10.Mix, env.ThrustN(aj10), env.MassFlowKgS(aj10));
        mixed.FillToStart();
        var mixModel = new StagePropulsion(new List<StageCore> { mixed }, 0d, true);
        int mixEvents = 0;
        double mixF0 = mixModel.ThrustN;
        bool groupCut = false;
        for (int g = 0; g < 20 && mixModel.MassFlowKgS > 0d; g++)
        {
            mixModel.CommitBurn(mixModel.SegmentBurnKg + (mixModel.MassFlowKgS * 1e-6d));
            mixEvents++;
            if (mixEvents == 1)
            {
                groupCut = mixModel.ThrustN < mixF0 && mixModel.ThrustN > 0d;
            }
        }

        Check(mixEvents == 2 && mixModel.ThrustN == 0d && groupCut,
            "T401 mixed two events", string.Format("событий {0} (граница состава + останов), группа глохла: {1}",
                mixEvents, groupCut));

        // A part outside the main assembly: model + DetachedMassKg == total mass.
        var orphanCores = new List<StageCore>
        {
            Make(0, rs25, Propellants.Lox, 3000d, Propellants.Lh2, 500d),
        };
        const double orphanMass = 1234d;
        var orphan = new StagePropulsion(orphanCores, orphanMass, false);
        Check(Math.Abs(orphan.AttachedTotalKg() + orphan.DetachedMassKg - (orphanCores[0].TotalMassKg + orphanMass)) < 1e-6d,
            "T401 orphan mass", string.Format("модель {0:F1} + вне сборки {1:F1}", orphan.AttachedTotalKg(), orphan.DetachedMassKg));

        return 0;
    }
}
