# Feature Specification: Operational Hardening

**Feature Branch**: `003-operational-hardening`
**Created**: 2026-11-10
**Status**: Approved
**Input**: Close the remaining staff-level production, security, concurrency, observability, API lifecycle, and delivery gaps.

## User Scenarios & Testing

### User Story 1 - Recoverable production administration (Priority: P1)

An operator can securely bootstrap an empty production database, recover a user's credential with host access, and transfer team ownership without exposing anonymous administrative HTTP endpoints.

**Why this priority**: A fresh production deployment is otherwise unusable and credential loss can permanently lock out every administrator.

**Independent Test**: Run administrative commands against an empty database, authenticate with the one-time credential, recover that user, and transfer a team to an existing member.

**Acceptance Scenarios**:

1. **Given** an empty database, **When** an operator bootstraps a user, **Then** one user and one one-time credential are created.
2. **Given** a non-empty database, **When** bootstrap is attempted again, **Then** it fails without creating data.
3. **Given** host-level database access, **When** recovery is requested, **Then** existing credentials are revoked and one replacement secret is returned once.
4. **Given** a team member, **When** the owner transfers ownership with a current precondition, **Then** ownership changes atomically and is audited.

---

### User Story 2 - Abuse-resistant and concurrency-safe API (Priority: P1)

Clients receive bounded request handling, deterministic validation, and precondition protection for destructive or administrative writes.

**Why this priority**: Authentication without abuse controls and partial concurrency protection leave avoidable denial-of-service and lost-update paths.

**Independent Test**: Exceed configured request limits, submit oversized and invalid enum payloads, and replay stale note/team preconditions.

**Acceptance Scenarios**:

1. **Given** excessive requests from one caller partition, **When** the limit is exceeded, **Then** the API returns `429` with retry guidance.
2. **Given** an oversized body or undefined enum value, **When** submitted, **Then** the API rejects it without a domain write.
3. **Given** stale note or team state, **When** delete, permission, owner, or team mutations are attempted, **Then** they fail without overwriting newer state.

---

### User Story 3 - Verifiable operations and observability (Priority: P2)

Operators can distinguish a merely connected database from a writable, schema-current service, correlate requests, export traces, verify audit integrity, and apply an explicit retention policy.

**Why this priority**: Operational signals must reflect the service's ability to serve writes and security evidence must be independently verifiable.

**Independent Test**: Exercise readiness under schema/write failures, round-trip correlation headers, capture trace activity, detect audit tampering, and preview/apply retention.

**Acceptance Scenarios**:

1. **Given** a stale or read-only database, **When** readiness is checked, **Then** it returns unhealthy.
2. **Given** a request correlation identifier, **When** a request completes, **Then** the identifier is validated, returned, and included in logs/traces.
3. **Given** a modified audit row, **When** verification runs, **Then** integrity verification fails.
4. **Given** a configured retention cutoff, **When** maintenance runs, **Then** expired events are reported and removed only through an explicit operator command.

---

### User Story 4 - Reviewable lifecycle and supply chain (Priority: P3)

Reviewers can use a versioned API, inspect complete OpenAPI responses, run a production-like container, and see automated source, secret, dependency, test, and container gates.

**Why this priority**: Delivery controls and explicit lifecycle policy make the implementation supportable beyond a local development run.

**Independent Test**: Call legacy and v1 routes, inspect deprecation and OpenAPI metadata, build/run the container, and inspect CI/security configuration.

**Acceptance Scenarios**:

1. **Given** a legacy route, **When** called, **Then** it remains compatible and advertises deprecation; `/api/v1` provides the supported equivalent.
2. **Given** generated OpenAPI, **When** inspected, **Then** each operation documents success and common Problem Details responses.
3. **Given** the container image, **When** run with production configuration, **Then** health becomes ready without development fixtures.
4. **Given** a pull request, **When** code, secrets, dependencies, tests, coverage, migrations, or container startup fail, **Then** automation fails.

