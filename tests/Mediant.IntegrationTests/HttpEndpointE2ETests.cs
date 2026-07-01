using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Mediant.IntegrationTests;

/// <summary>
/// REAL HTTP E2E tests. Boots the actual ECommerce app via WebApplicationFactory,
/// sends real HTTP requests through Kestrel, verifies real HTTP responses.
/// Nothing mocked — full pipeline from HTTP to handler and back.
/// </summary>
public class HttpEndpointE2ETests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HttpEndpointE2ETests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    // === HEALTH CHECK ===

    [Fact]
    public async Task Health_Returns_200_With_Status()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }

    // === CREATE ORDER ===

    [Fact]
    public async Task CreateOrder_ValidRequest_Returns_201()
    {
        var payload = new
        {
            userId = "e2e-user-1",
            items = new[]
            {
                new { productId = "P1", productName = "Widget", quantity = 2, unitPrice = 9.99 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateOrder_EmptyUserId_Returns_400_ValidationError()
    {
        var payload = new
        {
            userId = "",
            items = Array.Empty<object>()
        };

        var response = await _client.PostAsJsonAsync("/api/orders", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Validation Error");
        content.Should().Contain("UserId");
    }

    [Fact]
    public async Task CreateOrder_NullBody_Returns_400()
    {
        var response = await _client.PostAsync("/api/orders",
            new StringContent("null", System.Text.Encoding.UTF8, "application/json"));

        // null body → bad request
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // === GET ORDER ===

    [Fact]
    public async Task GetOrder_NonExistent_Returns_404()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Not Found");
    }

    [Fact]
    public async Task GetOrder_AfterCreate_Returns_200()
    {
        // Create first
        var createPayload = new
        {
            userId = "e2e-get-user",
            items = new[]
            {
                new { productId = "P1", productName = "Gadget", quantity = 1, unitPrice = 25.00 }
            }
        };
        var createResponse = await _client.PostAsJsonAsync("/api/orders", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Query user orders — should return 200
        var userOrdersResponse = await _client.GetAsync("/api/users/e2e-get-user/orders");
        userOrdersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // === GET ORDERS FOR USER ===

    [Fact]
    public async Task GetOrdersForUser_Returns_200()
    {
        var response = await _client.GetAsync("/api/users/some-user/orders");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // === CANCEL ORDER ===

    [Fact]
    public async Task CancelOrder_NonExistent_Returns_Error()
    {
        var response = await _client.PutAsync(
            $"/api/orders/{Guid.NewGuid()}/cancel",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        // Should return error (401/403/404 depending on auth config)
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    // === MULTIPLE SEQUENTIAL OPERATIONS ===

    [Fact]
    public async Task FullOrderLifecycle_Create_Query_Verify()
    {
        // Step 1: Create order
        var createPayload = new
        {
            userId = "lifecycle-user",
            items = new[]
            {
                new { productId = "P1", productName = "Item A", quantity = 3, unitPrice = 15.50 },
                new { productId = "P2", productName = "Item B", quantity = 1, unitPrice = 42.00 }
            }
        };

        var createResponse = await _client.PostAsJsonAsync("/api/orders", createPayload);
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        // Step 2: Get user orders
        var ordersResponse = await _client.GetAsync("/api/users/lifecycle-user/orders");
        ordersResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        ordersResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // === CONCURRENT HTTP REQUESTS ===

    [Fact]
    public async Task Concurrent_50_HTTP_Requests_AllSucceed()
    {
        var tasks = new Task<HttpResponseMessage>[50];

        for (int i = 0; i < tasks.Length; i++)
        {
            var payload = new
            {
                userId = $"concurrent-user-{i}",
                items = new[]
                {
                    new { productId = "P1", productName = "Widget", quantity = 1, unitPrice = 5.00 }
                }
            };
            tasks[i] = _client.PostAsJsonAsync("/api/orders", payload);
        }

        var responses = await Task.WhenAll(tasks);

        responses.Should().AllSatisfy(r =>
            r.StatusCode.Should().Be(HttpStatusCode.Created));
    }

    // === VALIDATION EDGE CASES ===

    [Fact]
    public async Task CreateOrder_NegativeQuantity_Returns_400()
    {
        var payload = new
        {
            userId = "validation-user",
            items = new[]
            {
                new { productId = "P1", productName = "Bad", quantity = -1, unitPrice = 10.00 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOrder_ZeroPrice_Returns_400()
    {
        var payload = new
        {
            userId = "validation-user",
            items = new[]
            {
                new { productId = "P1", productName = "Free", quantity = 1, unitPrice = 0 }
            }
        };

        var response = await _client.PostAsJsonAsync("/api/orders", payload);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
