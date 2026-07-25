using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Mediant.Abstractions;
using Mediant.AspNetCore.Attributes;
using Mediant.Results;

namespace Mediant.AspNetCore.Mapping;

/// <summary>
/// Discovers [HttpEndpoint] attributes and maps them to Minimal API endpoints.
/// <para>
/// This uses runtime reflection (assembly scanning, model binding, generic dispatch) and is
/// therefore not compatible with trimming/Native AOT. Map endpoints manually if you publish AOT.
/// </para>
/// </summary>
public static class EndpointMapper
{
    /// <summary>
    /// Maps all discovered HttpEndpoint attributes to Minimal API endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    [RequiresUnreferencedCode("Endpoint mapping scans assemblies and binds models via reflection; not trim-compatible.")]
    [RequiresDynamicCode("Endpoint mapping uses MakeGenericMethod; not Native AOT-compatible.")]
    public static IEndpointRouteBuilder MapMediantEndpoints(
        this IEndpointRouteBuilder app,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(assemblies);

        var discoveredEndpoints = DiscoverEndpoints(assemblies);
        var routeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in discoveredEndpoints)
        {
            // Duplicate detection on the exact method + route. Parameter-name/constraint ambiguity
            // (e.g. {id:int} vs {id:alpha}) is left to ASP.NET routing, which models it correctly;
            // collapsing parameters here produced false positives.
            var routeKey = $"{endpoint.Attribute.Method}:{endpoint.Attribute.Route}";
            if (!routeSet.Add(routeKey))
            {
                throw new InvalidOperationException(
                    $"Duplicate route '{endpoint.Attribute.Method} {endpoint.Attribute.Route}' found on type '{endpoint.RequestType.Name}'. " +
                    "Each HTTP method + route combination must be unique.");
            }

            // Validate: HttpEndpoint on INotification is not allowed
            if (typeof(INotification).IsAssignableFrom(endpoint.RequestType))
            {
                throw new InvalidOperationException(
                    $"[HttpEndpoint] cannot be applied to notification type '{endpoint.RequestType.Name}'. " +
                    "Only commands and queries can be mapped to HTTP endpoints.");
            }

            MapEndpoint(app, endpoint);
        }

