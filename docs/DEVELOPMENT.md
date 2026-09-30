# DogsEye developer guide

[Back to README](../README.md)

## Prerequisites

- Windows x64.
- .NET 10 SDK with WPF support.
- Visual Studio Build Tools with the MSVC x64/x86 C++ toolchain and a Windows SDK.
- Tobii Game Integration SDK locally under `sdk/`.
- Tobii device software and a supported connected tracker for live checks.

The supplied SDK version used during development is 9.0.4.26. The native build expects:

```text
sdk/
  include/tobii_gameintegration.h
  lib/tobii_gameintegration_x64.lib
  lib/tobii_gameintegration_x64.dll
```

The SDK is intentionally Git-ignored. Obtain and use it under its applicable vendor licence; it is not included in repository source.

## Build, run, and publish

Run commands from the repository root:

```powershell
# Build Release and run the acceptance suite
.\scripts\build.ps1

# Development launch; compiles changed source
dotnet run --project src/DogsEye.App

# Build/test and create a self-contained Windows x64 folder
.\scripts\build.ps1 -Publish

# Launch an already-published build without compiling
.\artifacts\DogsEye\DogsEye.exe
```

The native adapter is built by the application project when its source, SDK header, or build script is newer than the adapter output. `scripts/build-native.ps1` locates Visual Studio Build Tools through `vswhere`.

The publishing script copies the README, validation record, and applicable local SDK agreement files into `artifacts/DogsEye`. To include the detailed documentation and screenshots in a distributable folder, copy `docs/` alongside the README:

```powershell
Copy-Item -Recurse -Force docs artifacts/DogsEye/
```

Keep the self-contained output files together. Updating source does not update an existing executable until another build/publish is performed.

## Verification

The acceptance suite is an executable with no test-framework dependency:

```powershell
dotnet run --project tests/DogsEye.Tests -c Release
```

It simulates poses and records output instead of moving the Windows cursor. The latest compiled revision passed 44 tests. Coverage includes calibration, recenter, disabled previews, tracking loss, bounded output, response curves, smoothing, settings persistence, key commands, and simulated 1000 Hz cadence.

For a ten-second preview-only integration capture, launch an existing build:

```powershell
.\artifacts\DogsEye\DogsEye.exe --smoke
```

Smoke mode uses `artifacts/smoke-settings`, suspends global-key actions, and cannot enable Windows mouse output. It attempts activation and recenter, checks overlay behaviour, then exits. It writes:

- `artifacts/smoke-report.json`
- `artifacts/smoke-window.png`
- `artifacts/smoke-settings.png`

Run it from the repository root so output lands in the expected folder. Head visibility is needed for live calibration checks. A successful build or smoke check does not prove correct in-game camera limits or actual 1000 Hz timing. See [validation history](../VALIDATION.md).

## Architecture

| Location | Responsibility |
| --- | --- |
| `src/DogsEye.Core/AppSettings.cs` | Validated configuration and JSON persistence. |
| `src/DogsEye.Core/Tracking.cs` | Pose processing, calibration, state machine, targets, interpolation, and bounded output. |
| `src/DogsEye.Core/LookResponseCurve.cs` | Smooth monotone response through a single control point; diagonal points are linear. |
| `src/DogsEye.Core/AdaptiveTrackingFilter.cs` | Adaptive filtering of measured head angles. |
| `src/DogsEye.Core/InputCommandService.cs` | Key edges, hold timing, and toggle/recenter commands. |
| `src/DogsEye.App/TrackingRuntime.cs` | Dedicated worker, command queue, SDK polling, output mode, and snapshots. |
| `src/DogsEye.App/TrackingLoopTimer.cs` | Interruptible Windows waitable timer for polling/output cadence. |
| `src/DogsEye.App/MainWindow.xaml*` | Settings UI, persistence workflow, diagnostics, and smoke capture. |
| `src/DogsEye.App/HeadTrackingVisualizer.cs` | Fixed-scale relative head and mouse-target display. |
| `src/DogsEye.App/LookCurveEditor.cs` | Draggable curve point, preview drawing, and keyboard adjustment. |
| `src/DogsEye.App/StatusOverlayWindow.cs` | Movable overlay, click-through behaviour, and monitor recovery. |
| `native/DogsEye.Tobii.cpp` | Small C ABI adapter over the vendor SDK. |
| `tests/DogsEye.Tests` | Recording-output acceptance checks. |

The processing path is:

```text
Tobii pose → subtract centre → deadzone → smoothing
           → normalise head range → response curve
           → mouse counts × safety fraction → bounded target
           → interpolate → relative mouse delta
```

The UI receives worker snapshots. Runtime changes are queued onto the worker. Ordinary tuning retains centre and position; changing output mode rebuilds the controller and stops tracking. A disabled controller computes preview telemetry without queuing or emitting movement.

## Operating boundaries

Mouse output uses ordinary relative `SendInput`. DogsEye does not access game memory, inject into a process, hook rendering, inspect network traffic, or manage the game’s free-look binding. The controller estimates relative camera movement; it does not read the actual game camera or constrain the desktop cursor.

Configuration and logs use `%LOCALAPPDATA%\DogsEye`. Raw pose samples are not written to the normal log. The live-output choice and active state are not persisted.

## Repository and privacy hygiene

The repository ignores `sdk/`, `artifacts/`, `bin/`, `obj/`, `tmp/`, `.vs/`, and user-specific project files. Documentation images live in `docs/images` so Markdown links work after cloning.

Before committing, inspect the staged file list and changes. Keep absolute personal paths, email addresses, local settings, logs, and unreviewed screenshots out of source. Git author/committer identities are separate from source content: use the intended public or pseudonymous identity before creating commits. Do not publish vendor SDK files just because they exist locally.

No repository licence is currently supplied; choose a project licence before describing the code as licensed for public reuse.

