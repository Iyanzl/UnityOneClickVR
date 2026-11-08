#include "openvr.h"
#include <cstdint>
#include <string>
#include <mutex>
#include <cstring>

struct HandInput
{
    uint32_t valid, buttons;
    float trigger, grip, x, y;
    vr::HmdMatrix34_t gripPose, aimPose;
    uint32_t aimValid, active;
};
struct InputFrame { HandInput left, right; };
static_assert(sizeof(HandInput) == 128 && sizeof(InputFrame) == 256);
namespace
{
std::mutex gate;
vr::VRActionSetHandle_t actionSet = 0;
vr::VRActionHandle_t actions[2][8]{};
bool identityReady = false;
#ifdef HYBRID_INPUT_TEST
auto* Input() { return TestInput(); }
auto* Applications() { return TestApplications(); }
#else
vr::IVRApplications* Applications()
{
    vr::EVRInitError error{};
    return static_cast<vr::IVRApplications*>(vr::VR_GetGenericInterface(vr::IVRApplications_Version, &error));
}
vr::IVRInput* Input()
{
    vr::EVRInitError error{};
    return static_cast<vr::IVRInput*>(vr::VR_GetGenericInterface(vr::IVRInput_Version, &error));
}
#endif
}

extern "C" __declspec(dllexport) int __stdcall HybridInput_Abi() { return 2; }

// Isolate this package's actions from bindings retained for the game's generated EXE key.
extern "C" __declspec(dllexport) int __stdcall HybridInput_IdentifyApplication(
    const char* manifest, const char* appKey, uint32_t processId)
{
    std::lock_guard lock(gate);
    actionSet = 0;
    identityReady = false;
    if (!manifest || !*manifest || !appKey || !*appKey || !processId ||
        std::strlen(appKey) >= vr::k_unMaxApplicationKeyLength)
        return vr::VRApplicationError_InvalidParameter;
    auto* applications = Applications();
    if (!applications) return vr::VRApplicationError_IPCFailed;
    // Temporary registration does not leave a launcher entry after SteamVR exits.
    auto error = applications->AddApplicationManifest(manifest, true);
    if (error != vr::VRApplicationError_None) return error;
    error = applications->IdentifyApplication(processId, appKey);
    if (error != vr::VRApplicationError_None) return error;
    char actual[vr::k_unMaxApplicationKeyLength]{};
    error = applications->GetApplicationKeyByProcessId(processId, actual, sizeof(actual));
    if (error != vr::VRApplicationError_None) return error;
    if (std::strcmp(actual, appKey) != 0) return vr::VRApplicationError_InvalidApplication;
    identityReady = true;
    return vr::VRApplicationError_None;
}

// Reuse the renderer's session. Never initialize/shutdown VR or wait for a frame here.
extern "C" __declspec(dllexport) int __stdcall HybridInput_Initialize(const char* manifest)
{
    std::lock_guard lock(gate);
    actionSet = 0;
    if (!manifest || !*manifest) return vr::VRInputError_InvalidParam;
    if (!identityReady) return vr::VRInputError_InvalidHandle;
    auto* input = Input();
    if (!input) return vr::VRInputError_IPCError;
    auto error = input->SetActionManifestPath(manifest);
    if (error != vr::VRInputError_None) return error;
    const char* names[] = { "pose", "aim", "primary", "secondary", "click", "trigger", "grip", "stick" };
    for (int hand = 0; hand < 2; ++hand)
        for (int i = 0; i < 8; ++i)
        {
            std::string path = std::string("/actions/hybrid/in/") + (hand == 0 ? "left_" : "right_") + names[i];
            error = input->GetActionHandle(path.c_str(), &actions[hand][i]);
            if (error != vr::VRInputError_None) return error;
        }
    return input->GetActionSetHandle("/actions/hybrid", &actionSet);
}

extern "C" __declspec(dllexport) int __stdcall HybridInput_Read(InputFrame* frame, uint32_t size, int origin)
{
    if (!frame || size != sizeof(InputFrame)) return vr::VRInputError_InvalidParam;
    *frame = {};
    if (origin < 0 || origin > 2) return vr::VRInputError_InvalidParam;
    std::lock_guard lock(gate);
    if (!actionSet) return vr::VRInputError_InvalidHandle;
    auto* input = Input();
    if (!input) return vr::VRInputError_IPCError;
    vr::VRActiveActionSet_t set{};
    set.ulActionSet = actionSet;
    auto error = input->UpdateActionState(&set, sizeof(set), 1);
    if (error != vr::VRInputError_None) return error;
    for (int h = 0; h < 2; ++h)
    {
        auto& out = h == 0 ? frame->left : frame->right;
        for (int i = 0; i < 8; ++i)
        {
            if (i < 2)
            {
                vr::InputPoseActionData_t data{};
                error = input->GetPoseActionDataRelativeToNow(actions[h][i], static_cast<vr::ETrackingUniverseOrigin>(origin),
                    0, &data, sizeof(data), vr::k_ulInvalidInputValueHandle);
                if (error != vr::VRInputError_None) continue;
                if (data.bActive) out.active |= 1u << i;
                auto valid = data.bActive && data.pose.bDeviceIsConnected && data.pose.bPoseIsValid;
                if (i == 0) { out.valid = valid; out.gripPose = data.pose.mDeviceToAbsoluteTracking; }
                else { out.aimValid = valid; out.aimPose = data.pose.mDeviceToAbsoluteTracking; }
            }
            else if (i < 5)
            {
                vr::InputDigitalActionData_t data{};
                error = input->GetDigitalActionData(actions[h][i], &data, sizeof(data), vr::k_ulInvalidInputValueHandle);
                if (error != vr::VRInputError_None || !data.bActive) continue;
                out.active |= 1u << i;
                if (data.bState) out.buttons |= 1u << (i - 2);
            }
            else
            {
                vr::InputAnalogActionData_t data{};
                error = input->GetAnalogActionData(actions[h][i], &data, sizeof(data), vr::k_ulInvalidInputValueHandle);
                if (error != vr::VRInputError_None || !data.bActive) continue;
                out.active |= 1u << i;
                if (i == 5) out.trigger = data.x;
                else if (i == 6) out.grip = data.x;
                else { out.x = data.x; out.y = data.y; }
            }
        }
    }
    return vr::VRInputError_None;
}

extern "C" __declspec(dllexport) void __stdcall HybridInput_Reset()
{
    std::lock_guard lock(gate);
    actionSet = 0;
    identityReady = false;
}
