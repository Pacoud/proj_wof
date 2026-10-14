
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Power;

public static class CompositionCombatPowerCalculator
{
    private const double SmallArmsPowerPerUnit = 0.0005;
    private const double ArtilleryPowerPerGun = 0.03;
    private const double TankPowerPerTank = 0.035;

    // Compatibilité avec les anciens appels.
    public static CombatPowerProfile Calculate(
        DivisionComposition composition)
    {
        return Calculate(
            FormationResourceTotals.Capture(composition)
        );
    }

    // Nouveau calcul indépendant du propriétaire
    // des ressources physiques.
    public static CombatPowerProfile Calculate(
        FormationResourceTotals resources)
    {
        double personnelAvailability =
            resources.Personnel.Authorized == 0
                ? 0
                : (double)resources.Personnel.Available
                    / resources.Personnel.Authorized;

        int usableInfantryEquipment =
            Math.Min(
                resources.Personnel.Available,
                resources.InfantryEquipment.Available
            );

        double smallArmsPower =
            usableInfantryEquipment
            * SmallArmsPowerPerUnit;

        double artilleryPower =
            resources.Artillery.Available
            * ArtilleryPowerPerGun
            * personnelAvailability;

        double armoredPower =
            resources.Tanks.Operational
            * TankPowerPerTank
            * personnelAvailability;

        return new CombatPowerProfile(
            SmallArms: Math.Round(smallArmsPower, 4),
            Artillery: Math.Round(artilleryPower, 4),
            Armored: Math.Round(armoredPower, 4)
        );
    }
}
