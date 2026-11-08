#include <array>
#include <atomic>
#include <cmath>
#include <cstdio>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <d3d11.h>
#include <wrl/client.h>
#include "IUnityGraphics.h"
#include "openvr.h"
#include "SubmissionTiming.h"

#ifdef UUVR_BRIDGE_TEST
vr::EVRCompositorError TestSubmit(int eye, const vr::VRTextureWithPose_t& texture, vr::EVRSubmitFlags flags);
vr::EVRCompositorError TestWaitGetPoses();
void TestHandoff();
#endif

namespace
{
using Microsoft::WRL::ComPtr;
enum class Stage { Idle, Configured, CreateTargets, CopyLeft, CopyRight, Flush,
    SubmitLeft, SubmitRight, Handoff, Complete, Failed, Stopped, PoseWait, PoseReady };
constexpr const char* kStages[] = { "idle", "configured", "create-targets", "copy-left",
    "copy-right", "flush", "submit-left", "submit-right", "handoff", "complete", "failed", "stopped", "pose-wait", "pose-ready" };

struct StereoResources
{
    std::array<ComPtr<ID3D11Texture2D>, 2> source, target;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    D3D11_TEXTURE2D_DESC desc{};
    vr::VRTextureBounds_t bounds{};
    vr::EColorSpace colorSpace{};
    uintptr_t generation{};
    submit_timing::Mode timingMode = submit_timing::Mode::Off;
    std::atomic<bool> usable{true};
};
struct RenderFrame
{
    std::shared_ptr<StereoResources> resources;
    vr::HmdMatrix34_t renderPose{};
    uintptr_t token{};
    unsigned long long queuedUs = 0;
    // Only the serial graphics callback changes these flags.
    bool began = false, leftReady = false, frameReady = false;
};
struct EyeCounters
{
    std::atomic<int> error{0};
    std::atomic<unsigned long long> attempted{0}, returned{0}, accepted{0};
};
std::array<EyeCounters, 2> g_eyes;
std::mutex g_registry;
std::shared_ptr<StereoResources> g_current;
uintptr_t g_nextGeneration = 0;
uintptr_t g_nextFrame = 0;
constexpr size_t kMaxPendingFrames = 8;
std::unordered_map<uintptr_t, std::shared_ptr<RenderFrame>> g_frames;
std::atomic<unsigned> g_pendingFrames{0};
std::atomic<unsigned long long> g_refusedFrames{0}, g_lastSubmittedFrame{0};
std::atomic_flag g_renderBusy = ATOMIC_FLAG_INIT;
std::atomic<unsigned> g_inFlight{0};
std::atomic<Stage> g_stage{Stage::Idle};
std::atomic<ULONGLONG> g_stageSince{0};
std::atomic<long> g_resourceError{S_OK};
std::atomic<unsigned long long> g_entered{0}, g_dropped{0}, g_pairs{0}, g_targetBuilds{0}, g_generation{0};
std::atomic<unsigned> g_width{0}, g_height{0}, g_format{0};
std::atomic<unsigned long long> g_poseEntered{0}, g_poseReturned{0};
std::atomic<int> g_poseError{0};
std::atomic<DWORD> g_poseThread{0};

void SetStage(Stage stage)
{
    g_stageSince.store(GetTickCount64());
    g_stage.store(stage);
}

DXGI_FORMAT TargetFormat(DXGI_FORMAT format)
{
    switch (format)
    {
    case DXGI_FORMAT_R8G8B8A8_TYPELESS: return DXGI_FORMAT_R8G8B8A8_UNORM;
    case DXGI_FORMAT_B8G8R8A8_TYPELESS: return DXGI_FORMAT_B8G8R8A8_UNORM;
    case DXGI_FORMAT_R8G8B8A8_UNORM: case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
    case DXGI_FORMAT_B8G8R8A8_UNORM: case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: return format;
    default: return DXGI_FORMAT_UNKNOWN;
    }
}

bool SameDevice(ID3D11Device* a, ID3D11Device* b)
{
    ComPtr<IUnknown> ia, ib;
    return SUCCEEDED(a->QueryInterface(IID_PPV_ARGS(&ia))) &&
        SUCCEEDED(b->QueryInterface(IID_PPV_ARGS(&ib))) && ia.Get() == ib.Get();
}

HRESULT CreateTargets(StereoResources& r)
{
    if (r.target[0] && r.target[1]) return S_OK;
    SetStage(Stage::CreateTargets);
    auto desc = r.desc;
    desc.Format = TargetFormat(desc.Format);
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    desc.CPUAccessFlags = desc.MiscFlags = 0;
    for (auto& target : r.target)
    {
        const auto hr = r.device->CreateTexture2D(&desc, nullptr, target.ReleaseAndGetAddressOf());
        if (FAILED(hr)) return hr;
    }
    r.device->GetImmediateContext(r.context.GetAddressOf());
    ++g_targetBuilds;
    return r.context ? S_OK : E_FAIL;
}

void BeginFrame(RenderFrame& frame)
{
    auto& r = *frame.resources;
    const bool timed = r.timingMode != submit_timing::Mode::Off;
    if (timed)
    {
        submit_timing::Record(submit_timing::Part::Queue, frame.queuedUs);
        submit_timing::Record(submit_timing::Part::BetweenFrames, submit_timing::lastCompleteUs);
    }
    frame.began = true;
    frame.leftReady = frame.frameReady = false;
    SetStage(Stage::PoseWait);
    g_poseThread = GetCurrentThreadId();
    ++g_poseEntered;
    submit_timing::Scope waitTiming(submit_timing::Part::PoseWait, timed);
#ifdef UUVR_BRIDGE_TEST
    const auto error = TestWaitGetPoses();
#else
    vr::EVRInitError initError = vr::VRInitError_None;
    auto* compositor = static_cast<vr::IVRCompositor*>(
        vr::VR_GetGenericInterface(vr::IVRCompositor_Version, &initError));
    // Establish the submission cycle here, but submit the immutable pose actually
    // used by Unity, not this later sample's implicit compositor pose.
    vr::TrackedDevicePose_t hmd{};
    const auto error = compositor && initError == vr::VRInitError_None
        ? compositor->WaitGetPoses(&hmd, 1, nullptr, 0)
        : vr::VRCompositorError_RequestFailed;
#endif
    g_poseError = static_cast<int>(error);
    ++g_poseReturned;
    if (!r.usable) { SetStage(Stage::Stopped); return; }
    frame.frameReady = error == vr::VRCompositorError_None;
    if (!frame.frameReady && error != vr::VRCompositorError_DoNotHaveFocus) r.usable = false;
    SetStage(frame.frameReady ? Stage::PoseReady : Stage::Failed);
}

void SubmitPair(RenderFrame& frame)
{
    auto& r = *frame.resources;
#ifndef UUVR_BRIDGE_TEST
    vr::EVRInitError initError = vr::VRInitError_None;
    auto* compositor = static_cast<vr::IVRCompositor*>(
        vr::VR_GetGenericInterface(vr::IVRCompositor_Version, &initError));
    if (!compositor || initError != vr::VRInitError_None)
    {
        r.usable = false;
        for (auto& eye : g_eyes) eye.error = vr::VRCompositorError_RequestFailed;
        SetStage(Stage::Failed);
        return;
    }
#endif
    bool fatal = false;
    for (int eye = 0; eye < 2; ++eye)
    {
        vr::VRTextureWithPose_t texture{};
        texture.handle = r.target[eye].Get();
        texture.eType = vr::TextureType_DirectX;
        texture.eColorSpace = r.colorSpace;
        texture.mDeviceToAbsoluteTracking = frame.renderPose;
        constexpr auto flags = vr::Submit_TextureWithPose;
        ++g_eyes[eye].attempted;
        SetStage(eye == 0 ? Stage::SubmitLeft : Stage::SubmitRight);
        const auto submitStart = r.timingMode != submit_timing::Mode::Off ? submit_timing::Now() : 0;
#ifdef UUVR_BRIDGE_TEST
        const auto error = TestSubmit(eye, texture, flags);
#else
        const auto error = compositor->Submit(static_cast<vr::EVREye>(eye), &texture,
            &r.bounds, flags);
#endif
        submit_timing::Record(eye == 0 ? submit_timing::Part::Left : submit_timing::Part::Right, submitStart);
        g_eyes[eye].error = static_cast<int>(error);
        ++g_eyes[eye].returned;
        if (error == vr::VRCompositorError_None) ++g_eyes[eye].accepted;
        else if (error != vr::VRCompositorError_DoNotHaveFocus &&
                 error != vr::VRCompositorError_AlreadySubmitted) fatal = true;
    }
    g_lastSubmittedFrame = frame.token;
    // GC08 opt-in: Unity still renders its desktop after this callback. Let the
    // next frame's WaitGetPoses hand off instead of declaring rendering complete here.
    if (r.timingMode != submit_timing::Mode::NextWait)
    {
        SetStage(Stage::Handoff);
        submit_timing::Scope handoffTiming(submit_timing::Part::Handoff, r.timingMode != submit_timing::Mode::Off);
#ifdef UUVR_BRIDGE_TEST
        TestHandoff();
#else
        compositor->PostPresentHandoff();
#endif
    }
    if (r.timingMode != submit_timing::Mode::Off) submit_timing::lastCompleteUs = submit_timing::Now();
    if (fatal) r.usable = false;
    SetStage(fatal ? Stage::Failed : Stage::Complete);
}

struct FlightGuard
{
    std::shared_ptr<RenderFrame> frame;
    bool admitted = false;
    ~FlightGuard()
    {
        // Release the callback's last resource references before allowing runtime shutdown.
        frame.reset();
        if (admitted) --g_inFlight;
    }
};
struct BusyGuard { ~BusyGuard() { g_renderBusy.clear(); } };

void ForgetFrame(uintptr_t token)
{
    std::lock_guard lock(g_registry);
    g_frames.erase(token);
    g_pendingFrames = static_cast<unsigned>(g_frames.size());
}

void UNITY_INTERFACE_API OnRenderEventAndData(int eventId, void* token)
{
    if ((eventId != 1 && eventId != 2 && eventId != 3) || !token) return;
    if (g_renderBusy.test_and_set()) { ++g_dropped; return; }
    BusyGuard busy;
    FlightGuard flight;
    {
        std::lock_guard lock(g_registry);
        // Each token identifies one immutable pose and texture generation, never a
        // pointer to memory that the main thread can overwrite while graphics waits.
        auto found = g_frames.find(reinterpret_cast<uintptr_t>(token));
        if (found == g_frames.end() || !g_current || !g_current->usable ||
            found->second->resources != g_current)
        {
            ++g_dropped;
            return;
        }
        flight.frame = found->second;
        ++g_inFlight;
        flight.admitted = true;
    }
    ++g_entered;
    auto& frame = *flight.frame;
    auto& r = *frame.resources;
    if (eventId == 3)
    {
        if (frame.began) { ++g_dropped; return; }
        BeginFrame(frame);
        if (!frame.frameReady) ForgetFrame(frame.token);
        return;
    }
    if (!frame.frameReady) { ++g_dropped; ForgetFrame(frame.token); return; }
    const auto hr = CreateTargets(r);
    g_resourceError = hr;
    if (FAILED(hr)) { r.usable = false; SetStage(Stage::Failed); ForgetFrame(frame.token); return; }
    if (eventId == 1)
    {
        if (frame.leftReady) ++g_dropped;
        SetStage(Stage::CopyLeft);
        submit_timing::Scope copyTiming(submit_timing::Part::CopyLeft, r.timingMode != submit_timing::Mode::Off);
        r.context->CopyResource(r.target[0].Get(), r.source[0].Get());
        frame.leftReady = true;
        return;
    }
    if (!frame.leftReady) { ++g_dropped; ForgetFrame(frame.token); return; }
    frame.leftReady = false;
    frame.frameReady = false;
    SetStage(Stage::CopyRight);
    {
        submit_timing::Scope copyTiming(submit_timing::Part::CopyRight, r.timingMode != submit_timing::Mode::Off);
        r.context->CopyResource(r.target[1].Get(), r.source[1].Get());
    }
    ++g_pairs;
    SetStage(Stage::Flush);
    {
        submit_timing::Scope flushTiming(submit_timing::Part::Flush, r.timingMode != submit_timing::Mode::Off);
        r.context->Flush();
    }
    // No application mutex is held across runtime calls which may block in the driver.
    SubmitPair(frame);
    ForgetFrame(frame.token);
}
}

