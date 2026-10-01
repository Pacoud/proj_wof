using WoF.Simulation.Military.Combat.Power;

namespace WoF.Simulation.Military.Combat.Fuel;

public static class CombatFuelModifier
{
    public static FuelCombatModifiers GetModifiers(
        double fuelSatisfaction)
    {
        double satisfaction =
            Math.Clamp(
                fuelSatisfaction,
                0.0,
                1.0
            );

        return new FuelCombatModifiers(
            SmallArms:
                Interpolate(
                    minimum: 0.95,
                    satisfaction
                ),

            Artillery:
                Interpolate(
                    minimum: 0.70,
                    satisfaction
                ),

            Armored:
                Interpolate(
                    minimum: 0.20,
                    satisfaction
                )
        );
    }

    public static CombatPowerProfile Apply(
        CombatPowerProfile power,
        double fuelSatisfaction)
    {
        FuelCombatModifiers modifiers =
            GetModifiers(
                fuelSatisfaction
            );

        return new CombatPowerProfile(
            SmallArms:
                Math.Round(
                    power.SmallArms
                    * modifiers.SmallArms,
                    4
                ),

            Artillery:
                Math.Round(
                    power.Artillery
                    * modifiers.Artillery,
                    4
                ),

            Armored:
                Math.Round(
                    power.Armored
                    * modifiers.Armored,
                    4
                )
        );
    }

    private static double Interpolate(
        double minimum,
        double satisfaction)
    {
        return minimum
            + (1.0 - minimum)
            * satisfaction;
    }
}