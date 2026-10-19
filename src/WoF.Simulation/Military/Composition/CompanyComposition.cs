
namespace WoF.Simulation.Military.Composition;

public sealed class CompanyComposition
{
    public PersonnelPool Manpower { get; }

    public StrengthPool InfantryEquipment { get; }

    public StrengthPool Artillery { get; }

    public TankPool Tanks { get; }

    public StrengthPool Trucks { get; }

    public StrengthPool Radios {get; }

    private bool _assigned;

    internal void ClaimOwnership()
    {
        if (_assigned)
        {
            throw new InvalidOperationException(
                "Composition already belongs to a company.");
        }

        _assigned = true;
    }

    public CompanyComposition(
        int manpower,
        int infantryEquipment = 0,
        int artillery = 0,
        int tanks = 0,
        int trucks = 0,
        int radios = 0)
    {
        Manpower = new PersonnelPool(manpower);

        InfantryEquipment =
            new StrengthPool(infantryEquipment);

        Artillery = new StrengthPool(artillery);

        Tanks = new TankPool(tanks);

        Trucks = new StrengthPool(trucks);

        Radios = new StrengthPool(radios);
    }
}
