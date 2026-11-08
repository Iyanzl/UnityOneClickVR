#include <chrono>
#include <future>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include <cstring>
#include "NativeBridge.cpp"

namespace
{
int checks = 0;
std::atomic<bool> blockSubmit{false}, enteredSubmit{false}, releaseSubmit{false};
std::atomic<bool> blockPose{false}, enteredPose{false}, releasePose{false};
vr::EVRCompositorError poseError = vr::VRCompositorError_None;
vr::EVRCompositorError injectedError = vr::VRCompositorError_None;
std::array<bool, 2> submittedInCycle{};
std::array<ID3D11Texture2D*, 2> submitted{};
std::array<vr::HmdMatrix34_t, 2> submittedPose{};
std::array<vr::EVRSubmitFlags, 2> submittedFlags{};
std::atomic<unsigned> handoffCalls{0};
unsigned handoffDelayMs = 0;
struct ExperimentEnvironment
{
    std::wstring original;
    bool present = false;
    ExperimentEnvironment()
    {
        const auto size = GetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", nullptr, 0);
        if (size)
        {
            std::vector<wchar_t> buffer(size);
            GetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", buffer.data(), size);
            original = buffer.data();
            present = true;
        }
        SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", nullptr);
    }
    ~ExperimentEnvironment() { SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", present ? original.c_str() : nullptr); }
};
void Check(bool ok, const char* description)
{
    if (!ok) throw std::runtime_error(description);
    ++checks;
    std::cout << "PASS " << description << '\n';
}
ComPtr<ID3D11Device> Device()
{
    ComPtr<ID3D11Device> device;
    auto hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0,
        D3D11_SDK_VERSION, &device, nullptr, nullptr);
    if (FAILED(hr)) throw std::runtime_error("WARP device creation failed");
    return device;
}
ComPtr<ID3D11Texture2D> Texture(ID3D11Device* device, unsigned color, unsigned width = 16,
    DXGI_FORMAT format = DXGI_FORMAT_R8G8B8A8_UNORM)
{
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width; desc.Height = 16; desc.MipLevels = desc.ArraySize = 1;
    desc.Format = format; desc.SampleDesc.Count = 1;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    std::vector<unsigned> pixels(width*16, color);
    D3D11_SUBRESOURCE_DATA data{pixels.data(), width*4, 0};
    ComPtr<ID3D11Texture2D> texture;
    if (FAILED(device->CreateTexture2D(&desc, &data, &texture))) throw std::runtime_error("Texture creation failed");
    return texture;
}
void* Configure(ID3D11Texture2D* left, ID3D11Texture2D* right)
{
    return UuvrOpenVR_ConfigureStereo(left, right, 0, 1, 1, 0, vr::ColorSpace_Auto, 0);
}
unsigned Pixel(ID3D11Texture2D* texture)
{
    D3D11_TEXTURE2D_DESC desc{};
    texture->GetDesc(&desc);
    desc.Usage = D3D11_USAGE_STAGING; desc.BindFlags = 0; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Texture2D> staging;
    texture->GetDevice(&device); device->GetImmediateContext(&context);
    if (FAILED(device->CreateTexture2D(&desc, nullptr, &staging))) throw std::runtime_error("Staging create failed");
    context->CopyResource(staging.Get(), texture);
    D3D11_MAPPED_SUBRESOURCE mapped{};
    if (FAILED(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped))) throw std::runtime_error("Staging map failed");
    auto value = *static_cast<unsigned*>(mapped.pData);
    context->Unmap(staging.Get(), 0);
    return value;
}
vr::HmdMatrix34_t Pose(float x = 0)
{
    vr::HmdMatrix34_t pose{};
    pose.m[0][0] = pose.m[1][1] = pose.m[2][2] = 1;
    pose.m[0][3] = x;
    return pose;
}
void* Frame(void* generation)
{
    const auto pose = Pose();
    return UuvrOpenVR_QueueRenderPose(generation, &pose);
}
void RunFrame(void* token) { OnRenderEventAndData(3, token); OnRenderEventAndData(1, token); OnRenderEventAndData(2, token); }
void Pair(void* generation) { RunFrame(Frame(generation)); }
}

