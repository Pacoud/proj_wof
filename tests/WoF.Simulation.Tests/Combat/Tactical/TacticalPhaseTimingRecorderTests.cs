
using WoF.Simulation.Military.Combat.Tactical;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalPhaseTimingRecorderTests
{
    [Fact]
    public void RecordingProducesValidMeasurements()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var start = recorder.Start();

        var allocation = new byte[1024];
        GC.KeepAlive(allocation);

        recorder.Stop(
            TacticalBenchmarkPhase.Detection, start);

        var result = Assert.Single(recorder.Snapshot());

        Assert.Equal(
            TacticalBenchmarkPhase.Detection,
            result.Phase);

        Assert.Equal(1, result.Calls);
        Assert.True(double.IsFinite(result.Milliseconds));
        Assert.True(result.Milliseconds >= 0);
        Assert.True(result.AllocatedBytes >= 0);
    }

    [Fact]
    public void RepeatedPhaseMeasurementsAreAccumulated()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        for (int i = 0; i < 3; i++)
        {
            var start = recorder.Start();

            recorder.Stop(
                TacticalBenchmarkPhase.MovementContactDetection,
                start);
        }

        var result = Assert.Single(recorder.Snapshot());

        Assert.Equal(3, result.Calls);
        Assert.True(result.Milliseconds >= 0);
        Assert.True(result.AllocatedBytes >= 0);
    }

    [Fact]
    public void DifferentPhasesRemainIndependent()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var first = recorder.Start();

        recorder.Stop(
            TacticalBenchmarkPhase.Detection, first);

        var second = recorder.Start();

        recorder.Stop(
            TacticalBenchmarkPhase.FireExecution, second);

        var results = recorder.Snapshot();

        Assert.Equal(2, results.Count);

        Assert.Contains(results, m =>
            m.Phase == TacticalBenchmarkPhase.Detection);

        Assert.Contains(results, m =>
            m.Phase == TacticalBenchmarkPhase.FireExecution);
    }

    [Fact]
    public void CaptureAndResetClearsPreviousMeasurements()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var start = recorder.Start();

        recorder.Stop(
            TacticalBenchmarkPhase.Cohesion, start);

        var captured = recorder.CaptureAndReset();

        Assert.Single(captured);
        Assert.Empty(recorder.Snapshot());

        // La copie precedente reste utilisable.
        Assert.Single(captured);
    }

    [Fact]
    public void InvalidPhaseIsRejected()
    {
        var recorder = new TacticalPhaseTimingRecorder();

        var start = recorder.Start();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            recorder.Stop(
                (TacticalBenchmarkPhase)9999,
                start));
    }
}
