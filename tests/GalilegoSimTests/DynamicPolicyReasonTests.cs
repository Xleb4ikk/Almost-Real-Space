using Galilego.Spacecraft;
using Xunit;

namespace GalilegoPhysicsTests
{
    /// <summary>
    /// D3: additive observability. Every row of DECISION_TABLE.md gets an explicit
    /// reason, and SelectWithReason must return exactly the same integrator as the
    /// unchanged Select (the cases are the D2/T1 table itself).
    /// </summary>
    public sealed class DynamicPolicyReasonTests
    {
        [Fact]
        public void ReasonMatchesDecisionTableRowAndSelectIsUnchanged()
        {
            int checkedCases = 0;
            foreach (object[] row in DynamicPolicySelectTests.Cases())
            {
                string name = (string)row[0];
                var context = new IntegratorContext(
                    (double)row[1],
                    (bool)row[2],
                    (bool)row[3],
                    (bool)row[4],
                    (bool)row[5],
                    (double)row[6],
                    (double)row[7],
                    (double)row[8],
                    (double)row[9],
                    (bool)row[10]);
                SpacecraftIntegrator expected = (SpacecraftIntegrator)row[11];

                IntegratorSelectionReason expectedReason;
                if (expected == SpacecraftIntegrator.WhBm14a)
                {
                    expectedReason = IntegratorSelectionReason.PortedLongWarpCoast;
                }
                else if (expected == SpacecraftIntegrator.Dopri5)
                {
                    expectedReason = name.StartsWith("R2", System.StringComparison.Ordinal)
                        ? IntegratorSelectionReason.SurfaceFlight
                        : IntegratorSelectionReason.SunCoast;
                }
                else
                {
                    expectedReason = IntegratorSelectionReason.Default;
                }

                SpacecraftIntegrator selected = IntegratorPolicy.SelectWithReason(
                    context, out IntegratorSelectionReason reason);
                Assert.True(expected == selected,
                    name + ": SelectWithReason returned " + selected + ", expected " + expected);
                Assert.True(expectedReason == reason,
                    name + ": reason " + reason + ", expected " + expectedReason);
                Assert.Equal(IntegratorPolicy.Select(context), selected);
                checkedCases++;
            }

            Assert.True(checkedCases == 35, "expected 35 decision-table cases, got " + checkedCases);
        }
    }
}
