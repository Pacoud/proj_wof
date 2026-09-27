using WoF.Simulation.Logistics;
using WoF.Simulation.Military;
using WoF.Simulation.World;

namespace WoF.Simulation.Tests.Logistics;

public class SupplyDepotTests
{
    [Fact]
    public void WithdrawFuel_RemovesFuelFromStock()
    {
        var province = new Province(1, "Province A");

        var depot = new SupplyDepot(
            "Dépôt A",
            province,
            fuelStock: 500,
            fuelTransferPerHour: 20
        );

        double withdrawn = depot.WithdrawFuel(15);

        Assert.Equal(15, withdrawn);
        Assert.Equal(485, depot.FuelStock);
    }


}