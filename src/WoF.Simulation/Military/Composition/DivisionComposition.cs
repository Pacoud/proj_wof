namespace WoF.Simulation.Military.Composition;

public sealed class DivisionComposition
{
    public StrengthPool Manpower { get; }

    public StrengthPool InfantryEquipment { get; }

    public StrengthPool Artillery { get; }

    public StrengthPool Tanks { get; }

    public StrengthPool Trucks { get; }

    public DivisionComposition(
        int manpower,
        int infantryEquipment,
        int artillery,
        int tanks,
        int trucks = 0)
        : this(
            new StrengthPool(manpower),
            new StrengthPool(infantryEquipment),
            new StrengthPool(artillery),
            new StrengthPool(tanks),
            new StrengthPool(trucks)
        )
    {
    }

    public DivisionComposition(
        StrengthPool manpower,
        StrengthPool infantryEquipment,
        StrengthPool artillery,
        StrengthPool tanks,
        StrengthPool? trucks = null)
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
            
    }
}