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
    private static StarSystem TestSystem()
    {
        return ExampleSystemPresets.CreateExampleSystem();
    }

    private static void BuildShipPhysics(StarSystem sys, out SpacecraftPhysics phys, out EventDrivenPropagator prop, TestThrustSource thrust)
    {
        phys = new SpacecraftPhysics();
        phys.Sources.Add(new CachedGravitySource(sys));
        if (thrust != null)
        {
            phys.Sources.Add(thrust);
        }

        prop = new EventDrivenPropagator(phys);
    }

    private static string SanitizeNameForCheck(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return name.Trim();
    }
}
