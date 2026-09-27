using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Logistics;

public class SupplyDepotTests
{
    [Fact]
    public void Depot_RefuelsDivisionInSameProvince()
    {
        var province = new Province(1, "Province A");

        var division = new Division(
            "1re Division",
            province,
            fuel: 20,
            ammunition: 100
        );

        var depot = new SupplyDepot(
            "Dépôt A",
            province,
            fuelStock: 500,
            fuelTransferPerHour: 20
        );

        double transferred = depot.Supply(
            new[] { division }
        );

        Assert.Equal(20, transferred);
        Assert.Equal(40, division.Fuel);
        Assert.Equal(480, depot.FuelStock);
    }

    [Fact] //On s'assure qu'une division ne puisse pas etre ressourcée si pas dans la meme province que le dépot
    public void Depot_CannotRefuelDivisionInAnotherProvince()
    {
    var provinceA = new Province(1, "Province A");
    var provinceB = new Province(2, "Province B");

    var division = new Division(
        "1re Division",
        provinceB,
        fuel: 20,
        ammunition: 100
    );

    var depot = new SupplyDepot(
        "Dépôt A",
        provinceA,
        fuelStock: 500,
        fuelTransferPerHour: 20
    );

    double transferred = depot.Supply(
        new[] { division }
    );

    Assert.Equal(0, transferred);
    Assert.Equal(20, division.Fuel);
    Assert.Equal(500, depot.FuelStock);
    }

    [Fact] // Test pour vérifier qu'on ne dépasse jamais le réservoir
    public void Depot_DoesNotOverfillDivision()
    {
    var province = new Province(1, "Province A");

    var division = new Division(
        "1re Division",
        province,
        fuel: 95,
        ammunition: 100
    );

    var depot = new SupplyDepot(
        "Dépôt A",
        province,
        fuelStock: 500,
        fuelTransferPerHour: 20
    );

    double transferred = depot.Supply(
        new[] { division }
    );

    Assert.Equal(5, transferred);
    Assert.Equal(100, division.Fuel);
    Assert.Equal(495, depot.FuelStock);
    }



}