using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Power;

public static class CompositionCombatPowerCalculator
{
    // Valeurs de calibration provisoires.
    //
    // Elles permettent de conserver des ordres
    // de grandeur proches de notre ancien système,
    // sans prétendre encore représenter des données
    // historiques définitives.

    private const double SmallArmsPowerPerUnit =
        0.0005;

    private const double ArtilleryPowerPerGun =
        0.03;

    private const double TankPowerPerTank =
        0.035;

    public static CombatPowerProfile Calculate(
        DivisionComposition composition)
    {
        if (composition == null)
        {
            throw new ArgumentNullException(
                nameof(composition)
            );
        }

        double personnelAvailability =
            composition.Manpower.Authorized == 0
                ? 0
                : composition
                    .Manpower
                    .AvailabilityRatio;

        int usableInfantryEquipment =
            Math.Min(
                composition
                    .Manpower
                    .Current,

                composition
                    .InfantryEquipment
                    .Current
            );

        double smallArmsPower =
            usableInfantryEquipment
            * SmallArmsPowerPerUnit;

        double artilleryPower =
            composition
                .Artillery
                .Current
            * ArtilleryPowerPerGun
            * personnelAvailability;

        double armoredPower =
            composition
                .Tanks
                .Current
            * TankPowerPerTank
            * personnelAvailability;

        return new CombatPowerProfile(
            SmallArms:
                Math.Round(
                    smallArmsPower,
                    4
                ),

            Artillery:
                Math.Round(
                    artilleryPower,
                    4
                ),

            Armored:
                Math.Round(
                    armoredPower,
                    4
                )
        );
    }
}