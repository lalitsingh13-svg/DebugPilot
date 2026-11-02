using System;
using System.Threading;

namespace DebugPilot.Core
{
    /// <summary>In-memory per-session request cap. The host should display RequestsUsed/MaxRequests to the user.</summary>
    public sealed class AiUsageGuard
    {
        private readonly int _maxRequests;
        private int _requestsUsed;

        public AiUsageGuard(int maxRequests = 5)
        {
            if (maxRequests < 1 || maxRequests > 1000) throw new ArgumentOutOfRangeException(nameof(maxRequests));
            _maxRequests = maxRequests;
        }

        public int MaxRequests => _maxRequests;
        public int RequestsUsed => Volatile.Read(ref _requestsUsed);

        public bool TryAcquire()
        {
            while (true)
            {
                var current = Volatile.Read(ref _requestsUsed);
                if (current >= _maxRequests) return false;
                if (Interlocked.CompareExchange(ref _requestsUsed, current + 1, current) == current) return true;
            }
        }
    }
}
