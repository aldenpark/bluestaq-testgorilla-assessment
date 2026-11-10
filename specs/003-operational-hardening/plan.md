# Implementation Plan: Operational Hardening

**Branch**: `main` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

## Summary

Extend the existing single-service design with out-of-band administration, broader optimistic concurrency, built-in abuse controls, deep readiness, keyed audit integrity, request correlation/OpenTelemetry, route versioning, container verification, and supply-chain gates.

## Technical Context

**Language/Version**: C# / .NET 10
**Primary Dependencies**: ASP.NET Core, EF Core SQLite, Swashbuckle, OpenTelemetry
**Storage**: SQLite with checked-in migrations
**Testing**: xUnit, WebApplicationFactory, temporary SQLite files
**Target Platform**: Linux server/container and local development
**Project Type**: REST web service with out-of-band administrative command mode
**Performance Goals**: Bounded request bodies/queues and deterministic concurrent-write outcomes
**Constraints**: One service, one SQLite database, no required external identity/collector/broker
**Scale/Scope**: Several small teams, single application instance

## Constitution Check

- Runnable Simplicity: PASS; commands reuse the application project and the container has no external runtime dependency.
- Explicit Security Boundaries: PASS; administrative capabilities remain outside HTTP and all new negative paths are tested.
- Data Integrity: PASS; concurrency tokens, rollback write probes, HMAC tags, and migrations preserve existing data.
- Integration-First Verification: PASS; each route/command behavior receives SQLite-backed verification.
- Honest Documentation: PASS; remaining single-instance and key-management limits remain explicit.

## Project Structure

```text
src/Bluestaq.Notes.Api/
├── Administration/     # bootstrap, recovery, audit maintenance commands
├── Auth/               # authentication and rate-limit identity
├── Controllers/        # legacy and v1 API routes
├── Data/               # entities, readiness, migrations
├── Observability/      # correlation and tracing
└── Security/           # audit integrity and response metadata
tests/Bluestaq.Notes.Api.Tests/
Dockerfile
.dockerignore
.github/workflows/
.github/dependabot.yml
```

**Structure Decision**: Preserve the modular monolith and test project; add focused folders only where responsibilities are operationally distinct.