### Edge Cases

- Two operators attempt bootstrap or recovery concurrently.
- A transferred owner is removed or the current owner transfers to themselves.
- Rate-limit partitions must never retain plaintext API keys.
- A stale permission mutation races with a note edit.
- Readiness must roll back its write probe and never create business data.
- Missing production integrity keys must fail startup or administrative commands clearly.
- Audit retention must not silently run during ordinary application startup.

## Requirements

### Functional Requirements

- **FR-001**: Production bootstrap MUST be out-of-band, single-use, and return a credential once.
- **FR-002**: Host-authorized credential recovery MUST revoke prior credentials atomically and be auditable.
- **FR-003**: Team owners MUST be able to transfer ownership only to a current member using a current version.
- **FR-004**: HTTP requests MUST be rate-limited by authenticated identity or client address without storing plaintext credentials as partition keys.
- **FR-005**: JSON request bodies MUST have a documented maximum size.
- **FR-006**: Undefined enum values MUST be rejected.
- **FR-007**: Note deletion and permission changes MUST require current note preconditions.
- **FR-008**: Team rename, deletion, ownership transfer, invitation, and membership changes MUST require current team preconditions.
- **FR-009**: Readiness MUST verify connectivity, migration currency, and rollback-safe writeability.
- **FR-010**: Every request MUST have a validated or generated correlation identifier returned to the caller.
- **FR-011**: Request and database activities MUST be available through OpenTelemetry without requiring an external collector.
- **FR-012**: Audit events MUST carry verifiable keyed integrity tags.
- **FR-013**: Audit verification and retention MUST be explicit out-of-band operations; retention MUST be disabled by default.
- **FR-014**: The supported API MUST be available under `/api/v1`; legacy `/api` routes MUST remain compatible and advertise deprecation.
- **FR-015**: OpenAPI MUST document success plus common error responses for every operation.
- **FR-016**: CI MUST run formatting, warning, migration, dependency, secret, static-analysis, tests, coverage, and container-startup gates with immutable action references.
- **FR-017**: A production-like container definition and ignore file MUST be included.
- **FR-018**: Parallel HTTP write tests MUST exercise SQLite contention and prove bounded outcomes.
- **FR-019**: README MUST document bootstrap, recovery, ownership, limits, versioning, retention, integrity, tracing, and container operation honestly.

### Key Entities

- **User**: Owns credentials and can be bootstrapped or recovered by an operator.
- **Team**: Carries a concurrency version and transferable owner membership.
- **Note**: Existing version also guards deletion and permission mutations.
- **Security audit event**: Carries a keyed integrity tag over its immutable fields.
- **Readiness probe row**: A non-business sentinel used only inside rollback-safe write probes.

## Success Criteria

### Measurable Outcomes

- **SC-001**: An empty production database can become usable through one documented command, while every second bootstrap attempt fails.
- **SC-002**: In 100% of stale destructive/administrative mutation tests, newer state survives.
- **SC-003**: Oversized, invalid-enum, and over-limit requests produce zero domain writes.
- **SC-004**: Readiness fails within one probe for stale schema or unavailable writes while leaving sentinel data unchanged.
- **SC-005**: Every new audit event verifies successfully and any tested field mutation is detected.
- **SC-006**: Legacy and v1 endpoint behavior is equivalent, with deprecation metadata only on legacy routes.
- **SC-007**: The production-like image reaches readiness in CI and all existing coverage thresholds continue to pass.

## Assumptions

- Operating-system/database access is the trust boundary for bootstrap, recovery, audit verification, and retention.
- The assessment remains a single-instance SQLite service; distributed coordination is out of scope.
- Default retention is indefinite until an operator supplies an explicit cutoff.
- Existing unversioned clients remain supported during the documented deprecation window.
- OpenTelemetry console export is sufficient for a runnable assessment; deployments may select another exporter.
