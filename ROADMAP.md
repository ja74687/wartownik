# Roadmap

The MVP (iterations 1-7) is shipped. This file tracks ideas for future work, plus
the deliberate scope cuts we made so we don't accidentally re-debate them.

## Permissions matrix — depth

- **Per-table object overrides** (mockup `04_permissions_matrix` showed expandable rows
  per table). The matrix today is schema-level only.
- **Default ACL toggle** as a separate view (mockup showed `Schema-level | Default ACL`
  segmented control). Today defaults are coupled to the DML privileges.
- **Column-level ACLs**.
- **Sequence and function privileges**.
- **"Copy privileges from..."** — apply role A's permissions to role B with one click.
- **Filter** schemas/tables by name in the matrix.

## Permissions matrix — flow

- **Selective apply per pending change** — checkbox per pending change in the sticky bar
  so the user can apply only a subset (currently you apply per role or all-at-once).
- **Conflict detection on Apply** — if someone else changed permissions between
  load and apply, surface a diff before committing.
- **Confirm dialog before destructive Apply** — when the batch contains many revokes
  or touches owner-level objects, double-check.

## Audit log

- **Bounded reader** — JSONL store currently slurps the whole file. When the log grows,
  read only the tail.
- **Retention policy** — auto-trim entries older than N days (configurable per profile
  in the Settings tab — placeholder exists already).
- **Replay** — re-execute a past batch (or its inverse) from the SQL log tab.
- **Search / filter** in the SQL log tab (by role, by date range, by outcome).

## Profile and database management

- **Profile settings tab** (notifications, defaults, audit retention) — placeholder ships;
  needs real content.
- **Add/remove databases per profile** — mockup `02_profile_workspace` had
  "Add a database to this profile" dashed card; today it's visual only.
- **Background status refresh interval** — configurable; today it fires once on load.
- **Profile import/export** — drag-and-drop JSON already imports; export is missing.
- **Profile groups / favourites** in the sidebar.

## Multi-SQL

- The validator and grant service are PostgreSQL-specific today. The path forward
  (per `memory/project_pgperms.md`):
  refactor `PostgresService` → `IPermissionService` + `PostgresPermissionService`,
  keep `ISqlStatementValidator` generic, add MySQL/MariaDB/SQL Server impls behind
  the same interface.

## Distribution

- **Installer** for Windows / macOS / Linux — see [INSTALLER.md](docs/INSTALLER.md).
- **Auto-update** — Velopack pipeline tied to GitHub Releases.
- **Code signing** — once we have a publisher cert; not blocking the first release.

## Out of scope (will not do)

These come up periodically but stay out:

- Schema migrations / DDL beyond role and grant management.
- Data editing.
- Backups, restores, monitoring.
- Anything outside permission management.

The whole point of Wartownik is doing one thing — Postgres permissions — well.
pgAdmin already does the rest.
