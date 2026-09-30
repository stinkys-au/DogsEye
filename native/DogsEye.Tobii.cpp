#include <Windows.h>
#include <cmath>
#include <cstring>
#include "tobii_gameintegration.h"

using namespace TobiiGameIntegration;
static ITobiiGameIntegrationApi* api = nullptr;
static ULONGLONG nextDiscovery = 0;
struct Snapshot {
    int64_t timestamp;
    float yaw, pitch, roll;
    int connected, enabled, present, headSupported, hasPose;
    char model[128];
};
#define EXPORT extern "C" __declspec(dllexport)

// Every call is made on one DogsEye-owned worker thread. No game handles are used.
EXPORT int __cdecl dogseye_initialize(int left, int top, int right, int bottom) {
    try {
        if (api) return 1;
        api = GetApi("DogsEye", false);
        if (!api) return 0;
        api->GetStatistics()->StopAllLogging();
        TobiiGameIntegration::Rectangle area;
        area.Left = left; area.Top = top; area.Right = right; area.Bottom = bottom;
        api->GetTrackerController()->TrackRectangle(area);
        api->GetTrackerController()->UpdateTrackerInfos();
        nextDiscovery = 0;
        return 1;
    } catch (...) { return 0; }
}
EXPORT int __cdecl dogseye_poll(Snapshot* result) {
    if (!api || !result) return 0;
    *result = {};
    try {
        api->Update();
        auto controller = api->GetTrackerController();
        auto streams = api->GetStreamsProvider();
        if (!controller->IsConnected() && GetTickCount64() >= nextDiscovery) {
            nextDiscovery = GetTickCount64() + 2000;
            const TrackerInfo* trackers = nullptr;
            int count = 0;
            if (controller->GetTrackerInfos(trackers, count)) {
                for (int i = 0; i < count; ++i) {
                    const auto& tracker = trackers[i];
                    if (tracker.IsAttached && (tracker.Capabilities & StreamFlags::Head) != StreamFlags::None) {
                        controller->TrackRectangle(tracker.DisplayRectInOSCoordinates);
                        if (controller->IsConnected()) break;
                    }
                }
            }
            controller->UpdateTrackerInfos();
        }
        result->connected = controller->IsConnected();
        result->enabled = controller->IsEnabled();
        result->headSupported = controller->IsStreamSupported(StreamFlags::Head);
        result->present = streams->IsPresent();
        TrackerInfo info;
        if (controller->GetTrackerInfo(info) && info.ModelName)
            strncpy_s(result->model, info.ModelName, _TRUNCATE);
        HeadPose pose;
        if (streams->GetLatestHeadPose(pose) && std::isfinite(pose.Rotation.YawDegrees)
            && std::isfinite(pose.Rotation.PitchDegrees) && std::isfinite(pose.Rotation.RollDegrees)) {
            result->timestamp = pose.TimeStampMicroSeconds;
            result->yaw = pose.Rotation.YawDegrees;
            result->pitch = pose.Rotation.PitchDegrees;
            result->roll = pose.Rotation.RollDegrees;
            result->hasPose = 1;
        }
        return 1;
    } catch (...) { return 0; }
}
EXPORT void __cdecl dogseye_shutdown() {
    try { if (api) api->Shutdown(); } catch (...) { }
    api = nullptr;
    nextDiscovery = 0;
}
