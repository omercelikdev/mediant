# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

Security fixes are released for the latest stable version. Pre-release versions
(`-preview`, `-rc`) are not supported once the corresponding stable version ships.

This policy covers all packages shipped from this repository:
`Mediant`, `Mediant.Contracts`, `Mediant.Behaviors`, `Mediant.FluentValidation`,
`Mediant.AspNetCore`, `Mediant.EntityFrameworkCore`, `Mediant.Analyzers`,
and `Mediant.SourceGenerator`.

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Report vulnerabilities privately via
[GitHub Security Advisories](https://github.com/omercelikdev/mediant/security/advisories/new)
(preferred), or by email to **omer@omercelik.dev** with the subject line
`[SECURITY] mediant`.

Please include as much of the following as you can:

- The affected package(s) and version(s)
- A description of the vulnerability and its impact
- Steps to reproduce, ideally a minimal proof of concept
- Any suggested mitigation or fix

## What to Expect

- **Acknowledgement** within 48 hours of your report.
- **Assessment** and severity triage within 7 days.
- **Fix and release**: confirmed vulnerabilities are fixed as fast as severity
  demands; a patched version is published to NuGet.org and a security advisory
  is issued crediting the reporter (unless you prefer to remain anonymous).

Please give us a reasonable window to ship a fix before any public disclosure.
