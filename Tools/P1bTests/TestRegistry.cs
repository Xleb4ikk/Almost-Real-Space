using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Galilego.Core;
using Galilego.Debris;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{
    private readonly struct P1bCase
    {
        public readonly string Name;
        public readonly Func<int> Run;
        public readonly bool Slow;

        public P1bCase(string name, Func<int> run, bool slow)
        {
            Name = name;
            Run = run;
            Slow = slow;
        }
    }

    private static List<P1bCase> TestRegistry()
    {
        return new List<P1bCase>
        {
            new P1bCase("Test1_WarpIdentity", Test1_WarpIdentity, false),
            new P1bCase("Test2_ControlTickClip", Test2_ControlTickClip, false),
            new P1bCase("Test3_LongWarpStopsAtEvent", Test3_LongWarpStopsAtEvent, false),
            new P1bCase("Test4_SilentCancel", Test4_SilentCancel, false),
            new P1bCase("Test5_Singularity", Test5_Singularity, false),
            new P1bCase("Test6_CacheIdentical", Test6_CacheIdentical, false),
            new P1bCase("Test7_BudgetPriority", Test7_BudgetPriority, false),
            new P1bCase("Test8_PoolNoAllocs", Test8_PoolNoAllocs, false),
            new P1bCase("Test9_Eviction", Test9_Eviction, false),
            new P1bCase("Test10_SleepError", Test10_SleepError, false),
            new P1bCase("Test11_MultiCrossing", Test11_MultiCrossing, false),
            new P1bCase("Test12_Depletion", Test12_Depletion, false),
            new P1bCase("Test13_Tsiolkovsky", Test13_Tsiolkovsky, false),
            new P1bCase("Test14_BurnWarpIdentity", Test14_BurnWarpIdentity, false),
            new P1bCase("Test15_IspAltitude", Test15_IspAltitude, false),
            new P1bCase("Test16_PresetIsParameter", Test16_PresetIsParameter, false),
            new P1bCase("Test18_DenseGate", Test18_DenseGate, false),
            new P1bCase("Test19_AdaptiveGrain", Test19_AdaptiveGrain, false),
            new P1bCase("Test17_FrameBridge", Test17_FrameBridge, false),
            new P1bCase("Test17_Handedness", Test17_Handedness, false),
            new P1bCase("Test20_Kind", Test20_Kind, false),
            new P1bCase("Test20_SurfaceInverse", Test20_SurfaceInverse, false),
            new P1bCase("Test20_Reaction", Test20_Reaction, false),
            new P1bCase("Test20_SurfaceMotion", Test20_SurfaceMotion, false),
            new P1bCase("Test21_Kahan", Test21_Kahan, true),
            new P1bCase("Test22_DragContract", Test22_DragContract, false),
            new P1bCase("Test22_DragDip", Test22_DragDip, false),
            new P1bCase("Test23_Decay", Test23_Decay, false),
            new P1bCase("Test23_Retrograde", Test23_Retrograde, false),
            new P1bCase("Test23_TopCrossing", Test23_TopCrossing, false),
            new P1bCase("Test23_RateBaseline", Test23_RateBaseline, false),
            new P1bCase("Test24_History", Test24_History, false),
            new P1bCase("Test25_Lambert", Test25_Lambert, true),
            new P1bCase("Test26_CircularMatrix", Test26_CircularMatrix, false),
            new P1bCase("Test27_Impulsive", Test27_Impulsive, false),
            new P1bCase("Test28_FiniteBurn", Test28_FiniteBurn, true),
            new P1bCase("Test29_DescentChain", Test29_DescentChain, true),
            new P1bCase("Test29_TerminalVelocity", Test29_TerminalVelocity, false),
            new P1bCase("Test30_YearDrift", Test30_YearDrift, true),
            new P1bCase("Test31_Flyby", Test31_Flyby, true),
            new P1bCase("Test32_Escape", Test32_Escape, true),
            new P1bCase("Test30_PartsTrap", Test30_PartsTrap, false),
            new P1bCase("Test31_AttitudeSpin", Test31_AttitudeSpin, false),
            new P1bCase("Test31_AttitudeTumble", Test31_AttitudeTumble, false),
            new P1bCase("Test31_AttitudePD", Test31_AttitudePD, false),
            new P1bCase("Test31_TorqueLink", Test31_TorqueLink, false),
            new P1bCase("Test33_Ascent", Test33_Ascent, true),
            new P1bCase("Test33_StagingMomentum", Test33_StagingMomentum, false),
            new P1bCase("Test34_GoldenState", Test34_GoldenState, false),
            new P1bCase("Test35_KeplerEdge", Test35_KeplerEdge, false),
            new P1bCase("Test36_HelioDrift", Test36_HelioDrift, false),
            new P1bCase("Test37_LunarYears", Test37_LunarYears, true),
            new P1bCase("Test38_LowDrag", Test38_LowDrag, false),
            new P1bCase("Test39_SecularDrift", Test39_SecularDrift, true),
            new P1bCase("Test40_EphemerisBake", Test40_EphemerisBake, true),
            new P1bCase("Test41_LongBakeProbe", Test41_LongBakeProbe, true),
            new P1bCase("Test42_BakeRule", Test42_BakeRule, false),
            new P1bCase("Test43_BakeStream", Test43_BakeStream, false),
            new P1bCase("Test44_TimeCap", Test44_TimeCap, false),
            new P1bCase("Test45_PorkchopHorizon", Test45_PorkchopHorizon, false),
            new P1bCase("Test46_FullBake1024", Test46_FullBake1024, true),
            new P1bCase("Test60_TwelveBodyAccuracy", Test60_TwelveBodyAccuracy, true),
            new P1bCase("Test52_Centrifugal", Test52_Centrifugal, false),
            new P1bCase("Test53_LambertHalfPlane", Test53_LambertHalfPlane, false),
            new P1bCase("Test54_LambertOvershoot", Test54_LambertOvershoot, false),
            new P1bCase("Test55_ForceAcceptSync", Test55_ForceAcceptSync, false),
            new P1bCase("Test56_ApsisZero", Test56_ApsisZero, false),
            new P1bCase("Test57_RailValidation", Test57_RailValidation, false),
            new P1bCase("Test58_GrainProbe", Test58_GrainProbe, false),
            new P1bCase("Test59_BakeFilesAndSpeed", Test59_BakeFilesAndSpeed, false),
            new P1bCase("Test61_UnbakeableMoon", Test61_UnbakeableMoon, false),
            new P1bCase("Test62_CompressV2", Test62_CompressV2, false),
            new P1bCase("Test63_KeplerContinuation", Test63_KeplerContinuation, false),
            new P1bCase("Test64_HillValidation", Test64_HillValidation, false),
            new P1bCase("Test65_SystemBlueprint", Test65_SystemBlueprint, false),
            new P1bCase("Test66_NanProtocol", Test66_NanProtocol, false),
            new P1bCase("Test67_KeplerPredictorGuards", Test67_KeplerPredictorGuards, false),
            new P1bCase("Test68_StructuralContracts", Test68_StructuralContracts, false),
            new P1bCase("Test69_WarpLadder", Test69_WarpLadder, false),
            new P1bCase("Test70_TryLiftoffSpin", Test70_TryLiftoffSpin, false),
            new P1bCase("Test71_DebrisTunnel", Test71_DebrisTunnel, false),
            new P1bCase("Test72_VisualFrame", Test72_VisualFrame, false),
            new P1bCase("Test74_ScaleBounds", Test74_ScaleBounds, false),
            new P1bCase("Test73_EphemerisThreadGate", Test73_EphemerisThreadGate, false),
            new P1bCase("Test75_TorqueProbeGuard", Test75_TorqueProbeGuard, false),
            new P1bCase("Test76_QuaternionBridge", Test76_QuaternionBridge, false),
            new P1bCase("Test77_TerrainHeightfield", Test77_TerrainHeightfield, false),
            new P1bCase("Test78_JointedBreakup", Test78_JointedBreakup, false),
            new P1bCase("Test79_TidalLock", Test79_TidalLock, false),
            new P1bCase("Test80_StarColor", Test80_StarColor, false),
            new P1bCase("Test81_StarGranulation", Test81_StarGranulation, false),
            new P1bCase("Test82_TerrainAdvanced", Test82_TerrainAdvanced, false),
            new P1bCase("Test83_TerrainColor", Test83_TerrainColor, false),
            new P1bCase("Test84_TerrainFloatParity", Test84_TerrainFloatParity, false),
            new P1bCase("Test85_CubeSphere", Test85_CubeSphere, false),
            new P1bCase("Test86_NaNProbe", Test86_NaNProbe, false),
            new P1bCase("Test87_LandFraction", Test87_LandFraction, false),
            new P1bCase("Test88_SkyPhysics", Test88_SkyPhysics, false),
            new P1bCase("Test89_TerrainPlains", Test89_TerrainPlains, false),
            new P1bCase("Test90_AtmosphereOptics", Test90_AtmosphereOptics, false),
        };
    }
}
