using WoF.Simulation.World;

namespace WoF.Simulation.Logistics;

public sealed class SupplyRoute
{
    public Province ProvinceA { get; }

    public Province ProvinceB { get; }

    public InfrastructureType Type {get;}

    public int Level {get;}

    public double FuelCapacityPerHour =>  CalculateCapacity();

    public SupplyRoute(
        Province provinceA,
        Province provinceB,
        InfrastructureType type,
        int level)
    {
        if (!provinceA.IsNeighbourOf(provinceB))
        {
            throw new ArgumentException(
                "A supply route can only connect neighbouring provinces."
            );
        }

        ProvinceA = provinceA;
        ProvinceB = provinceB;
        Type = type;
        Level = level;
    }

    private double CalculateCapacity()
    {
        return Type switch 
        {
            InfrastructureType.Road =>
                Level * 10,

            InfrastructureType.Railway =>
                Level * 40, 

            _=> 0
        };
    }

    public Province? GetOtherProvince(Province province)
    {
        if (ReferenceEquals(province, ProvinceA))
            return ProvinceB;

        if (ReferenceEquals(province, ProvinceB))
            return ProvinceA;

        return null;
    }

}