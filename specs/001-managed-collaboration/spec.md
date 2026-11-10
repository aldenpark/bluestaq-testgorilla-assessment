# Feature Specification: Managed Collaboration

**Feature Branch**: `001-managed-collaboration`
**Created**: 2026-11-10
**Status**: Approved
**Input**: Replace fixture-only identity and broad team-member mutation rights with managed users, credentials, note roles, team administration, and scalable note discovery.

## User Scenarios & Testing

### User Story 1 - Managed access (Priority: P1)

A team owner invites a person and gives them a first API key. Each user can create,
list, rotate, and revoke their own keys without exposing stored secrets.

**Why this priority**: Identity lifecycle is the foundation for every other security boundary.

**Independent Test**: Invite a user, authenticate with the one-time key, rotate it,
and verify the old key immediately fails while the replacement succeeds.

**Acceptance Scenarios**:

1. **Given** an owner and a team, **When** the owner invites a display name, **Then** a team member and one plaintext API key are returned exactly once.
2. **Given** an active key, **When** its user rotates it, **Then** the old key is revoked and the new key authenticates.
3. **Given** another user, **When** they address a credential they do not own, **Then** the service reveals no credential data.

---

### User Story 2 - Least-privilege notes (Priority: P1)

Team members can read notes, while creators and team owners control destructive
actions and explicit reader/editor grants control note updates.

**Why this priority**: Broad write/delete access is the largest current authorization gap.

**Independent Test**: Create a note, verify an ordinary member cannot update it,
grant editor access, verify update succeeds, and verify delete remains denied.

**Acceptance Scenarios**:

1. **Given** an ordinary member, **When** they update or delete another member's note, **Then** access is denied.
2. **Given** an editor grant, **When** that member updates the note, **Then** the update succeeds but deletion remains denied.
3. **Given** a removed team member, **When** they authenticate, **Then** team and note access is denied and their note grants are removed.

---

### User Story 3 - Team administration (Priority: P2)

Owners can rename and delete teams; non-owners cannot.

**Independent Test**: Rename and delete a team as owner and repeat both requests as a member.

**Acceptance Scenarios**:

1. **Given** a team owner, **When** they rename the team, **Then** future reads show the new name.
2. **Given** a non-owner, **When** they rename or delete the team, **Then** access is denied.
3. **Given** a deleted team, **When** its former URL is requested, **Then** it is not found and dependent data is removed.

---

### User Story 4 - Discover notes (Priority: P2)

A member filters notes by full-text query, creator, and update time, and traverses
stable cursor-based pages without offset drift.

**Independent Test**: Seed ordered notes, filter them, then follow cursors until every matching note appears once.

**Acceptance Scenarios**:

1. **Given** matching title or content terms, **When** a member searches, **Then** only matching notes from that team are returned.
2. **Given** more matches than the page size, **When** the next cursor is followed, **Then** the next stable slice is returned without duplicates.
3. **Given** a malformed cursor, **When** notes are listed, **Then** validation fails without server error.

---

### User Story 5 - Review security changes (Priority: P1)

Users can review security changes affecting their identity, and team owners can
review changes to their team's access boundary. Audit history cannot be edited or
removed through the application and survives deletion of the affected resource.

**Why this priority**: Security controls need durable evidence showing who changed access and when.

**Independent Test**: Perform credential, membership, and permission changes; list
the resulting events as the affected user and team owner; then delete the team and
verify its events remain immutable and contain no secrets.

**Acceptance Scenarios**:

1. **Given** a successful security change, **When** the same transaction commits, **Then** one sanitized audit event records actor, credential, action, target, team, time, and trace identifier.
2. **Given** a team owner, **When** they list team security events, **Then** they receive newest-first cursor pages; a non-owner is denied.
3. **Given** a user, **When** they list personal security events, **Then** they see changes they performed or that directly affected them.
4. **Given** an audit event, **When** application or direct database code attempts to update or delete it, **Then** the operation fails.

