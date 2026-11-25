namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Refus de préparation pour un engagement actif et sa propre grille.</summary>
public enum EngagementBattleRefusal
{
    None,
    MissingSide,
    NoEntryBorder,
    BattlefieldTooShallow,
    ParticipantWithoutSide,
    LegacyResources,
    NoCompanies,
}

/// <summary>
/// Décisions à consommer dans la même opération de simulation, avant toute mutation du monde.
/// Les placements sont figés ; ils désignent les vraies compagnies et leurs parents.
/// </summary>
public sealed class EngagementBattlePreparation
{
    public Engagement Engagement { get; }
    public ProvinceBattlefieldGrid Field { get; }
    public IReadOnlyList<int> BattlefieldSectorIds { get; }
    public IReadOnlyList<CompanyPlacement> Companies { get; }

    internal EngagementBattlePreparation(
        Engagement engagement, EngagementDeploymentZones zones, IEnumerable<CompanyPlacement> companies)
    {
        Engagement = engagement;
        Field = zones.Field;
        // 3c fournit déjà les identifiants dans une collection en lecture seule.
        BattlefieldSectorIds = zones.BattlefieldSectorIds;
        Companies = Array.AsReadOnly(companies.ToArray());
    }
}