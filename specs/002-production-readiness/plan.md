# Implementation Plan: Production Readiness

**Branch**: `002-production-readiness` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)
**Input**: Production-readiness requirements from `specs/002-production-readiness/spec.md`

## Summary

Strengthen the existing modular monolith with optimistic note concurrency, serialized credential lifecycle changes, restricted-note readership, dependency-aware health, post-commit security telemetry, complete OpenAPI response metadata, deterministic time, focused policy tests, operational documentation, and stricter CI gates.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core, EF Core 10, Swashbuckle 10
**Storage**: SQLite with checked-in EF Core migrations
**Testing**: xUnit unit, HTTP integration, and smoke tests
**Target Platform**: Linux-compatible single-instance web service
**Project Type**: Backend REST service
**Performance Goals**: Avoid per-request authentication writes; bounded health checks and pages
**Constraints**: No external service, broker, identity provider, or deployment platform
**Scale/Scope**: Several small teams on one service instance

## Constitution Check

- Runnable Simplicity: PASS; changes stay inside the existing API and test projects.
- Explicit Security Boundaries: PASS; restricted reads and credential races receive negative-path tests.
- Data Integrity: PASS; migrations default existing notes to team visibility and versions to one.
- Integration-First Verification: PASS; HTTP and SQLite tests remain primary, with focused pure-policy unit tests.
- Honest Documentation: PASS; retry, recovery, telemetry, and residual limits are explicit.

## Project Structure

```text
src/Bluestaq.Notes.Api/
├── Auth/                 # credential authentication and policies
├── Contracts/            # wire contracts and response metadata
├── Controllers/          # HTTP orchestration and preconditions
├── Data/                 # entities, health check, interceptor, migrations
├── Security/             # access policies and audit telemetry
└── Program.cs            # composition, health, Swagger

tests/Bluestaq.Notes.Api.Tests/
├── *IntegrationTests.cs  # HTTP/SQLite behavior
├── *PolicyTests.cs       # isolated policy behavior
└── EndpointSmokeTests.cs # full route-family workflow
```

**Structure Decision**: Extend the existing modular monolith. Add small policy and infrastructure classes only where they support isolated invariants or framework integration.

## Complexity Tracking

No constitution violations require exceptions.
