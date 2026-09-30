# DogsEye

DogsEye turns Tobii head movement into bounded relative mouse movement for free-look controls in games. It is a Windows x64 desktop app built with .NET 10 and WPF, using the Tobii Game Integration SDK.

Head yaw controls horizontal movement; head pitch controls vertical movement. Eye gaze does not control the camera. DogsEye runs separately from the game and sends ordinary Windows mouse movement.

![DogsEye main window with tracking off](docs/images/main-window.png)

*Captured from the latest existing executable with tracking OFF. Live output is selected, but no movement is emitted while off. The displayed 1000 Hz is a custom saved setting; new configurations default to 500 Hz.*

## Quick start

1. Connect your Tobii tracker and enable tracking in Tobii Experience.
2. Run `DogsEye.exe` from `artifacts/DogsEye`. Keep its accompanying files together.
3. Check that the device is detected and the head pose is valid. The app starts with tracking **OFF** and mouse output in **preview** mode.
4. Face a comfortable direction and click **Recenter**. This works while tracking is off.
5. Move your head and inspect the blue head-position dot, yellow response dot, and red limits.
6. To use mouse output, select **Enable relative mouse output**, then **Enable tracking**. Activation calibrates a new centre. Enable the game's own free-look control separately.
7. Tap the configured key to recenter, or hold it to toggle tracking. The default is the backtick key (`Oem3`) with a three-second hold.

Valid settings changes save automatically after a short pause. There is no Apply button.

## Response curves

![Independent X and Y response curve editors](docs/images/response-curves.png)

*Example: X is shaped for gentler early response (42% head / 27% turn); Y is neutral (50% / 50%).*

## Understand the three tuning controls

| Control | Meaning |
| --- | --- |
| Head movement to reach full turn | Physical head angle from centre needed to reach the output limit. Lower values mean more sensitivity. |
| Camera limits | Maximum mouse counts in each direction, multiplied by the corresponding safety factor. These are not screen coordinates or camera degrees. |
| X/Y response curves | Shape how much of the output range is used at each fraction of head movement. A diagonal curve is neutral; raising its point increases early response. |

**A 20° head-down range does not limit the Windows cursor to 20° or keep it on a monitor.** With Camera down at 1500 counts and a 0.90 safety factor, a full downward turn requests 1350 counts. Reduce Camera down to reduce total travel.

## Documentation

- [User guide](docs/USER_GUIDE.md): controls, visualisation, every UI setting, tuning, and troubleshooting.
- [Developer guide](docs/DEVELOPMENT.md): prerequisites, build/run commands, architecture, tests, and repository hygiene.
- [Validation record](VALIDATION.md): completed checks and remaining manual validation.
- [Roadmap](roadmap.md): development history and design requirements; some earlier requirements are superseded by the current implementation.

## Features

- Recenter while on or off, without corrective mouse movement.
- Independent yaw and pitch curves with one draggable point each.
- Independent head ranges and mouse-count limits for all four directions.
- Adaptive or fixed smoothing and configurable deadzones.
- Polling/output cadence from **60–1000 Hz**, default **500 Hz**; existing saved rates are retained.
- Automatic settings persistence and live tuning.
- Movable, optionally click-through status overlay.
- Preview mode for checking response without sending Windows mouse input.

## Build from source

Requires Windows x64, the .NET 10 SDK, Visual Studio C++ Build Tools with a Windows SDK, and the Tobii Game Integration SDK placed locally in `sdk/`.

From the repository root:

```powershell
# Release build and acceptance tests
.\scripts\build.ps1

# Also create a self-contained Windows x64 app
.\scripts\build.ps1 -Publish
```

Published output goes to `artifacts/DogsEye`. The SDK, build output, and intermediate files are Git-ignored. See the [developer guide](docs/DEVELOPMENT.md) for details.

## Current validation

The latest compiled revision passed the Release build with zero warnings/errors and all **44 acceptance tests**. These tests use simulated poses and recording outputs. They do not establish in-game camera calibration or actual 1000 Hz timing. See [VALIDATION.md](VALIDATION.md).

The screenshots were captured directly from the latest existing executable without recompiling or changing tuning. Tracking was off. The copyright footer added in source is not included in this build yet. Documentation screenshots show example settings, not recommended values for every game.

