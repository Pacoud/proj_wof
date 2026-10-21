
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Capacites simplifiees d'un armement de tir direct.
/// Les valeurs reelles des armes seront introduites
/// dans une iteration ulterieure.
/// </summary>
public sealed class TacticalDirectFireProfile
{
    public double MaxRangeMeters { get; }
    public double FireArcDegrees { get; }

    public TacticalDirectFireProfile(
        double maxRangeMeters,
        double fireArcDegrees = 120)
    {
        if (!double.IsFinite(maxRangeMeters)
            || maxRangeMeters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxRangeMeters));
        }

        if (!double.IsFinite(fireArcDegrees)
            || fireArcDegrees <= 0
            || fireArcDegrees > 360)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fireArcDegrees));
        }

        MaxRangeMeters = maxRangeMeters;
        FireArcDegrees = fireArcDegrees;
    }
}

public enum TacticalDirectFireStatus
{
    Allowed,
    BattleInactive,
    SameUnit,
    NotEnemies,
    NotFrontLine,
    NoPersonnel,
    NoInfantryEquipment,
    NoCurrentLocalization,
    MissingPosition,
    OutOfRange,
    OutsideFiringArc,
    LineOfSightUnavailable
}

/// <summary>
/// Une decision, pas un tir effectue.
/// Aucune reference a la cible reelle n'est exposee.
/// </summary>
public readonly record struct TacticalDirectFireDecision(
    TacticalDirectFireStatus Status)
{
    public bool CanFire =>
        Status == TacticalDirectFireStatus.Allowed;
}

/// <summary>
/// Evaluation interne des conditions d'ouverture
/// du feu direct, sans mutation militaire.
/// </summary>
internal static class TacticalDirectFireEvaluator
{
    public static TacticalDirectFireDecision Evaluate(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy,
        TacticalPerceptionState perception,
        CompanyDeployment attacker,
        CompanyDeployment target,
        long currentHour,
        TacticalDirectFireProfile profile)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(perception);
        ArgumentNullException.ThrowIfNull(attacker);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(profile);

        if (!ReferenceEquals(geometry.Battle, battle)
            || !ReferenceEquals(perception.Battle, battle))
        {
            throw new InvalidOperationException(
                "Fire inputs belong to different battles.");
        }

        if (!battle.ContainsDeployment(attacker)
            || !battle.ContainsDeployment(target))
        {
            throw new InvalidOperationException(
                "Fire participants do not belong to this battle.");
        }

        if (currentHour < battle.Engagement.StartHour)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentHour));
        }

        if (!battle.Engagement.IsActive)
            return Denied(TacticalDirectFireStatus.BattleInactive);

        if (ReferenceEquals(attacker, target))
            return Denied(TacticalDirectFireStatus.SameUnit);

        var attackerDivision =
            attacker.Company.ParentBattalion!.ParentDivision!;

        var targetDivision =
            target.Company.ParentBattalion!.ParentDivision!;

        var attackerCountry = attackerDivision.Country;
        var targetCountry = targetDivision.Country;

        if (attackerCountry is null
            || targetCountry is null
            || !diplomacy.AreAtWar(
                attackerCountry, targetCountry))
        {
            return Denied(TacticalDirectFireStatus.NotEnemies);
        }

        // C2.4a : seuls les combattants de premiere ligne
        // participent aux tirs directs.
        if (attacker.Role != CompanyDeploymentRole.FrontLine
            || target.Role != CompanyDeploymentRole.FrontLine)
        {
            return Denied(TacticalDirectFireStatus.NotFrontLine);
        }

        var attackerResources = attacker.Company.Composition!;
        var targetResources = target.Company.Composition!;

        if (attackerResources.Manpower.Current == 0
            || targetResources.Manpower.Current == 0)
        {
            return Denied(TacticalDirectFireStatus.NoPersonnel);
        }

        // Premier modele : tir avec equipement
        // d'infanterie uniquement.
        if (attackerResources.InfantryEquipment.Current == 0)
        {
            return Denied(
                TacticalDirectFireStatus.NoInfantryEquipment);
        }

        // Aucune omniscience : la cible doit etre
        // actuellement localisee par CET observateur.
        if (!perception.HasCurrentLocalization(
            attacker, target, currentHour))
        {
            return Denied(
                TacticalDirectFireStatus.NoCurrentLocalization);
        }

        var first = geometry.GetPosition(attacker);
        var second = geometry.GetPosition(target);

        if (first is null || second is null)
            return Denied(TacticalDirectFireStatus.MissingPosition);

        var relation = geometry.Measure(attacker, target);

        if (relation.HorizontalDistanceMeters <= 0
            || relation.RelativeBearingDegrees is null)
        {
            return Denied(TacticalDirectFireStatus.MissingPosition);
        }

        if (relation.HorizontalDistanceMeters
            > profile.MaxRangeMeters)
        {
            return Denied(TacticalDirectFireStatus.OutOfRange);
        }

        double angle = relation.RelativeBearingDegrees.Value;
        double deviation = Math.Min(angle, 360.0 - angle);

        if (deviation > profile.FireArcDegrees / 2.0)
        {
            return Denied(TacticalDirectFireStatus.OutsideFiringArc);
        }

        var sight = TacticalSightEvaluator.Evaluate(
            geometry, attacker, target);

        if (sight.Status
            != TacticalSightStatus.ClearOfKnownObstacles)
        {
            return Denied(
                TacticalDirectFireStatus.LineOfSightUnavailable);
        }

        return new TacticalDirectFireDecision(
            TacticalDirectFireStatus.Allowed);
    }

    private static TacticalDirectFireDecision Denied(
        TacticalDirectFireStatus status)
    {
        return new TacticalDirectFireDecision(status);
    }
}
