# Feature Specification: Production Readiness

**Feature Branch**: `002-production-readiness`
**Created**: 2026-11-10
**Status**: Approved
**Input**: Add the full staff-level readiness set: concurrency safety, meaningful note roles, credential invariants, operability, telemetry, contract documentation, deterministic tests, and delivery gates.

## User Scenarios & Testing

### User Story 1 - Safe concurrent changes (Priority: P1)

Users can update notes and manage credentials without silently overwriting newer work or violating the rule that every user retains a usable credential.

**Why this priority**: Data loss and account lockout are correctness failures, not optional production polish.

**Independent Test**: Submit concurrent note updates and credential revocations and verify one succeeds while stale or conflicting operations receive a retryable conflict response.

**Acceptance Scenarios**:

1. **Given** two clients read the same note version, **When** both update it, **Then** the first succeeds and the stale update is rejected without overwriting data.
2. **Given** an expiring credential, **When** it is rotated, **Then** the replacement retains the original expiration.
3. **Given** two remaining usable credentials, **When** concurrent requests try to revoke both, **Then** at least one remains usable.

---

### User Story 2 - Meaningful note readership (Priority: P1)

Authors can create either team-readable or restricted notes. Restricted notes are visible only to their creator, the team owner, and members with explicit reader or editor access.

**Why this priority**: A reader role that changes no behavior is a misleading security contract.

**Independent Test**: Create a restricted note and verify it is absent from unauthorized retrieval and listing until a reader grant is added.

**Acceptance Scenarios**:

1. **Given** a restricted note, **When** an ungranted team member lists or retrieves notes, **Then** the note is not disclosed.
2. **Given** a reader grant, **When** that member retrieves the restricted note, **Then** reading succeeds while editing remains forbidden.
3. **Given** a team-readable note, **When** any current member reads it, **Then** existing shared-team behavior remains unchanged.

---

### User Story 3 - Trustworthy operations (Priority: P2)

Operators can distinguish a running process from a service that can use its database, consume structured security signals, and follow documented backup, recovery, and migration procedures.

**Why this priority**: A healthy response that ignores dependencies and an audit log with no export path create false confidence.

**Independent Test**: Exercise liveness and readiness with an available and unavailable database and capture a committed security event through the configured logging provider.

**Acceptance Scenarios**:

1. **Given** a running process with an unavailable database, **When** readiness is checked, **Then** readiness fails while liveness remains healthy.
2. **Given** a committed security-sensitive change, **When** persistence succeeds, **Then** a structured security event is emitted with action, actor, target, team, and trace identifiers.
3. **Given** an operator following the runbook, **When** a database is backed up and restored, **Then** integrity can be verified before service startup.

---

### User Story 4 - Reviewable API and delivery evidence (Priority: P3)

Reviewers can see accurate success and error responses in interactive API documentation, understand retry behavior, and trust automated formatting, build, migration, test, coverage, and vulnerability gates.

**Why this priority**: A staff-level submission makes contracts and delivery evidence inspectable rather than implicit.

**Independent Test**: Inspect the generated API document and CI workflow, then run all documented gates locally.

**Acceptance Scenarios**:

1. **Given** an endpoint in interactive documentation, **When** a reviewer inspects it, **Then** documented success and Problem Details responses match runtime behavior.
2. **Given** a create operation, **When** a client considers retrying it, **Then** documentation states whether the operation is safe to retry.
3. **Given** a pull request, **When** formatting, compilation, migration drift, tests, coverage, or dependency security fails, **Then** CI fails.

### Edge Cases

- A stale note version is supplied after another writer has updated the note.
- Two credential mutations race on the same user.
- A restricted note grant is removed while the former reader is paging results.
- Authentication occurs repeatedly inside the last-used update interval.
- A security event is staged but its database transaction fails.
- Readiness is called while the database is locked or unavailable.
- Swagger is requested outside the development environment.

## Requirements

### Functional Requirements

- **FR-001**: Note updates MUST reject missing or stale preconditions and MUST never silently overwrite a newer version.
- **FR-002**: Successful note reads and writes MUST return the current version as an entity tag.
- **FR-003**: Credential rotation MUST preserve the credential expiration policy.
- **FR-004**: Concurrent credential mutations MUST preserve at least one usable credential per user.
- **FR-005**: Credential last-used timestamps MUST be rate-limited so ordinary reads do not force a write on every request.
- **FR-006**: All application time decisions MUST use an injectable clock.
- **FR-007**: Notes MUST support team-readable and restricted visibility.
- **FR-008**: Restricted note retrieval and listing MUST enforce creator, owner, reader, or editor access.
- **FR-009**: Reader access MUST permit reading but not editing, deleting, or permission administration.
- **FR-010**: Existing notes MUST remain team-readable after migration.
- **FR-011**: The service MUST expose separate liveness and database-backed readiness checks.
- **FR-012**: Successfully committed security events MUST emit structured log records suitable for external collection.
- **FR-013**: Failed or malformed authentication attempts MUST emit rate-conscious structured warning records without credential material.
- **FR-014**: Interactive API documentation MUST remain development-only and MUST describe API-key authentication.
- **FR-015**: API documentation MUST describe expected success and error responses for every route family.
- **FR-016**: Retry and idempotency behavior MUST be documented for create, replace, and delete operations.
- **FR-017**: Backup, restore, integrity-check, and migration rollback procedures MUST be documented.
- **FR-018**: Delivery automation MUST enforce formatting, warning-free Release compilation, migration consistency, tests, coverage thresholds, and dependency vulnerability checks.
- **FR-019**: Focused unit tests MUST cover extracted credential and note-access policies.
- **FR-020**: Integration tests MUST cover concurrency, visibility, readiness, production Swagger exposure, telemetry, and migrated-data behavior.

### Key Entities

- **User**: Owns credentials and a concurrency version used to serialize credential lifecycle changes.
- **API-key credential**: Stores a named hash, lifecycle timestamps, and a throttled last-used timestamp.
- **Note**: Stores content, immutable visibility, and a concurrency version exposed as an entity tag.
- **Note permission**: Grants reader or editor access to a current team member.
- **Security audit event**: Immutable transactional evidence that is exported only after a successful commit.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In 100% of tested concurrent note updates, stale content never replaces newer content.
- **SC-002**: In 100% of tested concurrent credential revocations, at least one usable credential remains.
- **SC-003**: Unauthorized members discover zero restricted notes through direct retrieval or listing.
- **SC-004**: Readiness changes to unhealthy within one check when database access fails, while liveness remains healthy.
- **SC-005**: Every committed security mutation produces one structured export record and failed transactions produce none.
- **SC-006**: All documented local and CI verification gates pass with at least 90% line and 70% branch coverage.

## Assumptions

- Team-readable remains the default visibility for backward compatibility; restricted visibility is selected at note creation and is immutable.
- A stale note mutation returns a precondition failure; a concurrent credential mutation returns a conflict suitable for retry.
- Create operations remain non-idempotent unless explicitly stated; clients must not automatically retry a request after an unknown outcome.
- Structured logging is the external security-event integration boundary; production deployments route those records to their chosen collector.
- SQLite remains the assessment storage engine and single-instance deployment remains the supported runtime model.
