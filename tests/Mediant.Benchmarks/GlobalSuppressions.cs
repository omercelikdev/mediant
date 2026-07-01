using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Benchmark method names use underscores for readability.")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "BenchmarkDotNet requires instance methods for benchmarks.")]
[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments", Justification = "Not applicable in benchmark setup code.")]
[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "No logging used in benchmarks.")]
