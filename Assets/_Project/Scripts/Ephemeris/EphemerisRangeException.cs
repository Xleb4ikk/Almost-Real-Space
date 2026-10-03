using System;

namespace Galilego.Universe
{
    [Serializable]
    public sealed class EphemerisRangeException : InvalidOperationException
    {
        public double TimeSeconds;
        public string BodyName;

        public EphemerisRangeException(string message, double timeSeconds, string bodyName)
            : base(message)
        {
            TimeSeconds = timeSeconds;
            BodyName = bodyName;
        }
    }
}
