using System;
using System.Collections.Generic;

namespace SyntaX
{
    internal sealed class SpamCheckResult
    {
        public bool Allowed { get; private set; }
        public string Message { get; private set; }
        public int RetryAfterSeconds { get; private set; }
        public bool IsSpam { get; private set; }

        private SpamCheckResult(bool allowed, string message, int retryAfterSeconds, bool isSpam)
        {
            Allowed = allowed;
            Message = message;
            RetryAfterSeconds = retryAfterSeconds;
            IsSpam = isSpam;
        }

        public static SpamCheckResult Ok() => new SpamCheckResult(true, null, 0, false);

        public static SpamCheckResult Blocked(string message, int retrySeconds, bool isSpam = false)
            => new SpamCheckResult(false, message, retrySeconds, isSpam);
    }

    internal static class LoginCooldown
    {
        private static int _failCount;
        private static DateTime _cooldownUntilUtc = DateTime.MinValue;
        private static DateTime _lastAttemptUtc = DateTime.MinValue;
        private static readonly Queue<DateTime> _recentAttempts = new Queue<DateTime>();

        private const int BaseSeconds = 5;
        private const int MaxSeconds = 60;
        private const int MinIntervalSeconds = 2;
        private const int WindowSeconds = 30;
        private const int MaxAttemptsInWindow = 5;
        private const int SpamCooldownSeconds = 30;

        public static bool IsActive => DateTime.UtcNow < _cooldownUntilUtc;

        public static int RemainingSeconds
        {
            get
            {
                if (!IsActive) return 0;
                return Math.Max(1, (int)Math.Ceiling((_cooldownUntilUtc - DateTime.UtcNow).TotalSeconds));
            }
        }

        public static SpamCheckResult CheckBeforeAttempt()
        {
            if (IsActive)
            {
                return SpamCheckResult.Blocked(
                    "Please wait " + RemainingSeconds + "s before trying again.",
                    RemainingSeconds);
            }

            var now = DateTime.UtcNow;
            if (_lastAttemptUtc != DateTime.MinValue)
            {
                double sinceLast = (now - _lastAttemptUtc).TotalSeconds;
                if (sinceLast < MinIntervalSeconds)
                {
                    int wait = Math.Max(1, MinIntervalSeconds - (int)Math.Floor(sinceLast));
                    ApplyCooldown(wait);
                    return SpamCheckResult.Blocked("Slow down — too many clicks.", wait, isSpam: true);
                }
            }

            PruneWindow(now);
            if (_recentAttempts.Count >= MaxAttemptsInWindow)
            {
                ApplyCooldown(SpamCooldownSeconds);
                return SpamCheckResult.Blocked("Spam detected. Please wait.", SpamCooldownSeconds, isSpam: true);
            }

            return SpamCheckResult.Ok();
        }

        public static void RecordAttempt()
        {
            var now = DateTime.UtcNow;
            _lastAttemptUtc = now;
            _recentAttempts.Enqueue(now);
            PruneWindow(now);
        }

        public static void RecordFailure(int? serverRetrySeconds = null)
        {
            _failCount++;
            int seconds = serverRetrySeconds ?? Math.Min(MaxSeconds, BaseSeconds * _failCount);
            ApplyCooldown(seconds);
        }

        public static void ApplyServerCooldown(int seconds) => ApplyCooldown(seconds);

        public static void Reset()
        {
            _failCount = 0;
            _cooldownUntilUtc = DateTime.MinValue;
            _lastAttemptUtc = DateTime.MinValue;
            _recentAttempts.Clear();
        }

        private static void ApplyCooldown(int seconds)
        {
            if (seconds <= 0) return;
            var until = DateTime.UtcNow.AddSeconds(seconds);
            if (until > _cooldownUntilUtc)
                _cooldownUntilUtc = until;
        }

        private static void PruneWindow(DateTime now)
        {
            while (_recentAttempts.Count > 0 && (now - _recentAttempts.Peek()).TotalSeconds > WindowSeconds)
                _recentAttempts.Dequeue();
        }
    }
}
