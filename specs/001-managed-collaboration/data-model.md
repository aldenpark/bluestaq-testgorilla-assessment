# Data Model: Managed Collaboration

## User

- `Id` GUID primary key
- `DisplayName` required, 100 characters
- has many memberships and API keys

## ApiKeyCredential

- `Id` GUID primary key; `UserId` foreign key
- `KeyHash` fixed 64-character unique digest
- `Name` required, 100 characters
- `CreatedAt`, optional `ExpiresAt`, optional `RevokedAt`, optional `LastUsedAt`
- deleting a user cascades credentials; revoked/expired rows remain auditable

## Team and TeamMembership

- Team retains one owner and cascades memberships, notes, and grants on deletion
- Membership remains unique by `(TeamId, UserId)`
- removing membership also deletes grants for that team/user

## NotePermission

- composite key `(NoteId, UserId)`
- role is `Reader` or `Editor`
- belongs to note and user; controller enforces current membership

## FTS index

- external-content index over note title and content using note SQLite `rowid`
- triggers keep index synchronized for insert, update, and delete

## SecurityAuditEvent

- `Id` GUID primary key with `OccurredAt` UTC ordering
- actor user and credential IDs are required snapshots, not foreign keys
- action, target type/ID, optional team ID, trace ID, and sanitized JSON details
- no foreign keys, so history survives deletion of users, keys, notes, and teams
- database triggers and DbContext guards reject updates and deletes

## Migration

1. Create API keys and note permissions.
2. Copy every legacy user hash into a named development credential.
3. Remove the legacy user hash column.
4. Create and backfill FTS5, then install synchronization triggers.
5. Create the audit-event table and append-only update/delete rejection triggers.
