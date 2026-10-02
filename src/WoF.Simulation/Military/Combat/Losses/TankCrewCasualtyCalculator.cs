namespace WoF.Simulation.Military.Combat.Losses;

public static class TankCrewCasualtyCalculator
{
    // Abstraction provisoire.
    //
    // Plus tard chaque modèle de char pourra
    // définir sa propre taille d'équipage.
    public const int CrewSize =
        5;

    private const double DamagedTankKiaRate =
        0.01;

    private const double DamagedTankWiaRate =
        0.09;

    private const double DestroyedTankKiaRate =
        0.18;

    private const double DestroyedTankWiaRate =
        0.27;

    public static PersonnelCasualtyReport Calculate(
        TankLossResult losses,
        int exposedCrewPositions)
    {
        if (losses.Total <= 0
            || exposedCrewPositions <= 0)
        {
            return PersonnelCasualtyReport.None;
        }

        double expectedKilled =
            losses.Damaged
                * CrewSize
                * DamagedTankKiaRate
            +
            losses.Destroyed
                * CrewSize
                * DestroyedTankKiaRate;

        double expectedWounded =
            losses.Damaged
                * CrewSize
                * DamagedTankWiaRate
            +
            losses.Destroyed
                * CrewSize
                * DestroyedTankWiaRate;

        int killed =
            (int)Math.Round(
                expectedKilled,
                MidpointRounding.AwayFromZero
            );

        int wounded =
            (int)Math.Round(
                expectedWounded,
                MidpointRounding.AwayFromZero
            );

        int total =
            killed + wounded;

        if (total > exposedCrewPositions)
        {
            int excess =
                total
                - exposedCrewPositions;

            int woundedReduction =
                Math.Min(
                    wounded,
                    excess
                );

            wounded -=
                woundedReduction;

            excess -=
                woundedReduction;

            killed =
                Math.Max(
                    0,
                    killed - excess
                );
        }

        return new PersonnelCasualtyReport(
            KilledInAction:
                killed,

            WoundedInAction:
                wounded,

            MissingOrCaptured:
                0
        );
    }
}