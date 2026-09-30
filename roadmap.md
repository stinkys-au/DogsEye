# DogsEye — Development Plan

Latest source update: polling/mouse output supports 60–1000 Hz with a 500 Hz default. Existing explicit rates are retained. This source-only change has not been built or measured; the earlier implementation report below is historical.

Implementation update (29 September 2026): MVP 1 and MVP 2 application code is implemented. The Release build and 37 automated acceptance tests pass, and a self-contained Windows x64 build is available in `artifacts/DogsEye`. Device detection, live head-pose calibration, and overlay checks pass. The user confirmed the Windows Raw Input key binding works and reported cockpit jitter. Adaptive smoothing has been added with a fixed-filter fallback. Polling and mouse output cadence are now configurable from 60–500 Hz, defaulting to 250 Hz (4 ms), with bounded interpolation toward measured targets between SDK packets. Live preview measured approximately 250 polling ticks/second and 33 fresh head samples/second; cockpit feel remains to be evaluated. See `README.md` for usage and `VALIDATION.md` for evidence and remaining manual checks. The requirements below remain the design reference.

## 1. Objective

Build a lightweight Windows desktop application that converts Tobii head-tracking movement into bounded relative mouse movement for use as free-look control in WARDOGS.

The application must remain external to the game and must not:

- inject into the game process;
- read or write game memory;
- hook DirectX or rendering APIs;
- inspect network traffic;
- interact with anti-cheat components;
- depend on game-specific process integration.

The application will read Tobii tracking data and emit normal Windows mouse input.

Head pitch and yaw will be the primary control mechanism.

Eye tracking will not control the camera in the initial implementation.

---

## 2. Technology Stack

### Application

- C#
- .NET 8 or later
- WPF desktop application
- Windows x64 target

### Tracking

- Tobii Game Integration API
- Tobii head pose:
  - yaw;
  - pitch;
  - optional roll for diagnostics only.

### Windows Input

- Win32 APIs via P/Invoke
- `SendInput()` for relative mouse movement
- global keyboard input handling for activation/recenter binding

### Configuration

Persist user settings under:

```text
%LOCALAPPDATA%\DogsEye\
```

Example:

```text
%LOCALAPPDATA%\DogsEye\settings.json
```

---

# 3. Core Design Principles

## 3.1 Head Tracking Is Primary

Head movement will control free-look.

Default control sources:

```text
Head yaw   → horizontal camera movement
Head pitch → vertical camera movement
```

Eye position will not influence mouse movement.

The reason is that a user may look toward an object on the display without intending to rotate the in-game camera.

Eye tracking may be reconsidered later as an optional advanced feature.

---

## 3.2 Absolute Bounded Head Mapping

Head movement must not behave as an unlimited mouse velocity command.

Instead:

```text
Head position
      ↓
Normalised head offset
      ↓
Bounded virtual camera target
      ↓
Difference from previous target
      ↓
Relative mouse movement
```

For example:

```text
Neutral head position = 0%

Maximum comfortable right head turn = +100%

Maximum comfortable left head turn = -100%
```

These values map onto a configured game-camera range.

Holding the head at maximum rotation must therefore result in:

```text
Target reached
→ no additional mouse movement
```

It must not continue rotating indefinitely.

---

# 4. Tracking State Machine

The application will have four principal states:

```text
DISABLED
CALIBRATING
ACTIVE
TRACKING LOST
```

## DISABLED

- Tobii may continue to be monitored for diagnostics.
- No mouse movement is emitted.
- No active calibration is retained.

## CALIBRATING

Triggered when tracking is enabled.

The application gathers a small number of valid head-pose samples and calculates the current neutral position.

## ACTIVE

Head movement is converted into bounded mouse movement.

## TRACKING LOST

Entered if Tobii temporarily loses a valid head pose.

During this state:

- stop producing mouse input immediately;
- retain current tracking session;
- wait for tracking to recover;
- do not attempt to extrapolate missing data.

---

# 5. Activation and Recenter Control

## Default Keybind

The default activation/recenter key is:

