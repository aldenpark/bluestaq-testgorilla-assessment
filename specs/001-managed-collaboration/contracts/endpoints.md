# HTTP Contract Changes

All routes except `/health` require `X-Api-Key`.

| Method | Route | Authorization | Result |
|---|---|---|---|
| POST | `/api/teams/{teamId}/invitations` | team owner | user, membership, one-time key |
| PATCH | `/api/teams/{teamId}` | team owner | renamed team |
| DELETE | `/api/teams/{teamId}` | team owner | 204 |
| GET | `/api/api-keys` | current user | key metadata only |
| POST | `/api/api-keys` | current user | metadata and one-time key |
| POST | `/api/api-keys/{keyId}/rotate` | key owner | replacement metadata and one-time key |
| DELETE | `/api/api-keys/{keyId}` | key owner | 204 unless final usable key |
| GET | `/api/notes/{noteId}/permissions` | creator/team owner | grants |
| PUT | `/api/notes/{noteId}/permissions/{userId}` | creator/team owner | reader/editor grant |
| DELETE | `/api/notes/{noteId}/permissions/{userId}` | creator/team owner | 204 |
| GET | `/api/security-events` | current user | personal security event page |
| GET | `/api/teams/{teamId}/security-events` | team owner | team security event page |

`GET /api/teams/{teamId}/notes` accepts `q`, `createdById`, `updatedAfter`,
`cursor`, and `pageSize`. It returns `{ items, nextCursor }`; cursor is null at the
end. Existing note create/get/update/delete and team membership routes remain.

Security-event listing accepts opaque `cursor` and `pageSize` (1-100), returning
`{ items, nextCursor }`. Details are structured sanitized JSON; secrets and hashes
are never returned or persisted.
