# Wartownik

> Cross-platform desktop GUI for managing PostgreSQL roles, users, and grants — with a strict guardrail that the app can ONLY touch users and permissions, never your data or schema.

**Wartownik** (Polish for "sentinel") is an open-source MIT-licensed desktop tool that fills the gap between heavy DBA suites like pgAdmin (which can also drop your tables) and CLI-only tools like pgbedrock (which require a YAML pipeline). It is a focused GUI for one thing: PostgreSQL permission management — and it is **safe by construction**.

Works on Windows, Linux, and macOS as a single self-contained binary per platform.

## Why this exists

Managing PostgreSQL permissions is famously painful. Grants are per-database, default privileges are per-creator-role, schema changes silently break access for downstream roles, and there is no tool that gives you a focused GUI for exactly this concern. Wartownik solves that — and refuses to do anything else.

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

This is enforced at runtime by an SQL statement whitelist in `SqlStatementValidator`, with an exhaustive test suite that runs before any other code.

## Installation

_Coming soon — pre-built binaries for Windows / Linux / macOS will be published to GitHub Releases once v0 is ready._

For now, build from source:

```bash
git clone https://github.com/<owner>/wartownik
cd wartownik
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true     # Windows
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true   # Linux
dotnet publish -c Release -r osx-arm64 --self-contained -p:PublishSingleFile=true   # macOS Apple Silicon
```

Requires .NET 10 SDK to build.

## Security model

- **SQL whitelist:** every statement passes through `ISqlStatementValidator` before execution. Default-deny.
- **Two execution paths:** read-only queries open a `SET TRANSACTION READ ONLY`; permission statements run inside a transaction with rollback on error.
- **SQL preview:** every modifying operation requires explicit user confirmation showing the exact SQL that will run.
- **Credentials in OS keystore:** passwords stored via DPAPI on Windows, libsecret on Linux, Keychain on macOS — never on disk in plain text.
- **TLS by default:** new connections default to `SslMode=Require`.
- **Identifiers quoted and escaped:** all identifiers go through `QuoteIdentifier()` before being interpolated into SQL.

## Languages

The UI is available in:

- English (default)
- Polish

Adding a new language only requires a new `.resx` file and a contribution PR.

## Project status

🚧 **Under active early development.** v0 is in progress. Expect breaking changes until 1.0.

## License

[MIT](LICENSE)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) (coming soon).