```text
`
```

On a standard Australian/US keyboard this will normally correspond to:

```text
VK_OEM_3
```

The application should store the physical/key-code binding rather than relying on the generated text character.

The keybind must be configurable from the UI.

---

## 5.1 Long Press

Holding the configured key for:

```text
3 seconds
```

toggles tracking.

### When OFF

```text
Hold key ≥ 3 seconds
        ↓
Start calibration
        ↓
Capture current head pose
        ↓
Current head position becomes zero
        ↓
Tracking ACTIVE
```

### When ON

```text
Hold key ≥ 3 seconds
        ↓
Tracking DISABLED
```

Deactivation must:

- stop mouse output immediately;
- discard the current neutral position;
- reset filtering state;
- reset virtual tracking state;
- not move the game camera back toward centre.

---

# 6. Short Press Behaviour

A key press released before the 3-second threshold performs a recenter operation while tracking is active.

```text
0–2999 ms
→ recenter

3000+ ms
→ toggle ON/OFF
```

When tracking is disabled, a short press performs no action.

---

# 7. Recenter Behaviour

A short press while ACTIVE must:

1. capture the current physical head position as the new neutral position;
2. reset filtered yaw/pitch state;
3. set the application's virtual X/Y offset to zero;
4. treat the game's current camera orientation as the new logical centre;
5. emit no corrective mouse movement during the reset.

This provides a ratcheting mechanism.

Example:

```text
User turns head right
        ↓
Game camera moves right
        ↓
User taps `
        ↓
Current head position becomes neutral
        ↓
Current game view becomes logical centre
```

The user can then physically return their head toward a more comfortable position and retain additional tracking range.

---

# 8. Activation Calibration

Activation automatically performs calibration.

The user does not require a separate initial calibration button.

At the moment the 3-second activation threshold is reached:

```text
Current physical head orientation
=
Yaw 0°
Pitch 0°
```

Do not use a single sample.

Instead collect approximately:

```text
50–100 ms
```

of valid head-pose samples.

Suggested requirement:

```text
5–10 valid samples
```

Calculate a stable neutral using either:

- median values; or
- a short robust average.

On completion:

```text
neutralYaw = measured yaw
neutralPitch = measured pitch

virtualX = 0
virtualY = 0
```

All filtering history must also be cleared.

---

# 9. Input Key State Handling

Keyboard repeat events must not cause repeated actions.

The application should track:

```text
keyHeld
keyDownTime
longPressTriggered
```

Pseudo-flow:

```text
KEY DOWN
    ↓
if not already held:
    record timestamp
    mark key held

WHILE HELD
    ↓
if duration >= 3 seconds
and long press not already triggered:
    toggle tracking
    mark long press triggered

KEY UP
    ↓
if long press was not triggered
and tracking is ACTIVE:
    recenter
```

Once a long-press action has occurred, releasing the key must not also trigger a short-press action.

---

# 10. Optional HOTAS Support

The architecture should allow HOTAS/controller input to be added without changing the tracking engine.

Potential future input structure:

```text
InputCommandService
├── Keyboard
└── HOTAS / HID
```

A HOTAS binding should be capable of either:

### Single-button mode

Same behaviour as the keyboard:

```text
short press → recenter
long press  → toggle
```

### Separate-button mode

Example:

```text
Button 15 → Toggle
Button 16 → Recenter
```

HOTAS button handling must use edge detection and avoid repeated activation while the button remains held.

---

# 11. Head Tracking Model

## 11.1 Relative Head Position

During active tracking:

```text
relativeYaw =
    currentYaw - neutralYaw

relativePitch =
    currentPitch - neutralPitch
```

These values are passed into the tracking processor.

---

# 12. Deadzone

Small natural head movement around centre must not move the game camera.

Initial suggested defaults:

```text
Yaw deadzone:   ±2.5°
Pitch deadzone: ±2.0°
```

These values must eventually be user-configurable.

Within the deadzone:

```text
output target = 0
```

Outside the deadzone, remap the remaining head range to:

```text
0.0 → 1.0
```

or:

```text
0.0 → -1.0
```

depending on direction.

---

# 13. Maximum Physical Head Range

The application should allow configurable comfortable head ranges.

Initial example defaults:

