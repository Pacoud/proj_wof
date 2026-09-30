using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Recovery;

public sealed class OrganizationRecoverySystem
{
    private readonly DiplomacySystem _diplomacy;

    public OrganizationRecoverySystem(
        DiplomacySystem diplomacy)
    {
        _diplomacy = diplomacy;
    }

    public void ProcessRecovery(
        IEnumerable<Division> divisions)
    {
        var allDivisions =
            divisions.ToList();

        foreach (var division
                 in allDivisions)
        {
            if (!CanRecover(
                    division,
                    allDivisions))
            {
                continue;
            }

            double recovery =
                GetRecoveryPerHour(
                    division.Type
                );

            division.RecoverOrganization(
                recovery
            );
        }
    }

    private bool CanRecover(
        Division division,
        IReadOnlyCollection<Division> allDivisions)
    {
        if (division.Organization
            >= division.MaxOrganization)
        {
            return false;
        }

        if (division.IsEngaged)
            return false;

        if (division.IsRetreating)
            return false;

        if (division.IsInTransit)
            return false;

        if (division.CurrentProvince == null)
            return false;

        if (division.Country == null)
            return false;

        Country? controller =
            division.CurrentProvince.Controller;

        if (controller == null)
            return false;

        if (controller.Id
            != division.Country.Id)
        {
            return false;
        }

        if (HasNearbyHostileForce(
                division,
                allDivisions))
        {
            return false;
        }

        return true;
    }

    private bool HasNearbyHostileForce(
        Division division,
        IReadOnlyCollection<Division> allDivisions)
    {
        Province province =
            division.CurrentProvince!;

        foreach (var other
                 in allDivisions)
        {
            if (ReferenceEquals(
                    other,
                    division))
            {
                continue;
            }

            if (other.IsBroken)
                continue;

            if (other.Country == null)
                continue;

            if (other.CurrentProvince == null)
                continue;

            if (!_diplomacy.AreAtWar(
                    division.Country!,
                    other.Country))
            {
                continue;
            }

            bool sameProvince =
                ReferenceEquals(
                    province,
                    other.CurrentProvince
                );

            bool adjacentProvince =
                province.Neighbours.Contains(
                    other.CurrentProvince
                );

            if (sameProvince
                || adjacentProvince)
            {
                return true;
            }
        }

        return false;
    }

    private static double GetRecoveryPerHour(
        DivisionType type)
    {
        return type switch
        {
            DivisionType.Infantry => 6.0,
            DivisionType.Motorized => 5.0,
            DivisionType.Armored => 4.0,

            _ => 5.0
        };
    }
}