// Convenience meta-package: referencing Qorpe.Mediator.Contracts brings the core abstraction and
// result namespaces transitively (via the Qorpe.Mediator project/package reference) so application
// and domain layers have a single, stable package to depend on.
//
// NOTE: these global usings only apply WITHIN this assembly — they are a convenience for code
// compiled here, not a cross-package re-export. Consumers still write the usual
// `using Qorpe.Mediator.Abstractions;` etc. (the types resolve from Qorpe.Mediator, brought in
// transitively). A future major version may relocate the abstractions into this package for a
// genuinely slimmer dependency.
global using Qorpe.Mediator.Abstractions;
global using Qorpe.Mediator.Results;
global using Qorpe.Mediator.Attributes;
global using Qorpe.Mediator.Exceptions;