```text
Yaw:
    Left  = -30°
    Right = +30°

Pitch:
    Up    = +20°
    Down  = -20°
```

These values describe the user's desired physical movement range.

Movement beyond the configured limit must simply clamp to the maximum target.

Example:

```text
Configured max yaw = 30°

Head yaw 30° → 100% target
Head yaw 40° → 100% target
Head yaw 50° → 100% target
```

No additional mouse movement should be produced.

---

# 14. Camera Output Model

The application maintains its own virtual camera coordinate:

```text
virtualX
virtualY
```

Head position produces a target coordinate:

```text
targetX
targetY
```

The required mouse movement is:

```text
mouseDeltaX = targetX - virtualX
mouseDeltaY = targetY - virtualY
```

After sending accepted movement:

```text
virtualX += mouseDeltaX
virtualY += mouseDeltaY
```

This prevents head position from becoming an endless camera-rotation velocity command.

---

# 15. Per-Direction Limits

The design must support independent movement limits for:

```text
Left
Right
Up
Down
```

Do not assume symmetry.

Example:

```text
MaxLeft  = 3000 counts
MaxRight = 3200 counts
MaxUp    = 1800 counts
MaxDown  = 1400 counts
```

This allows for asymmetric cockpit camera limits.

---

# 16. Camera Safety Envelope

The application should not deliberately drive the WARDOGS camera against its absolute movement limits.

Doing so could create disagreement between:

```text
application virtual position
```

and:

```text
actual game camera position
```

For example:

```text
Application sends +50 counts
Game hits its camera limit after +17
```

The application would incorrectly believe all 50 counts were applied.

To reduce this risk, introduce a safety margin.

Suggested initial values:

```text
Yaw usable range:   92%
Pitch usable range: 90%
```

Example:

```text
Measured game limit:
3250 mouse counts

Configured usable limit:
3250 × 0.92
= 2990 mouse counts
```

The application should remain inside this software-defined boundary.

---

# 17. WARDOGS Camera Calibration

No reliable published WARDOGS aircraft pitch/yaw camera limits have been identified.

The application should therefore eventually support an optional camera-range calibration mode.

This must not read game memory.

Instead, it can emit controlled relative mouse movements while the user visually determines the camera limit.

Example process:

```text
Centre WARDOGS camera
        ↓
Start RIGHT calibration
        ↓
Send mouse in small increments
        ↓
User confirms camera has reached limit
        ↓
Record total displacement
```

Repeat for:

```text
Left
Right
Up
Down
```

Suggested movement step:

```text
25 mouse counts
```

Possible controls:

```text
Space      → move one step
Backspace  → reverse one step
Enter      → save current limit
Escape     → cancel
```

The resulting values are stored in configuration.

This functionality is not required for the first tracking prototype.

---

# 18. Smoothing

Head tracking should support light filtering to remove tracking noise without introducing noticeable latency.

The first implementation should use a simple, predictable smoothing algorithm such as:

- exponential smoothing; or
- low-pass filtering.

Filtering must operate on head pose before conversion into mouse position.

Example:

```text
raw Tobii yaw
      ↓
deadzone
      ↓
smoothing
      ↓
normalisation
      ↓
target camera position
```

The implementation should expose smoothing strength later as a configurable setting.

---

# 19. Response Curve

The default head response should remain close to linear.

Head movement is deliberate, so aggressive acceleration curves are undesirable.

Suggested response:

```text
0–deadzone
    → no movement

Immediately outside deadzone
    → gentle soft-start

Remaining movement
    → approximately linear

Maximum configured head range
    → maximum camera target
```

A small easing region near centre can reduce abrupt camera activation.

---

# 20. Windows Mouse Output

Use:

```text
SendInput()
```

with relative mouse movement.

The application must not:

- manipulate the physical desktop cursor position directly;
- teleport the cursor to screen coordinates;
- communicate with WARDOGS;
- attempt to disguise synthetic input.

Mouse output should be abstracted behind an interface such as:

```csharp
public interface IMouseOutput
{
    void Move(int deltaX, int deltaY);
}
```

This keeps tracking logic independent from the Windows output implementation.

---

# 21. Main Application Window

On startup, display a normal Windows configuration and diagnostic window.

