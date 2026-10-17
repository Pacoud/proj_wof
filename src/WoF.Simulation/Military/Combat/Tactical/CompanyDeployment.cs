
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

    public BattleSector Sector { get; }

    public CompanyDeploymentRole Role { get; }

    internal CompanyDeployment(
        Company company,
        BattleSector sector,
        CompanyDeploymentRole role)
    {
        Company = company;
        Sector = sector;
        Role = role;
    }
}
