
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Combat.Fuel;

public static class CompositionCombatFuelCalculator
{
    private const double FuelPerTruck = 0.002;
    private const double FuelPerArtillery = 0.005;
    private const double FuelPerTank = 0.020;

    // Compatibilité avec le modèle existant.
    public static CombatFuelDemandProfile Calculate(
        DivisionComposition composition)
    {
        return Calculate(
            FormationResourceTotals.Capture(composition)
        );
    }

    public static CombatFuelDemandProfile Calculate(
        FormationResourceTotals resources)
    {
        double personnelAvailability =
            resources.Personnel.Authorized == 0
                ? 0
                : (double)resources.Personnel.Available
                    / resources.Personnel.Authorized;

        double truckDemand =
            resources.Trucks.Available
            * FuelPerTruck
            * personnelAvailability;

        double artilleryDemand =
            resources.Artillery.Available
            * FuelPerArtillery
            * personnelAvailability;

        double tankDemand =
            resources.Tanks.Operational
            * FuelPerTank
            * personnelAvailability;

        return new CombatFuelDemandProfile(
            Trucks: Math.Round(truckDemand, 4),
            Artillery: Math.Round(artilleryDemand, 4),
            Tanks: Math.Round(tankDemand, 4)
        );
    }
}
