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
{    private static int Test503_ReferenceConvergence()
    {
        Console.WriteLine("--- Reference convergence ---");
        StarSystem real = StudyRealSolar7();
        StudyNBody realNb = StudyMakeNBody(real);
        StudyBake("Reference real", real, realNb, 1d, 1d);

        StarSystem custom = StudyCustomSystem();
        StudyNBody customNb = StudyMakeNBody(custom);
        StudyBake("Reference custom", custom, customNb, 1d, 1d);

        StudyReferenceCase("Earth LEO 400 km", real, realNb, 1, 6.771e6d, 6.771e6d, 30d, false);
        StudyReferenceCase("Earth ecc 400/40000", real, realNb, 1, 6.771e6d, 4.6371e7d, 30d, true);
        StudyReferenceCase("Terra LEO 400 km", custom, customNb, 1, 1.543e6d, 1.543e6d, 30d, false);
        StudyReferenceCase("Terra ecc 400/40000", custom, customNb, 1, 1.543e6d, 4.114e7d, 30d, true);

        StudyKeplerReference();
        StudyTimeReversal();
        StudyPlanetReference(realNb, 10d);

        Console.WriteLine("--- Reference convergence complete ---");
        return 0;
    }
}