        return app;
    }

    private static List<EndpointDescriptor> DiscoverEndpoints(Assembly[] assemblies)
    {
        var result = new List<EndpointDescriptor>();

        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try
            {
                types = assemblies[i].GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).ToArray()!;
            }

            for (int j = 0; j < types.Length; j++)
            {
                var type = types[j];
                var attr = type.GetCustomAttribute<HttpEndpointAttribute>();
                if (attr is null) continue;

                result.Add(new EndpointDescriptor(type, attr));
            }
        }

        return result;
    }

    private static void MapEndpoint(IEndpointRouteBuilder app, EndpointDescriptor descriptor)
    {
        var requestType = descriptor.RequestType;
        var attr = descriptor.Attribute;

        // Find the response type from IRequest<TResponse>
        var requestInterface = requestType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>));

        if (requestInterface is null)
        {
            throw new InvalidOperationException(
                $"Type '{requestType.Name}' has [HttpEndpoint] but does not implement IRequest<TResponse>.");
        }

        var responseType = requestInterface.GetGenericArguments()[0];
        var isCommand = requestType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

        var defaultSuccessCode = attr.SuccessStatusCode > 0
            ? attr.SuccessStatusCode
            : (isCommand && attr.Method == "POST" ? StatusCodes.Status201Created : StatusCodes.Status200OK);

        var routeBuilder = attr.Method switch
        {
            "GET" => app.MapGet(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "POST" => app.MapPost(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "PUT" => app.MapPut(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "DELETE" => app.MapDelete(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            "PATCH" => app.MapPatch(attr.Route, CreateHandler(requestType, responseType, defaultSuccessCode)),
            _ => throw new InvalidOperationException($"Unsupported HTTP method '{attr.Method}' on type '{requestType.Name}'.")
        };

        // Apply metadata
        if (attr.Tags is { Length: > 0 })
        {
            routeBuilder.WithTags(attr.Tags);
        }

        if (attr.Summary is not null)
        {
            routeBuilder.WithSummary(attr.Summary);
        }

        if (attr.Description is not null)
        {
            routeBuilder.WithDescription(attr.Description);
        }

        // Endpoint names must be unique across the app; the short type name collides for
        // same-named types in different namespaces, so use the full name.
        routeBuilder.WithName(requestType.FullName ?? requestType.Name);

        // OpenAPI schema enrichment — add Produces metadata for Result-based responses
        if (responseType == typeof(Result))
        {
            routeBuilder.Produces(defaultSuccessCode);
        }
        else if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            // Invoke a generic helper we own, rather than binding an overload of a framework
            // extension by parameter count (which is fragile across framework versions).
            typeof(EndpointMapper)
                .GetMethod(nameof(AddProducesTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(valueType)
                .Invoke(null, new object[] { routeBuilder, defaultSuccessCode });
        }

        // OpenAPI request enrichment: body-bound verbs declare their request schema
        // (Accepts), and EVERY endpoint stashes its request type as metadata so a host
        // can surface query-bound properties as documented parameters — the exported
        // contract must carry the request side, not only the responses.
        var bodyBound = attr.Method is "POST" or "PUT" or "PATCH";
        if (bodyBound)
        {
            typeof(EndpointMapper)
                .GetMethod(nameof(AddAcceptsTyped), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(requestType)
                .Invoke(null, new object[] { routeBuilder });
        }

        routeBuilder.WithMetadata(new MediantEndpointRequestMetadata(requestType, bodyBound));

        // Standard error responses for all endpoints
        routeBuilder.ProducesProblem(StatusCodes.Status400BadRequest)
                    .ProducesProblem(StatusCodes.Status500InternalServerError);
    }

    // A typed invoker that sends the request and maps the response to an IResult with NO
    // per-request reflection. One is built per request type at registration time.
    private delegate Task<IResult> EndpointInvoker(ISender sender, object request, int successStatusCode, CancellationToken ct);

    private static readonly ConcurrentDictionary<Type, EndpointInvoker> InvokerCache = new();
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static readonly ConcurrentDictionary<Type, ConstructorInfo?> ParameterlessCtorCache = new();
    private static readonly ConcurrentDictionary<Type, ConstructorInfo?> BindingCtorCache = new();

    private static Delegate CreateHandler(Type requestType, Type responseType, int successStatusCode)
    {
        var invoker = InvokerCache.GetOrAdd(requestType, static (_, respType) => BuildInvoker(respType), responseType);

        return async (HttpContext context, ISender sender) =>
        {
            object? request;
            var isGet = HttpMethods.IsGet(context.Request.Method);

            if (isGet)
            {
                // GET binds from query + route in one pass and surfaces conversion errors.
                var (boundRequest, bindingErrors) = BindFromQueryAndRoute(context, requestType);
                if (bindingErrors.Count > 0)
                {
                    return BindingErrorResult(bindingErrors);
                }
                request = boundRequest!;
            }
            else
            {
                request = await context.Request.ReadFromJsonAsync(requestType, context.RequestAborted);
                if (request is null)
                {
                    return Microsoft.AspNetCore.Http.Results.BadRequest(new { error = "Request body cannot be null." });
                }

                // Overlay route parameters (e.g. PUT /orders/{id}) and surface conversion errors
                // instead of silently operating on a default value.
                var routeErrors = BindRouteParameters(context, request, requestType);
                if (routeErrors.Count > 0)
                {
                    return BindingErrorResult(routeErrors);
                }
            }

            return await invoker(sender, request!, successStatusCode, context.RequestAborted);
        };
    }

    private static IResult BindingErrorResult(List<string> errors)
        => Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid Request Parameters",
            detail: string.Join("; ", errors));

    private static EndpointInvoker BuildInvoker(Type responseType)
    {
        if (responseType == typeof(Result))
        {
            return InvokeResult;
        }

        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = responseType.GetGenericArguments()[0];
            var method = typeof(EndpointMapper)
                .GetMethod(nameof(InvokeResultOfT), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(valueType);
            return (EndpointInvoker)Delegate.CreateDelegate(typeof(EndpointInvoker), method);
        }

        var plain = typeof(EndpointMapper)
            .GetMethod(nameof(InvokePlain), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(responseType);
        return (EndpointInvoker)Delegate.CreateDelegate(typeof(EndpointInvoker), plain);
    }

    private static async Task<IResult> InvokeResult(ISender sender, object request, int successStatusCode, CancellationToken ct)
    {
        var result = await sender.Send((IRequest<Result>)request, ct).ConfigureAwait(false);
        return ResultToActionResultMapper.ToHttpResult(result, successStatusCode);
    }

    private static async Task<IResult> InvokeResultOfT<TValue>(ISender sender, object request, int successStatusCode, CancellationToken ct)
    {
        var result = await sender.Send((IRequest<Result<TValue>>)request, ct).ConfigureAwait(false);
        return ResultToActionResultMapper.ToHttpResult(result, successStatusCode);
    }

    private static async Task<IResult> InvokePlain<TResponse>(ISender sender, object request, int successStatusCode, CancellationToken ct)
    {
        var response = await sender.Send((IRequest<TResponse>)request, ct).ConfigureAwait(false);
        return response is null
            ? Microsoft.AspNetCore.Http.Results.Ok()
            : Microsoft.AspNetCore.Http.Results.Ok(response);
    }

    private static void AddProducesTyped<TValue>(Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder, int statusCode)
        => builder.Produces<TValue>(statusCode);

    private static void AddAcceptsTyped<TRequest>(Microsoft.AspNetCore.Builder.RouteHandlerBuilder builder)
        where TRequest : notnull
        => builder.Accepts<TRequest>("application/json");

    private static PropertyInfo[] GetWritableProperties(Type requestType)
        => PropertyCache.GetOrAdd(requestType, static t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToArray());

    private static (object? Instance, List<string> Errors) BindFromQueryAndRoute(HttpContext context, Type requestType)
    {
        // Types with a public parameterless constructor (classes, init-property records) are bound
        // by setting properties. Positional records expose only a parameterized primary
        // constructor, so those are bound by matching constructor parameters to route/query values.
        var parameterless = ParameterlessCtorCache.GetOrAdd(
            requestType, static t => t.GetConstructor(Type.EmptyTypes));

        return parameterless is not null
            ? BindViaProperties(context, requestType)
            : BindViaConstructor(context, requestType);
    }

    private static (object? Instance, List<string> Errors) BindViaProperties(HttpContext context, Type requestType)
    {
        var instance = Activator.CreateInstance(requestType)!;
        var properties = GetWritableProperties(requestType);
        var errors = new List<string>();

        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            if (!prop.CanWrite) continue;

            // Try route values first, then query string
            string? value = context.Request.RouteValues.TryGetValue(prop.Name, out var routeVal)
                ? routeVal?.ToString()
                : context.Request.Query[prop.Name].FirstOrDefault();

            if (value is not null)
            {
                var converted = ConvertValue(value, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(instance, converted);
                }
                else
                {
                    errors.Add($"Parameter '{prop.Name}' has invalid value '{value}' for type '{prop.PropertyType.Name}'.");
                }
            }
        }

        return (instance, errors);
    }

    private static (object? Instance, List<string> Errors) BindViaConstructor(HttpContext context, Type requestType)
    {
        var errors = new List<string>();

        // Positional records have a single public primary constructor. Guard against additional
        // public constructors by preferring the greediest one (deterministic, matches the record
        // shape); a copy constructor is protected and never appears here.
        var ctor = BindingCtorCache.GetOrAdd(requestType, static t =>
            t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault());

        if (ctor is null)
        {
            errors.Add($"Type '{requestType.Name}' has no public constructor to bind from the query string.");
            return (null, errors);
        }

        var parameters = ctor.GetParameters();
        var args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            var param = parameters[i];
            var name = param.Name!;

            // Route values first, then query string — both collections match names
            // case-insensitively.
            string? value = context.Request.RouteValues.TryGetValue(name, out var routeVal)
                ? routeVal?.ToString()
                : context.Request.Query[name].FirstOrDefault();

            if (value is not null)
            {
                var converted = ConvertValue(value, param.ParameterType);
                if (converted is not null)
                {
                    args[i] = converted;
                }
                else
                {
                    errors.Add($"Parameter '{name}' has invalid value '{value}' for type '{param.ParameterType.Name}'.");
                }
            }
            else if (param.HasDefaultValue)
            {
                args[i] = param.DefaultValue;
            }
            else if (IsNullableParameter(param))
            {
                // Reference types and Nullable<T> can hold null; leave it to the handler/validation.
                args[i] = null;
            }
            else
            {
                // A required value-type parameter with no value would make ctor.Invoke throw
                // (NRE/InvalidCast) → 500. Surface a clear 400 instead.
                errors.Add($"Required parameter '{name}' is missing.");
            }
        }

        if (errors.Count > 0)
        {
            return (null, errors);
        }

        var instance = ctor.Invoke(args);

        // A positional record may also declare extra init/settable properties beyond the primary
        // constructor; bind those too so hybrid shapes work.
        BindExtraProperties(context, instance, requestType, parameters);

        return (instance, errors);
    }

    private static void BindExtraProperties(HttpContext context, object instance, Type requestType, ParameterInfo[] ctorParams)
    {
        var ctorParamNames = new HashSet<string>(ctorParams.Length, StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ctorParams.Length; i++)
        {
            ctorParamNames.Add(ctorParams[i].Name!);
        }

        var properties = GetWritableProperties(requestType);
        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];
            if (ctorParamNames.Contains(prop.Name)) continue;

            string? value = context.Request.RouteValues.TryGetValue(prop.Name, out var routeVal)
                ? routeVal?.ToString()
                : context.Request.Query[prop.Name].FirstOrDefault();

            if (value is not null)
            {
                var converted = ConvertValue(value, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(instance, converted);
                }
            }
        }
    }

    private static bool IsNullableParameter(ParameterInfo parameter)
        => !parameter.ParameterType.IsValueType
           || Nullable.GetUnderlyingType(parameter.ParameterType) is not null;

    private static List<string> BindRouteParameters(HttpContext context, object request, Type requestType)
    {
        var properties = GetWritableProperties(requestType);
        var errors = new List<string>();

        for (int i = 0; i < properties.Length; i++)
        {
            var prop = properties[i];

            if (context.Request.RouteValues.TryGetValue(prop.Name, out var routeVal) && routeVal is not null)
            {
                var raw = routeVal.ToString()!;
                var converted = ConvertValue(raw, prop.PropertyType);
                if (converted is not null)
                {
                    prop.SetValue(request, converted);
                }
                else
                {
                    // Do not silently leave the property at its default — that would run the
                    // handler against the wrong entity (e.g. id 0 / Guid.Empty).
                    errors.Add($"Route parameter '{prop.Name}' has invalid value '{raw}' for type '{prop.PropertyType.Name}'.");
                }
            }
        }

        return errors;
    }

    private static object? ConvertValue(string value, Type targetType)
    {
        try
        {
            var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Enum: case-insensitive parsing matching ASP.NET Core conventions
            if (underlyingType.IsEnum)
            {
                return Enum.TryParse(underlyingType, value, ignoreCase: true, out var enumResult)
                    ? enumResult
                    : null;
            }

            // Guid: special handling (Convert.ChangeType doesn't support Guid)
            if (underlyingType == typeof(Guid))
            {
                return Guid.TryParse(value, out var guidResult) ? guidResult : null;
            }

            return Convert.ChangeType(value, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }
    }

    private sealed class EndpointDescriptor
    {
        public Type RequestType { get; }
        public HttpEndpointAttribute Attribute { get; }

        public EndpointDescriptor(Type requestType, HttpEndpointAttribute attribute)
        {
            RequestType = requestType;
            Attribute = attribute;
        }
    }
}
