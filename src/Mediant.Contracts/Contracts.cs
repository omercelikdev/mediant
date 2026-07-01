// Convenience meta-package: referencing Mediant.Contracts brings the core abstraction and
// result namespaces transitively (via the Mediant project/package reference) so application
// and domain layers have a single, stable package to depend on.
//
// NOTE: these global usings only apply WITHIN this assembly — they are a convenience for code
// compiled here, not a cross-package re-export. Consumers still write the usual
// `using Mediant.Abstractions;` etc. (the types resolve from Mediant, brought in
// transitively). A future major version may relocate the abstractions into this package for a
// genuinely slimmer dependency.
global using Mediant.Abstractions;
global using Mediant.Results;
global using Mediant.Attributes;
global using Mediant.Exceptions;
