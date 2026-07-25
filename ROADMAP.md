# Roadmap

The MVP (iterations 1-7) is shipped. This file tracks ideas for future work, plus
the deliberate scope cuts we made so we don't accidentally re-debate them.

## Shipped since the MVP

- **Confirm dialog before destructive Apply** — a batch containing revokes asks first.
- **Selective apply per pending change** — checkbox per staged change in the sticky bar.
- **Profile import/export** — drag a JSON file in to import; export writes one out
  (without the password, which stays in the OS keystore).
- **Role membership** — put a role in a group with `GRANT group TO member`, so privileges
  can be managed on the group and inherited by its members.
- **App settings** — a Settings screen; the chosen UI language now survives a restart.
- **Installer + auto-update** — Windows installer and portable zip via Velopack,
  wired to GitHub Releases.

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

- **Conflict detection on Apply** — if someone else changed permissions between
  load and apply, surface a diff before committing.

## Audit log

- **Bounded reader** — JSONL store currently slurps the whole file. When the log grows,
  read only the tail.
- **Retention policy** — auto-trim entries older than N days (configurable per profile
  in the Settings tab — placeholder exists already).
- **Replay** — re-execute a past batch (or its inverse) from the SQL log tab.
- **Search / filter** in the SQL log tab (by role, by date range, by outcome).

## Profile and database management

- **More app settings** — the Settings screen holds the UI language; default SSL mode and
  audit retention are the obvious next entries (the settings store takes new fields as-is).
- **Add/remove databases per profile** — mockup `02_profile_workspace` had
  "Add a database to this profile" dashed card; today it's visual only.
- **Background status refresh interval** — configurable; today it fires once on load.
- **Profile groups / favourites** in the sidebar.

## Multi-SQL

- The validator and grant service are PostgreSQL-specific today. The path forward
  (per `memory/project_pgperms.md`):
  refactor `PostgresService` → `IPermissionService` + `PostgresPermissionService`,
  keep `ISqlStatementValidator` generic, add MySQL/MariaDB/SQL Server impls behind
  the same interface.

## Distribution

- **Linux and macOS installers** — Windows ships today; the other two are still
  build-from-source only. See [INSTALLER.md](docs/INSTALLER.md).
- **Code signing** — once we have a publisher cert; not blocking the first release.

## Known limitations

- **Quoted identifiers in the SQL whitelist** — the validator strips `'...'` literals but not
  `"..."` identifiers, so a role or schema name containing `;` is refused as if it were two
  statements. It fails closed (never mis-executed), but such objects can't be managed.

## Out of scope (will not do)

These come up periodically but stay out:

- Schema migrations / DDL beyond role and grant management.
- Data editing.
- Backups, restores, monitoring.
- Anything outside permission management.

The whole point of Wartownik is doing one thing — Postgres permissions — well.
pgAdmin already does the rest.
