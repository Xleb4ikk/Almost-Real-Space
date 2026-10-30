using System;
using Xunit;

namespace GalilegoPhysicsTests.Support
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SlowFactAttribute : FactAttribute
    {
        public SlowFactAttribute()
        {
            if (!Env.SlowEnabled)
            {
                Skip = Env.SlowSkipReason;
            }
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SlowTheoryAttribute : TheoryAttribute
    {
        public SlowTheoryAttribute()
        {
            if (!Env.SlowEnabled)
            {
                Skip = Env.SlowSkipReason;
            }
        }
    }
}
