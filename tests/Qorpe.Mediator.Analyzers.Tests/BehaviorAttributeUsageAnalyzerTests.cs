using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Qorpe.Mediator.Analyzers;

namespace Qorpe.Mediator.Analyzers.Tests;

public class BehaviorAttributeUsageAnalyzerTests
{
    private const string Usings = """
        using Qorpe.Mediator.Abstractions;
        using Qorpe.Mediator.Results;
        using Qorpe.Mediator.Behaviors.Attributes;
        using Qorpe.Mediator.AspNetCore.Attributes;
        """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(Usings + "\n" + source);

        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();

        // Ensure the Qorpe assemblies carrying the interfaces/attributes are referenced.
        foreach (var anchor in new[]
                 {
                     typeof(Qorpe.Mediator.Abstractions.IRequest<>).Assembly.Location,
                     typeof(Qorpe.Mediator.Behaviors.Attributes.CacheableAttribute).Assembly.Location,
                     typeof(Qorpe.Mediator.AspNetCore.Attributes.HttpEndpointAttribute).Assembly.Location,
                 })
        {
            if (!references.Any(r => string.Equals((r as PortableExecutableReference)?.FilePath, anchor, StringComparison.OrdinalIgnoreCase)))
            {
                references.Add(MetadataReference.CreateFromFile(anchor));
            }
        }

        var compilation = CSharpCompilation.Create(
            "AnalyzerTestAsm",
            new[] { tree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var withAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new BehaviorAttributeUsageAnalyzer()));

        var diagnostics = await withAnalyzers.GetAnalyzerDiagnosticsAsync();
        return diagnostics;
    }

    private static async Task<string[]> IdsAsync(string source)
        => (await AnalyzeAsync(source)).Select(d => d.Id).OrderBy(x => x).ToArray();

    [Fact]
    public async Task Cacheable_On_Command_Reports_QM1001()
    {
        var ids = await IdsAsync("[Cacheable] public sealed record BadCmd : ICommand<Result>;");
        ids.Should().Contain("QM1001");
    }

    [Fact]
    public async Task Cacheable_On_Query_Reports_Nothing()
    {
        var ids = await IdsAsync("[Cacheable] public sealed record GoodQry : IQuery<Result<int>>;");
        ids.Should().NotContain("QM1001");
    }

    [Fact]
    public async Task Transactional_On_Query_Reports_QM1002()
    {
        var ids = await IdsAsync("[Transactional] public sealed record BadQry : IQuery<Result<int>>;");
        ids.Should().Contain("QM1002");
    }

    [Fact]
    public async Task Transactional_On_Command_Reports_Nothing()
    {
        var ids = await IdsAsync("[Transactional] public sealed record GoodCmd : ICommand<Result>;");
        ids.Should().NotContain("QM1002");
    }

    [Fact]
    public async Task Idempotent_On_Query_Reports_QM1003()
    {
        var ids = await IdsAsync("[Idempotent] public sealed record BadIdem : IQuery<Result<int>>;");
        ids.Should().Contain("QM1003");
    }

    [Fact]
    public async Task HttpEndpoint_On_NonRequest_Reports_QM1004()
    {
        var ids = await IdsAsync("[HttpEndpoint(\"GET\", \"/x\")] public sealed record NotARequest : INotification;");
        ids.Should().Contain("QM1004");
    }

    [Fact]
    public async Task HttpEndpoint_On_Query_Reports_Nothing()
    {
        var ids = await IdsAsync("[HttpEndpoint(\"GET\", \"/x\")] public sealed record GoodEndpoint : IQuery<Result<int>>;");
        ids.Should().NotContain("QM1004");
    }
}
