<p align="center">
  <img src="src/Wartownik/Assets/wartownik.png" alt="Wartownik" width="128" height="128"/>
</p>

<h1 align="center">Wartownik</h1>

<p align="center">
  Cross-platform desktop GUI for managing PostgreSQL roles, users, and grants — with a strict guardrail that the app can <strong>only</strong> touch users and permissions, never your data or schema.
</p>

<p align="center">
  <a href="https://buycoffee.to/softime-pk" target="_blank">
    <img src="https://buycoffee.to/static/img/share/share-button-primary.png" width="234" height="61" alt="Postaw kawę dla SofTime - Piotr Krakowski na buycoffee.to"/>
  </a>
</p>

---

**Wartownik** (Polish for "sentinel") is an open-source MIT-licensed desktop tool that fills the gap between heavy DBA suites like pgAdmin (which can also drop your tables) and CLI-only tools like pgbedrock (which require a YAML pipeline). It is a focused GUI for one thing — PostgreSQL permission management — and it is **safe by construction**.

Works on Windows, Linux, and macOS as a single self-contained binary per platform.

## Why this exists

Managing PostgreSQL permissions is famously painful. Grants are per-database, default privileges are per-creator-role, schema changes silently break access for downstream roles, and there is no tool that gives you a focused GUI for exactly this concern. Wartownik solves that — and refuses to do anything else.

## What you get

- **Permissions matrix** — schema-level USAGE / CREATE / SELECT / INSERT / UPDATE / DELETE per role with per-cell toggles, transactional Apply, and a Preview SQL modal that shows the exact GRANT/REVOKE batch before it runs. Pending edits stay cached when you switch between users in the dropdown so you don't lose work.
- **Per-role apply** — review and ship one user's batch at a time, or apply all at once.
- **Audit log** — every Apply (success or failure) lands in a JSONL log on disk. Browse it in the SQL log tab grouped by day, expand any entry to see the exact statements that ran.
- **AT A GLANCE stats** — schemas, grants count, pending edits, login users on the cluster, last apply timestamp, and a heuristic risk indicator that flags if any login role on the cluster is also a SUPERUSER (they bypass every GRANT/REVOKE).
- **YAML export** — pgbedrock-compatible snapshot of the current state per database, one click.
- **Auto-update** — Velopack-powered updater pulls new versions from GitHub Releases on startup.

## Scope

The app **MAY**:
- Create, modify, drop roles (LOGIN and NOLOGIN)
- Change role passwords and attributes
- Execute `GRANT` and `REVOKE` on schemas, tables, sequences, functions, databases
- Manage role membership (`GRANT role TO user`)
- Configure `ALTER DEFAULT PRIVILEGES`
- Read metadata from system catalogs

The app **MUST NEVER**:
- Create, alter, or drop databases, schemas, tables, views, indexes, sequences, functions, triggers, or types
- Execute `INSERT`, `UPDATE`, `DELETE`, `TRUNCATE`, `MERGE`, `COPY`
- Run `VACUUM`, `REINDEX`, `CLUSTER`, `ANALYZE`
- Modify database configuration

This is enforced at runtime by an SQL statement whitelist in `PostgresSqlStatementValidator`, with an exhaustive test suite (420+ tests) that runs before any other code.

## Installation

Pre-built installers are published to [GitHub Releases](https://github.com/ja74687/wartownik/releases). Pick the latest tag and download:

- **Windows:** `Wartownik-win-Setup.exe` — installer (drops the app in `%LOCALAPPDATA%\Wartownik`, adds a Start menu shortcut, sets up auto-update)
- **Windows portable:** `Wartownik-win-Portable.zip` — no-install zip, just unpack and run

Linux and macOS builds are coming with a future release.

> The first time Windows shows a SmartScreen warning because the binary isn't yet code-signed — click **More info → Run anyway**. Code signing through SignPath/Certum is on the roadmap.

## Build from source

Requires the .NET 10 SDK.

```bash
git clone https://github.com/ja74687/wartownik
cd wartownik
dotnet build src/Wartownik/Wartownik.csproj -c Release
dotnet run  --project src/Wartownik
dotnet test tests/Wartownik.UnitTests/Wartownik.UnitTests.csproj
```

For producing your own installer with Velopack see [docs/INSTALLER.md](docs/INSTALLER.md).

## Security model

- **SQL whitelist** — every statement passes through `ISqlStatementValidator` before execution. Default-deny.
- **Two execution paths** — read-only metadata queries vs. permission-changing statements that run inside a transaction with rollback on error.
- **SQL preview** — every modifying batch can be previewed (the exact SQL that will run, grouped by role) before Apply.
- **Credentials in OS keystore** — passwords stored via DPAPI on Windows, libsecret on Linux, Keychain on macOS — never on disk in plain text.
- **TLS by default** — new connections default to `SslMode=Require`.
- **Identifiers quoted and escaped** — all identifiers go through `QuoteIdentifier()` before being interpolated into SQL.

## Languages

The UI is available in:

- English (default)
- Polish

Adding a new language only requires a new `.resx` file and a pull request.

## Project status

🟢 **MVP shipped (v0.1.0).** The core flow works end-to-end: connect → matrix → Apply → audit log → YAML export. Future work is tracked in [ROADMAP.md](ROADMAP.md).

## Support the project

If Wartownik saves you time, consider buying me a coffee — it keeps the lights on:

<p>
  <a href="https://buycoffee.to/softime-pk" target="_blank">
    <img src="https://buycoffee.to/static/img/share/share-button-primary.png" width="234" height="61" alt="Postaw kawę dla SofTime - Piotr Krakowski na buycoffee.to"/>
  </a>
</p>

## License

[MIT](LICENSE) © SofTime - Piotr Krakowski

## Contributing

PRs welcome — issue first for anything bigger than a typo. The project is firmly scoped (see *Scope* above), so feature requests outside permission management will be politely declined.
