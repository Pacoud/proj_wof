
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

            bool normalFrontlineCombat =
                attacker.Role == CompanyDeploymentRole.FrontLine
                && target.Role == CompanyDeploymentRole.FrontLine
                && !attacker.IsInTransit
                && !target.IsInTransit;

            bool confirmedMobileCombat =
                battle.MobileEngagements.Find(
                attacker, target, currentHour) is not null;

        if (!normalFrontlineCombat && !confirmedMobileCombat)
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

        // Option b : le centre de la cible, puis les extremites de
        // son front. Le premier point qui passe autorise le tir ;
        // sinon, le refus est celui du centre.
        var points = geometry.GetFrontPoints(second);
        var centerStatus = TacticalDirectFireStatus.Allowed;

        for (int i = 0; i < points.Count; i++)
        {
            var status = EvaluatePoint(
                geometry, first, points[i], profile);

            if (status == TacticalDirectFireStatus.Allowed)
                return new TacticalDirectFireDecision(status);

            if (i == 0)
                centerStatus = status;
        }

        return Denied(centerStatus);
    }

    /// <summary>
    /// Portee, arc de tir puis vue, du centre du tireur vers un point
    /// de la cible, dans l'ordre historique des controles.
    /// </summary>
    private static TacticalDirectFireStatus EvaluatePoint(
        TacticalGeometryMap geometry,
        CompanyTacticalPosition shooter,
        TacticalPoint point,
        TacticalDirectFireProfile profile)
    {
        double distance = shooter.Point.HorizontalDistanceTo(point);

        if (distance <= 0)
            return TacticalDirectFireStatus.MissingPosition;

        if (distance > profile.MaxRangeMeters)
            return TacticalDirectFireStatus.OutOfRange;

        double angle = TacticalGeometryMap.RelativeBearingDegrees(
            shooter.Point,
            shooter.FacingDegrees,
            point);

        double deviation = Math.Min(angle, 360.0 - angle);

        if (deviation > profile.FireArcDegrees / 2.0)
            return TacticalDirectFireStatus.OutsideFiringArc;

        var sight = TacticalSightEvaluator.EvaluatePoints(
            geometry, shooter.Point, point);

        return sight.Status == TacticalSightStatus.ClearOfKnownObstacles
            ? TacticalDirectFireStatus.Allowed
            : TacticalDirectFireStatus.LineOfSightUnavailable;
    }

    private static TacticalDirectFireDecision Denied(
        TacticalDirectFireStatus status)
    {
        return new TacticalDirectFireDecision(status);
    }
}