The application must not automatically activate tracking.

Initial state:

```text
Application: Running
Tobii:       Detecting / Connected
Tracking:    OFF
```

---

# 22. Main UI Layout

The initial window should contain three primary sections.

## 22.1 Status

Display:

```text
Tobii Device
Tracking State
Tracking Update Rate
Head Pose Validity
```

Suggested states:

```text
OFF
CALIBRATING
ACTIVE
TRACKING LOST
```

The active state must be visually obvious.

---

## 22.2 Controls

Display:

```text
Toggle / Recenter Key: [ ` ] [Change]
Hold Duration:         [3.0 seconds]

[Enable / Disable Tracking]
[Recenter]
```

The UI buttons should call the same underlying commands as the global keyboard bindings.

---

# 23. Configurable Key Capture

Selecting:

```text
[Change]
```

should switch the control into capture mode:

```text
Press any key...
```

The next valid physical key press becomes the configured activation/recenter key.

Store the physical key code.

Example configuration:

```json
{
  "activationKey": "Oem3",
  "holdDurationMs": 3000
}
```

The key must work globally even when:

- the application is minimised;
- WARDOGS has focus.

---

# 24. Head Tracking X/Y Visualiser

The main window must contain a live X/Y tracking grid.

Conceptually:

```text
             -Yaw          +Yaw

              ┌─────────────┐
              │      │      │
              │      │  ●   │
       +Pitch │──────┼──────│
              │      │      │
              │      │      │
              └─────────────┘
                    -Pitch
```

The grid represents:

```text
X = relative yaw
Y = relative pitch
```

It must not represent the actual Windows cursor position.

---

# 25. Visualiser Overlays

The grid should display:

### Neutral centre

```text
0° yaw
0° pitch
```

### Deadzone

Show the region in which no camera movement will occur.

### Maximum tracking envelope

Show the configured comfortable head limits.

### Current position

Show the current Tobii-derived head position.

---

# 26. Raw and Processed Tracking Markers

Preferably display two markers:

```text
● Raw Tobii pose
○ Processed/filtered pose
```

This provides direct feedback about smoothing behaviour.

Example:

```text
         ● raw
       ○ filtered
```

This will make filter tuning much easier.

---

# 27. Numeric Diagnostics

Alongside the visualiser display values such as:

```text
Raw Yaw:       +7.4°
Raw Pitch:     -2.1°

Relative Yaw:  +5.2°
Relative Pitch:-1.4°

Processed X:   +0.17
Processed Y:   -0.04

Target X:      +512
Target Y:      -73

Mouse ΔX:      +4
Mouse ΔY:      -1
```

These diagnostics may be hidden later in a developer/advanced mode.

---

# 28. Visualiser Behaviour When Disabled

While tracking is OFF:

- Tobii raw head pose may continue updating;
- no mouse output is generated;
- no active calibrated relative centre is required.

The UI should clearly indicate:

```text
TRACKING OFF
```

When tracking is enabled:

```text
current head pose
→ becomes centre
```

and the relative marker should immediately appear at:

```text
X = 0
Y = 0
```

---

# 29. Visualiser Behaviour During Recenter

When the user taps the configured key:

```text
current head pose
→ becomes new zero
```

The processed marker should visibly snap back to the centre of the grid.

No mouse movement should be generated as part of this reset.

This provides immediate confirmation that recentering succeeded.

---

# 30. Audible Feedback

Provide optional audio feedback.

Suggested events:

```text
Tracking enabled
→ short high double beep

Tracking disabled
→ short low beep

Recentered
→ short confirmation beep

