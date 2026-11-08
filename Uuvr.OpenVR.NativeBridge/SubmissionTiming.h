#pragma once
#include <array>
#include <atomic>
#include <chrono>
#include <cwchar>

namespace submit_timing
{
enum class Mode { Off, Observe, NextWait };
inline Mode ReadMode()
{
    wchar_t value[64]{};
    const auto size = GetEnvironmentVariableW(L"HYBRIDVR_SUBMIT_EXPERIMENT", value, 64);
    if (!size || size >= 64) return Mode::Off;
    if (std::wcscmp(value, L"observe") == 0) return Mode::Observe;
    if (std::wcscmp(value, L"next-wait") == 0) return Mode::NextWait;
    return Mode::Off;
}

enum class Part { Queue, BetweenFrames, PoseWait, CopyLeft, CopyRight, Flush, Left, Right, Handoff, Count };
constexpr const char* names[] = {"queue", "between", "wait", "copyL", "copyR", "flush", "submitL", "submitR", "handoff"};
struct Counter
{
    std::atomic<unsigned long long> count{0}, sumUs{0}, maxUs{0}, slow{0}, lastSlowUs{0}, lastSlowTickMs{0};
    void Reset() { count = 0; sumUs = 0; maxUs = 0; slow = 0; lastSlowUs = 0; lastSlowTickMs = 0; }
    void Record(unsigned long long duration, unsigned long long tick)
    {
        count.fetch_add(1, std::memory_order_relaxed);
        sumUs.fetch_add(duration, std::memory_order_relaxed);
        auto previous = maxUs.load(std::memory_order_relaxed);
        while (duration > previous && !maxUs.compare_exchange_weak(previous, duration, std::memory_order_relaxed)) {}
        if (duration >= 20000)
        {
            slow.fetch_add(1, std::memory_order_relaxed);
            lastSlowUs.store(duration, std::memory_order_relaxed);
            lastSlowTickMs.store(tick, std::memory_order_relaxed);
        }
    }
};
inline std::atomic<Mode> mode{Mode::Off};
inline std::array<Counter, static_cast<size_t>(Part::Count)> counters;
inline unsigned long long lastCompleteUs = 0; // Serial graphics callback only.
inline unsigned long long Now()
{
    return static_cast<unsigned long long>(std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}
inline void Record(Part part, unsigned long long start)
{
    if (!start) return;
    const auto end = Now();
    if (end >= start) counters[static_cast<size_t>(part)].Record(end - start, GetTickCount64());
}
inline void Reset(Mode selected)
{
    for (auto& counter : counters) counter.Reset();
    lastCompleteUs = 0;
    mode = selected;
}
struct Scope
{
    Part part;
    unsigned long long start;
    Scope(Part value, bool enabled) : part(value), start(enabled ? Now() : 0) {}
    ~Scope() { Record(part, start); }
};
}
