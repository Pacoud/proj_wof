
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public enum CompanyDeploymentRole
{
    FrontLine,
    Support,
    Reserve
}

public sealed class CompanyDeployment
{

    private readonly TacticalBattle _battle;

    public Company Company { get; }

    public BattleSector Sector { get; private set; }

    public CompanyDeploymentRole Role { get; private set; }

    public bool IsInTransit {get; private set; }

    public bool IsRouted { get; private set; }

    // Le scénario peut déclencher une déroute ou un ralliement sur place.
    // Le front, le rôle, le trajet et la cohésion gardent leur propre état.
    public void SetRouted(bool isRouted)
    {
        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException("Cannot change rout in an ended battle.");

        IsRouted = isRouted;
    }

    internal CompanyDeployment(
        TacticalBattle battle,
        Company company,
        BattleSector sector,
        CompanyDeploymentRole role)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));

        Company = company;
        Sector = sector;
        Role = role;
    }


    internal void SetInTransit(bool value)
    {
        if (IsInTransit == value)
            return;

        IsInTransit = value;
        _battle.NotifyDeploymentChanged(this);
    }

    internal void ApplyMovementArrival(
        BattleSector destination,
        CompanyDeploymentRole role,
        bool? isInTransit = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        bool nextTransit = isInTransit ?? IsInTransit;

        if (ReferenceEquals(Sector, destination)
            && Role == role
            && IsInTransit == nextTransit)
        {
            return;
        }

        Sector = destination;
        Role = role;
        IsInTransit = nextTransit;
        _battle.NotifyDeploymentChanged(this);

    }

}
