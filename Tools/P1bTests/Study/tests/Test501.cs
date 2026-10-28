using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;

internal static partial class P1bTests
{    private static int Test501_IntegratorStudySlow()
    {
        Console.WriteLine("--- Integrator study: slow ---");
        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);

        StudyPlanetTable("Custom 100 y", customNb, 100d, 112.5d, false);
        StudyBake("Custom", custom, customNb, 100d, 400d);
        StudyPlanetTable("Real solar 7-body 100 y", realNb, 100d, 112.5d, false);
        StudyBake("Real solar 7-body", real, realNb, 100d, 200d);

        StudyLiveCost(customNb, realNb);
        Console.WriteLine("--- Integrator study: slow complete ---");
        return 0;
    }
}

