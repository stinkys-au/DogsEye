# DogsEye MVP validation

## Current build — 30 September 2026

Compiled the accumulated source changes in Release with zero warnings and zero errors. All 44 C# acceptance tests passed, including off-state recenter, neutral/raised response curves, live settings changes, and 1000 Hz simulated output cadence. Refreshed the self-contained Windows x64 application in `artifacts/DogsEye` using cached .NET 10.0.12 runtime packages. No UI or in-game validation was performed in this build pass, and actual 1000 Hz scheduling has not been measured. Earlier source-only entries below describe the history; this build includes those changes.

## Stable axes: no movement-driven scaling — source update, not built

Removed all observed-extreme accumulation and dynamic axis sizing. Both axes now use fixed ±180° domains, matching wrapped relative head angles and containing every valid head reach (up to 90°). Mouse-response targets share those coordinates. Original control dimensions are retained. Movement, toggling, and recentering cannot move the red limit lines; editing a reach value intentionally moves its own line. This supersedes earlier auto-fit visual implementations. Static XAML/source inspection and numeric checks only; no build, app launch, C# tests or publishing performed.

## 1000 Hz polling support, 500 Hz default — source update, not built

Settings validation and UI now accept 60–1000 Hz; the default is 500 Hz. Explicit saved rates remain unchanged. The existing timer takes the configured rate directly and supports a requested 1 ms interval. Updated acceptance-test source to include 1000 Hz output cadence and persistence, reject 1001 Hz, and expect 500 Hz for new/legacy settings without a rate. Source inspection only: no build, C# tests, app launch or publish. Actual 1000 Hz timing has not been measured; earlier timing results below refer to earlier revisions.

## Fit chart data inside original dimensions — source update, not built

Removed the uniform whole-visual transform and restored drawing at the control's original full width and height. The plotted domain now includes the largest observed movement and configured mouse limit in each direction, keeping zero centred. Both dots, the deadzone, and all four red limit lines use this shared mapping with an 8-pixel edge inset. No panning, chart scrolling, or off-scale labels. This supersedes prior requirements to keep display extents wholly independent of reach settings: the limits must be included to show all chart content. Static source, XAML, and numerical mapping checks only; no build, app launch, C# tests, or publish.

## Fit the complete visual to its bounds — source update, not built

The head-position drawing now scales uniformly as a complete 520×310 layout into the actual control space, preserving aspect ratio and centring the result. Text is measured and kept within that layout, with ellipsis for overlong labels. Both markers share an inset clamp so the full circles remain visible even at extreme motion or with stale samples. Angular extents still come only from observed SDK motion, independently of reach settings. Resizing changes only display size. Static review and geometry checks only; no build, app launch, C# tests, or publish performed.

## Recenter while off and yellow mouse response — source update, not built

Recenter now accepts any fresh valid pose while off, preserves the disabled state, and resets the visual centre/observed ranges. The blue marker is unfiltered SDK motion relative to that centre; absolute SDK readouts remain visible. The controller calculates a response preview while disabled without queuing or emitting output. A yellow marker shows the processed target on the comparable head-degree scale; deadzone suppression, smoothing, and reach clamping still apply. Actual emitted mouse deltas remain in diagnostics.

The single-point curve now has a neutral linear diagonal (including the default 50%/50% point). Points above the diagonal increase response, including early in the turn. The same curve evaluates the editor preview, yellow target, and real mouse target. Independent JavaScript numerical checks covered all control points at 1% steps: boundedness, monotonicity, diagonal identity, and raised-curve response. Test source was updated for off-state recenter, no output while previewing, persistent centre on disable, early response gain, and neutral mapping. Static review and XAML parsing only for the app; no compilation, app launch, C# test execution, or publishing was performed.

## Visual fits detected motion — source update, not built

Supersedes the fixed ±180° plot below. The visual keeps the largest valid raw SDK angle observed in each direction during the current window session, with a 1° minimum extent. Left/right/up/down are scaled independently so each observed extreme reaches its corresponding edge while SDK zero stays centred. Extents grow with new extremes and do not shrink when the head returns. These are observed session ranges, not a claimed SDK hardware maximum. Reach settings cannot change them.

The red mouse-limit lines and calibrated deadzone use the same coordinate mapping. Limits beyond observed motion are labelled off scale rather than drawn as false boundaries at an edge. Raw SDK motion continues past visible limit lines. Angle labels show the current observed extents. Static source/XAML and coordinate checks only; no compilation, launch, test-suite execution or publishing.

## Raw SDK visual and calibrated mouse limits — source update, not built

