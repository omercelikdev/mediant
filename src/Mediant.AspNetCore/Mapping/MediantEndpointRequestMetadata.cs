namespace Mediant.AspNetCore.Mapping;

/// <summary>
/// Endpoint metadata stamped by the <c>[HttpEndpoint]</c> mapper: the request type behind
/// the generic dispatcher, and whether it binds from the body. Hosts use it to complete
/// the exported OpenAPI contract (query-bound request properties become documented
/// parameters — the dispatcher hides them from the framework's own inference).
/// </summary>
/// <param name="RequestType">The Mediant request (command/query) CLR type.</param>
/// <param name="BodyBound">Whether the request binds from the JSON body (POST/PUT/PATCH).</param>
public sealed record MediantEndpointRequestMetadata(Type RequestType, bool BodyBound);
