using WoF.Simulation.Military.Combat.Power;

namespace WoF.Simulation.Military.Combat.Losses;

public static class TankLossCalculator
{
    // Première calibration.
    //
    // Ce ne sont PAS encore des statistiques
    // historiques définitives.

    private const double DamageRatePerTankPowerHour =
        0.00030;

    private const double DestructionRatePerTankPowerHour =
        0.00012;

    public static TankLossExpectation Calculate(
        CombatPowerProfile hostilePower,
        int exposedTanks)
    {
        if (exposedTanks <= 0)
        {
            return TankLossExpectation.None;
        }

        // Pour l'instant :
        //
        // SmallArms n'a pas de capacité antichar.
        //
        // Artillery peut endommager / détruire,
        // mais moins efficacement que la puissance
        // blindée adverse.
        double damagePressure =
            hostilePower.Armored
            +
            hostilePower.Artillery
                * 0.60;

        double destructionPressure =
            hostilePower.Armored
            +
            hostilePower.Artillery
                * 0.25;

        double expectedDamaged =
            exposedTanks
            * damagePressure
            * DamageRatePerTankPowerHour;

        double expectedDestroyed =
            exposedTanks
            * destructionPressure
            * DestructionRatePerTankPowerHour;

        double expectedTotal =
            expectedDamaged
            + expectedDestroyed;

        if (expectedTotal > exposedTanks)
        {
            double scale =
                exposedTanks
                / expectedTotal;

            expectedDamaged *=
                scale;

            expectedDestroyed *=
                scale;
        }

        return new TankLossExpectation(
            Damaged:
                expectedDamaged,

            Destroyed:
                expectedDestroyed
        );
    }
}