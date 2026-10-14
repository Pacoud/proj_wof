
namespace WoF.Simulation.Military.Combat.Losses;

public readonly record struct EquipmentLossReport(
    int InfantryEquipment,
    int Artillery,
    int Trucks
);
