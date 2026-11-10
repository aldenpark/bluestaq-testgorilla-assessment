# Data Model: Operational Hardening

## Team

- Add integer `Version`, initialized to 1 and configured as a concurrency token.
- Increment for rename, ownership, invitation, membership, and deletion preconditions.

## SecurityAuditEvent

- Add required fixed-length `IntegrityHash` containing lowercase HMAC-SHA256 hex.
- Hash canonical immutable fields and details; never include a plaintext credential.
- Existing rows are backfilled during migration using an explicit legacy marker and are reported as unverifiable legacy events.

## ReadinessProbe

- Singleton row with ID 1 and UTC timestamp.
- Readiness updates it inside a transaction that is always rolled back.
- No endpoint exposes it.

## State transitions

- Team ownership: current owner -> current non-owner member; membership remains intact.
- Credential recovery: all usable credentials -> revoked; one replacement -> usable.
- Audit retention: ordinary append-only state -> explicit maintenance deletion before cutoff; disabled by default.
