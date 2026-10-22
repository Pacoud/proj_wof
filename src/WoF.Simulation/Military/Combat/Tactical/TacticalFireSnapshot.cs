
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Instantane interne d'une salve executee.
/// Il contient des informations reelles qui ne
/// doivent pas etre exposees au joueur.
/// </summary>
internal readonly record struct TacticalFireSnapshot(
    TacticalFireEvent FireEvent,
    CompanyDeployment RealTarget,
    TacticalPoint ShooterPoint,
    TacticalPoint ReportedTargetPoint,
    TacticalPoint RealTargetPoint,
    double LocalizationUncertaintyMeters,
    TacticalDefensiveDisposition TargetDefense);