### Edge Cases

- Duplicate display names are allowed because user IDs are authoritative.
- A user cannot revoke their final usable credential without creating a replacement.
- Expired and revoked credentials never authenticate.
- Note grants can target only current members and cannot grant ownership/delete rights.
- Owners cannot remove themselves from their own team.
- Search terms are treated as literal text, not executable query syntax.
- Audit details never contain plaintext API keys, credential hashes, note content, or other secrets.
- Rejected requests do not claim that a security state change occurred.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST allow a team owner to invite a new user into that team and issue a one-time API key.
- **FR-002**: The system MUST store only API-key hashes and MUST support multiple named keys per user with optional expiry and revocation.
- **FR-003**: A user MUST be able to list, create, rotate, and revoke only their own credentials.
- **FR-004**: The system MUST reject revoked and expired credentials immediately.
- **FR-005**: All team members MAY read team notes; only creators, team owners, or explicit editors MAY update them.
- **FR-006**: Only note creators and team owners MAY delete notes or manage reader/editor grants.
- **FR-007**: Note grants MUST apply only to current team members and MUST be removed with membership.
- **FR-008**: Only a team owner MAY rename or delete a team.
- **FR-009**: Note listing MUST support literal full-text search, creator and updated-since filters, and opaque cursor pagination.
- **FR-010**: Cursor traversal MUST use deterministic newest-update-first ordering with an ID tie-breaker.
- **FR-011**: Existing development users and keys MUST remain usable after migration.
- **FR-012**: All new route families and security boundaries MUST have HTTP integration coverage and smoke coverage.
- **FR-013**: Every successful invitation, credential create/rotate/revoke, membership add/remove, note-permission set/remove, team create, and team delete MUST append a security audit event atomically with the change.
- **FR-014**: Audit events MUST contain the actor user and credential, action, target, optional team, UTC occurrence time, request trace identifier, and sanitized details.
- **FR-015**: Audit events MUST remain readable after target deletion and MUST reject update and delete operations.
- **FR-016**: Team audit history MUST be visible only to the current team owner; personal history MUST be limited to changes performed by or directly affecting the caller.
- **FR-017**: Audit history MUST support deterministic newest-first cursor pagination.

### Key Entities

- **User**: A person identified by an immutable ID and display name.
- **API Key Credential**: A named, hashed secret owned by one user with creation, expiry, revocation, and last-use metadata.
- **Team**: An owner-controlled collaboration boundary with members and notes.
- **Team Membership**: A user's current access to a team.
- **Note**: Team-scoped content with creator and last-updater identities.
- **Note Permission**: A reader or editor grant for a current team member on one note.
- **Security Audit Event**: Immutable evidence of one committed security state change, stored without foreign keys to mutable targets.

## Success Criteria

### Measurable Outcomes

- **SC-001**: No API response after credential creation or rotation can retrieve the plaintext secret again.
- **SC-002**: Every authorization rule has at least one successful and one denied integration scenario.
- **SC-003**: Following returned cursors yields every matching note exactly once for an unchanged dataset.
- **SC-004**: Every documented endpoint is exercised by an automated smoke test.
- **SC-005**: A migration upgrades existing seeded data without losing authentication access.
- **SC-006**: Build, formatting, all tests, and dependency vulnerability audit pass from documented commands.
- **SC-007**: Every in-scope successful security mutation produces exactly one expected event without plaintext keys or hashes.
- **SC-008**: Audit update and delete attempts fail through both application persistence and direct database commands.
- **SC-009**: Following audit cursors yields every visible event exactly once for an unchanged dataset.

## Assumptions

- Invitation secrets are delivered out of band by the owner; email delivery is out of scope.
- Team ownership remains single-owner and is not transferable in this iteration.
- SQLite is appropriate for the assessment and small-team deployment scope.
- API-key authentication remains the service's only authentication mechanism.
