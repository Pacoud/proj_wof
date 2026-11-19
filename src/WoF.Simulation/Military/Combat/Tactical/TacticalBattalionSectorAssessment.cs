
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Classification instantanee de la couverture
/// d'un secteur par un bataillon.
/// Ce n'est pas une machine a etats persistante.
/// </summary>
internal enum TacticalBattalionSectorStatus
{
    Uncovered,
    Capable,
    AtRisk,
    UnableToHold
}

/// <summary>
/// Photographie de la capacite d'un bataillon
/// dans un secteur tactique donne.
///
/// Les valeurs sont derivees des compagnies
/// et de leurs deploiements existants.
/// </summary>
internal sealed record TacticalBattalionSectorAssessment(
    Battalion Battalion,
    BattleSector Sector,
    bool IsPrioritySector,
    int FrontLineAssignments,
    int StationaryFrontLineCompanies,
    int AuthorizedFrontLinePersonnel,
    int AvailableFrontLinePersonnel,
    int SupportAssignments,
    int ReserveAssignments,
    double FrontLineReadiness,
    TacticalBattalionSectorStatus Status);