extern "C" __declspec(dllexport) int UNITY_INTERFACE_API UuvrOpenVR_GetAbiVersion() { return 4; }

extern "C" __declspec(dllexport) UnityRenderingEventAndData UNITY_INTERFACE_API
UuvrOpenVR_GetRenderEventFunc() { return OnRenderEventAndData; }

extern "C" __declspec(dllexport) void* UNITY_INTERFACE_API UuvrOpenVR_ConfigureStereo(
    void* left, void* right, float uMin, float vMin, float uMax, float vMax,
    int colorSpace, unsigned submitFlags)
{
    g_resourceError = E_INVALIDARG;
    if (!left || !right || left == right || submitFlags != 0 || colorSpace < 0 || colorSpace > 2)
        return nullptr;
    for (float value : {uMin, vMin, uMax, vMax})
        if (!std::isfinite(value) || value < 0 || value > 1) return nullptr;
    if (uMin == uMax || vMin == vMax) return nullptr;
    try
    {
        auto r = std::make_shared<StereoResources>();
        void* textures[] = {left, right};
        ComPtr<ID3D11Device> rightDevice;
        for (int eye = 0; eye < 2; ++eye)
        {
            // Caller supplies freshly queried Unity native textures and keeps them alive here.
            auto hr = static_cast<IUnknown*>(textures[eye])->QueryInterface(IID_PPV_ARGS(&r->source[eye]));
            if (FAILED(hr)) { g_resourceError = hr; return nullptr; }
            D3D11_TEXTURE2D_DESC desc{};
            r->source[eye]->GetDesc(&desc);
            if (!desc.Width || !desc.Height || desc.Width > 8192 || desc.Height > 8192 ||
                desc.ArraySize != 1 || desc.MipLevels != 1 || desc.SampleDesc.Count != 1 ||
                TargetFormat(desc.Format) == DXGI_FORMAT_UNKNOWN) return nullptr;
            if (eye == 0) r->desc = desc;
            else if (r->desc.Width != desc.Width || r->desc.Height != desc.Height ||
                     r->desc.Format != desc.Format) return nullptr;
        }
        r->source[0]->GetDevice(&r->device);
        r->source[1]->GetDevice(&rightDevice);
        if (!SameDevice(r->device.Get(), rightDevice.Get())) return nullptr;
        r->bounds = {uMin, vMin, uMax, vMax};
        r->colorSpace = static_cast<vr::EColorSpace>(colorSpace);
        r->timingMode = submit_timing::ReadMode();
        std::lock_guard lock(g_registry);
        if (g_inFlight != 0) { g_resourceError = HRESULT_FROM_WIN32(ERROR_BUSY); return nullptr; }
        submit_timing::Reset(r->timingMode);
        if (g_current) g_current->usable = false;
        g_frames.clear();
        g_pendingFrames = 0;
        g_refusedFrames = g_lastSubmittedFrame = 0;
        r->generation = ++g_nextGeneration;
        g_current = r;
        g_generation = r->generation;
        g_width = r->desc.Width; g_height = r->desc.Height; g_format = r->desc.Format;
        for (auto& e : g_eyes) { e.error = 0; e.attempted = 0; e.returned = 0; e.accepted = 0; }
        g_poseEntered = g_poseReturned = 0;
        g_poseError = 0;
        g_poseThread = 0;
        g_resourceError = S_OK;
        SetStage(Stage::Configured);
        return reinterpret_cast<void*>(r->generation);
    }
    catch (...) { g_resourceError = E_OUTOFMEMORY; return nullptr; }
}

