# Research: Managed Collaboration

## EF Core and FTS5

**Decision**: Use an FTS5 external-content virtual table maintained by insert,
update, and delete triggers created in the EF migration. Query it through a
parameterized composable `FromSql` SELECT and apply team/security filters in LINQ.

**Rationale**: SQLite documents triggers as the synchronization mechanism for
external-content FTS5 tables. EF Core documents that parameterized raw SELECTs can
be composed with LINQ when valid as subqueries.

**Rejected**: `LIKE` filtering. It is simple but does not satisfy full-text search
and does not scale through a text index.

## Cursor shape

**Decision**: Base64url-encode JSON containing `UpdatedAt` and `Id`; order by both
descending and fetch `pageSize + 1` rows.

**Rationale**: The timestamp provides business ordering and the ID is a stable
tie-breaker. Opaque encoding keeps the wire contract evolvable.

## Credential lifecycle

**Decision**: Generate 256-bit random secrets, prefix them for recognition, store
SHA-256 hashes, and return plaintext only on create/rotate/invite.

**Rationale**: Random API keys do not need password hashing; a high-entropy secret
plus one-way digest permits indexed lookup without recoverable credential storage.

## Security audit durability

**Decision**: Write audit rows through the same DbContext save as the mutation,
retain snapshot IDs without foreign keys, and reject update/delete with both a
change-tracker guard and SQLite triggers.

**Rationale**: One transaction prevents a committed change without evidence, while
database enforcement protects immutability outside the application. Avoiding target
foreign keys preserves evidence when a team, permission, user, or key disappears.

**Rejected**: File or console logs. They are useful operational telemetry but do
not provide transactional durability, structured authorization, or local querying.
