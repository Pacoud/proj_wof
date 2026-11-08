
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
    public Company Company { get; }

    public BattleSector Sector { get; private set; }

    public CompanyDeploymentRole Role { get; private set; }

    internal CompanyDeployment(
        Company company,
        BattleSector sector,
        CompanyDeploymentRole role)
    {
        Company = company;
        Sector = sector;
        Role = role;
    }


    internal void ApplyMovementArrival(
    BattleSector destination,
    CompanyDeploymentRole role)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        Sector = destination;
        Role = role;
    }
}
