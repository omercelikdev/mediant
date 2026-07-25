using Mediant.AspNetCore.Mapping;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mediant.IntegrationTests;

/// <summary>
/// The OpenAPI request-side enrichment: body-bound endpoints carry Accepts metadata and
/// every mapped endpoint stamps its request type — the exported contract's request half
/// depends on both (the generic dispatcher hides them from framework inference).
/// </summary>
public class EndpointRequestMetadataTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EndpointRequestMetadataTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private List<RouteEndpoint> Endpoints()
        => _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

    [Fact]
    public void Every_mapped_request_stamps_its_type_metadata()
    {
        var stamped = Endpoints()
            .Select(e => e.Metadata.GetMetadata<MediantEndpointRequestMetadata>())
            .Where(m => m is not null)
            .ToList();

        Assert.NotEmpty(stamped);
        Assert.All(stamped, m => Assert.NotNull(m!.RequestType));
    }

    [Fact]
    public void Body_bound_endpoints_declare_their_request_schema()
    {
        var posts = Endpoints()
            .Where(e => e.Metadata.GetMetadata<MediantEndpointRequestMetadata>() is { BodyBound: true })
            .ToList();

        Assert.NotEmpty(posts);
        Assert.All(posts, e =>
        {
            var accepts = e.Metadata.GetMetadata<IAcceptsMetadata>();
            Assert.NotNull(accepts);
            Assert.Equal(e.Metadata.GetMetadata<MediantEndpointRequestMetadata>()!.RequestType, accepts!.RequestType);
        });
    }

    [Fact]
    public void Query_bound_endpoints_stamp_the_type_without_a_body_claim()
    {
        var gets = Endpoints()
            .Where(e => e.Metadata.GetMetadata<MediantEndpointRequestMetadata>() is { BodyBound: false })
            .ToList();

        Assert.NotEmpty(gets);
        Assert.All(gets, e => Assert.Null(e.Metadata.GetMetadata<IAcceptsMetadata>()));
    }
}
