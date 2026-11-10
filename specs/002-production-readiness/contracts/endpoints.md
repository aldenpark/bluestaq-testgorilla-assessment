# Endpoint Contract Changes

## Notes

- `POST /api/teams/{teamId}/notes` accepts optional `visibility` (`team` default or `restricted`) and returns `ETag`.
- `GET /api/teams/{teamId}/notes` omits restricted notes the caller cannot read.
- `GET /api/notes/{noteId}` returns `404` for inaccessible restricted notes and includes `ETag` on success.
- `PUT /api/notes/{noteId}` requires `If-Match`; missing returns `428`, stale returns `412`, success returns a new `ETag`.
- Note responses include `visibility` and `version`.

## Credentials

- Rotation preserves expiration.
- Concurrent credential mutations return `409` and may be retried after refreshing key metadata.
- The final usable credential cannot be revoked, including under concurrent requests.

## Health

- `GET /health` remains a backward-compatible liveness alias.
- `GET /health/live` reports process liveness without dependency checks.
- `GET /health/ready` reports `503` when SQLite cannot be opened or queried.

## Retry semantics

- `GET`, permission `PUT`, and successful `DELETE` operations are safe to retry.
- Resource-creation `POST` operations are not safe to retry after an unknown outcome.
- Note replacement is safe only with the same current entity tag; a repeated stale request returns `412`.
