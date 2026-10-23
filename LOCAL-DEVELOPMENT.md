# Local Windows version

This repository preserves the original Snapline MIT license and upstream assets.
Upstream: https://github.com/mohabujubara/snapline

Local changes include DirectComposition screenshot flight and panel transitions,
cached rendering, normal/maximized-window detection, configurable auto-hide
(default 2.5 seconds), guarded clear-all confirmation, and per-login/day capture
lifecycle. Original screenshot files retain full quality. Large-capture compact
previews are optional and disabled by default.

## Build from a fresh clone

Requirements: Windows 10/11 x64, .NET 8 SDK, Internet for NuGet restore.
No local SDK directory, credentials, screenshot files or installed Snapline copy
is required. The project pins its Vortice dependencies in Snapline.csproj;
Windows SDK references are resolved by the .NET SDK. Font licenses and MIT
copyright notices remain in this repository.

```powershell
dotnet restore src/Snapline/Snapline.csproj
dotnet build src/Snapline/Snapline.csproj -c Release
dotnet publish src/Snapline/Snapline.csproj -c Release -r win-x64 --self-contained true -o dist/windows
.\dist\windows\Snapline.exe
```

Exit another running Snapline before starting a new normal build. Normal builds
use the existing LocalAppData/Snapline settings. Back that directory up before
testing new capture lifecycle changes. Do not commit personal screenshots or
settings. There are no API keys or server-side components required.

## Isolated regression checks

```powershell
$env:SNAPLINE_TEST_PROFILE='SnaplineDeveloperTests'
$exe='.\src\Snapline\bin\Release\net8.0-windows10.0.19041.0\Snapline.exe'
& $exe --session-selftest "$PWD\test-results\behavior.json"
& $exe --animation-bench retained "$PWD\test-results\flight.json"
```

These tests use synthetic images. Rendering callbacks and timeline durations
are not actual displayed FPS. Test animations only on an unlocked desktop.
Reboot/date boundaries have synthetic coverage, not an actual Windows reboot
acceptance test. Remove SNAPLINE_TEST_PROFILE before normal use.

## Continue with Codex

Open this checkout, inspect `git status` and the latest commits, then make scoped
changes. Build and test before `git add`, `git commit`, and `git push`. Never
force-push or reset away uncommitted work. Fetch and compare the remote first.
