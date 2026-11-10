# Quickstart: Production Readiness Verification

1. Restore, format-check, and build the solution in Release mode.
2. Run unit, integration, smoke, migration, coverage, and vulnerability gates.
3. Start the Development profile and verify `/swagger`, `/health/live`, and `/health/ready`.
4. Create a restricted note; confirm another member cannot list or retrieve it.
5. Grant reader access; confirm retrieval succeeds and update remains forbidden.
6. Read a note ETag, update it, then retry with the stale ETag and confirm `412`.
7. Rotate an expiring key and confirm the expiration is preserved.
8. Capture structured logs while changing a permission and confirm one committed security record.
9. Follow the README backup, integrity check, restore, and rollback procedures on a disposable database.