vr::EVRCompositorError TestWaitGetPoses()
{
    if (blockPose)
    {
        enteredPose = true;
        while (!releasePose) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    submittedInCycle = {};
    return poseError;
}

vr::EVRCompositorError TestSubmit(int eye, const vr::VRTextureWithPose_t& texture, vr::EVRSubmitFlags flags)
{
    if (submittedInCycle[eye]) return vr::VRCompositorError_AlreadySubmitted;
    submittedInCycle[eye] = true;
    submitted[eye] = static_cast<ID3D11Texture2D*>(texture.handle);
    submittedPose[eye] = texture.mDeviceToAbsoluteTracking;
    submittedFlags[eye] = flags;
    if (eye == 0 && blockSubmit)
    {
        enteredSubmit = true;
        while (!releaseSubmit) std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    return injectedError;
}
void TestHandoff()
{
    ++handoffCalls;
    if (handoffDelayMs) std::this_thread::sleep_for(std::chrono::milliseconds(handoffDelayMs));
}

int main()
{
    try
    {
        ExperimentEnvironment environment;
        Check(UuvrOpenVR_GetAbiVersion() == 4, "explicit ABI 4 prevents mixing generation and frame tokens");
        auto device = Device();
        auto left = Texture(device.Get(), 0xFF123456), right = Texture(device.Get(), 0xFF987654);
        auto token = Configure(left.Get(), right.Get());
        Check(token != nullptr, "valid stereo pair with flipped V accepted");
        Check(g_current->source[0].Get() == left.Get(), "Unity source strongly retained");
        auto frame = Frame(token);
        OnRenderEventAndData(1, frame); OnRenderEventAndData(2, frame);
        Check(g_poseEntered == 0 && UuvrOpenVR_GetSubmitCount(0) == 0, "eyes without compositor begin cannot submit");
        frame = Frame(token);
        OnRenderEventAndData(3, frame);
        OnRenderEventAndData(2, frame);
        Check(UuvrOpenVR_GetSubmitCount(0) == 0, "right without left does not submit");
        auto sourceLeft = left.Get(), sourceRight = right.Get();
        left.Reset(); right.Reset();
        Pair(token);
        Check(submitted[0] != sourceLeft && submitted[1] != sourceRight && submitted[0] != submitted[1], "native submission owns distinct targets");
        Check(Pixel(submitted[0]) == 0xFF123456 && Pixel(submitted[1]) == 0xFF987654, "both eye copies survive caller releasing sources");
        auto targets = submitted;
        auto builds = g_targetBuilds.load();
        for (int i = 0; i < 120; ++i) Pair(token);
        Check(g_targetBuilds == builds && submitted == targets, "120 frames reuse one target pair");
        Check(UuvrOpenVR_GetSubmitCount(0) == 121 && UuvrOpenVR_GetSubmitCount(1) == 121, "paired accepted counters");
        Check(g_poseEntered == g_poseReturned && g_poseEntered == 122, "one completed wait per submitted cycle plus orphan begin");
        frame = Frame(token);
        OnRenderEventAndData(1, frame); OnRenderEventAndData(2, frame);
        Check(UuvrOpenVR_GetSubmitCount(0) == 121, "duplicate eyes need a new frame begin");
        frame = Frame(token);
        OnRenderEventAndData(3, frame);
        auto drop = g_dropped.load();
        OnRenderEventAndData(1, frame); OnRenderEventAndData(1, frame); OnRenderEventAndData(2, frame);
        Check(g_dropped == drop+1, "duplicate left replaces incomplete pair");
        left = Texture(device.Get(), 1); right = Texture(device.Get(), 2);
        auto otherDevice = Device();
        auto other = Texture(otherDevice.Get(), 3), wide = Texture(device.Get(), 4, 32);
        Check(!Configure(left.Get(), left.Get()), "aliased eye sources rejected");
        Check(!Configure(left.Get(), other.Get()), "cross-device pair rejected");
        Check(!Configure(left.Get(), wide.Get()), "mismatched size rejected");
        Check(!UuvrOpenVR_ConfigureStereo(left.Get(), right.Get(), NAN, 0, 1, 1, 0, 0), "NaN bounds rejected");
        Check(!UuvrOpenVR_ConfigureStereo(left.Get(), right.Get(), 0, 0, 1, 1, 0, 1), "unsupported submit flags rejected");
        Check(UuvrOpenVR_IsActive() == 1, "invalid configuration preserves active generation");
        auto next = Configure(left.Get(), right.Get());
        Check(next && next != token, "reconfiguration has a new generation");
        Pair(token);
        Check(UuvrOpenVR_GetSubmitCount(0) == 0, "stale queued events ignored");
        Pair(next);
        poseError = vr::VRCompositorError_DoNotHaveFocus;
        Pair(next);
        Check(UuvrOpenVR_IsActive() && g_eyes[0].attempted == 1, "pose focus loss skips pair without stopping session");
        poseError = vr::VRCompositorError_None;
        injectedError = vr::VRCompositorError_DoNotHaveFocus;
        Pair(next);
        Check(UuvrOpenVR_IsActive() == 1 && g_eyes[0].returned == 2 && g_eyes[0].accepted == 1, "focus loss is counted but recoverable");
        injectedError = vr::VRCompositorError_InvalidTexture;
        Pair(next);
        auto attempts = g_eyes[0].attempted.load();
        Pair(next);
        Check(!UuvrOpenVR_IsActive() && g_eyes[0].attempted == attempts, "fatal submit error stops retry loop");
        injectedError = vr::VRCompositorError_None;
        next = Configure(left.Get(), right.Get());
        std::weak_ptr<StereoResources> held = g_current;
        blockSubmit = true;
        auto worker = std::async(std::launch::async, [next] { Pair(next); });
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (!enteredSubmit && std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        if (!enteredSubmit) { releaseSubmit = true; throw std::runtime_error("Mock submit not entered"); }
        char diagnostic[2048]{};
        UuvrOpenVR_GetDiagnostics(diagnostic, sizeof(diagnostic));
        const bool stage = std::string(diagnostic).find("stage=submit-left") != std::string::npos;
        const bool busy = !Configure(left.Get(), right.Get());
        const auto start = std::chrono::steady_clock::now();
        const auto stopped = UuvrOpenVR_TryStop();
        const bool fast = std::chrono::steady_clock::now()-start < std::chrono::milliseconds(100);
        const bool retained = !held.expired();
        releaseSubmit = true;
        worker.get();
        Check(stage, "diagnostics readable during blocked Submit");
        Check(busy, "reconfigure rejected while callback admitted");
        Check(stopped == 0 && fast, "stop reports busy without waiting on Submit");
        Check(retained && held.expired(), "inflight owns resources until callback returns");
        Check(UuvrOpenVR_TryStop() == 1 && g_inFlight == 0, "stop reports drained after callback returns");
        attempts = g_eyes[0].attempted.load();
        Pair(next);
        Check(g_eyes[0].attempted == attempts, "events queued before stop cannot access runtime");
        Check(UuvrOpenVR_GetDiagnostics(nullptr, 0) == 0, "null diagnostic buffer guarded");

        next = Configure(left.Get(), right.Get());
        poseError = vr::VRCompositorError_RequestFailed;
        Pair(next);
        auto waits = g_poseEntered.load();
        Pair(next);
        Check(!UuvrOpenVR_IsActive() && g_eyes[0].attempted == 0 && g_poseEntered == waits,
            "fatal pose error stops retry and cannot submit old eyes");
        poseError = vr::VRCompositorError_None;
        next = Configure(left.Get(), right.Get());
        held = g_current;
        blockPose = true;
        auto poseWorker = std::async(std::launch::async, [next] { Pair(next); });
        const auto poseDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (!enteredPose && std::chrono::steady_clock::now() < poseDeadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        if (!enteredPose) { releasePose = true; throw std::runtime_error("Mock pose wait not entered"); }
        UuvrOpenVR_GetDiagnostics(diagnostic, sizeof(diagnostic));
        const bool poseStage = std::string(diagnostic).find("stage=pose-wait") != std::string::npos;
        const bool renderThread = g_poseThread != GetCurrentThreadId();
        const bool poseBusy = !Configure(left.Get(), right.Get());
        const auto poseStopStart = std::chrono::steady_clock::now();
        const auto poseStopped = UuvrOpenVR_TryStop();
        const bool poseFast = std::chrono::steady_clock::now()-poseStopStart < std::chrono::milliseconds(100);
        const bool poseRetained = !held.expired();
        releasePose = true;
        poseWorker.get();
        Check(poseStage && renderThread, "blocked pose diagnostics identify worker thread and exact stage");
        Check(poseBusy && poseStopped == 0 && poseFast, "blocked pose forbids replace and shutdown without waiting");
        Check(poseRetained && held.expired() && g_inFlight == 0, "pose wait retains resources until callback drains");
        Check(g_eyes[0].attempted == 0, "stop during pose wait prevents post-wait submit");
        Check(UuvrOpenVR_TryStop() == 1, "drained pose callback permits shutdown");
        waits = g_poseEntered.load();
        Pair(next);
        Check(g_poseEntered == waits, "stale begin events cannot touch runtime after stop");

        blockPose = blockSubmit = false;
        next = Configure(left.Get(), right.Get());
        auto poseA = Pose(1.25f), poseB = Pose(-4.5f);
        auto frameA = UuvrOpenVR_QueueRenderPose(next, &poseA);
        auto frameB = UuvrOpenVR_QueueRenderPose(next, &poseB);
        Check(frameA && frameB && frameA != frameB && g_pendingFrames == 2, "each queued frame has an independent token");
        poseA.m[0][3] = 1000;
        RunFrame(frameA);
        Check(submittedPose[0].m[0][3] == 1.25f && submittedPose[1].m[0][3] == 1.25f,
            "both eyes use original frame A pose despite caller mutation and queued B");
        Check(submittedFlags[0] == vr::Submit_TextureWithPose && submittedFlags[1] == vr::Submit_TextureWithPose,
            "both submissions explicitly declare their render pose");
        Check(g_pendingFrames == 1 && g_lastSubmittedFrame == reinterpret_cast<uintptr_t>(frameA),
            "completed frame released without consuming later pose");
        RunFrame(frameB);
        Check(submittedPose[0].m[0][3] == -4.5f && g_pendingFrames == 0, "queued frame B cannot borrow A pose");
        enteredPose = releasePose = false;
        blockPose = true;
        auto blockedPose = Pose(7), laterPose = Pose(-8);
        auto blockedFrame = UuvrOpenVR_QueueRenderPose(next, &blockedPose);
        auto delayedFrame = std::async(std::launch::async, [blockedFrame] { RunFrame(blockedFrame); });
        const auto delayedDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(5);
        while (!enteredPose && std::chrono::steady_clock::now() < delayedDeadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        if (!enteredPose) { releasePose = true; throw std::runtime_error("Delayed frame did not enter wait"); }
        auto laterFrame = UuvrOpenVR_QueueRenderPose(next, &laterPose);
        blockedPose.m[0][3] = 700;
        releasePose = true;
        delayedFrame.get();
        Check(laterFrame && submittedPose[0].m[0][3] == 7 && submittedPose[1].m[0][3] == 7,
            "queueing next pose during graphics wait cannot mutate inflight stereo pose");
        blockPose = false;
        RunFrame(laterFrame);
        Check(submittedPose[0].m[0][3] == -8 && g_pendingFrames == 0,
            "pose queued concurrently during wait is independently consumed next");
        auto poseRotation = Pose();
        poseRotation.m[0][0] = 0; poseRotation.m[0][2] = 1;
        poseRotation.m[2][0] = -1; poseRotation.m[2][2] = 0;
        poseRotation.m[1][3] = 1.73f;
        auto rotated = UuvrOpenVR_QueueRenderPose(next, &poseRotation);
        RunFrame(rotated);
        Check(std::memcmp(&submittedPose[0], &poseRotation, sizeof(poseRotation)) == 0 &&
              std::memcmp(&submittedPose[1], &poseRotation, sizeof(poseRotation)) == 0,
            "raw OpenVR orientation and position survive without Unity handedness conversion");
        attempts = g_eyes[0].attempted.load();
        waits = g_poseEntered.load();
        RunFrame(rotated);
        Check(g_eyes[0].attempted == attempts && g_poseEntered == waits, "replayed frame cannot wait or submit again");
        frame = Frame(next);
        OnRenderEventAndData(3, frame); OnRenderEventAndData(3, frame);
        Check(g_poseEntered == waits + 1, "duplicate begin cannot change compositor cycle");
        OnRenderEventAndData(1, frame); OnRenderEventAndData(2, frame);
        auto invalidPose = Pose(); invalidPose.m[0][1] = NAN;
        Check(!UuvrOpenVR_QueueRenderPose(next, &invalidPose), "non-finite pose rejected");
        invalidPose = {};
        Check(!UuvrOpenVR_QueueRenderPose(next, &invalidPose) && !UuvrOpenVR_QueueRenderPose(next, nullptr),
            "zero rotation and null pose rejected");
        std::vector<void*> pending;
        for (size_t i = 0; i < kMaxPendingFrames; ++i) pending.push_back(Frame(next));
        Check(g_pendingFrames == kMaxPendingFrames && !Frame(next) && g_refusedFrames == 1,
            "blocked graphics queue has a fixed eight-frame bound");
        for (auto queued : pending) RunFrame(queued);
        Check(g_pendingFrames == 0 && Frame(next), "queue drains and accepts new frames");
        auto retiredFrame = Frame(next);
        auto newest = Configure(left.Get(), right.Get());
        RunFrame(retiredFrame);
        Check(newest && g_pendingFrames == 0 && g_poseEntered == 0 && !Frame(next),
            "reconfiguration invalidates old queued poses and texture generations");
        frame = Frame(newest);
        Check(UuvrOpenVR_TryStop() == 1 && g_pendingFrames == 0, "stop releases unconsumed pose packets");
        RunFrame(frame);
        Check(g_poseEntered == 0, "stopped pose packets never touch compositor");

        using namespace submit_timing;
        Counter counter;
        counter.Record(19999, 1); counter.Record(20000, 2); counter.Record(30000, 3);
        Check(counter.count == 3 && counter.sumUs == 69999 && counter.maxUs == 30000 &&
            counter.slow == 2 && counter.lastSlowUs == 30000 && counter.lastSlowTickMs == 3,
            "timing counters preserve threshold, maximum and last slow event");
        counter.Reset();
        Check(counter.count == 0 && counter.sumUs == 0 && counter.maxUs == 0 && counter.slow == 0 &&
            counter.lastSlowUs == 0 && counter.lastSlowTickMs == 0, "timing reset clears all aggregates");
        Check(ReadMode() == Mode::Off, "experiment defaults off");
        for (const auto* invalid : {L"", L"Observe", L"next-wait-extra", L"1",
             L"next-wait-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"})
        {
            SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", invalid);
            Check(ReadMode() == Mode::Off, "only exact process-local experiment modes accepted");
        }
        for (const auto selected : {Mode::Off, Mode::Observe, Mode::NextWait})
        {
            const auto* value = selected == Mode::Off ? nullptr : selected == Mode::Observe ? L"observe" : L"next-wait";
            SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", value);
            auto generation = Configure(left.Get(), right.Get());
            Check(generation && g_current->timingMode == selected, "generation captures requested handoff mode");
            const auto callsBefore = handoffCalls.load();
            const auto buildsBefore = g_targetBuilds.load();
            const auto expectedPose = Pose(2.7f);
            for (int i = 0; i < 120; ++i)
            {
                auto packet = expectedPose;
                auto queued = UuvrOpenVR_QueueRenderPose(generation, &packet);
                packet.m[0][3] = -100;
                RunFrame(queued);
                Check(std::memcmp(&submittedPose[0], &expectedPose, sizeof(packet)) == 0 &&
                    std::memcmp(&submittedPose[1], &expectedPose, sizeof(packet)) == 0 &&
                    submittedFlags[0] == vr::Submit_TextureWithPose && submittedFlags[1] == vr::Submit_TextureWithPose,
                    "all handoff modes retain immutable explicit pose for both eyes");
            }
            Check(g_poseEntered == 120 && g_poseReturned == 120 && g_pendingFrames == 0 &&
                g_eyes[0].accepted == 120 && g_eyes[1].accepted == 120 && g_targetBuilds == buildsBefore + 1,
                "all handoff modes keep one wait per pair and reuse textures");
            Check(handoffCalls == callsBefore + (selected == Mode::NextWait ? 0 : 120),
                "next-wait alone omits early handoff; default and observe preserve it");
            Check(counters[static_cast<size_t>(Part::PoseWait)].count == (selected == Mode::Off ? 0 : 120) &&
                counters[static_cast<size_t>(Part::BetweenFrames)].count == (selected == Mode::Off ? 0 : 119),
                "default has no timing samples and first frame has no prior-completion gap");
            Check(counters[static_cast<size_t>(Part::Handoff)].count == (selected == Mode::Observe ? 120 : 0),
                "handoff timing count reflects actual call count");
            char text[2048]{};
            const auto length = UuvrOpenVR_GetDiagnostics(text, sizeof(text));
            Check(length > 0 && length < static_cast<int>(sizeof(text)) &&
                (selected == Mode::Off ? std::strstr(text, "gc08=") == nullptr : std::strstr(text, "handoff n=") != nullptr),
                "native telemetry fits existing managed diagnostic buffer without ABI change");
            SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", L"next-wait");
            Check(g_current->timingMode == selected, "environment cannot change a live generation's policy");
            Check(UuvrOpenVR_TryStop() == 1, "every handoff mode drains without retained packets");
        }
        SetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", L"observe");
        auto timedGeneration = Configure(left.Get(), right.Get());
        handoffDelayMs = 30;
        Pair(timedGeneration);
        handoffDelayMs = 0;
        const auto& handoff = counters[static_cast<size_t>(Part::Handoff)];
        Check(handoff.slow == 1 && handoff.lastSlowUs >= 20000 && handoff.lastSlowTickMs != 0,
            "slow mock handoff is attributed to handoff rather than eye rendering");
        char guarded[] = {'x', 'x', 'x'};
        UuvrOpenVR_GetDiagnostics(guarded, 1);
        Check(guarded[0] == '\0' && guarded[1] == 'x' && guarded[2] == 'x', "tiny diagnostic buffer stays bounded");
        UuvrOpenVR_TryStop();
        std::cout << checks << " checks passed; WARP + mock compositor, no headset validation.\n";
        return 0;
    }
    catch (const std::exception& e) { std::cerr << "FAIL " << e.what() << '\n'; return 1; }
}