extern "C" __declspec(dllexport) void* UNITY_INTERFACE_API UuvrOpenVR_QueueRenderPose(
    void* generation, const vr::HmdMatrix34_t* pose)
{
    static_assert(sizeof(vr::HmdMatrix34_t) == 48);
    if (!generation || !pose) return nullptr;
    for (const auto& row : pose->m)
        for (float value : row)
            if (!std::isfinite(value)) return nullptr;
    for (int row = 0; row < 3; ++row)
    {
        const auto length = pose->m[row][0]*pose->m[row][0] + pose->m[row][1]*pose->m[row][1] + pose->m[row][2]*pose->m[row][2];
        if (length < 0.9f || length > 1.1f) return nullptr;
    }
    try
    {
        std::lock_guard lock(g_registry);
        if (!g_current || !g_current->usable || g_current->generation != reinterpret_cast<uintptr_t>(generation)) return nullptr;
        if (g_frames.size() >= kMaxPendingFrames) { ++g_refusedFrames; return nullptr; }
        auto frame = std::make_shared<RenderFrame>();
        frame->resources = g_current;
        frame->renderPose = *pose;
        frame->token = ++g_nextFrame;
        if (g_current->timingMode != submit_timing::Mode::Off) frame->queuedUs = submit_timing::Now();
        g_frames.emplace(frame->token, frame);
        g_pendingFrames = static_cast<unsigned>(g_frames.size());
        return reinterpret_cast<void*>(frame->token);
    }
    catch (...) { ++g_refusedFrames; return nullptr; }
}