Tracking lost
→ optional warning tone
```

Audio must be optional.

The user should not need to look away from the game to determine tracking state.

---

# 31. Settings Persistence

Persist at minimum:

```text
Activation key
Hold duration
Yaw deadzone
Pitch deadzone
Maximum yaw left/right
Maximum pitch up/down
Smoothing value
Camera left/right/up/down limits
Safety margin
Audio enabled
```

Example future schema:

```json
{
  "activationKey": "Oem3",
  "holdDurationMs": 3000,

  "tracking": {
    "yawDeadzoneDegrees": 2.5,
    "pitchDeadzoneDegrees": 2.0,

    "maxHeadYawLeft": 30.0,
    "maxHeadYawRight": 30.0,
    "maxHeadPitchUp": 20.0,
    "maxHeadPitchDown": 20.0,

    "smoothing": 0.15
  },

  "camera": {
    "maxLeft": 3000,
    "maxRight": 3000,
    "maxUp": 1700,
    "maxDown": 1500,

    "yawSafetyFactor": 0.92,
    "pitchSafetyFactor": 0.90
  },

  "audioFeedback": true
}
```

---

# 32. Proposed Code Structure

```text
DogsEye
│
├── App
│   ├── App.xaml
│   └── App.xaml.cs
│
├── UI
│   ├── MainWindow.xaml
│   ├── MainWindowViewModel.cs
│   └── Controls
│       └── HeadTrackingVisualizer
│
├── Tracking
│   ├── TobiiService.cs
│   ├── HeadPose.cs
│   ├── TrackingController.cs
│   ├── TrackingState.cs
│   ├── CalibrationService.cs
│   ├── HeadTrackingProcessor.cs
│   └── TrackingFilter.cs
│
├── Input
│   ├── GlobalKeyboardService.cs
│   ├── InputCommandService.cs
│   └── HotasService.cs
│
├── Output
│   ├── IMouseOutput.cs
│   └── WindowsSendInputMouseOutput.cs
│
├── Configuration
│   ├── AppSettings.cs
│   └── SettingsService.cs
│
├── Diagnostics
│   ├── TrackingTelemetry.cs
│   └── AudioFeedbackService.cs
│
└── Interop
    └── NativeMethods.cs
```

---

# 33. Separation of Responsibilities

## TobiiService

Responsible only for:

```text
connecting to Tobii
updating Tobii API
providing latest valid head pose
reporting tracking/device status
```

It must not know anything about mouse movement.

---

## TrackingController

Responsible for:

```text
OFF
CALIBRATING
ACTIVE
TRACKING LOST
```

and commands such as:

```text
Toggle()
Recenter()
Disable()
```

---

## HeadTrackingProcessor

Responsible for:

```text
relative head angle
deadzone
filtering
normalisation
response curve
limits
target virtual coordinates
```

It must not call Windows APIs directly.

---

## MouseOutput

Responsible only for:

```text
relative ΔX / ΔY
→ Windows SendInput()
```

---

## InputCommandService

Converts:

```text
keyboard
HOTAS
UI buttons
```

into common commands:

```text
ToggleTracking
RecenterTracking
```

---

# 34. Development Phases

## Phase 1 — Project Foundation

Create:

- .NET WPF application;
- x64 build target;
- project structure;
- settings model;
- logging/diagnostics foundation.

Acceptance criteria:

- application launches normally;
- configuration file can be read/written;
- no tracking functionality required yet.

---

## Phase 2 — Tobii Integration

Implement:

- Tobii SDK loading;
- Tobii device detection;
- head-pose retrieval;
- head-pose validity;
- update loop.

Display raw:

```text
Yaw
Pitch
Roll
```

in the UI.

Acceptance criteria:

- physical head movement produces stable live values;
- tracking-loss state can be detected;
- no mouse input is emitted.

---

## Phase 3 — X/Y Visualiser

Implement:

- live head-position grid;
- raw pose marker;
- centre axes;
- numeric yaw/pitch diagnostics.

Acceptance criteria:

- marker follows head yaw/pitch correctly;
- no game or mouse integration exists yet.

---

## Phase 4 — Global Key Binding

Implement configurable activation/recenter key.

Default:

```text
`
```

Implement:

```text
short press
long press ≥3 seconds
keyboard repeat suppression
global operation without app focus
```

Acceptance criteria:

- key events are recognised while another application has focus;
- long press fires once;
- release after long press does not trigger recenter.

---

## Phase 5 — Tracking State Machine

Implement:

```text
DISABLED
CALIBRATING
ACTIVE
TRACKING LOST
```

Activation:

```text
hold key
→ capture neutral
→ reset filters
→ ACTIVE
```

Short press:

```text
recenter
```

Acceptance criteria:

