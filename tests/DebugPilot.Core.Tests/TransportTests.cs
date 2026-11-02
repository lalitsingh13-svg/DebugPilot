using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using DebugPilot.Core;
using Xunit;

namespace DebugPilot.Core.Tests
{
    public class TransportTests
    {
        private const string Context = "CS8602: possible null reference";
        private const string ValidJson = "{\"choices\":[{\"message\":{\"content\":\"{\\\"rootCause\\\":\\\"A possible null dereference.\\\",\\\"evidence\\\":[\\\"CS8602\\\"],\\\"suggestedActions\\\":[],\\\"confidence\\\":0.6}\"}}]}";

        private static AiProviderOptions Options(string? key = "k") => new AiProviderOptions
        {
            Kind = AiProviderKind.OpenAiCompatible,
            Endpoint = new Uri("https://api.example.com/v1"),
            Model = "m",
            ApiKey = key
        };

        private static AiConsent Consent() => new AiConsent(true, AiDiagnosisOrchestrator.ComputeSha256(Context));

        private static ResilientAiProvider Resilient(HttpMessageHandler handler, List<TimeSpan>? delays = null, int failureThreshold = 5)
        {
            var inner = new HttpAiProvider(new HttpClient(handler), Options());
            return new ResilientAiProvider(inner, maxRetries: 2, failureThreshold: failureThreshold, baseDelayMilliseconds: 10, maxJitterMilliseconds: 0,
                delay: (d, _) => { delays?.Add(d); return Task.CompletedTask; });
        }

        [Theory]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(404)]
        [InlineData(400)]
        public async Task Client_errors_are_not_retried(int status)
        {
            var handler = new SequenceHandler((HttpStatusCode)status);
            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => Resilient(handler).DiagnoseAsync(Context, Consent(), CancellationToken.None));
            Assert.Equal(status, ex.HttpStatus);
            Assert.False(ex.IsTransient);
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task Client_errors_do_not_open_the_circuit_breaker()
        {
            var handler = new SequenceHandler(HttpStatusCode.Unauthorized);
            var provider = Resilient(handler, failureThreshold: 1);
            for (int i = 0; i < 3; i++)
                await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.DiagnoseAsync(Context, Consent(), CancellationToken.None));
            Assert.Equal(3, handler.Calls); // a breaker would have blocked calls 2 and 3
        }

        [Fact]
        public async Task Service_unavailable_is_retried_then_succeeds()
        {
            var handler = new SequenceHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
            var result = await Resilient(handler).DiagnoseAsync(Context, Consent(), CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(2, handler.Calls);
        }

        [Fact]
        public async Task Rate_limit_honors_retry_after()
        {
            var delays = new List<TimeSpan>();
            var handler = new SequenceHandler(HttpStatusCode.TooManyRequests, HttpStatusCode.OK) { RetryAfterSeconds = 7 };
            var result = await Resilient(handler, delays).DiagnoseAsync(Context, Consent(), CancellationToken.None);
            Assert.NotNull(result);
            Assert.Equal(TimeSpan.FromSeconds(7), delays[0]);
        }

        [Fact]
        public async Task Excessive_retry_after_stops_retrying()
        {
            var handler = new SequenceHandler(HttpStatusCode.TooManyRequests) { RetryAfterSeconds = 3600 };
            await Assert.ThrowsAsync<InvalidOperationException>(() => Resilient(handler).DiagnoseAsync(Context, Consent(), CancellationToken.None));
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task Redirect_is_refused_and_not_followed()
        {
            var handler = new SequenceHandler(HttpStatusCode.Found);
            var provider = new HttpAiProvider(new HttpClient(handler), Options());
            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.DiagnoseAsync(Context, Consent(), CancellationToken.None));
            Assert.Contains("redirect", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, handler.Calls);
        }

        [Fact]
        public async Task Error_message_never_contains_the_response_body()
        {
            var handler = new SequenceHandler(HttpStatusCode.InternalServerError) { Body = "echoed prompt with SECRET_BODY_SENTINEL" };
            var provider = new HttpAiProvider(new HttpClient(handler), Options());
            var ex = await Assert.ThrowsAsync<AiProviderHttpException>(() => provider.DiagnoseAsync(Context, Consent(), CancellationToken.None));
            Assert.DoesNotContain("SECRET_BODY_SENTINEL", ex.Message);
        }

        [Fact]
        public async Task Provider_can_be_created_on_an_already_used_http_client()
        {
            var handler = new SequenceHandler(HttpStatusCode.OK);
            var client = new HttpClient(handler);
            await client.GetAsync("https://warmup.example.com/");
            var provider = new HttpAiProvider(client, Options()); // used to throw InvalidOperationException
            var result = await provider.DiagnoseAsync(Context, Consent(), CancellationToken.None);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task Slow_provider_times_out_with_a_clear_error()
        {
            var options = Options();
            options.TimeoutSeconds = 1;
            var provider = new HttpAiProvider(new HttpClient(new HangingHandler()), options);
            await Assert.ThrowsAsync<TimeoutException>(() => provider.DiagnoseAsync(Context, Consent(), CancellationToken.None));
        }

        [Fact]
        public async Task Caller_cancellation_is_not_reported_as_timeout()
        {
            var provider = new HttpAiProvider(new HttpClient(new HangingHandler()), Options());
            using (var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)))
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.DiagnoseAsync(Context, Consent(), source.Token));
        }

        [Fact]
        public void Create_http_client_returns_a_usable_client()
        {
            using (var client = HttpAiProvider.CreateHttpClient()) Assert.NotNull(client);
        }

        private sealed class SequenceHandler : HttpMessageHandler
        {
            private readonly Queue<HttpStatusCode> _statuses;
            private HttpStatusCode _last;
            public SequenceHandler(params HttpStatusCode[] statuses) { _statuses = new Queue<HttpStatusCode>(statuses); _last = statuses[statuses.Length - 1]; }
            public int Calls { get; private set; }
            public int? RetryAfterSeconds { get; set; }
            public string? Body { get; set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                var status = _statuses.Count > 0 ? _statuses.Dequeue() : _last;
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent(status == HttpStatusCode.OK ? ValidJson : (Body ?? "error"), System.Text.Encoding.UTF8, "application/json")
                };
                if (RetryAfterSeconds.HasValue) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(RetryAfterSeconds.Value));
                return Task.FromResult(response);
            }
        }

        private sealed class HangingHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }
        }
    }
}
