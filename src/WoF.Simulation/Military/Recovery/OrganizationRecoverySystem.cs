
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Recovery;

/// <summary>
/// Gere la recuperation horaire de
/// l'organisation operationnelle des divisions.
/// </summary>
public sealed class OrganizationRecoverySystem
{
    private readonly DiplomacySystem _diplomacy;

    public OrganizationRecoverySystem(
        DiplomacySystem diplomacy)
    {
        _diplomacy = diplomacy
            ?? throw new ArgumentNullException(
                nameof(diplomacy));
    }

    public void ProcessRecovery(
        IEnumerable<Division> divisions)
    {
        var allDivisions = divisions.ToList();

        foreach (var division in allDivisions)
        {
            // L'organisation divisionnaire ne peut
            // pas depasser son maximum.
            if (division.Organization
                >= division.MaxOrganization)
            {
                continue;
            }

            // Les conditions de repos sont
            // partagees avec la recuperation
            // de cohesion des compagnies.
            if (!DivisionRecoveryEligibility
                .CanRecoverInPlace(
                    division,
                    allDivisions,
                    _diplomacy))
            {
                continue;
            }

            double recovery = GetRecoveryPerHour(
                division.Type);

            division.RecoverOrganization(recovery);
        }
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
