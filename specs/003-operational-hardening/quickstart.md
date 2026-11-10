# Quickstart: Operational Hardening Verification

1. Bootstrap an empty Production database and authenticate using the printed secret.
2. Confirm a second bootstrap fails and recovery revokes the first secret.
3. Create a team, add a member, transfer ownership, and exercise stale ETags.
4. Verify 400, 413, 428, 429, and 412 negative paths create no domain writes.
5. Exercise legacy and `/api/v1` parity plus deprecation headers.
6. Verify correlation headers and capture an OpenTelemetry console trace.
7. Tamper with a copied audit row and confirm `verify-audit` fails.
8. Run a parallel HTTP write test and verify bounded success/conflict outcomes.
9. Build and run the container in Production; wait for `/health/ready`.
10. Run format, Release build, migration drift, all tests/categories, coverage, CVE, static analysis, and secret gates.
