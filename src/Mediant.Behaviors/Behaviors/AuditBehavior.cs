using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Attributes;
using Mediant.Audit;
using Mediant.Behaviors.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that captures audit entries for requests.
/// Logs EVERYTHING first — even auth failures. Persistence goes through the
/// <see cref="IAuditStore"/> abstraction, which owns any buffering/batching strategy.
/// </summary>
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 100;

    // Cached attribute lookup — runs once per closed generic type (per TRequest), not per request
    private static readonly AuditableAttribute? CachedAttribute =
        typeof(TRequest).GetCustomAttributes(typeof(AuditableAttribute), true)
            .Cast<AuditableAttribute>()
            .FirstOrDefault();

    // Cached type checks
    private static readonly bool IsCommandType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
    private static readonly bool IsQueryType = typeof(TRequest).GetInterfaces()
        .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

    private readonly IAuditStore _auditStore;
    private readonly IAuditUserContext? _userContext;
    private readonly ILogger<AuditBehavior<TRequest, TResponse>> _logger;
    private readonly AuditBehaviorOptions _options;

    public AuditBehavior(
        IAuditStore auditStore,
        ILogger<AuditBehavior<TRequest, TResponse>> logger,
        IOptions<AuditBehaviorOptions> options,
        IAuditUserContext? userContext = null)
    {
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _userContext = userContext;
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        if (!ShouldAudit())
        {
            return await next().ConfigureAwait(false);
        }

        var entry = new AuditEntry
        {
            RequestType = typeof(TRequest).FullName ?? typeof(TRequest).Name,
            UserId = _userContext?.GetUserId() ?? "SYSTEM",
            UserName = _userContext?.GetUserName(),
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            Timestamp = DateTimeOffset.UtcNow
        };

        // Enrich with IAuditableRequest metadata if implemented
        if (request is IAuditableRequest auditableRequest)
        {
            entry.ActionName = auditableRequest.ActionName;
            entry.EntityType = auditableRequest.EntityType;
            entry.EntityId = auditableRequest.EntityId;

            if (auditableRequest.AuditMetadata is not null)
            {
                try
                {
                    var metadataJson = JsonSerializer.Serialize(auditableRequest.AuditMetadata);
                    entry.Metadata["AuditMetadata"] = metadataJson;
                }
                catch
                {
                    // Best-effort metadata serialization
                }
            }
        }

        if (CachedAttribute?.IncludeRequestBody != false)
        {
            entry.RequestData = SafeSerializeRequest(request);
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            sw.Stop();

            entry.IsSuccess = true;
            entry.DurationMs = sw.Elapsed.TotalMilliseconds;

            if (CachedAttribute?.IncludeResponseBody == true)
            {
                entry.ResponseData = SafeSerializeResponse(response, CachedAttribute.MaxResponseSize);
            }

            await SafeSaveAuditEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            entry.IsSuccess = false;
            entry.DurationMs = sw.Elapsed.TotalMilliseconds;
            entry.ErrorMessage = ex.Message;
            entry.ExceptionType = ex.GetType().FullName;

            await SafeSaveAuditEntryAsync(entry, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    // Cached check for IAuditableRequest implementation
    private static readonly bool ImplementsIAuditableRequest =
        typeof(IAuditableRequest).IsAssignableFrom(typeof(TRequest));

    private bool ShouldAudit()
    {
        if (IsCommandType && _options.AuditCommands) return true;
        if (IsQueryType && _options.AuditQueries) return true;

        // Audit if [Auditable] attribute or IAuditableRequest interface is present
        return CachedAttribute is not null || ImplementsIAuditableRequest;
    }

    private string SafeSerializeRequest(TRequest request)
    {
        try
        {
            if (request is null) return "null";

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            var properties = typeof(TRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < properties.Length; i++)
            {
                var prop = properties[i];
                if (ShouldMask(prop))
                {
                    dict[prop.Name] = "***";
                    continue;
                }

                try
                {
                    dict[prop.Name] = prop.GetValue(request);
                }
                catch
                {
                    dict[prop.Name] = "<error>";
                }
            }

            return JsonSerializer.Serialize(dict);
        }
        catch
        {
            return "<serialization failed>";
        }
    }

    private static string SafeSerializeResponse(TResponse? response, int maxSize)
    {
        try
        {
            if (response is null) return "null";
            var json = JsonSerializer.Serialize(response);
            if (json.Length > maxSize)
            {
                return json[..maxSize] + "...(truncated)";
            }

            return json;
        }
        catch
        {
            return "<serialization failed>";
        }
    }

    private bool ShouldMask(PropertyInfo prop)
    {
        if (prop.GetCustomAttribute<SensitiveDataAttribute>() is not null) return true;
        if (prop.GetCustomAttribute<AuditMaskAttribute>() is not null) return true;
        return _options.SensitivePatterns.Contains(prop.Name);
    }

    private async ValueTask SafeSaveAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await _auditStore.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save audit entry for {RequestType}", entry.RequestType);

            if (_options.FallbackToConsole)
            {
                try
                {
                    Console.Error.WriteLine(
                        $"[AUDIT FALLBACK] {entry.Timestamp:O} | {entry.UserId} | {entry.RequestType} | " +
                        $"Success={entry.IsSuccess} | Duration={entry.DurationMs}ms | Error={entry.ErrorMessage}");
                }
                catch
                {
                    // Never fail the request due to audit
                }
            }
        }
    }
}
