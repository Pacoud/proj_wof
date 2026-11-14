using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Contact atteint physiquement durant une heure.
/// Les positions correspondent a la geometrie reelle
/// des compagnies apres application du mouvement.
/// </summary>
internal sealed record TacticalConfirmedMobileContact(
    long Hour,
    CompanyDeployment First,
    CompanyDeployment Second,
    double ContactTimeFraction,
    TacticalPoint FirstPosition,
    TacticalPoint SecondPosition);

/// <summary>
/// Autorisations de participation aux combats mobiles
/// pour une seule heure tactique.
///
/// Un contact n'accorde aucune localisation ennemie.
/// </summary>
internal sealed class TacticalMobileEngagementState
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<
        (CompanyDeployment, CompanyDeployment),
        TacticalConfirmedMobileContact> _pairs = new();

    private IReadOnlyList<TacticalConfirmedMobileContact> _contacts =
        Array.Empty<TacticalConfirmedMobileContact>();

    private HashSet<CompanyDeployment> _participants =
        new(ReferenceEqualityComparer.Instance);
    private long? _hour;

    internal TacticalMobileEngagementState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal IReadOnlyList<TacticalConfirmedMobileContact>
        GetContacts(long hour)
    {
        return _hour == hour
            ? _contacts
            : Array.Empty<TacticalConfirmedMobileContact>();
    }

    internal TacticalConfirmedMobileContact? Find(
        CompanyDeployment first,
        CompanyDeployment second,
        long hour)
    {
        if (_hour != hour)
            return null;

        return _pairs.TryGetValue(
            (first, second), out var contact)
                ? contact
                : null;
    }

    internal bool IsParticipant(
        CompanyDeployment company,
        long hour)
    {
        return _hour == hour && _participants.Contains(company);
    }

    internal void Publish(
        long hour,
        IReadOnlyList<TacticalConfirmedMobileContact> contacts,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        double contactRadiusMeters)
    {
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(contacts);
        ArgumentNullException.ThrowIfNull(geometry);

        if (!ReferenceEquals(geometry.Battle, _battle))
            throw new InvalidOperationException(
                "Foreign tactical geometry.");

        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Engagement has ended.");

        if (hour < _battle.Engagement.StartHour
            || (_hour is long last && hour <= last))
        {
            throw new InvalidOperationException(
                "Invalid mobile engagement hour.");
        }

        if (!double.IsFinite(contactRadiusMeters)
            || contactRadiusMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contactRadiusMeters));
        }

        var next = new Dictionary<
            (CompanyDeployment, CompanyDeployment),
            TacticalConfirmedMobileContact>();

        var participants = new HashSet<CompanyDeployment>(
            ReferenceEqualityComparer.Instance);
        foreach (var contact in contacts)
        {
            if (contact is null
                || contact.Hour != hour
                || ReferenceEquals(contact.First, contact.Second)
                || !_battle.ContainsDeployment(contact.First)
                || !_battle.ContainsDeployment(contact.Second)
                || (!contact.First.IsInTransit
                    && !contact.Second.IsInTransit)
                || !double.IsFinite(contact.ContactTimeFraction)
                || contact.ContactTimeFraction < 0
                || contact.ContactTimeFraction > 1)
            {
                throw new InvalidOperationException(
                    "Invalid mobile contact.");
            }

            var firstCountry = contact.First.Company.ParentBattalion?.ParentDivision?.Country;
            var secondCountry = contact.Second.Company.ParentBattalion?.ParentDivision?.Country;

            if (firstCountry is null || secondCountry is null
                || !diplomacy.AreAtWar(firstCountry, secondCountry))
            {
                throw new InvalidOperationException(
                    "Mobile contacts require countries at war.");
            }

            var a = geometry.GetPosition(contact.First);
            var b = geometry.GetPosition(contact.Second);

            if (a is null || b is null
                || a.Point.HorizontalDistanceTo(
                    contact.FirstPosition) > 0.01
                || b.Point.HorizontalDistanceTo(
                    contact.SecondPosition) > 0.01
                || a.Point.HorizontalDistanceTo(b.Point)
                    > contactRadiusMeters + 0.01)
            {
                throw new InvalidOperationException(
                    "Contact was not reached physically.");
            }

            if (next.ContainsKey(
                (contact.First, contact.Second)))
            {
                throw new InvalidOperationException(
                    "Duplicated mobile contact.");
            }

            next.Add(
                (contact.First, contact.Second), contact);

            next.Add(
                (contact.Second, contact.First), contact);
            participants.Add(contact.First);
            participants.Add(contact.Second);
        }

        _pairs.Clear();

        foreach (var pair in next)
            _pairs.Add(pair.Key, pair.Value);

        _contacts = Array.AsReadOnly(contacts.ToArray());
        _participants = participants;
        _hour = hour;
    }
}
