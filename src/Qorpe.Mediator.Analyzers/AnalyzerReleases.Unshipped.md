; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
QM1001  | Usage    | Warning  | [Cacheable] must be applied to a query
QM1002  | Usage    | Warning  | [Transactional] must be applied to a command
QM1003  | Usage    | Warning  | [Idempotent] must be applied to a command
QM1004  | Usage    | Warning  | [HttpEndpoint] must be applied to a request
