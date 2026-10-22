
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Position du combattant face au tir ennemi.
/// </summary>
internal enum TacticalStance
{
    Standing,
    Crouched,
    Prone
}

/// <summary>
/// Protection locale reellement utilisee.
/// Le simple fait d'etre dans un secteur boise
/// ou urbain ne garantit pas cette protection.
/// </summary>
internal enum TacticalCover
{
    None,
    Vegetation,
    Building,
    Entrenchment
}

/// <summary>
/// Etat defensif d'une compagnie.
/// Aucun homme ni materiel n'est stocke ici.
/// </summary>
internal readonly record struct TacticalDefensiveDisposition(
    TacticalStance Stance,
    TacticalCover Cover);
