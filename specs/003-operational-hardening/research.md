# Research: Operational Hardening

## Administrative access

- **Decision**: Use explicit command mode with host/database access for bootstrap, recovery, audit verification, and retention.
- **Rationale**: It closes production lockout without creating anonymous or globally privileged HTTP routes.
- **Alternatives considered**: Public registration expands scope; environment auto-seeding can recreate predictable credentials.

## Abuse controls

- **Decision**: Use the built-in partitioned limiter after authentication, keyed by user ID or remote address, plus a small server body limit.
- **Rationale**: It avoids plaintext API-key partitions and external infrastructure.
- **Alternatives considered**: Header-key partitioning retains secrets in memory; distributed limiting conflicts with single-instance scope.

## Audit integrity

- **Decision**: HMAC each immutable audit event with a deployment key and verify out-of-band.
- **Rationale**: Independent tags survive retention and avoid cross-request chain races.
- **Alternatives considered**: Hash chains complicate concurrent insertion and pruning; unsigned hashes do not resist database-only attackers.

## API lifecycle

- **Decision**: Serve identical controllers under `/api/v1` and legacy `/api`, adding deprecation metadata to legacy responses.
- **Rationale**: It demonstrates lifecycle discipline without a new versioning framework or breaking existing clients.
- **Alternatives considered**: Replacing routes breaks clients; a package adds machinery for one version.

## Telemetry

- **Decision**: Register ASP.NET and application activity sources with OpenTelemetry and provide console export by configuration.
- **Rationale**: Traces are runnable without a collector while standard propagation remains available.
- **Alternatives considered**: Collector-only export harms quick start; bespoke tracing is non-standard.
