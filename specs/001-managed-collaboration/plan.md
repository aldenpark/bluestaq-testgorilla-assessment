# Implementation Plan: Managed Collaboration

**Branch**: `001-managed-collaboration` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

## Summary

Normalize credentials into a managed multi-key model, add invitation and key
lifecycle APIs, enforce creator/owner/editor note permissions, add owner-only team
mutation, and replace offset listing with filtered FTS5 cursor traversal.
Append immutable, sanitized security events in the same transaction as every access-control change.

## Technical Context

**Language/Version**: C# 14 / .NET 10
**Primary Dependencies**: ASP.NET Core, EF Core 10, Microsoft.Data.Sqlite
**Storage**: SQLite with EF migrations, an FTS5 index, and append-only audit triggers
**Testing**: xUnit, WebApplicationFactory, isolated SQLite databases
**Target Platform**: Linux-compatible HTTP service
**Project Type**: Single backend REST service
**Performance Goals**: Indexed team filtering and full-text lookup; bounded pages of 100
**Constraints**: Existing seeded keys survive migration; secrets never enter audit details
**Scale/Scope**: Several small teams on one service instance

## Constitution Check

- Runnable simplicity: PASS; one service and existing direct DbContext pattern.
- Security boundaries: PASS; every mutation has explicit owner/user/note checks.
- Data integrity: PASS; migration copies credentials before dropping the old column.
- Integration verification: PASS; real HTTP and SQLite tests cover routes and denial paths.
- Honest documentation: PASS; README and limitations change with verified behavior.
- Audit durability: PASS; events have no mutable-target foreign keys and reject updates/deletes.

## Project Structure

```text
src/Bluestaq.Notes.Api/
├── Auth/
├── Contracts/
├── Controllers/
└── Data/
tests/Bluestaq.Notes.Api.Tests/
specs/001-managed-collaboration/
```

**Structure Decision**: Extend the existing API and test projects. No repository or
service layer is introduced because authorization queries remain short and local.

## Delivery Phases

1. Schema and authentication migration.
2. Invitations, credential lifecycle, team mutation, and note permissions.
3. FTS5 search and cursor pagination.
4. Integration/smoke coverage, README, formatting, build, audit.
5. Append-only security events, owner/personal history endpoints, and immutability tests.

## Complexity Tracking

No constitution violations.
