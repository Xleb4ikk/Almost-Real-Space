namespace Galilego.Events
{
    /// <summary>
    /// Direction of the event function's zero crossing the detector reacts to.
    /// Falling — entry (into atmosphere, surface contact), Rising — exit,
    /// Either — any (e.g. a pair of detectors on one shell for entry and exit).
    /// </summary>
    public enum EventDirection
    {
        Rising,
        Falling,
        Either
    }
}
