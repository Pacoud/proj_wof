
using System.Diagnostics.Tracing;

namespace WoF.Benchmarks;

[EventSource(Name = "WoF-Benchmarks")]
public sealed class BenchmarkEventSource : EventSource
{
    public static readonly BenchmarkEventSource Log = new();

    private const EventTask TickTask = (EventTask)1;
    private const EventTask MeasurementTask = (EventTask)2;

    private BenchmarkEventSource()
    {
    }

    [Event(
        1,
        Level = EventLevel.Informational,
        Task = TickTask,
        Opcode = EventOpcode.Start)]
    public void MeasuredTickStart(int tick)
    {
        if (IsEnabled())
            WriteEvent(1, tick);
    }

    [Event(
        2,
        Level = EventLevel.Informational,
        Task = TickTask,
        Opcode = EventOpcode.Stop)]
    public void MeasuredTickStop(int tick)
    {
        if (IsEnabled())
            WriteEvent(2, tick);
    }

    [Event(
        3,
        Level = EventLevel.Informational,
        Task = MeasurementTask,
        Opcode = EventOpcode.Start)]
    public void MeasurementStart()
    {
        if (IsEnabled())
            WriteEvent(3);
    }

    [Event(
        4,
        Level = EventLevel.Informational,
        Task = MeasurementTask,
        Opcode = EventOpcode.Stop)]
    public void MeasurementStop()
    {
        if (IsEnabled())
            WriteEvent(4);
    }
}
