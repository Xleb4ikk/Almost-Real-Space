namespace Galilego.Tablet
{
    /// <summary>
    /// Global "navigation tablet overlay is open" flag. IMGUI HUDs (hotbar, flight
    /// readout, cheat/perf text) are always drawn above UI Toolkit, so they must
    /// stand down while the navigation interface is on screen.
    /// </summary>
    public static class NavOverlayState
    {
        public static bool Open;
    }
}
