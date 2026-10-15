
using System.Diagnostics.Tracing;

namespace WoF.Benchmarks;

[EventSource(Name = "WoF-Benchmarks")]
public sealed class BenchmarkEventSource : EventSource
{
    public static readonly BenchmarkEventSource Log = new();

    private BenchmarkEventSource()
    {
    }

    [Event(1, Level = EventLevel.Informational)]
    public void MeasuredTickStart(int tick)
    {
        if (IsEnabled())
            WriteEvent(1, tick);
    }

    [Event(2, Level = EventLevel.Informational)]
    public void MeasuredTickEnd(int tick)
    {
        if (IsEnabled())
            WriteEvent(2, tick);
    }
}
