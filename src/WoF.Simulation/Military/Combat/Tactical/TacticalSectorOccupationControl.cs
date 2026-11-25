using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>Contrôle réel des petits secteurs. Conserver une instance par bataille.</summary>
public sealed class TacticalSectorOccupationControl
{
    private readonly TacticalGeometryMap _geometry;
    private readonly DiplomacySystem _diplomacy;
    private readonly Country? _initial;
    private readonly Dictionary<BattleSector, Country> _changes = new();

    // Pour une case restée bloquée, mémoriser les présences et les relations
    // diplomatiques qui expliquaient la décision de la passe précédente.
    private readonly record struct Presence(Country Country, bool CanCapture);
    private readonly record struct Relation(Country First, Country Second, bool AtWar);
    private sealed record Blocked(Presence[] Present, List<Relation> Relations);
    private Dictionary<BattleSector, Blocked> _blocked = new();

    public TacticalSectorOccupationControl(TacticalGeometryMap geometry, DiplomacySystem diplomacy)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);
        _geometry = geometry;
        _diplomacy = diplomacy;

        var engagement = geometry.Battle.Engagement;
        if (!engagement.IsActive || geometry.Terrain is not { HasSectorMap: true } terrain)
            throw new InvalidOperationException("An active battle on a mapped province grid is required.");

        // Pour une rencontre sur liaison, la grille indique sur laquelle des
        // deux provinces se joue la bataille. Son contrôleur peut être nul.
        var provinces = new[] { engagement.Province, engagement.ConnectionA, engagement.ConnectionB };
        var province = provinces.FirstOrDefault(p => p?.Id == terrain.Grid.Descriptor.ProvinceId)
            ?? throw new InvalidOperationException("Grid province does not belong to this engagement.");
        _initial = province.Controller;
    }

    public Country? GetController(BattleSector sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
        if (!_geometry.Battle.Battlefield.ContainsSector(sector))
            throw new InvalidOperationException("Sector does not belong to this battle.");

        return _changes.TryGetValue(sector, out var owner) ? owner : _initial;
    }

    /// <summary>Applique l'occupation et renvoie le nombre de cases ayant changé de main.</summary>
    public int ApplyOccupation()
    {
        var battle = _geometry.Battle;
        if (!battle.Engagement.IsActive)
            throw new InvalidOperationException("Cannot update an ended battle.");

        var index = new TacticalSectorPresenceIndex(battle, _geometry);
        var nextBlocked = new Dictionary<BattleSector, Blocked>();
        int transfers = 0;

        // Une case devenue vide garde son détenteur. Sa décision bloquée
        // éventuelle disparaît de nextBlocked, sans balayer toute la grille.
        foreach (var sector in index.GetCoveredSectors())
        {
            var present = ReadPresence(index.GetCovering(sector));
            var owner = GetController(sector);
            if (present.Length == 0 || present.Any(p => p.Country.Id == owner?.Id))
                continue;

            if (_blocked.TryGetValue(sector, out var known)
                && present.SequenceEqual(known.Present)
                && known.Relations.All(r => _diplomacy.AreAtWar(r.First, r.Second) == r.AtWar))
            {
                nextBlocked.Add(sector, known);
                continue;
            }

            var relations = new List<Relation>();
            var winner = FindClaimant(present, owner, relations);
            if (winner is null)
            {
                nextBlocked.Add(sector, new Blocked(present, relations));
                continue;
            }

            // Seuls les écarts au contrôleur initial sont stockés.
            if (winner.Id == _initial?.Id)
                _changes.Remove(sector);
            else
                _changes[sector] = winner;

            transfers++;
        }

        _blocked = nextBlocked;
        return transfers;
    }

    private static Presence[] ReadPresence(IReadOnlyList<CompanyDeployment> covering)
    {
        var countries = new Dictionary<int, Presence>();
        foreach (var deployment in covering)
        {
            var company = deployment.Company;
            var country = company.ParentBattalion?.ParentDivision?.Country;
            if (deployment.IsInTransit || company.Composition!.Manpower.Current == 0 || country is null)
                continue;

            // Une compagnie en déroute reste présente. Une autre compagnie
            // du même pays peut néanmoins être en état de prendre la case.
            bool canCapture = !deployment.IsRouted;
            if (!countries.TryGetValue(country.Id, out var other) || (!other.CanCapture && canCapture))
                countries[country.Id] = new Presence(country, canCapture);
        }

        return countries.Values.OrderBy(p => p.Country.Id).ToArray();
    }

    private Country? FindClaimant(Presence[] present, Country? owner, List<Relation> relations)
    {
        bool AtWar(Country first, Country second)
        {
            bool atWar = _diplomacy.AreAtWar(first, second);
            relations.Add(new Relation(first, second, atWar));
            return atWar;
        }

        // La présence physique de deux pays en guerre maintient la contestation.
        for (int i = 0; i < present.Length; i++)
            for (int j = i + 1; j < present.Length; j++)
                if (AtWar(present[i].Country, present[j].Country))
                    return null;

        // Une troupe en état de prendre peut réclamer une case sans détenteur
        // ou ennemie. Entre prétendants admissibles, l'ordre des ID départage.
        foreach (var candidate in present)
            if (candidate.CanCapture && (owner is null || AtWar(candidate.Country, owner)))
                return candidate.Country;

        return null;
    }
}