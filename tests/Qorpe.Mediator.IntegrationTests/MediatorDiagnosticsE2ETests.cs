using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Qorpe.Mediator.Diagnostics;

namespace Qorpe.Mediator.IntegrationTests;

/// <summary>
/// REAL HTTP E2E: boots the ECommerce app, attaches an <see cref="ActivityListener"/> to the
/// mediator's <see cref="ActivitySource"/>, drives a real HTTP request, and asserts a
/// <c>mediator.send</c> span was produced — proving OpenTelemetry instrumentation flows through
/// the whole HTTP → endpoint → mediator → handler chain.
/// </summary>
public class MediatorDiagnosticsE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public MediatorDiagnosticsE2ETests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Real_Http_Request_Produces_Mediator_Send_Span()
    {
        var sendSpans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == MediatorDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName.StartsWith("mediator.send", StringComparison.Ordinal))
                {
                    lock (sendSpans) { sendSpans.Add(a); }
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var client = _factory.CreateClient();
        var payload = new
        {
            userId = "otel-e2e-user",
            items = new[] { new { productId = "P1", productName = "Widget", quantity = 1, unitPrice = 5.0 } },
        };

        var response = await client.PostAsJsonAsync("/api/orders", payload);
        response.EnsureSuccessStatusCode();

        // At least one successful mediator.send span must have been recorded for the request that
        // served this HTTP call (other parallel tests may add more — hence >= 1, not exactly 1).
        List<Activity> snapshot;
        lock (sendSpans) { snapshot = sendSpans.ToList(); }

        snapshot.Should().Contain(a => a.Status == ActivityStatusCode.Ok,
            "the HTTP request must flow through the instrumented mediator and record a span");
    }
}
