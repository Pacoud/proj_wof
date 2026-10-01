using WoF.Simulation.Military.Combat.Power;

namespace WoF.Simulation.Military.Combat.Losses;

public static class PersonnelCasualtyCalculator
{
    /*
     * Calibration initiale.
     *
     * La pondération Artillery est volontairement forte :
     * les études WWII montrent que les fragments
     * d'artillerie/mortier produisaient une part majeure
     * des pertes.
     */

    private const double SmallArmsCasualtyWeight =
        1.0;

    private const double ArtilleryCasualtyWeight =
        5.5;

    private const double ArmoredCasualtyWeight =
        1.5;

    /*
     * Choisi pour qu'un affrontement division contre
     * division de puissance comparable soit proche
     * de ~1 % de pertes par journée complète de combat.
     *
     * Ce coefficient sera calibré plus tard avec des
     * scénarios historiques.
     */
    private const double CasualtyRatePerPowerHour =
        0.0000335;

    /*
     * Valeur située entre les ordres de grandeur
     * historiques open operations (~16.7 % KIA)
     * et trench operations (~20 %).
     */
    private const double KilledInActionRatio =
        0.18;

    public static PersonnelCasualtyReport Calculate(
        CombatPowerProfile hostilePower,
        int exposedManpower)
    {
        if (exposedManpower <= 0)
        {
            return PersonnelCasualtyReport.None;
        }

        double casualtyPressure =
            hostilePower.SmallArms
                * SmallArmsCasualtyWeight
            +
            hostilePower.Artillery
                * ArtilleryCasualtyWeight
            +
            hostilePower.Armored
                * ArmoredCasualtyWeight;

        if (casualtyPressure <= 0)
        {
            return PersonnelCasualtyReport.None;
        }

        double expectedCasualties =
            exposedManpower
            * casualtyPressure
            * CasualtyRatePerPowerHour;

        int totalCasualties =
            (int)Math.Round(
                expectedCasualties,
                MidpointRounding.AwayFromZero
            );

        totalCasualties =
            Math.Clamp(
                totalCasualties,
                0,
                exposedManpower
            );

        if (totalCasualties == 0)
        {
            return PersonnelCasualtyReport.None;
        }

        int killed =
            (int)Math.Round(
                totalCasualties
                * KilledInActionRatio,
                MidpointRounding.AwayFromZero
            );

        int wounded =
            totalCasualties
            - killed;

        return new PersonnelCasualtyReport(
            KilledInAction:
                killed,

            WoundedInAction:
                wounded,

            MissingOrCaptured:
                0
        );
    }
}