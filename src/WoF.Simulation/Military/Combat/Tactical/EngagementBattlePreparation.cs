using WoF.Simulation.Military.Organization;

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
    // Les formations hiérarchiques ont des parents verrouillés, mais leurs effectifs
    // et la liste des participants à l'engagement peuvent évoluer.
    private readonly Division[] _participants;
    private readonly (Company Company, int Current)[] _strengths;

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

        // Une compagnie écartée à zéro peut ensuite recevoir un homme.
        // Mémoriser aussi son état, indépendamment de la liste de placements.
        _participants = engagement.Participants.ToArray();
        _strengths = _participants.SelectMany(d => d.Battalions)
            .SelectMany(b => b.Companies)
            .Select(c => (c, c.Composition!.Manpower.Current))
            .ToArray();
    }

    internal void EnsureCurrent()
    {
        if (!Engagement.IsActive)
            throw new InvalidOperationException("The engagement has ended. Prepare again for an active engagement.");

        if (!_participants.SequenceEqual(Engagement.Participants, ReferenceEqualityComparer.Instance)
            || _participants.Any(d => !ReferenceEquals(d.CurrentEngagement, Engagement)))
            throw new InvalidOperationException("The engagement participants have changed. Prepare again.");

        foreach (var (company, current) in _strengths)
        {
            if (company.Composition!.Manpower.Current != current)
                throw new InvalidOperationException($"Personnel strength changed for {company.Name}. Prepare again.");
        }
    }
}