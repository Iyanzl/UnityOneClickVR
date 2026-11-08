using System;
using System.Globalization;

namespace Uuvr.OpenVR;

// Wall time of main-thread calls, including queue waits; not a GPU timing query.
internal sealed class RenderCostWindow
{
    public int Count { get; private set; }
    public double PoseTotal { get; private set; }
    public double LeftTotal { get; private set; }
    public double RightTotal { get; private set; }
    public double IntervalTotal { get; private set; }
    public double MaxInterval { get; private set; }
    public double MaxEyeCalls { get; private set; }

    public void Add(double poseMs, double leftMs, double rightMs, double intervalMs)
    {
        if (!Valid(poseMs) || !Valid(leftMs) || !Valid(rightMs) || !Valid(intervalMs)) return;
        Count++;
        PoseTotal += poseMs;
        LeftTotal += leftMs;
        RightTotal += rightMs;
        IntervalTotal += intervalMs;
        MaxInterval = Math.Max(MaxInterval, intervalMs);
        MaxEyeCalls = Math.Max(MaxEyeCalls, leftMs + rightMs);
    }

    private static bool Valid(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0;

    public string Format() => string.Format(CultureInfo.InvariantCulture,
        "main-thread calls: samples={0}, poseAvgMs={1:0.00}, leftAvgMs={2:0.00}, rightAvgMs={3:0.00}, eyeCallsMaxMs={4:0.00}, intervalAvgMs={5:0.00}, intervalMaxMs={6:0.00}; includes queue waits, not GPU timing",
        Count, PoseTotal / Math.Max(1, Count), LeftTotal / Math.Max(1, Count),
        RightTotal / Math.Max(1, Count), MaxEyeCalls, IntervalTotal / Math.Max(1, Count), MaxInterval);

    public void Reset()
    {
        Count = 0;
        PoseTotal = LeftTotal = RightTotal = IntervalTotal = MaxInterval = MaxEyeCalls = 0;
    }
}