- activating always creates a fresh zero;
- recenter visibly resets the relative visualiser;
- deactivation produces no camera-centering action.

---

## Phase 6 — Deadzone and Bounded Mapping

Implement:

- yaw deadzone;
- pitch deadzone;
- independent directional physical limits;
- normalisation to `-1..+1`;
- bounded virtual target.

Visualise:

```text
deadzone
outer tracking envelope
processed marker
```

Acceptance criteria:

- movement inside deadzone produces zero target output;
- movement beyond configured limits clamps cleanly;
- holding head at maximum produces a static target.

---

## Phase 7 — Filtering

Implement lightweight smoothing.

Display:

```text
raw marker
processed marker
```

Acceptance criteria:

- high-frequency tracking noise is reduced;
- noticeable control lag remains minimal;
- recenter resets filtering history.

---

## Phase 8 — Mouse Output

Implement Win32 `SendInput()` relative mouse movement.

Initially provide an explicit test mode.

Acceptance criteria:

- head movement can produce bounded relative mouse movement;
- holding the head still at a target does not generate continuing movement;
- returning to neutral returns the virtual target toward zero.

---

## Phase 9 — WARDOGS Tuning

Test:

- horizontal free-look;
- vertical free-look;
- WARDOGS mouse sensitivity;
- camera limits;
- deadzone;
- smoothing;
- safety envelope.

Determine practical:

```text
MaxLeft
MaxRight
MaxUp
MaxDown
```

without interacting with game memory.

---

## Phase 10 — Camera Range Calibration

Add optional guided calibration for:

```text
Left
Right
Up
Down
```

Store results per profile.

Potential future support:

```text
WARDOGS Helicopter
WARDOGS Ground Vehicle
Other Game
```

---

## Phase 11 — HOTAS Input

Implement controller/HID support.

Support:

```text
single-button tap/hold mode
or
separate toggle/recenter buttons
```

Acceptance criteria:

- HOTAS commands invoke the same tracking controller as keyboard/UI;
- controller polling does not affect the tracking update loop.

---

## Phase 12 — Packaging

Publish as a self-contained Windows x64 application.

The user should not require:

- Visual Studio;
- .NET SDK;
- PowerShell;
- command-line startup.

Distribution options:

```text
Portable ZIP
or
Windows installer
```

Application should ultimately support:

```text
Start with Windows
Start minimised
Minimise to tray
Remember settings
```

Tracking must remain OFF at application startup unless explicitly changed in a future design decision.

---

# 35. Initial MVP Scope

The first useful prototype should deliberately exclude WARDOGS mouse output.

MVP 1 should deliver only:

```text
Tobii device detection
        +
live head yaw/pitch
        +
configurable ` key
        +
3-second toggle detection
        +
short-press recenter
        +
activation calibration
        +
tracking state machine
        +
X/Y visualiser
        +
deadzone visualisation
```

This proves the complete tracking and control model safely before introducing mouse movement.

---

# 36. MVP 2 Scope

Once MVP 1 behaves correctly:

```text
Add bounded virtual X/Y processing
        ↓
Add SendInput mouse output
        ↓
Test outside WARDOGS
        ↓
Test WARDOGS free-look
        ↓
Tune camera range
```

---

# 37. Success Criteria

The application should ultimately allow the user to:

1. launch a normal Windows application;
2. see that Tobii is connected;
3. visually inspect head movement on an X/Y grid;
4. configure the activation/recenter key;
5. enter WARDOGS;
6. face a comfortable neutral position;
7. hold `` ` `` for three seconds;
8. have that position automatically become tracking zero;
9. rotate the in-game free-look camera using head yaw and pitch;
10. reach a bounded camera limit without continuous drift;
11. tap `` ` `` at any time to establish a new centre;
12. hold `` ` `` for three seconds to disable tracking;
13. continue using the physical mouse normally.

The solution must remain an external input translation utility with no direct integration into the WARDOGS process.

## 38. Always-on-Top Status Overlay

Add an optional lightweight status overlay that shows the current tracking state at all times.

The overlay must be a normal Windows top-level window and must not inject into, hook, or render inside the game.

Its purpose is to provide an immediate visual indication of whether DogsEye is currently active.