extern "C" __declspec(dllexport) int UNITY_INTERFACE_API UuvrOpenVR_TryStop()
{
    std::lock_guard lock(g_registry);
    if (g_current) g_current->usable = false;
    g_frames.clear();
    g_pendingFrames = 0;
    g_current.reset();
    if (g_inFlight != 0) return 0;
    SetStage(Stage::Stopped);
    return 1;
}

extern "C" __declspec(dllexport) int UNITY_INTERFACE_API UuvrOpenVR_IsActive()
{
    std::lock_guard lock(g_registry);
    return g_current && g_current->usable ? 1 : 0;
}

extern "C" __declspec(dllexport) int UNITY_INTERFACE_API UuvrOpenVR_GetLastError(int eye)
{ return eye >= 0 && eye < 2 ? g_eyes[eye].error.load() : vr::VRCompositorError_RequestFailed; }

extern "C" __declspec(dllexport) unsigned long long UNITY_INTERFACE_API UuvrOpenVR_GetSubmitCount(int eye)
{ return eye >= 0 && eye < 2 ? g_eyes[eye].accepted.load() : 0; }

extern "C" __declspec(dllexport) int UNITY_INTERFACE_API UuvrOpenVR_GetDiagnostics(char* buffer, int capacity)
{
    if (!buffer || capacity < 1) return 0;
    const auto since = g_stageSince.load();
    // Only atomics: readable even when the Unity main/render threads have stopped progressing.
    auto used = snprintf(buffer, capacity,
        "abi=4 generation=%llu stage=%s stageAgeMs=%llu inFlight=%u resourceHR=0x%08lX\n"
        "sourceSize=%ux%u format=%u entered=%llu dropped=%llu copiedPairs=%llu targetPairsCreated=%llu\n"
        "left attempted=%llu returned=%llu accepted=%llu error=%d\n"
        "right attempted=%llu returned=%llu accepted=%llu error=%d\n"
        "poseWait entered=%llu returned=%llu error=%d threadId=%lu\n"
        "renderPose pending=%u refused=%llu lastSubmittedFrame=%llu explicit=1\n",
        g_generation.load(), kStages[static_cast<int>(g_stage.load())], GetTickCount64()-since,
        g_inFlight.load(), static_cast<unsigned long>(g_resourceError.load()),
        g_width.load(), g_height.load(), g_format.load(), g_entered.load(), g_dropped.load(),
        g_pairs.load(), g_targetBuilds.load(),
        g_eyes[0].attempted.load(), g_eyes[0].returned.load(), g_eyes[0].accepted.load(), g_eyes[0].error.load(),
        g_eyes[1].attempted.load(), g_eyes[1].returned.load(), g_eyes[1].accepted.load(), g_eyes[1].error.load(),
        g_poseEntered.load(), g_poseReturned.load(), g_poseError.load(), g_poseThread.load(),
        g_pendingFrames.load(), g_refusedFrames.load(), g_lastSubmittedFrame.load());
    const auto timingMode = submit_timing::mode.load();
    if (timingMode != submit_timing::Mode::Off && used >= 0 && used < capacity)
    {
        used += snprintf(buffer + used, capacity - used,
            "gc08=%s tickMs=%llu timings=us(cumulative;slow>=20000;non-transactional)\n",
            timingMode == submit_timing::Mode::NextWait ? "next-wait" : "observe", GetTickCount64());
        for (size_t i = 0; i < submit_timing::counters.size() && used >= 0 && used < capacity; ++i)
        {
            const auto& c = submit_timing::counters[i];
            used += snprintf(buffer + used, capacity - used,
                "%s n=%llu sum=%llu max=%llu slow=%llu last=%llu at=%llu\n", submit_timing::names[i],
                c.count.load(), c.sumUs.load(), c.maxUs.load(), c.slow.load(), c.lastSlowUs.load(), c.lastSlowTickMs.load());
        }
    }
    return used;
}
