namespace Galilego.Events
{
    /// <summary>
    /// Направление пересечения нуля event-функцией, на которое реагирует детектор.
    /// Falling — вход (в атмосферу, контакт с поверхностью), Rising — выход,
    /// Either — любое (например, пара детекторов на одной оболочке для входа и выхода).
    /// </summary>
    public enum EventDirection
    {
        Rising,
        Falling,
        Either
    }
}
