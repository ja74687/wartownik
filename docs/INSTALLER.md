# Installer & auto-update

Wartownik ships through [Velopack](https://github.com/velopack/velopack) — a free,
MIT-licensed installer + auto-updater that publishes to GitHub Releases. No paid
infrastructure, no MSIX certificate, no extra server.

The release flow is:

1. Tag a version → GitHub Actions builds a self-contained binary per platform.
2. Velopack's `vpk pack` turns the publish folder into the proper installer assets.
3. `vpk upload github` pushes them to a GitHub Release.
4. Every running installed copy of Wartownik checks the release feed and auto-updates.

## First-time release (manual, locally)

This is the path to use the first time you ship a build, before you set up CI.

### 1. Install the Velopack CLI once

```bash
dotnet tool install -g vpk
```

### 2. Build a self-contained publish folder

```bash
dotnet publish src/Wartownik/Wartownik.csproj `
  -c Release `
  -r win-x64 `
  --self-contained `
  -o publish/win-x64
```

(Use `osx-x64` / `osx-arm64` / `linux-x64` for the matching platforms — see the
[Velopack docs](https://docs.velopack.io/) for the full list.)

### 3. Pack into a Velopack release

```bash
vpk pack `
  --packId Wartownik `
  --packVersion 0.1.0 `
  --packDir publish/win-x64 `
  --mainExe Wartownik.exe `
  --packTitle "Wartownik" `
  --packAuthors "SofTime - Piotr Krakowski" `
  --icon src/Wartownik/Assets/wartownik.ico
```

Output lands in `Releases/`:
- `Wartownik-win-Setup.exe` — the installer end users download
- `Wartownik-win-Portable.zip` — no-install portable ZIP
- `Wartownik-0.1.0-full.nupkg` — the Velopack delta package
- `releases.win.json` + `RELEASES` — manifests the updater reads

### 4. Push to GitHub Releases

```bash
vpk upload github `
  --repoUrl https://github.com/ja74687/wartownik `
  --tag v0.1.0 `
  --releaseName "Wartownik 0.1.0" `
  --token $env:GITHUB_TOKEN
```

`GITHUB_TOKEN` needs `repo` scope (Settings → Developer settings → Personal
access tokens → Fine-grained, scoped to this repo).

End users download `Wartownik-Setup.exe` from the Release page → installer drops
the app in `%LOCALAPPDATA%\Wartownik` → Start menu shortcut + uninstaller appear.

## Subsequent releases

Bump `--packVersion`, repeat steps 2-4. Velopack diffs against the previous version
on the same channel, so users only download the deltas.

## CI/CD via GitHub Actions

See [`.github/workflows/release.yml`](../.github/workflows/release.yml). The
workflow runs on tag push (`v*`) and does steps 2-4 for `win-x64` automatically.
Add more `runs-on` matrix entries for macOS / Linux when you want them.

## Auto-update from inside the app

The hook is already wired:

- `Program.cs` calls `VelopackApp.Build().Run()` so the updater can intercept its
  special command-line arguments early.
- `IUpdateService` (DI-registered as `VelopackUpdateService`) exposes
  `CheckForUpdatesAsync` / `DownloadAsync` / `ApplyAndRestart`.
- `IsInstalled` is `false` under `dotnet run` and the IDE — every method short-circuits
  so dev workflows aren't disturbed.

A future iteration plugs this into the UI (e.g. a "Check for updates" button in the
Settings tab, or a banner when an update is detected). For now it's a service you
can call from anywhere via DI.

## Code signing

The first releases will be unsigned — Windows will show a SmartScreen warning until
the binary builds reputation, and macOS will require a right-click-Open. That's
acceptable for an open-source tool.

When ready, options:

- **SignPath.io** — free for OSS projects, signs Windows binaries with their cert.
- **Certum Open Source** (~$30/yr) — gives a code signing certificate.
- **DigiCert / Sectigo** — commercial, ~$200-400/yr, EV variant clears SmartScreen instantly.

Velopack accepts a signing config; see their docs for details. No code change needed
in the app itself.

## Troubleshooting

- **"VelopackApp.Run() must be called before any other code"** — make sure it's the
  very first call in `Main`. Anything that touches the file system or Avalonia
  before it can break the install/uninstall hooks.
- **`vpk` complains about the `--mainExe`** — needs to match the `.exe` name in
  the publish folder, which is `Wartownik.exe` for us.
- **Update check returns null in dev** — by design. `IsInstalled` is `false` outside
  an installed channel; spin up a packed release and run that to test the updater.