Supersedes the reach-scaled visual below. The graph now always plots raw SDK yaw/pitch on fixed ±180° axes, with exact numeric readouts. Angles wrap at the ±180° seam. Reach, curves, smoothing and camera counts cannot rescale or clamp the raw marker. Stale samples are grey and explicitly labelled. The former processed marker is replaced by a calibrated-centre marker; processed response remains in diagnostics.

Telemetry now exposes the calibrated neutral angles. Deadzone and red dotted mouse-limit overlays use that neutral in raw SDK coordinates, including wrapped boundaries. Before calibration, overlays are labelled as a preview around SDK zero. Recentring changes the overlays, not the raw SDK pose. Red lines mark the input angles that reach the maximum camera target; output may still settle due to smoothing/interpolation, and the other axis remains independent. Test source includes neutral calibration, recenter and clear-on-disable assertions. No build, app launch, test execution or publish was performed.

## Centred head-position visual and range limits — source update, not built

The visual now keeps zero at the geometric centre with a symmetric degree scale on each axis. The deadzone remains centred even when directional ranges differ. Four labelled red dotted lines show the left/right/up/down head-range clamp thresholds. Axis extents include 20% beyond the largest configured range so raw pose can visibly cross the limits; the processed marker reaches its direction's limit line at full response. Tracking behaviour is unchanged. Static XAML and coordinate checks only; no compilation, app launch, or publish for this source-only iteration.

## Automatic settings and clarified head ranges — source update, not built

Removed Apply and save. Numeric fields, adaptive smoothing, and curve points save as a validated group after a 300 ms editing pause; numeric focus loss and window close flush pending edits. Invalid or incomplete input retains the last valid configuration and reports the error. A successful save updates the running controller without discarding calibration, filter history, or emitted camera position. Old queued movement is cancelled; the next fresh pose supplies the new target through the existing bounded output path. Hold-duration changes require release of an already-held key. Output-mode changes and key rebinding retain their existing stop behaviour.

Head-range labels now explicitly describe physical degrees from calibrated centre to full turn: lower values are more sensitive, higher values less sensitive. Verified pitch direction against the local SDK header: positive pitch is up. Added acceptance test source for live reconfiguration and the inverse relationship between physical range and sensitivity. Static review only, per the user's source-only iteration preference: no build, test execution, app launch, or publish was performed for this revision.

## Single-point X/Y S curves — source update, not built

Replaced the shared gain slider with independent yaw and pitch graphs, each with one draggable point, keyboard adjustment, and reset. Both tracking and the graph preview use the same bounded cubic response. Settings persist the two points independently; older settings adopt the default S curves while retaining other tuning.

Acceptance test source now covers point interpolation, monotonicity, smooth joins, default S shape, axis independence, signed response, deadzones, limits, validation, and persistence. Existing fractional-output and filter comparisons were adjusted for nonlinear mapping. At the user's request, this revision has **not been compiled, launched, published, or run through the C# acceptance suite**. Earlier build/test results below apply to earlier revisions. The existing executable in `artifacts/DogsEye` still contains the shared slider.

## Settings sections and look-turn curve — 30 September 2026

Release build passes with zero warnings/errors; all 39 acceptance tests pass. Settings now have labelled common and advanced sections. The persisted look-turn slider ranges from 0.25 to 2.00, with 1.00 retaining the original response. Tests cover increasing response with increasing slider values, both axis signs, monotonic head movement, exact deadzones, full-range clamping, invalid values, legacy defaults, and persistence.

Preview smoke checks passed startup, keyboard registration, and overlay checks. The grouped settings screenshot was visually inspected. The SDK detected the Tobii device but supplied no live head samples in this run; in-game curve feel remains a manual check. Mouse output stayed disabled. Confirmed SDK headers, libraries, binaries, documentation, samples, and licence are present under `sdk/`; `git check-ignore sdk` confirms the folder is ignored and `git ls-files sdk` returns no tracked files.

## Configurable polling and mouse cadence

Release build passes with zero warnings/errors and all 37 acceptance tests pass. Polling and mouse output now share a configurable 60–500 Hz worker cadence, defaulting to 250 Hz (4 ms), replacing the previous nominal 125 Hz (8 ms) wait. Older settings adopt 250 Hz and retain their other tuning; custom rates persist and out-of-range values are rejected.

The live preview check measured 249.93 polling ticks/second with the high-resolution Windows waitable timer and 32.86 fresh Tobii samples/second. It observed 203 distinct head samples in UI snapshots, reached ACTIVE, recentered, and passed the existing overlay/startup checks. Nonzero preview output reached 82.65 events/second in the final measurement window; event frequency depends on movement and is not expected to equal the configured tick rate. Windows mouse output was disabled throughout. Both the diagnostics and expanded settings screenshots were visually checked.

