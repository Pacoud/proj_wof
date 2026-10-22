
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Etat fige lors de l'execution d'une salve.
/// Contient des informations reelles internes
/// au moteur, non destinees au renseignement.
/// </summary>
/// 
/// 
internal readonly record struct TacticalFireSnapshot(
    TacticalFireEvent FireEvent,
    CompanyDeployment RealTarget,
    TacticalPoint ShooterPoint,
    TacticalPoint ReportedTargetPoint,
    TacticalPoint RealTargetPoint,
    double LocalizationUncertaintyMeters,
    TacticalDefensiveDisposition TargetDefense,

    // Donnees de population capturees au tir.
    
    TacticalPersonnelExposure TargetPersonnel,
    double TargetFrontageMeters,
    double TargetFacingDegrees);
