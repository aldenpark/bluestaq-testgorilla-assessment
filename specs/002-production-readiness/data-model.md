# Data Model: Production Readiness

## User

- Add `CredentialVersion` integer, required, default `1`, concurrency token.
- Increment for API-key create, rotate, and revoke.
- A concurrency conflict returns `409` and leaves the winning transaction intact.

## Note

- Add `Visibility` string enum: `Team` or `Restricted`; required, default `Team`.
- Add `Version` integer, required, default `1`, concurrency token.
- Increment `Version` on content replacement.
- `Visibility` is immutable after creation.

## API-key credential

- Retain existing fields.
- Rotation copies `ExpiresAt` from the replaced credential.
- `LastUsedAt` updates only when absent or older than the configured interval.

## Security audit event

- Schema remains append-only.
- Newly committed rows are emitted as structured logs and increment a metric counter after persistence succeeds.

## Migration

- Existing users receive `CredentialVersion = 1`.
- Existing notes receive `Visibility = Team` and `Version = 1`.
- Downgrade removes the three new columns without changing existing content.
