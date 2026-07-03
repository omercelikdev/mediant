# Contributing to Mediant

Thanks for your interest in contributing! This document explains how to build,
test, and submit changes.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (the solution multi-targets
  .NET 8/9/10; the newest SDK builds all targets)

## Building and Testing

```bash
dotnet build --configuration Release
dotnet test  --configuration Release
```

The whole suite (unit, integration, EF Core, analyzer, source generator, and load
tests) must pass. CI additionally runs the matrix on .NET 8, 9, and 10 plus a
Native AOT publish of `tests/Mediant.AotSample` — reflection- or trim-unsafe code
in the generated dispatch path will fail there.

`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` are on: a build with any
warning fails. Please don't suppress warnings to get past this; fix the cause or
raise it in the PR discussion.

## Public API Baselines

The public surface of every shipped package is frozen in
`tests/Mediant.UnitTests/PublicApi/<Assembly>.approved.txt` and verified by
`PublicApiTests`. If your change intentionally alters the public API:

1. Run the unit tests — the failing test writes a `<Assembly>.received.txt`
   next to the test binaries.
2. Review the diff, then copy the received file over the matching
   `approved.txt` baseline and commit it.
3. Call out the API change explicitly in your PR description. Breaking changes
   need a very good reason after 1.0.

## Pull Requests

- Open an issue first for anything non-trivial, so the approach can be agreed on
  before you invest time.
- Branch from `main`; keep PRs focused on one logical change.
- Use [Conventional Commits](https://www.conventionalcommits.org/) for the PR
  title (it becomes the squash commit), e.g. `fix(behaviors): ...`,
  `feat(aspnetcore): ...`, `docs: ...`.
- Add tests for new functionality and regression tests for bug fixes.
- Update `docs/CHANGELOG.md` under an `Unreleased` heading for user-visible
  changes.
- Fill in the PR template checklist.

## Reporting Issues

Use the issue templates for bug reports and feature requests. For security
vulnerabilities, **do not open a public issue** — follow [SECURITY.md](SECURITY.md).

## License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE).
