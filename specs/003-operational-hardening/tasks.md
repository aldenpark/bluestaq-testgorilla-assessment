# Tasks: Operational Hardening

## Phase 1: Setup

- [x] T001 Verify clean tracked state and ignore rules in `.gitignore` and `.dockerignore`
- [x] T002 Add tests-first scenarios in `tests/Bluestaq.Notes.Api.Tests/OperationalHardeningTests.cs`

## Phase 2: Foundational

- [x] T003 Add validated hardening configuration in `src/Bluestaq.Notes.Api/`
- [x] T004 Add team version, audit integrity, and readiness entities/migration in `src/Bluestaq.Notes.Api/Data/`
- [x] T005 Add reusable precondition and audit-integrity policies in `src/Bluestaq.Notes.Api/Security/`

## Phase 3: User Story 1 - Recoverable administration

- [x] T006 [US1] Add bootstrap/recovery/audit command mode in `src/Bluestaq.Notes.Api/Administration/`
- [x] T007 [US1] Add ownership transfer and team concurrency in `src/Bluestaq.Notes.Api/Controllers/TeamsController.cs`
- [x] T008 [US1] Verify bootstrap, recovery, and ownership flows in `tests/Bluestaq.Notes.Api.Tests/OperationalHardeningTests.cs`

## Phase 4: User Story 2 - Abuse and concurrency

- [x] T009 [US2] Add rate and body limits in `src/Bluestaq.Notes.Api/Program.cs`
- [x] T010 [US2] Add strict enum validation in `src/Bluestaq.Notes.Api/Contracts/ApiContracts.cs`
- [x] T011 [US2] Extend note/team preconditions in `src/Bluestaq.Notes.Api/Controllers/`
- [x] T012 [US2] Add parallel HTTP contention and negative tests in `tests/Bluestaq.Notes.Api.Tests/OperationalHardeningTests.cs`

## Phase 5: User Story 3 - Operations and observability

- [x] T013 [US3] Deepen database readiness in `src/Bluestaq.Notes.Api/Data/DatabaseHealthCheck.cs`
- [x] T014 [US3] Add correlation/OpenTelemetry in `src/Bluestaq.Notes.Api/Observability/` and `Program.cs`
- [x] T015 [US3] Add audit integrity/retention verification in `src/Bluestaq.Notes.Api/Security/` and command mode
- [x] T016 [US3] Verify readiness, telemetry, and integrity in `tests/Bluestaq.Notes.Api.Tests/OperationalHardeningTests.cs`

## Phase 6: User Story 4 - Lifecycle and supply chain

- [x] T017 [US4] Add `/api/v1` routes and legacy deprecation middleware in `src/Bluestaq.Notes.Api/Controllers/` and `Observability/`
- [x] T018 [US4] Complete OpenAPI response metadata in `src/Bluestaq.Notes.Api/Security/ProblemDetailsResponseOperationFilter.cs`
- [x] T019 [US4] Add production container assets in `Dockerfile` and `.dockerignore`
- [x] T020 [US4] Add CodeQL, secret scanning, Dependabot, immutable action pins, and container smoke in `.github/`
- [x] T021 [US4] Verify versioning and OpenAPI in `tests/Bluestaq.Notes.Api.Tests/SwaggerTests.cs`

## Phase 7: Polish

- [x] T022 Update smoke coverage in `tests/Bluestaq.Notes.Api.Tests/EndpointSmokeTests.cs`
- [x] T023 Document commands, limits, lifecycle, tracing, audit policy, and container use in `README.md`
- [ ] T024 Run formatting, Release build, migration drift, all categories, coverage, CVE, and container smoke
  - Local format, Release build, migration drift, 41 tests, coverage, and CVE scan pass. Container build is CI-gated but could not run locally because this account cannot access `/var/run/docker.sock`.
- [x] T025 Reconcile task status and verified README metrics in `specs/003-operational-hardening/tasks.md`

## Dependencies

- Phase 2 blocks every user story.
- US1 establishes team concurrency used by US2.
- US2 and US3 can proceed independently after foundation.
- US4 depends on stable runtime contracts from US1-US3.

## Implementation Strategy

Implement tests before each slice, keep administrative authority outside HTTP, and retain the single-service SQLite architecture.
