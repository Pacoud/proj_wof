
using System.Diagnostics;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Phases du moteur tactique pouvant etre mesurees.
/// Les mesures de sous-phases doivent etre exclusives.
/// </summary>
internal enum TacticalBenchmarkPhase
{
    Validation,

    MovementPreparation,
    MovementContactDetection,
    MovementStabilization,
    MovementPositionValidation,
    MovementApplication,
    MovementConfirmation,

    MobileParticipantPreparation,
    Detection,
    FireOrderPlanning,
    FireExecution,

    PersonnelContacts,
    PersonnelEffects,
    CasualtyApplication,
    ExposureReconciliation,

    Suppression,
    Cohesion,
    BattalionAssessment,
    BattalionReactions
}

/// <summary>
/// Mesure agregee d'une phase tactique.
/// </summary>
internal readonly record struct TacticalPhaseMeasurement(
    TacticalBenchmarkPhase Phase,
    double Milliseconds,
    long AllocatedBytes,
    int Calls);

/// <summary>
/// Collecteur facultatif de performances.
/// N'intervient jamais dans les decisions militaires.
/// Utilisation monothread.
/// </summary>
internal sealed class TacticalPhaseTimingRecorder
{
    internal readonly record struct StartMark(
        long Timestamp,
        long AllocatedBytes,
        int ThreadId);

    private readonly Dictionary<
        TacticalBenchmarkPhase,
        TacticalPhaseMeasurement> _measurements = new();

    /// <summary>
    /// Capture le debut d'une mesure.
    /// </summary>
    internal StartMark Start()
    {
        return new StartMark(
            Stopwatch.GetTimestamp(),
            GC.GetAllocatedBytesForCurrentThread(),
            Environment.CurrentManagedThreadId);
    }

    /// <summary>
    /// Termine une mesure et l'ajoute aux statistiques.
    /// Chaque StartMark doit etre arrete une seule fois.
    /// </summary>
    internal void Stop(
        TacticalBenchmarkPhase phase,
        StartMark start)
    {
        if (!Enum.IsDefined(phase))
            throw new ArgumentOutOfRangeException(
                nameof(phase));

        if (start.ThreadId !=
            Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException(
                "Measurement must end on its starting thread.");
        }

        long endTimestamp = Stopwatch.GetTimestamp();

        long endAllocated =
            GC.GetAllocatedBytesForCurrentThread();

        long elapsedTicks =
            endTimestamp - start.Timestamp;

        long allocatedBytes =
            endAllocated - start.AllocatedBytes;

        if (elapsedTicks < 0 || allocatedBytes < 0)
        {
            throw new InvalidOperationException(
                "Invalid performance measurement.");
        }

        double milliseconds =
            elapsedTicks * 1000.0 / Stopwatch.Frequency;

        if (_measurements.TryGetValue(
            phase, out var previous))
        {
            _measurements[phase] =
                new TacticalPhaseMeasurement(
                    phase,
                    previous.Milliseconds + milliseconds,
                    checked(previous.AllocatedBytes
                        + allocatedBytes),
                    checked(previous.Calls + 1));
        }
        else
        {
            _measurements.Add(
                phase,
                new TacticalPhaseMeasurement(
                    phase,
                    milliseconds,
                    allocatedBytes,
                    1));
        }
    }

    /// <summary>
    /// Retourne une copie independante des mesures.
    /// </summary>
    internal IReadOnlyList<TacticalPhaseMeasurement> Snapshot()
    {
        return Array.AsReadOnly(
            _measurements.Values
                .OrderBy(m => m.Phase)
                .ToArray());
    }

    /// <summary>
    /// Recupere les mesures et prepare
    /// l'enregistreur pour le prochain echantillon.
    /// </summary>
    internal IReadOnlyList<TacticalPhaseMeasurement>
        CaptureAndReset()
    {
        var result = Snapshot();
        _measurements.Clear();
        return result;
    }

    internal void Reset()
    {
        _measurements.Clear();
    }
}
