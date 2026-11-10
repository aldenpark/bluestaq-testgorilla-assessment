# Tasks: Managed Collaboration

## Phase 1 - Schema and authentication

- [x] T001 Add credential and note-permission entities and EF mappings.
- [x] T002 Migrate legacy hashes and create the FTS5 index/triggers.
- [x] T003 Authenticate active credentials and seed normalized keys.

## Phase 2 - Managed collaboration APIs

- [x] T004 Add invitation and owner-only team rename/delete.
- [x] T005 Add current-user key create/list/rotate/revoke.
- [x] T006 Enforce note creator/owner/editor rules and permission endpoints.

## Phase 3 - Discovery and validation

- [x] T007 Add filtered FTS5 cursor listing.
- [x] T008 Add integration and smoke coverage for every new route and boundary.
- [x] T009 Update README, run all quality gates, and inspect the final diff.

## Phase 4 - Security audit trail

- [x] T010 Add failing integration tests for audit creation, authorization, redaction, pagination, retention, and immutability in `tests/Bluestaq.Notes.Api.Tests/SecurityAuditTests.cs`.
- [x] T011 Add the append-only audit entity, writer, mappings, and SQLite migration in `src/Bluestaq.Notes.Api/Data/` and `src/Bluestaq.Notes.Api/Security/`.
- [x] T012 Record in-scope security mutations atomically in API controllers under `src/Bluestaq.Notes.Api/Controllers/`.
- [x] T013 Add personal and owner-only team audit endpoints and smoke coverage in `src/Bluestaq.Notes.Api/Controllers/SecurityEventsController.cs` and `tests/Bluestaq.Notes.Api.Tests/EndpointSmokeTests.cs`.
- [x] T014 Update `README.md`, run all quality gates, and review the final diff.
