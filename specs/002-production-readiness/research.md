# Research: Production Readiness

## Note concurrency

- **Decision**: Use an integer concurrency token exposed as a strong entity tag and require `If-Match` for note replacement.
- **Rationale**: It is deterministic on SQLite and makes lost-update prevention visible in the HTTP contract.
- **Alternatives considered**: Timestamp comparison is vulnerable to precision issues; unconditional writes preserve the current bug.

## Reader semantics

- **Decision**: Add immutable team or restricted visibility, defaulting existing and new notes to team visibility.
- **Rationale**: Existing sharing remains compatible while reader grants gain an observable purpose.
- **Alternatives considered**: Removing reader weakens the requested product model; making every note restricted breaks existing team sharing.

## Credential serialization

- **Decision**: Add a user credential-version concurrency token incremented by every credential mutation.
- **Rationale**: Conflicting key operations fail atomically and can be retried against current state.
- **Alternatives considered**: Process locks do not work across instances; check-then-write alone permits account lockout.

## Security telemetry

- **Decision**: Export committed audit events through a save interceptor to structured logs and built-in metrics.
- **Rationale**: Logs integrate with external collectors without adding infrastructure, and post-commit emission avoids false evidence.
- **Alternatives considered**: Logging before save can report rolled-back actions; a broker violates the assessment constraints.

## Retry behavior

- **Decision**: Document create operations as non-idempotent and replacement/deletion semantics explicitly rather than caching secrets.
- **Rationale**: Durable response replay for key creation would require storing recoverable credential material.
- **Alternatives considered**: An in-memory idempotency cache is unreliable; database response caching expands secret exposure.
