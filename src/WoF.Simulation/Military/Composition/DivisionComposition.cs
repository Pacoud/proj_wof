namespace WoF.Simulation.Military.Composition;

public sealed class DivisionComposition
{
    public PersonnelPool Manpower { get; }

    public StrengthPool InfantryEquipment { get; }

    public StrengthPool Artillery { get; }

    public TankPool Tanks { get; }

    public StrengthPool Trucks { get; }

    public StrengthPool Radios {get; }

    public DivisionComposition(
        int manpower,
        int infantryEquipment,
        int artillery,
        int tanks,
        int trucks = 0,
        int radios = 0)
        : this(
            new PersonnelPool(manpower),
            new StrengthPool(infantryEquipment),
            new StrengthPool(artillery),
            new TankPool(tanks),
            new StrengthPool(trucks),
            new StrengthPool(radios)
        )
    {
    }

    public DivisionComposition(
        PersonnelPool manpower,
        StrengthPool infantryEquipment,
        StrengthPool artillery,
        TankPool tanks,
        StrengthPool? trucks = null,
        StrengthPool? radios = null)
    {
        Manpower =
            manpower
            ?? throw new ArgumentNullException(
                nameof(manpower)
            );

        InfantryEquipment =
            infantryEquipment
            ?? throw new ArgumentNullException(
                nameof(infantryEquipment)
            );

        Artillery =
            artillery
            ?? throw new ArgumentNullException(
                nameof(artillery)
            );

        Tanks =
            tanks
            ?? throw new ArgumentNullException(
                nameof(tanks)
            );

        Trucks = 
            trucks
            ?? new StrengthPool(0);

        Radios = radios ?? new StrengthPool(0);
            
    }
}