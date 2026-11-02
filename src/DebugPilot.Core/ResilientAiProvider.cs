using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DebugPilot.Core
{
    /// <summary>
    /// Adds bounded retries and a per-instance circuit breaker to an AI provider.
    /// It retries transient transport/time-out failures and rejected structured responses only.
    /// No retry is performed when the caller cancels the operation.
    /// </summary>
    public sealed class ResilientAiProvider : IAiProvider
    {
        private readonly IAiProvider _inner;
        private readonly int _maxRetries;
        private readonly int _failureThreshold;
        private readonly TimeSpan _breakDuration;
        private readonly int _baseDelayMilliseconds;
        private readonly int _maxJitterMilliseconds;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);
        private readonly object _sync = new object();
        private int _consecutiveFailures;
        private DateTime _openUntilUtc = DateTime.MinValue;
        private readonly Random _random = new Random();

        public ResilientAiProvider(
            IAiProvider inner,
            int maxRetries = 2,
            int failureThreshold = 5,
            TimeSpan? breakDuration = null,
            int baseDelayMilliseconds = 250,
            int maxJitterMilliseconds = 150,
            Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (maxRetries < 0 || maxRetries > 5) throw new ArgumentOutOfRangeException(nameof(maxRetries));
            if (failureThreshold < 1 || failureThreshold > 20) throw new ArgumentOutOfRangeException(nameof(failureThreshold));
            if (baseDelayMilliseconds < 0 || baseDelayMilliseconds > 30000) throw new ArgumentOutOfRangeException(nameof(baseDelayMilliseconds));
            if (maxJitterMilliseconds < 0 || maxJitterMilliseconds > 5000) throw new ArgumentOutOfRangeException(nameof(maxJitterMilliseconds));
            _maxRetries = maxRetries;
            _failureThreshold = failureThreshold;
            _breakDuration = breakDuration ?? TimeSpan.FromSeconds(30);
            if (_breakDuration <= TimeSpan.Zero || _breakDuration > TimeSpan.FromMinutes(10)) throw new ArgumentOutOfRangeException(nameof(breakDuration));
            _baseDelayMilliseconds = baseDelayMilliseconds;
            _maxJitterMilliseconds = maxJitterMilliseconds;
            _delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        }

        public string Name => _inner.Name;

        public async Task<AiDiagnosisResult?> DiagnoseAsync(string approvedRedactedContext, AiConsent consent, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(approvedRedactedContext)) throw new ArgumentException("Approved context is required.", nameof(approvedRedactedContext));
            if (consent == null || !consent.UserApproved) throw new InvalidOperationException("Explicit user consent is required before sending context to an AI provider.");
            if (!string.Equals(AiDiagnosisOrchestrator.ComputeSha256(approvedRedactedContext), consent.ApprovedContextSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The context differs from the context approved by the user.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCircuitClosed();

            Exception? lastFailure = null;
            for (var attempt = 0; attempt <= _maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await _inner.DiagnoseAsync(approvedRedactedContext, consent, cancellationToken).ConfigureAwait(false);
                    if (result == null)
                        throw new AiResponseRejectedException("The AI response was rejected: The provider returned no diagnosis.");
                    if (!AiResponseValidator.TryValidate(result, approvedRedactedContext, out var reason))
                        throw new AiResponseRejectedException("The AI response was rejected: " + reason);
                    lock (_sync) { _consecutiveFailures = 0; _openUntilUtc = DateTime.MinValue; }
                    return result;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (IsRetryable(ex))
                {
                    lastFailure = ex;
                    if (attempt >= _maxRetries) break;
                    var exponential = _baseDelayMilliseconds == 0 ? 0 : (int)Math.Min(30000L, (long)_baseDelayMilliseconds * (1L << attempt));
                    int jitter;
                    lock (_sync) jitter = _maxJitterMilliseconds == 0 ? 0 : _random.Next(0, _maxJitterMilliseconds + 1);
                    var wait = TimeSpan.FromMilliseconds(exponential + jitter);
                    var retryAfter = (ex as AiProviderHttpException)?.RetryAfter;
                    if (retryAfter.HasValue)
                    {
                        // Honor the provider's Retry-After, but never block the user for more than the cap.
                        if (retryAfter.Value > MaxRetryAfter) break;
                        if (retryAfter.Value > wait) wait = retryAfter.Value;
                    }
                    await _delay(wait, cancellationToken).ConfigureAwait(false);
                }
            }

            lock (_sync)
            {
                _consecutiveFailures++;
                if (_consecutiveFailures >= _failureThreshold)
                    _openUntilUtc = DateTime.UtcNow.Add(_breakDuration);
            }
            var detail = lastFailure is AiProviderHttpException http ? " Last error: HTTP " + http.HttpStatus + "." : string.Empty;
            throw new InvalidOperationException("AI diagnosis failed after bounded retries." + detail + " No fix was applied.", lastFailure);
        }

        private void EnsureCircuitClosed()
        {
            lock (_sync)
            {
                if (_openUntilUtc > DateTime.UtcNow)
                    throw new InvalidOperationException("AI provider circuit breaker is open after repeated failures. Try again after the cooldown.");
                if (_openUntilUtc != DateTime.MinValue && _openUntilUtc <= DateTime.UtcNow)
                {
                    // Cooldown elapsed; permit a recovery attempt and reset the failure window.
                    _openUntilUtc = DateTime.MinValue;
                    _consecutiveFailures = 0;
                }
            }
        }

        /// <summary>
        /// Only failures that can plausibly succeed on retry. Credential/endpoint errors (400/401/403/404), redirects and
        /// oversized responses are NOT retried and do NOT count toward the circuit breaker, because they are
        /// configuration problems, not provider outages.
        /// </summary>
        private static bool IsRetryable(Exception exception)
        {
            if (exception is AiProviderHttpException http) return http.IsTransient;
            return exception is HttpRequestException || exception is TimeoutException ||
                   exception is JsonException || exception is AiResponseRejectedException ||
                   exception is TaskCanceledException;
        }
    }
}
