
using WoF.Simulation.Diplomacy;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Diagnostic local d'un secteur.
///
/// Les indicateurs sont derives des rapports
/// existants. Aucun score militaire supplementaire
/// n'est cree ni conserve.
/// </summary>
internal sealed record TacticalBattalionSectorFragilityReport(
    TacticalBattalionSectorAssessment Defense,
    TacticalBattalionSectorHostileReport Hostiles,
    TacticalBattalionSectorFirePressureReport Fire,
    bool IsDefensivelyWeak,
    bool IsUnderCurrentFire,
    bool HasCurrentSuppression,
    bool IsWeakUnderFire);

/// <summary>
/// Etat des positions occupees bordant
/// un intervalle non couvert du bataillon.
/// </summary>
internal sealed record TacticalBattalionGapFragilityReport(
    TacticalBattalionGapHostileReport Exposure,
    int OccupiedAnchorCount,
    int WeakAnchorCount,
    int AnchorsUnderCurrentFire,
    int AnchorsWithCurrentSuppression);

internal sealed record TacticalBattalionFrontFragilityReport(
    long Hour,
    IReadOnlyList<TacticalBattalionSectorFragilityReport> Sectors,
    IReadOnlyList<TacticalBattalionGapFragilityReport> Gaps);

/// <summary>
/// Croise capacite defensive, continuite du front,
/// presence ennemie et pression de feu effective.
///
/// Lecture physique interne uniquement.
/// Aucun ordre de mouvement ou de retraite.
/// </summary>
internal sealed class TacticalBattalionFrontFragilitySystem
{
    private readonly TacticalBattle _battle;

    internal TacticalBattalionFrontFragilitySystem(
        TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal TacticalBattalionFrontFragilityReport Evaluate(
        Battalion battalion,
        TacticalBattalionAssessmentProfile assessmentProfile,
        IReadOnlyList<BattleSector> responsibilitySectors,
        DiplomacySystem diplomacy,
        TacticalSuppressionProfile suppressionProfile,
        long hour)
    {
        ArgumentNullException.ThrowIfNull(battalion);
        ArgumentNullException.ThrowIfNull(assessmentProfile);
        ArgumentNullException.ThrowIfNull(responsibilitySectors);
        ArgumentNullException.ThrowIfNull(diplomacy);
        ArgumentNullException.ThrowIfNull(suppressionProfile);

        // Evaluer d'abord 9f : il protege la lecture
        // contre les changements de deploiement
        // posterieurs a la phase de suppression.
        var fire = new TacticalBattalionFrontFirePressureSystem(
            _battle).Evaluate(
                battalion,
                assessmentProfile,
                responsibilitySectors,
                suppressionProfile,
                hour);

        var exposure = new TacticalBattalionFrontExposureSystem(
            _battle).Evaluate(
                battalion,
                assessmentProfile,
                responsibilitySectors,
                diplomacy);

        if (fire.Sectors.Count != exposure.Sectors.Count)
        {
            throw new InvalidOperationException(
                "Front diagnostics have inconsistent sector coverage.");
        }

        var fireBySector = new Dictionary<
            BattleSector,
            TacticalBattalionSectorFirePressureReport>(
                ReferenceEqualityComparer.Instance);

        foreach (var sector in fire.Sectors)
        {
            if (!fireBySector.TryAdd(
                sector.Sector.Sector, sector))
            {
                throw new InvalidOperationException(
                    "Duplicated fire pressure sector.");
            }
        }

        var combined = new List<
            TacticalBattalionSectorFragilityReport>();

        var combinedBySector = new Dictionary<
            BattleSector,
            TacticalBattalionSectorFragilityReport>(
                ReferenceEqualityComparer.Instance);

        foreach (var hostile in exposure.Sectors)
        {
            var defense = hostile.Friendly;

            if (!fireBySector.TryGetValue(
                defense.Sector, out var sectorFire))
            {
                throw new InvalidOperationException(
                    "Missing fire pressure for defended sector.");
            }

            // Les deux diagnostics doivent decrire
            // le meme secteur et le meme etat defensif.
            if (!Equals(sectorFire.Sector, defense))
            {
                throw new InvalidOperationException(
                    "Inconsistent defensive sector snapshots.");
            }

            bool weak =
                defense.Status !=
                TacticalBattalionSectorStatus.Capable;

            bool underFire =
                sectorFire.CompaniesUnderThreat > 0;

            bool suppressed =
                sectorFire.MaximumCurrentSuppression > 0;

            var report =
                new TacticalBattalionSectorFragilityReport(
                    Defense: defense,
                    Hostiles: hostile,
                    Fire: sectorFire,
                    IsDefensivelyWeak: weak,
                    IsUnderCurrentFire: underFire,
                    HasCurrentSuppression: suppressed,
                    IsWeakUnderFire: weak && underFire);

            if (!combinedBySector.TryAdd(
                defense.Sector, report))
            {
                throw new InvalidOperationException(
                    "Duplicated defensive sector.");
            }

            combined.Add(report);
        }

        var gaps = new List<
            TacticalBattalionGapFragilityReport>();

        foreach (var hostileGap in exposure.Gaps)
        {
            int weakAnchors = 0;
            int anchorsUnderFire = 0;
            int suppressedAnchors = 0;

            var anchors =
                hostileGap.Gap.AdjacentOccupiedSectors;

            foreach (var sector in anchors)
            {
                if (!combinedBySector.TryGetValue(
                    sector, out var anchor))
                {
                    throw new InvalidOperationException(
                        "Gap anchor is outside the assessed front.");
                }

                if (anchor.IsDefensivelyWeak)
                    weakAnchors++;

                if (anchor.IsUnderCurrentFire)
                    anchorsUnderFire++;

                if (anchor.HasCurrentSuppression)
                    suppressedAnchors++;
            }

            gaps.Add(new TacticalBattalionGapFragilityReport(
                Exposure: hostileGap,
                OccupiedAnchorCount: anchors.Count,
                WeakAnchorCount: weakAnchors,
                AnchorsUnderCurrentFire: anchorsUnderFire,
                AnchorsWithCurrentSuppression:
                    suppressedAnchors));
        }

        return new TacticalBattalionFrontFragilityReport(
            hour,
            Array.AsReadOnly(combined.ToArray()),
            Array.AsReadOnly(gaps.ToArray()));
    }
}
