using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mediant.Analyzers;

/// <summary>
/// Flags behavior attributes applied to the wrong kind of request, which the runtime silently
/// ignores (e.g. <c>[Cacheable]</c> on a command is never cached) or rejects.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BehaviorAttributeUsageAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Usage";
    private const string AbstractionsNamespace = "Mediant.Abstractions";

    internal static readonly DiagnosticDescriptor CacheableOnNonQuery = new(
        "QM1001",
        "[Cacheable] must be applied to a query",
        "'{0}' has [Cacheable] but is not a query (IQuery<T>); CachingBehavior skips non-queries, so it is never cached",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor TransactionalOnNonCommand = new(
        "QM1002",
        "[Transactional] must be applied to a command",
        "'{0}' has [Transactional] but is not a command (ICommand/ICommand<T>); TransactionBehavior skips queries, so no transaction is opened",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor IdempotentOnNonCommand = new(
        "QM1003",
        "[Idempotent] must be applied to a command",
        "'{0}' has [Idempotent] but is not a command (ICommand/ICommand<T>); IdempotencyBehavior skips queries, so it is never deduplicated",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    internal static readonly DiagnosticDescriptor HttpEndpointOnNonRequest = new(
        "QM1004",
        "[HttpEndpoint] must be applied to a request",
        "'{0}' has [HttpEndpoint] but does not implement IRequest<T> (command/query); endpoint mapping will throw at startup",
        Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(CacheableOnNonQuery, TransactionalOnNonCommand, IdempotentOnNonCommand, HttpEndpointOnNonRequest);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || type.TypeKind == TypeKind.Interface)
        {
            return;
        }

        var attributes = type.GetAttributes();
        if (attributes.Length == 0)
        {
            return;
        }

        bool isQuery = ImplementsAbstraction(type, "IQuery");
        bool isCommand = ImplementsAbstraction(type, "ICommand");
        bool isRequest = ImplementsAbstraction(type, "IRequest");

        foreach (var attribute in attributes)
        {
            var attributeName = attribute.AttributeClass?.Name;
            if (attributeName is null)
            {
                continue;
            }

            switch (attributeName)
            {
                case "CacheableAttribute" when !isQuery:
                    Report(context, CacheableOnNonQuery, type, attribute);
                    break;
                case "TransactionalAttribute" when !isCommand:
                    Report(context, TransactionalOnNonCommand, type, attribute);
                    break;
                case "IdempotentAttribute" when !isCommand:
                    Report(context, IdempotentOnNonCommand, type, attribute);
                    break;
                case "HttpEndpointAttribute" when !isRequest:
                    Report(context, HttpEndpointOnNonRequest, type, attribute);
                    break;
            }
        }
    }

    private static bool ImplementsAbstraction(INamedTypeSymbol type, string interfaceName)
    {
        foreach (var iface in type.AllInterfaces)
        {
            if (iface.Name == interfaceName &&
                iface.ContainingNamespace?.ToDisplayString() == AbstractionsNamespace)
            {
                return true;
            }
        }

        return false;
    }

    private static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, INamedTypeSymbol type, AttributeData attribute)
    {
        var location = attribute.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? type.Locations.FirstOrDefault()
            ?? Location.None;

        context.ReportDiagnostic(Diagnostic.Create(descriptor, location, type.Name));
    }
}
