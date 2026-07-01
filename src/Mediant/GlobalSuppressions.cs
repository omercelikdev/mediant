using System.Diagnostics.CodeAnalysis;

// These suppressions are intentional for the mediator library's public API design.
// The naming conventions follow established patterns from MediatR and similar libraries.

[assembly: SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "RequestHandlerDelegate is the established pattern for mediator libraries.")]
[assembly: SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Error and next are intentional names following domain-driven design patterns.")]
[assembly: SuppressMessage("Design", "CA1000:Do not declare static members on generic types", Justification = "Static factory methods on Result<T> are the standard Result pattern API.")]
[assembly: SuppressMessage("Design", "CA1068:CancellationToken parameters must come last", Justification = "Internal wrapper API requires publisher parameter after cancellation token for pipeline composition.")]
[assembly: SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance methods on generic wrappers enable polymorphic dispatch through base class.")]
