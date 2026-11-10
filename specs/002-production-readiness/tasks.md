# Tasks: Production Readiness

**Input**: Design documents from `specs/002-production-readiness/`
**Prerequisites**: `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/endpoints.md`

## Phase 1: Setup

- [x] T001 Verify existing ignore rules and preserve user-owned untracked files in `.gitignore`
- [x] T002 Add test-first production-readiness scenarios in `tests/Bluestaq.Notes.Api.Tests/`

## Phase 2: Foundational

- [x] T003 Add injectable time and pure credential/note access policies in `src/Bluestaq.Notes.Api/Auth/` and `src/Bluestaq.Notes.Api/Security/`
- [x] T004 Add user and note concurrency/visibility fields in `src/Bluestaq.Notes.Api/Data/Entities.cs`
- [x] T005 Add EF configuration and migration in `src/Bluestaq.Notes.Api/Data/`

## Phase 3: User Story 1 - Safe concurrent changes

- [x] T006 [US1] Preserve expiration and serialize credential mutations in `src/Bluestaq.Notes.Api/Controllers/ApiKeysController.cs`
- [x] T007 [US1] Throttle credential last-used writes in `src/Bluestaq.Notes.Api/Auth/ApiKeyAuthentication.cs`
- [x] T008 [US1] Enforce `ETag` and `If-Match` note versions in `src/Bluestaq.Notes.Api/Controllers/NotesController.cs`
- [x] T009 [US1] Verify credential and note concurrency in `tests/Bluestaq.Notes.Api.Tests/ProductionReadinessTests.cs`

## Phase 4: User Story 2 - Meaningful note readership

- [x] T010 [US2] Extend note request/response contracts in `src/Bluestaq.Notes.Api/Contracts/ApiContracts.cs`
- [x] T011 [US2] Filter restricted note retrieval and listing in `src/Bluestaq.Notes.Api/Controllers/NotesController.cs`
- [x] T012 [US2] Verify reader/editor/team visibility in `tests/Bluestaq.Notes.Api.Tests/ProductionReadinessTests.cs`

## Phase 5: User Story 3 - Trustworthy operations

- [x] T013 [US3] Add database readiness and liveness endpoints in `src/Bluestaq.Notes.Api/Data/DatabaseHealthCheck.cs` and `Program.cs`
- [x] T014 [US3] Add post-commit security logging and metrics in `src/Bluestaq.Notes.Api/Security/SecurityAuditTelemetryInterceptor.cs`
- [x] T015 [US3] Add sanitized authentication warnings in `src/Bluestaq.Notes.Api/Auth/ApiKeyAuthentication.cs`
- [x] T016 [US3] Verify readiness and telemetry behavior in `tests/Bluestaq.Notes.Api.Tests/OperationalReadinessTests.cs`

## Phase 6: User Story 4 - Reviewable delivery

- [x] T017 [US4] Add response metadata across controllers in `src/Bluestaq.Notes.Api/Controllers/`
- [x] T018 [US4] Verify Development-only Swagger and response contracts in `tests/Bluestaq.Notes.Api.Tests/SwaggerTests.cs`
- [x] T019 [US4] Add format, warning, and migration gates in `.github/workflows/ci.yml`
- [x] T020 [US4] Document retry, backup, restore, rollback, telemetry, and health behavior in `README.md`

## Phase 7: Polish and validation

- [x] T021 Add focused policy unit tests in `tests/Bluestaq.Notes.Api.Tests/PolicyTests.cs`
- [x] T022 Update the full smoke workflow in `tests/Bluestaq.Notes.Api.Tests/EndpointSmokeTests.cs`
- [x] T023 Run formatting, Release build, all tests, category tests, migration drift, coverage, and CVE scan
- [x] T024 Mark completed tasks and reconcile README metrics in `specs/002-production-readiness/tasks.md` and `README.md`

## Dependencies

- T003-T005 block all implementation stories.
- T006-T009 complete before restricted-note controller changes.
- T013-T016 are independent after foundational work.
- T017-T020 depend on stable endpoint behavior.
- T021-T024 finish after all stories.

## Implementation Strategy

Implement and verify each invariant independently: credential safety, note concurrency, restricted readership, operational truth, then documentation and delivery gates. Keep the existing single-service architecture.
