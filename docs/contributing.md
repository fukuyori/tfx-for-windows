# Contributing to tfx for Windows

Thanks for your interest in contributing. This guide covers the local development workflow, test commands, project layout conventions, and CI expectations.

For the longer-term plan, see [docs/roadmap.md](roadmap.md).

---

## Prerequisites

- Windows 10 22H2 or Windows 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — preinstalled on Windows 11. Required for the Markdown / HTML preview to render; the app falls back to source view if missing.
- Optional: Visual Studio 2022 / 2025 with the *.NET desktop development* workload, or JetBrains Rider, or VS Code with the C# extension.

The build is plain `dotnet`, so the SDK alone is enough — Visual Studio is not required.

---

## Repository Layout

```text
Tfx.csproj                          Main WPF application (WinExe, net10.0-windows)
Tfx.Core/Tfx.Core.csproj            Pure-logic library used by Tfx and Tfx.Tests
                                    (net10.0, no WPF / no Windows-only dependencies)
Tfx.Tests/Tfx.Tests.csproj          xUnit tests (net10.0)
tfx.sln                             Solution wiring the three projects
.github/workflows/build.yml         CI: restore + build + test on windows-latest
docs/                               Project documentation
scripts/                            Build / release helper scripts
src/                                Application code (partial-class MainWindow,
                                    services, models, controls)
```

The split between **Tfx** (WPF) and **Tfx.Core** (pure logic) lets the test project run without depending on WPF or any Windows-only assembly. Move new pure-logic types into `Tfx.Core` whenever practical so they can be covered by `Tfx.Tests` directly.

---

## Build

```powershell
dotnet build tfx.sln
```

Or build the application alone:

```powershell
dotnet build Tfx.csproj
```

Run it from the IDE or:

```powershell
dotnet run --project Tfx.csproj -- "C:\path\to\folder"
```

The first command-line argument, when it points at an existing directory, becomes the initial folder of the left pane. See `README.md` for the full initial-folder resolution order.

---

## Tests

Run the suite from the repo root:

```powershell
dotnet test
```

Run just the Windows test project:

```powershell
dotnet test Tfx.Tests/Tfx.Tests.csproj
```

The test project is `Tfx.Tests` (xUnit). It references `Tfx.Core` only — not the WPF application — so tests do not need a graphical environment to run.

### Adding tests

- Put new tests under `Tfx.Tests/`, one file per class under test (`ArchivePathTests.cs`, `FsHelpersTests.cs`, etc.).
- Use `[Fact]` for self-contained tests and `[Theory] + [InlineData]` for parameterized cases.
- New public mutators on long-lived state (e.g. settings, navigation history, archive helpers) should ship with at least one focused test.
- Tests must not require manual setup. File-system tests should use `Path.GetTempPath()` and clean up after themselves.

### Bench / performance probes

Performance benchmarks live alongside the unit tests in [`Tfx.Tests/Benchmarks/`](../Tfx.Tests/Benchmarks/). They are regular `[Fact]` tests but print per-iteration timings via `ITestOutputHelper` and **never assert** — comparison is manual against rolling baselines on the same machine.

Run them alongside the rest of the suite:

```powershell
dotnet test Tfx.Tests/Tfx.Tests.csproj
```

Or just the benchmarks, with their timings visible:

```powershell
dotnet test Tfx.Tests/Tfx.Tests.csproj --filter "FullyQualifiedName~PerformanceBenchmarks" --logger "console;verbosity=detailed"
```

Enable runtime tracing in the application itself by either:

- Setting the environment variable `TFX_PERFORMANCE_LOGS=1` before launching `Tfx.exe` / `dotnet run`. The env var wins over the in-app setting so CI and scripted runs do not have to flip a flag.
- Toggling the persisted setting `ShowPerformanceLogs` in `%APPDATA%\tfx\settings.json`.

When tracing is on, `PerformanceTrace.Begin(...)` and `Measure(...)` calls in hot paths (`DirectoryLoader.Load`, `PreviewLoader.Load`, `ApplySearchFilter`, `CsvParser.Parse`, `JsonPrettyPrinter.TryPrettyPrint`) print one line each to `Debug` and the console:

```text
[tfx perf] DirectoryLoader.Load(Downloads)               12.345 ms
```

---

## CI

`.github/workflows/build.yml` runs on every push to `main`, every pull request, and on manual dispatch. It executes on `windows-latest` and performs:

1. Checkout
2. Setup .NET 10 SDK
3. `dotnet restore tfx.sln`
4. `dotnet build tfx.sln --configuration Release --no-restore`
5. `dotnet test Tfx.Tests/Tfx.Tests.csproj --configuration Release --no-build` with TRX output
6. Upload `artifacts/test-results/` as a `test-results` artifact (7-day retention)

`concurrency` cancels superseded runs on the same ref so a follow-up push doesn't queue behind an in-flight run.

Failing CI is a release blocker. Open a PR rather than pushing directly to `main` if your change might affect builds or tests, so CI exercises the change in isolation first.

---

## Code Style

- Follow the existing patterns in `src/` — partial-class `MainWindow.*.cs` files, `Pane`-based helpers, async event handlers as `async void`, cancellation tokens on long-running work.
- No new compiler warnings or WPF binding errors at build time.
- Avoid `using` directive churn — keep imports tight.
- Localization strings go through `Loc.T(...)` / `Loc.F(...)` with both English and Japanese entries.
- Run `dotnet build` before opening a PR to catch warnings.

---

## Release Process

To be expanded once auto-update (§2.11 in the roadmap) lands. For now:

1. Bump `<Version>` in `Tfx.csproj`.
2. Update `README.md`, `CHECKLIST.md`, and the status-bar example in the README.
3. Add a new section to `CHANGELOG.md`.
4. Run `dotnet build` and `dotnet test` locally; ensure CI is green.
5. Tag the commit (`git tag vX.Y.Z`) and push.
6. Run `scripts\build-release.ps1` to publish a self-contained executable to `artifacts\release\`.
7. Upload the artifact to a GitHub Release.

---

## Reporting Issues

Open an issue at <https://github.com/fukuyori/tfx-for-windows/issues> with:

- tfx version (from the status bar).
- Windows build (`winver`).
- WebView2 Runtime status if the issue involves Markdown / HTML preview.
- Steps to reproduce, expected vs. actual behavior, and any relevant log output (set `TFX_PERFORMANCE_LOGS=1` for verbose timings).
