using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mediant.Abstractions;
using Mediant.Attributes;
using Mediant.Behaviors.Configuration;

namespace Mediant.Behaviors.Behaviors;

/// <summary>
/// Pipeline behavior that logs requests and responses with structured logging.
/// Automatically masks sensitive properties by name and attribute.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>, IBehaviorOrder
    where TRequest : IRequest<TResponse>
{
    public int Order => 200;
    private readonly ILogger<LoggingBehavior<TRequest, TResponse>> _logger;
    private readonly LoggingBehaviorOptions _options;

    public LoggingBehavior(
        ILogger<LoggingBehavior<TRequest, TResponse>> logger,
        IOptions<LoggingBehaviorOptions> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public async ValueTask<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return await next().ConfigureAwait(false);
        }

        var requestName = typeof(TRequest).Name;
        var serializedRequest = SafeSerialize(request);

        _logger.LogInformation("Handling {RequestName}: {RequestData}", requestName, serializedRequest);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next().ConfigureAwait(false);
            sw.Stop();

            _logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs}ms",
                requestName, sw.Elapsed.TotalMilliseconds);

            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Error handling {RequestName} after {ElapsedMs}ms",
                requestName, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    private static readonly JsonSerializerOptions SafeSerializerOptions = new()
    {
        MaxDepth = 32,
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
    };

    private string SafeSerialize(TRequest request)
    {
        try
        {
            if (request is null) return "null";

            var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
            var properties = typeof(TRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            for (int i = 0; i < properties.Length; i++)
            {
                var prop = properties[i];
                var name = prop.Name;

                if (ShouldMask(prop))
                {
                    dict[name] = "***";
                    continue;
                }

                try
                {
                    dict[name] = prop.GetValue(request);
                }
                catch
                {
                    dict[name] = "<error reading>";
                }
            }

            var json = JsonSerializer.Serialize(dict, SafeSerializerOptions);
            if (json.Length > _options.MaxSerializedLength)
            {
                return json[.._options.MaxSerializedLength] + "...(truncated)";
            }

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Request serialization failed for {RequestName}, logging will be incomplete", typeof(TRequest).Name);
            return "<serialization failed>";
        }
    }

    private bool ShouldMask(PropertyInfo prop)
    {
        if (prop.GetCustomAttribute<SensitiveDataAttribute>() is not null)
        {
            return true;
        }

        if (prop.GetCustomAttribute<AuditMaskAttribute>() is not null)
        {
            return true;
        }

        return _options.MaskProperties.Contains(prop.Name);
    }
}
