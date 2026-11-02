using System;
using System.Net.Http;

namespace DebugPilot.Core
{
    /// <summary>
    /// The provider answered with a non-success HTTP status. The response BODY is intentionally never captured
    /// (providers can echo prompts or secrets). Only transient statuses are worth retrying.
    /// </summary>
    public sealed class AiProviderHttpException : HttpRequestException
    {
        public AiProviderHttpException(int statusCode, TimeSpan? retryAfter = null) : base(Describe(statusCode))
        {
            HttpStatus = statusCode;
            RetryAfter = retryAfter;
        }

        public int HttpStatus { get; }
        public TimeSpan? RetryAfter { get; }

        /// <summary>408, 429 and 5xx may succeed on retry; 400/401/403/404 and redirects will not.</summary>
        public bool IsTransient => HttpStatus == 408 || HttpStatus == 429 || (HttpStatus >= 500 && HttpStatus <= 599);

        private static string Describe(int status)
        {
            if (status == 401 || status == 403) return "The AI provider rejected the credentials (HTTP " + status + "). Check the API key and endpoint.";
            if (status == 404) return "The AI provider endpoint, model or deployment was not found (HTTP 404). Check the settings.";
            if (status == 429) return "The AI provider rate limit was reached (HTTP 429).";
            if (status >= 300 && status < 400) return "The AI provider answered with a redirect (HTTP " + status + "), which is refused to protect your API key. Use the final endpoint URL.";
            return "The AI provider request failed with HTTP status " + status + ".";
        }
    }

    /// <summary>The provider replied, but the structured answer failed schema or evidence validation.</summary>
    public sealed class AiResponseRejectedException : InvalidOperationException
    {
        public AiResponseRejectedException(string message) : base(message) { }
    }
}
