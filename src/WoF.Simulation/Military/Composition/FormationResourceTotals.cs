
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
    public static FormationResourceTotals Capture(
        CompanyComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        var p = composition.Manpower;
        var i = composition.InfantryEquipment;
        var a = composition.Artillery;
        var t = composition.Tanks;
        var tr = composition.Trucks;

        return new FormationResourceTotals(
            new PersonnelTotals(
                p.Authorized,
                p.Current,
                p.WoundedUnavailable,
                p.KilledInAction,
                p.TotalWoundedInAction,
                p.MissingOrCaptured),

            new StockTotals(
                i.Authorized, i.Current),

            new StockTotals(
                a.Authorized, a.Current),

            new TankTotals(
                t.Authorized,
                t.Operational,
                t.Damaged,
                t.Destroyed),

            new StockTotals(
                tr.Authorized, tr.Current)
        );
    }

    public static FormationResourceTotals operator +(
        FormationResourceTotals a,
        FormationResourceTotals b)
        => new(
            a.Personnel + b.Personnel,
            a.InfantryEquipment + b.InfantryEquipment,
            a.Artillery + b.Artillery,
            a.Tanks + b.Tanks,
            a.Trucks + b.Trucks);
}
