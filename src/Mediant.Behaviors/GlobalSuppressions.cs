using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Behavior naming follows established mediator pipeline convention.")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Error and next are intentional names following domain-driven design patterns.")]
[assembly: SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Static factory methods on Result<T> are the standard Result pattern API.")]
[assembly: SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates", Justification = "Open generic behaviors cannot use LoggerMessage source generators due to generic type parameters.")]
[assembly: SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "Log templates are static strings in generic context.")]
[assembly: SuppressMessage("Performance", "CA1873", Justification = "Logger interpolation in generic behaviors is acceptable — LoggerMessage source generators cannot be used with open generic type parameters.")]
