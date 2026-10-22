
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Coefficients provisoires d'exposition et
/// d'interception par les protections.
/// Ils ne sont pas encore calibres historiquement.
/// </summary>
internal sealed class TacticalDefenseProfile
{
    public double StandingExposureFactor { get; }
    public double CrouchedExposureFactor { get; }
    public double ProneExposureFactor { get; }

    public double VegetationInterceptionFraction { get; }
    public double BuildingInterceptionFraction { get; }
    public double EntrenchmentInterceptionFraction { get; }

    internal TacticalDefenseProfile(
        double standingExposureFactor,
        double crouchedExposureFactor,
        double proneExposureFactor,
        double vegetationInterceptionFraction,
        double buildingInterceptionFraction,
        double entrenchmentInterceptionFraction)
    {
        ValidateFactor(standingExposureFactor);
        ValidateFactor(crouchedExposureFactor);
        ValidateFactor(proneExposureFactor);

        ValidateFactor(vegetationInterceptionFraction);
        ValidateFactor(buildingInterceptionFraction);
        ValidateFactor(entrenchmentInterceptionFraction);

        StandingExposureFactor = standingExposureFactor;
        CrouchedExposureFactor = crouchedExposureFactor;
        ProneExposureFactor = proneExposureFactor;

        VegetationInterceptionFraction =
            vegetationInterceptionFraction;

        BuildingInterceptionFraction =
            buildingInterceptionFraction;

        EntrenchmentInterceptionFraction =
            entrenchmentInterceptionFraction;
    }

    private static void ValidateFactor(double value)
    {
        if (!double.IsFinite(value)
            || value < 0
            || value > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "A factor must be between 0 and 1.");
        }
    }

    internal double GetExposureFactor(TacticalStance stance)
    {
        return stance switch
        {
            TacticalStance.Standing =>
                StandingExposureFactor,

            TacticalStance.Crouched =>
                CrouchedExposureFactor,

            TacticalStance.Prone =>
                ProneExposureFactor,

            _ => throw new ArgumentOutOfRangeException(
                nameof(stance))
        };
    }

    internal double GetInterceptionFraction(TacticalCover cover)
    {
        return cover switch
        {
            TacticalCover.None => 0,

            TacticalCover.Vegetation =>
                VegetationInterceptionFraction,

            TacticalCover.Building =>
                BuildingInterceptionFraction,

            TacticalCover.Entrenchment =>
                EntrenchmentInterceptionFraction,

            _ => throw new ArgumentOutOfRangeException(
                nameof(cover))
        };
    }
}
