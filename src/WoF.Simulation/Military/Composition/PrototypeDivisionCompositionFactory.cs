namespace WoF.Simulation.Military.Composition;

public static class PrototypeDivisionCompositionFactory
{
    public static DivisionComposition Create(
        DivisionType type)
    {
        return type switch
        {
            DivisionType.Infantry =>
                new DivisionComposition(
                    manpower: 10_000,
                    infantryEquipment: 9_000,
                    artillery: 48,
                    tanks: 0,
                    trucks : 300
                ),

            DivisionType.Motorized =>
                new DivisionComposition(
                    manpower: 9_000,
                    infantryEquipment: 7_500,
                    artillery: 36,
                    tanks: 0,
                    trucks : 1_400
                ),

            DivisionType.Armored =>
                new DivisionComposition(
                    manpower: 8_000,
                    infantryEquipment: 5_000,
                    artillery: 36,
                    tanks: 180,
                    trucks : 600
                ),

            _ =>
                new DivisionComposition(
                    manpower: 10_000,
                    infantryEquipment: 9_000,
                    artillery: 48,
                    tanks: 0
                )
        };
    }
}