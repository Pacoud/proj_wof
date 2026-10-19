
namespace WoF.Simulation.Military.Composition;

// Totaux d'un stock de matériel classique.
public readonly record struct StockTotals(
    int Authorized,
    int Available)
{
    public static StockTotals operator +(
        StockTotals a,
        StockTotals b)
        => new(
            a.Authorized + b.Authorized,
            a.Available + b.Available);
}

// Totaux du personnel.
public readonly record struct PersonnelTotals(
    int Authorized,
    int Available,
    int WoundedUnavailable,
    int KilledInAction,
    int TotalWoundedInAction,
    int MissingOrCaptured)
{
    public static PersonnelTotals operator +(
        PersonnelTotals a,
        PersonnelTotals b)
        => new(
            a.Authorized + b.Authorized,
            a.Available + b.Available,
            a.WoundedUnavailable + b.WoundedUnavailable,
            a.KilledInAction + b.KilledInAction,
            a.TotalWoundedInAction + b.TotalWoundedInAction,
            a.MissingOrCaptured + b.MissingOrCaptured);
}

// Les chars ont trois états matériels distincts.
public readonly record struct TankTotals(
    int Authorized,
    int Operational,
    int Damaged,
    int Destroyed)
{
    public static TankTotals operator +(
        TankTotals a,
        TankTotals b)
        => new(
            a.Authorized + b.Authorized,
            a.Operational + b.Operational,
            a.Damaged + b.Damaged,
            a.Destroyed + b.Destroyed);
}

    // Vue agrégée de toutes les ressources.

    public readonly record struct FormationResourceTotals(
        PersonnelTotals Personnel,
        StockTotals InfantryEquipment,
        StockTotals Artillery,
        TankTotals Tanks,
        StockTotals Trucks)
    {

    public StockTotals Radios {get; init; }

    public static FormationResourceTotals Capture(
        CompanyComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        return CapturePools(
            composition.Manpower,
            composition.InfantryEquipment,
            composition.Artillery,
            composition.Tanks,
            composition.Trucks,
            composition.Radios
        );
    }

    public static FormationResourceTotals Capture(
        DivisionComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        return CapturePools(
            composition.Manpower,
            composition.InfantryEquipment,
            composition.Artillery,
            composition.Tanks,
            composition.Trucks,
            composition.Radios
        );
    }

    private static FormationResourceTotals CapturePools(
        PersonnelPool personnel,
        StrengthPool infantry,
        StrengthPool artillery,
        TankPool tanks,
        StrengthPool trucks,
        StrengthPool radios)
    {
        return new FormationResourceTotals(
            new PersonnelTotals(
                personnel.Authorized,
                personnel.Current,
                personnel.WoundedUnavailable,
                personnel.KilledInAction,
                personnel.TotalWoundedInAction,
                personnel.MissingOrCaptured
            ),

            new StockTotals(
                infantry.Authorized,
                infantry.Current
            ),

            new StockTotals(
                artillery.Authorized,
                artillery.Current
            ),

            new TankTotals(
                tanks.Authorized,
                tanks.Operational,
                tanks.Damaged,
                tanks.Destroyed
            ),

            new StockTotals(
                trucks.Authorized,
                trucks.Current
            )
        )
        {
            Radios = new StockTotals(
                radios.Authorized,
                radios.Current
            )
        };
    }

    public static FormationResourceTotals operator +(
        FormationResourceTotals a,
        FormationResourceTotals b)
    {
        return new FormationResourceTotals(
            a.Personnel + b.Personnel,
            a.InfantryEquipment + b.InfantryEquipment,
            a.Artillery + b.Artillery,
            a.Tanks + b.Tanks,
            a.Trucks + b.Trucks
        )
        {
            Radios = a.Radios + b.Radios
        };
    }
}

