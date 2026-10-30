using System;
using System.Collections.Generic;
using GalilegoPhysicsTests.Support.Scenarios;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace GalilegoPhysicsTests.Support.Matrix
{
    public sealed class MatrixCase : IXunitSerializable
    {
        public string ScenarioId;
        public string IntegratorName;
        public double Tolerance;
        public string Display;
        public string Family;
        public string Tier;
        public string ScenarioClass;
        public string Skip;

        public MatrixCase()
        {
        }

        public void Serialize(IXunitSerializationInfo info)
        {
            info.AddValue(nameof(ScenarioId), ScenarioId);
            info.AddValue(nameof(IntegratorName), IntegratorName);
            info.AddValue(nameof(Tolerance), Tolerance);
            info.AddValue(nameof(Display), Display);
            info.AddValue(nameof(Family), Family);
            info.AddValue(nameof(Tier), Tier);
            info.AddValue(nameof(ScenarioClass), ScenarioClass);
            info.AddValue(nameof(Skip), Skip);
        }

        public void Deserialize(IXunitSerializationInfo info)
        {
            ScenarioId = info.GetValue<string>(nameof(ScenarioId));
            IntegratorName = info.GetValue<string>(nameof(IntegratorName));
            Tolerance = info.GetValue<double>(nameof(Tolerance));
            Display = info.GetValue<string>(nameof(Display));
            Family = info.GetValue<string>(nameof(Family));
            Tier = info.GetValue<string>(nameof(Tier));
            ScenarioClass = info.GetValue<string>(nameof(ScenarioClass));
            Skip = info.GetValue<string>(nameof(Skip));
        }

        public Dictionary<string, List<string>> BuildTraits()
        {
            var traits = new Dictionary<string, List<string>>(StringComparer.Ordinal)
            {
                ["Integrator"] = new List<string> { IntegratorName },
                ["Scenario"] = new List<string> { Family },
                ["Tier"] = new List<string> { Tier },
                ["ScenarioClass"] = new List<string> { ScenarioClass }
            };
            return traits;
        }

        public override string ToString() => Display;
    }

    [XunitTestCaseDiscoverer("GalilegoPhysicsTests.Support.Matrix.MatrixDiscoverer", "GalilegoPhysicsTests")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class MatrixTheoryAttribute : TheoryAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class MatrixDataAttribute : DataAttribute
    {
        public override IEnumerable<object[]> GetData(System.Reflection.MethodInfo testMethod)
        {
            foreach (Scenario scenario in ScenarioCatalog.All)
            {
                foreach (IntegratorKind integrator in new[] { IntegratorKind.Dopri5, IntegratorKind.Dop853 })
                {
                    foreach (double tolerance in scenario.Tolerances)
                    {
                        string skip = scenario.Tier == ScenarioTier.Slow && !Env.SlowEnabled
                            ? Env.SlowSkipReason
                            : null;
                        yield return new object[]
                        {
                            new MatrixCase
                            {
                                ScenarioId = scenario.Id,
                                IntegratorName = scenario.IntegratorLabel(integrator),
                                Tolerance = tolerance,
                                Display = scenario.DisplayName(integrator, tolerance),
                                Family = scenario.Family,
                                Tier = scenario.Tier.ToString(),
                                ScenarioClass = scenario.Class.ToString(),
                                Skip = skip
                            }
                        };
                    }
                }
            }
        }
    }

    public sealed class MatrixDiscoverer : TheoryDiscoverer
    {
        public MatrixDiscoverer(IMessageSink diagnosticMessageSink)
            : base(diagnosticMessageSink)
        {
        }

        protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
            ITestFrameworkDiscoveryOptions discoveryOptions,
            ITestMethod testMethod,
            IAttributeInfo theoryAttribute,
            object[] dataRow)
        {
            var matrix = (MatrixCase)dataRow[0];
            yield return new MatrixTestCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                dataRow,
                matrix);
        }
    }

    public sealed class MatrixTestCase : XunitTestCase
    {
        private MatrixCase matrix;

        [Obsolete("Called by the de-serializer", true)]
        public MatrixTestCase()
        {
        }

        public MatrixTestCase(
            IMessageSink diagnosticMessageSink,
            TestMethodDisplay defaultMethodDisplay,
            TestMethodDisplayOptions defaultMethodDisplayOptions,
            ITestMethod testMethod,
            object[] testMethodArguments,
            MatrixCase matrix)
            : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
        {
            Apply(matrix);
        }

        public override void Serialize(IXunitSerializationInfo data)
        {
            base.Serialize(data);
            data.AddValue("matrix", matrix);
        }

        public override void Deserialize(IXunitSerializationInfo data)
        {
            base.Deserialize(data);
            Apply(data.GetValue<MatrixCase>("matrix"));
        }

        private void Apply(MatrixCase value)
        {
            matrix = value;
            Traits = value.BuildTraits();
            if (!string.IsNullOrEmpty(value.Skip))
            {
                SkipReason = value.Skip;
            }
        }
    }
}
