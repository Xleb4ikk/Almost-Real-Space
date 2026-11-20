using System.Reflection;
using System.Text;
using Galilego.Building;
using Galilego.Universe;
using UnityEngine;

/// <summary>Зонд состояния билдера и корабля (Play Mode).</summary>
public static class Probe
{
    public static string Run()
    {
        SimulationRunner runner = Object.FindAnyObjectByType<SimulationRunner>();
        if (runner == null)
        {
            return "no runner";
        }

        var sb = new StringBuilder();
        sb.Append("build=").Append(runner.BuildMode);
        sb.Append(" regime=").Append(runner.Regime);
        sb.Append(" prop=").Append(runner.Propulsion != null ? "yes" : "null");
        sb.Append(" flight=").Append(runner.Flight != null ? "yes" : "null");
        sb.Append(" parts=").Append(runner.Parts != null ? runner.Parts.Length : -1);
        sb.Append(" paused=").Append(runner.Paused);
        sb.Append(" zone=").Append(Galilego.Simulation.ContactZone.ContactZoneHost.Active);

        SpacecraftBuilder builder = Object.FindAnyObjectByType<SpacecraftBuilder>();
        sb.Append(" builder=").Append(builder != null ? "yes" : "null");
        if (builder != null)
        {
            sb.Append(" draft=").Append(builder.Parts != null ? builder.Parts.Count : -1);
            sb.Append(" physicsOn=").Append(builder.PhysicsOn);
            FieldInfo pending = typeof(SpacecraftBuilder).GetField("pendingPhysics", BindingFlags.NonPublic | BindingFlags.Instance);
            sb.Append(" pending=").Append(pending != null ? pending.GetValue(builder) : "?");
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        sb.Append(" tc=").Append(tc != null ? "yes" : "no");
        if (tc != null)
        {
            sb.Append(" started=").Append(tc.DebugStarted);
            sb.Append(" points=").Append(tc.DebugPointCount);
        }

        return sb.ToString();
    }
}