### 38.1 Display States

At minimum, display:

```text
[OFF]
```

and:

```text
[ON]
```

The overlay must show the correct initial state immediately when the application starts.

Default startup state:

```text
[OFF]
```

When tracking is successfully activated and calibration completes:

```text
[ON]
```

When tracking is disabled:

```text
[OFF]
```

If tracking is temporarily unavailable because Tobii loses the user, the overlay should preferably support an additional state:

```text
[LOST]
```

or:

```text
[WAIT]
```

This avoids showing `[ON]` when the application is technically enabled but unable to obtain valid head tracking.

Recommended state mapping:

```text
DISABLED       → [OFF]
CALIBRATING    → [CAL]
ACTIVE         → [ON]
TRACKING LOST  → [LOST]
```

The minimal `[ON]` / `[OFF]` presentation should remain the default visual style.

---

## 38.2 Visual Style

The overlay should resemble a small FPS-counter-style display.

Requirements:

- transparent background;
- no normal window chrome;
- no title bar;
- no border;
- always on top;
- small footprint;
- high readability against both bright and dark backgrounds;
- minimal visual distraction.

Recommended appearance:

```text
[ON]
```

with:

- bold or semi-bold sans-serif font;
- approximately 18–24 px equivalent display size;
- bright foreground text;
- dark outline or drop shadow;
- optional very subtle translucent backing behind the text.

The text should remain readable over:

```text
sky
dark cockpit
bright terrain
UI panels
clouds
night scenes
```

A good implementation would use either:

```text
white text
+
black outline/shadow
```

or:

```text
light text
+
small semi-transparent dark backing
```

The styling should avoid solid opaque panels unless the user explicitly enables one.

---

## 38.3 Positioning

The overlay must be freely movable by the user.

Example workflow:

```text
launch DogsEye
        ↓
overlay appears
        ↓
drag [OFF] to top-left corner
        ↓
launch WARDOGS
        ↓
overlay remains in that position
```

The user should be able to place it anywhere across the Windows virtual desktop, including secondary monitors.

Its location must be stored using screen coordinates and restored the next time the application starts.

Example configuration:

```json
{
  "overlay": {
    "enabled": true,
    "left": 20,
    "top": 18
  }
}
```

Multi-monitor coordinates may be negative, so the implementation must support the complete Windows virtual screen coordinate space.

---

## 38.4 Dragging Behaviour

Because the overlay has no normal title bar, the user must still be able to drag it.

Recommended behaviour:

```text
left-click and drag overlay
→ move overlay
```

However, this presents a usability issue while gaming.

The overlay should therefore support two modes:

### Configuration mode

```text
overlay movable
overlay receives mouse input
```

### Locked mode

```text
overlay remains visible
mouse clicks pass through it
```

This is preferable because once the user has positioned the indicator, it should not interfere with clicking game UI beneath it.

The main application should therefore contain:

```text
Status Overlay
[✓] Enabled
[✓] Always on top
[ ] Lock position / click-through
[Reset Position]
```

Recommended default:

```text
Enabled:      On
Always on top: On
Locked:       Off during first launch/configuration
```

After positioning it, the user can enable:

```text
Lock overlay
```

to prevent accidental movement.

---

## 38.5 Click-Through Mode

When locked, configure the overlay as a click-through Windows window.

Conceptually:

```text
WS_EX_TRANSPARENT
```

along with the appropriate layered-window behaviour.

The result should be:

```text
overlay visible
        +
mouse input goes to the window underneath
```

This is especially important if the overlay sits over part of the game window.

The overlay must still remain completely external to the game.

---

## 38.6 Always-on-Top Behaviour

Use standard Windows topmost-window behaviour.

The overlay should remain above ordinary windows using an appropriate topmost setting such as:

```text
Topmost = true
```

or equivalent Win32 window positioning.

It must not continually steal keyboard or mouse focus.

Showing or updating the overlay must not activate it.

The user should be able to continue interacting with WARDOGS without focus changing to DogsEye.

---

## 38.7 Fullscreen Behaviour

The overlay should be expected to work over:

```text
windowed applications
borderless fullscreen applications
```

