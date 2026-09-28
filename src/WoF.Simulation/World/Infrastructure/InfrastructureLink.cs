using WoF.Simulation.World;

namespace WoF.Simulation.World.Infrastructure;

public sealed class InfrastructureLink
{
    public Province ProvinceA { get; }

    public Province ProvinceB { get; }

    public InfrastructureType Type {get;}

    public int Level {get;}

    public double TransportCapacityPerHour =>  CalculateTransportCapacity();

    public InfrastructureLink(
        Province provinceA,
        Province provinceB,
        InfrastructureType type,
        int level)
    {
        if (!provinceA.IsNeighbourOf(provinceB))
        {
            throw new ArgumentException(
                "Infrastructure can only connect neighbouring provinces."
            );
        }

        if(level  < 1 || level > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "Infrastructure level must be between 1 and 3"
            );
        }

        ProvinceA = provinceA;
        ProvinceB = provinceB;
        Type = type;
        Level = level;
    }

    private double CalculateTransportCapacity()
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

    public bool Connects(
        Province first,
        Province second
    )
    {
        return
        (ReferenceEquals(ProvinceA, first)
        && ReferenceEquals(ProvinceB, second))
        ||
        (ReferenceEquals(ProvinceA, second)
        && ReferenceEquals(ProvinceB, first));
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
