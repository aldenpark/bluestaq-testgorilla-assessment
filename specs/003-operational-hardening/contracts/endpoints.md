# Contracts: Operational Hardening

## Versioned routes

- Every existing `/api/...` route also exists under `/api/v1/...`.
- Legacy responses include `Deprecation: true`, `Sunset`, and a successor `Link` header.

## Preconditions

- Team responses expose `version` and an ETag.
- Team administrative writes require `If-Match`; missing returns 428, stale returns 412.
- Note delete and permission writes require the current note ETag with the same status behavior.

## Ownership transfer

- `PUT /api[/v1]/teams/{teamId}/owner`
- Body: `{ "userId": "guid" }`
- Owner-only; target must be a current member.
- Success: 200 team response and new ETag.

## Limits and errors

- Rate rejection: 429 Problem Details with `Retry-After` when available.
- Oversized body: 413 Problem Details.
- Undefined enum: 400 validation Problem Details.
- OpenAPI lists common 400/401/403/404/409/412/413/428/429 responses as applicable.

## Administrative commands

- `bootstrap --display-name <name> --key-name <name>`
- `recover-credential --user-id <guid> --key-name <name>`
- `verify-audit`
- `prune-audit --before <UTC timestamp> --confirm`
- Secrets print once to standard output; failures return nonzero.
