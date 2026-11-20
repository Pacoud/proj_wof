
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Echec de capture pour un bataillon.
///
/// Ne contient aucune reference militaire vivante.
/// </summary>
internal sealed record TacticalCommandSituationCaptureFailure(
    int BattalionId,
    string FriendlyDivisionName,
    string Reason);

/// <summary>
/// Situations du QG publiees pour une heure tactique.
///
/// Une capture reussie reste consultable apres
/// l'emission d'ordres modifiant les deploiements.
/// Les echecs sont explicites et ne produisent
/// pas de photographie inventee.
/// </summary>
internal sealed record TacticalCommandSituationPhaseResult(
    long Hour,
    IReadOnlyList<TacticalBattalionCommandSituationSnapshot>
        Snapshots,
    IReadOnlyList<TacticalCommandSituationCaptureFailure>
        Failures);
