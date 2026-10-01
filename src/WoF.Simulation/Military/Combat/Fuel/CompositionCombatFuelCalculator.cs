using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Fuel;

public static class CompositionCombatFuelCalculator
{
    // Valeurs provisoires de calibration par heure
    // de combat.

    private const double FuelPerTruck =
        0.002;

    private const double FuelPerArtillery =
        0.005;

    private const double FuelPerTank =
        0.020;

    public static CombatFuelDemandProfile Calculate(
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

        double truckDemand =
            composition.Trucks.Current
            * FuelPerTruck
            * personnelAvailability;

        double artilleryDemand =
            composition.Artillery.Current
            * FuelPerArtillery
            * personnelAvailability;

        double tankDemand =
            composition.Tanks.Current
            * FuelPerTank
            * personnelAvailability;

        return new CombatFuelDemandProfile(
            Trucks:
                Math.Round(
                    truckDemand,
                    4
                ),

            Artillery:
                Math.Round(
                    artilleryDemand,
                    4
                ),

            Tanks:
                Math.Round(
                    tankDemand,
                    4
                )
        );
    }
}