New deterministic tests exercise 125/250/500 Hz output between tracker packets, target settling without extrapolation, cancellation of queued movement on loss/disable/recenter, staleness checked directly by the output loop, bounded recovery, target reversal, and filtering independent of output cadence. Interpolation introduces up to one measured sample interval of settling time (clamped to 4–50 ms); in-game feel remains a user check. No game integration or input-concealment functionality was added.

## Adaptive smoothing update

Release build passes with zero warnings/errors and all 30 acceptance tests pass. Adaptive smoothing is enabled by default, preserves existing settings, and can be switched back to fixed smoothing in the UI. Strength zero still bypasses filtering. Additional tests cover noise-versus-response performance, 30/60/120 Hz sampling, filter reset, bypass, settings migration, and parameter validation.

In a deterministic 33 Hz test with strength 0.5, a held 10-degree pose with alternating +/-0.12-degree noise, followed by a 60-degrees/second turn, adaptive/fixed RMS jitter was 0.419 and mean fast-turn lag was 0.565. That is approximately 58% less stationary jitter and 44% less turn lag in this synthetic test. These numbers do not establish in-game improvement; cockpit feel needs interactive validation.

## Keyboard update — 29 September 2026

Replaced activation-key polling with Windows Raw Input delivered to DogsEye's own window. Added a visible hold countdown, registration-error feedback, and command-source logging. All 25 acceptance tests pass, including key edges between tracking ticks and hold-progress timing. The integration check confirms successful Raw Input registration.

The latest live check received 179 valid samples, reached ACTIVE after calibration, and requested recenter successfully. The earlier absence of head data described below no longer blocks live calibration. Physical key operation while another app has focus still needs confirmation with the updated executable; registration and logic tests alone do not prove that a hardware press reached the application.

Validated on 29 September 2026 with .NET SDK 10.0.401, MSVC 14.51, Windows SDK 10.0.28000.0, and the supplied Tobii Game Integration SDK 9.0.4.26.

## Passed

- Release solution build: zero warnings and errors.
- Self-contained Windows x64 publish; output in `artifacts/DogsEye`.
- All 23 deterministic acceptance tests, using a recording output rather than real Windows mouse input.
- Native SDK loading and detection of `IS5_Large_Eyetracker_5`.
- SDK reports connected, enabled, and head-stream support.
- WPF application launch, rendering, and orderly shutdown.
- Startup controller and overlay are OFF.
- Overlay state labels: OFF, CAL, ON, LOST.
- Overlay no-activate style, click-through when locked, removal of click-through when unlocked, hiding, and recovery of off-screen coordinates.
- Native Windows mouse-input structure has the required 40-byte x64 size.
- Visual inspection of the rendered main window.

Core tests exercise calibration with fresh samples/outliers/angular wrap, gaps between SDK packets, deadzones, independent directional limits, pitch sign, steady-target drift prevention, return to neutral, recenter without corrective movement, deactivation without movement, renewed activation, tracking loss/recovery, stale/non-finite data, rate-limited recovery, rejected output, fractional counts, smoothing, tap/hold thresholds, repeat suppression, rebind release gating, settings persistence, and invalid settings.

## Live checks still required

The connected tracker reported `Present=0` and `HasPose=0` throughout the ten-second checks. No valid live head samples arrived. Consequently, live head movement, physical calibration/recenter, global key operation while another app has focus, and actual mouse control still need an interactive test. The app correctly remained unable to activate without valid calibration. Core tests verify these transitions with simulated samples.

The checks intentionally never enabled Windows mouse output and never launched or interacted with WARDOGS. Camera limits, sensitivity, control feel, and acceptance of ordinary synthetic input by the game are not validated. Default camera counts remain the roadmap's example values.

The smoke report and rendered window are in `artifacts/smoke-report.json` and `artifacts/smoke-window.png`. Raw live head samples are not saved in the report or logs.

## Implementation boundaries

The application calls the supplied Tobii SDK from its own native adapter, receives its configured key through Windows Raw Input, and optionally sends relative mouse movement with `SendInput`. The overlay uses ordinary Windows window styles and monitor/work-area APIs. No code was added for game-process access, game memory, injection, rendering hooks, traffic inspection, anti-cheat interaction, or input concealment. The app runs as the normal user and uses no privilege escalation for output.

Windows antivirus blocked one read-only source-search shell command during review. No security settings were changed and that command was not bypassed. The build, acceptance suite, self-contained publish, and app smoke checks completed separately. This is not an anti-cheat compatibility certification.











