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
{    private static int Test500_IntegratorStudyFast()
    {
        Console.WriteLine("--- Integrator study: fast ---");
        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);
        StudySystemTable("Custom system", custom);
        StudySystemTable("Real solar 7-body (report fixture)", real);

        StudyPlanetTable("Real solar 7-body 10 y", realNb, 10d, 112.5d, true);
        StudyBake("Real solar 7-body", real, realNb, 10d);
        StudyPlanetTable("Custom 10 y", customNb, 10d, 112.5d, true);
        StudyBake("Custom", custom, customNb, 10d);

        StudyShipTable("Real solar: Earth LEO 400 km", real, realNb, 1, 6.371e6d + 4e5d, 6.371e6d + 4e5d, 30d);
        StudyShipTable("Real solar: Earth eccentric 400/40000 km", real, realNb, 1, 6.371e6d + 4e5d, 6.371e6d + 4e7d, 30d);
        StudyShipTable("Real solar: Sun 1 AU coast", real, realNb, 0, StudyAU, StudyAU, 30d);
        StudyShipTable("Real solar: Sun high-e 0.1/5 AU", real, realNb, 0, 0.1d * StudyAU, 5d * StudyAU, 30d);
        StudyShipTable("Custom: Terra LEO 400 km", custom, customNb, 1, 1.143e6d + 4e5d, 1.143e6d + 4e5d, 30d);
        StudyShipTable("Custom: Terra eccentric 400/40000 km", custom, customNb, 1, 1.143e6d + 4e5d, 1.143e6d + 4e7d, 30d);

        StudyLiveCost(customNb, realNb);
        StudyEvent(custom);
        Console.WriteLine("--- Integrator study: fast complete ---");
        return 0;
    }
}