Exclusive fullscreen behaviour may vary depending on Windows, the graphics mode, and the game.

The application must not introduce game hooks merely to force the overlay to appear above exclusive fullscreen applications.

If WARDOGS supports borderless fullscreen, that should be the recommended mode for reliable overlay visibility.

---

## 38.8 State Updates

The overlay should subscribe to the same tracking state maintained by `TrackingController`.

Do not maintain a separate overlay state.

Architecture:

```text
TrackingController
        │
        ├── Main UI
        ├── audio feedback
        └── StatusOverlay
```

This ensures:

```text
one authoritative tracking state
```

and avoids discrepancies such as:

```text
main window says OFF
overlay says ON
```

---

## 38.9 Activation Transition

When the user holds the configured activation key:

```text
[OFF]
```

remains displayed until calibration has successfully completed.

During the short calibration period, optionally show:

```text
[CAL]
```

Then:

```text
valid neutral acquired
        ↓
TrackingState = ACTIVE
        ↓
overlay becomes [ON]
```

Do not display `[ON]` before the application actually has a valid calibration.

---

## 38.10 Tracking Loss

If Tobii tracking is lost while the controller remains enabled:

```text
[ON]
        ↓
head tracking unavailable
        ↓
[LOST]
```

When valid tracking returns:

```text
[LOST]
        ↓
valid pose restored
        ↓
[ON]
```

No new calibration should automatically occur merely because tracking was temporarily lost unless separately specified.

---

## 38.11 Overlay Configuration

Persist settings such as:

```json
{
  "overlay": {
    "enabled": true,
    "topmost": true,
    "locked": true,
    "left": 20,
    "top": 20,
    "fontSize": 22,
    "opacity": 0.9,
    "showCalibrationState": true,
    "showTrackingLostState": true
  }
}
```

Additional visual customisation can be added later, but should not be required for the MVP.

---

## 38.12 Main Window Controls

Add a new configuration section:

```text
Status Overlay

[✓] Show status overlay
[✓] Always on top
[✓] Click-through when locked
[ ] Lock overlay position

Display:
[ON] [OFF]

[Reset Position]
```

Optionally add:

```text
[Show Test Overlay]
```

to make positioning easy without activating tracking.

---

## 38.13 Position Recovery

The application must handle cases where the stored overlay position is no longer visible.

Example:

```text
user previously had second monitor
        ↓
monitor disconnected
        ↓
stored overlay coordinates are off-screen
```

On startup:

1. compare stored position against the current Windows virtual desktop;
2. if the overlay is completely outside all connected monitor work areas;
3. restore it to a safe default.

Suggested safe default:

```text
top-left of primary display
with a 20 px margin
```

---

## 38.14 Overlay Window Architecture

Implement the overlay as a separate WPF window, for example:

```text
UI
├── MainWindow.xaml
└── StatusOverlayWindow.xaml
```

Responsibilities of the overlay should be limited to:

```text
display current state
display state text
manage its own position
support drag movement
support locked/click-through state
```

It must not contain tracking logic.

---

## 38.15 MVP Acceptance Criteria

The status overlay is complete when:

1. launching DogsEye immediately displays `[OFF]`;
2. the overlay remains above normal Windows applications;
3. the user can drag it anywhere on any connected monitor;
4. its location persists after application restart;
5. activation changes it to `[ON]`;
6. deactivation changes it back to `[OFF]`;
7. calibration optionally displays `[CAL]`;
8. tracking loss optionally displays `[LOST]`;
9. locking it prevents accidental dragging;
10. locked mode allows mouse clicks to pass through;
11. the overlay never takes focus during normal tracking;
12. disabling the overlay from settings hides it completely.

---

## 38.16 Revised MVP 1 Scope

MVP 1 should now deliver:

```text
Tobii device detection
        +
live head yaw/pitch
        +
configurable activation key
        +
3-second toggle detection
        +
short-press recenter
        +
activation calibration
        +
tracking state machine
        +
X/Y visualiser
        +
deadzone visualisation
        +
always-on-top state overlay
```

Mouse movement should still remain disabled during this phase.

This allows the entire user interaction model to be tested before WARDOGS receives any generated input.

