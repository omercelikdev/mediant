using System.Reflection;
using PublicApiGenerator;

namespace Qorpe.Mediator.UnitTests.PublicApi;

/// <summary>
/// Freezes the public API surface of each shipped package. A breaking or unintended public API
/// change fails here with a diff. To intentionally change the API, update the matching
/// <c>PublicApi/&lt;Assembly&gt;.approved.txt</c> baseline with the generated <c>.received.txt</c>.
/// </summary>
public class PublicApiTests
{
    private static readonly ApiGeneratorOptions Options = new()
    {
        ExcludeAttributes = new[]
        {
            "System.Runtime.CompilerServices.InternalsVisibleToAttribute",
            "System.Reflection.AssemblyMetadataAttribute",
            "System.Runtime.Versioning.TargetFrameworkAttribute",
            "System.Reflection.AssemblyCompanyAttribute",
            "System.Reflection.AssemblyConfigurationAttribute",
            "System.Reflection.AssemblyFileVersionAttribute",
            "System.Reflection.AssemblyInformationalVersionAttribute",
            "System.Reflection.AssemblyProductAttribute",
            "System.Reflection.AssemblyTitleAttribute",
            "System.Reflection.AssemblyVersionAttribute",
        },
    };

    public static IEnumerable<object[]> Assemblies()
    {
        yield return new object[] { typeof(Qorpe.Mediator.Abstractions.IMediator).Assembly };
        yield return new object[] { typeof(Qorpe.Mediator.Behaviors.Behaviors.CachingBehavior<,>).Assembly };
        yield return new object[] { typeof(Qorpe.Mediator.AspNetCore.Mapping.EndpointMapper).Assembly };
        yield return new object[] { typeof(Qorpe.Mediator.FluentValidation.ValidationBehavior<,>).Assembly };
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Public_Api_Has_Not_Changed(Assembly assembly)
    {
        var name = assembly.GetName().Name!;
        var actual = Normalize(assembly.GeneratePublicApi(Options));

        var approvedPath = Path.Combine(AppContext.BaseDirectory, "PublicApi", name + ".approved.txt");
        if (!File.Exists(approvedPath))
        {
            WriteReceived(name, actual);
            throw new Xunit.Sdk.XunitException(
                $"Missing approved API baseline for '{name}'. A '{name}.received.txt' was written to " +
                $"'{AppContext.BaseDirectory}'. Review it and copy it to 'tests/Qorpe.Mediator.UnitTests/PublicApi/{name}.approved.txt'.");
        }

        var approved = Normalize(File.ReadAllText(approvedPath));
        if (actual != approved)
        {
            WriteReceived(name, actual);
        }

        actual.Should().Be(approved,
            $"the public API of '{name}' must match its frozen baseline; if intentional, update PublicApi/{name}.approved.txt");
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n").Trim();

    private static void WriteReceived(string name, string content)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name + ".received.txt");
        File.WriteAllText(path, content);
    }
}
