
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Pondérations provisoires pour évaluer un bataillon.
/// Ces paramètres ne sont pas des données historiques.
/// </summary>
internal sealed class TacticalBattalionAssessmentProfile
{
    internal double FrontLineWeight { get; }
    internal double SupportWeight { get; }
    internal double ReserveWeight { get; }
    internal double PriorityFrontMultiplier { get; }
    internal double HoldThreshold { get; }
    internal double RiskThreshold { get; }

    internal TacticalBattalionAssessmentProfile(
        double frontLineWeight,
        double supportWeight,
        double reserveWeight,
        double priorityFrontMultiplier,
        double holdThreshold,
        double riskThreshold)
    {
        CheckPositive(frontLineWeight, nameof(frontLineWeight));
        CheckPositive(supportWeight, nameof(supportWeight));
        CheckPositive(reserveWeight, nameof(reserveWeight));
        CheckPositive(
            priorityFrontMultiplier,
            nameof(priorityFrontMultiplier));

        CheckThreshold(holdThreshold, nameof(holdThreshold));
        CheckThreshold(riskThreshold, nameof(riskThreshold));

        if (riskThreshold >= holdThreshold)
            throw new ArgumentException(
                "Risk threshold must be below hold threshold.");

        FrontLineWeight = frontLineWeight;
        SupportWeight = supportWeight;
        ReserveWeight = reserveWeight;
        PriorityFrontMultiplier = priorityFrontMultiplier;
        HoldThreshold = holdThreshold;
        RiskThreshold = riskThreshold;
    }

    private static void CheckPositive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void CheckThreshold(double value, string name)
    {
        if (!double.IsFinite(value)
            || value < 0 || value > 100)
            throw new ArgumentOutOfRangeException(name);
    }
}
