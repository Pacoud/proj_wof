
using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Recovery;

/// <summary>
/// Determine si une division se trouve dans
/// des conditions permettant la recuperation
/// de ses capacites militaires.
///
/// Ne calcule aucun taux de recuperation.
/// </summary>
internal static class DivisionRecoveryEligibility
{
    internal static bool CanRecoverInPlace(
        Division division,
        IReadOnlyCollection<Division> allDivisions,
        DiplomacySystem diplomacy)
    {
        ArgumentNullException.ThrowIfNull(division);
        ArgumentNullException.ThrowIfNull(allDivisions);
        ArgumentNullException.ThrowIfNull(diplomacy);

        // Une division engagee ne peut pas
        // beneficier du repos hors combat.
        if (division.IsEngaged)
            return false;

        // Une retraite ou un mouvement en cours
        // empechent la recuperation sur place.
        if (division.IsRetreating)
            return false;

        if (division.IsInTransit)
            return false;

        Province? province = division.CurrentProvince;
        Country? country = division.Country;

        if (province is null || country is null)
            return false;

        // Le territoire doit etre controle
        // par le pays de la division.
        Country? controller = province.Controller;

        if (controller is null
            || controller.Id != country.Id)
        {
            return false;
        }

        // Une force ennemie proche empeche
        // la recuperation dans des conditions sures.
        if (HasNearbyHostileForce(
            division,
            allDivisions,
            diplomacy))
        {
            return false;
        }

        return true;
    }

    private static bool HasNearbyHostileForce(
        Division division,
        IReadOnlyCollection<Division> allDivisions,
        DiplomacySystem diplomacy)
    {
        Province province = division.CurrentProvince!;
        Country country = division.Country!;

        foreach (var other in allDivisions)
        {
            if (ReferenceEquals(other, division))
                continue;

            if (other.IsBroken)
                continue;

            if (other.Country is null
                || other.CurrentProvince is null)
            {
                continue;
            }

            if (!diplomacy.AreAtWar(
                country,
                other.Country))
            {
                continue;
            }

            bool sameProvince = ReferenceEquals(
                province,
                other.CurrentProvince);

            bool adjacentProvince =
                province.Neighbours.Contains(
                    other.CurrentProvince);

            if (sameProvince || adjacentProvince)
                return true;
        }

        return false;
    }
}
