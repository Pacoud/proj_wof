
namespace WoF.Simulation.Military.Reinforcements;

public readonly record struct ReinforcementCargo(
    int Personnel,
    int InfantryEquipment,
    int Artillery,
    int Tanks,
    int Trucks)
{
    public bool IsEmpty =>
        Personnel == 0 &&
        InfantryEquipment == 0 &&
        Artillery == 0 &&
        Tanks == 0 &&
        Trucks == 0;

    public void ValidateNonNegative()
    {
        if (Personnel < 0 ||
            InfantryEquipment < 0 ||
            Artillery < 0 ||
            Tanks < 0 ||
            Trucks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReinforcementCargo),
                "Resource quantities cannot be negative.");
        }
    }

    public bool Contains(ReinforcementCargo other)
    {
        return Personnel >= other.Personnel &&
               InfantryEquipment >= other.InfantryEquipment &&
               Artillery >= other.Artillery &&
               Tanks >= other.Tanks &&
               Trucks >= other.Trucks;
    }
}
