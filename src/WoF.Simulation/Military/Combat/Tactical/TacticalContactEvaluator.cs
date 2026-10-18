
using WoF.Simulation.Diplomacy;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Contact potentiel enrichi par sa géométrie.
///
/// Les références aux déploiements restent vivantes.
/// Ce résultat est destiné à une évaluation ponctuelle,
/// et non à l'archivage historique d'une bataille.
///
/// Aucun calcul de détection réelle ou de tir.
/// </summary>
public sealed record TacticalEvaluatedContact(
    TacticalContactCandidate Candidate,
    TacticalSpatialRelation? SpatialRelation,
    TacticalSightResult FirstToSecondSight,
    TacticalSightResult SecondToFirstSight)
{
    /// <summary>
    /// Vrai lorsque les deux corridors géométriques
    /// ont pu être évalués.
    /// Ne signifie pas que les unités se détectent.
    /// </summary>
    public bool HasResolvedGeometry =>
        SpatialRelation is not null
        && FirstToSecondSight.Status != TacticalSightStatus.Unknown
        && SecondToFirstSight.Status != TacticalSightStatus.Unknown;
}

/// <summary>
/// Raccorde les contacts topologiques, les positions
/// spatiales et les corridors d'observation.
///
/// Ce système ne modifie aucun état militaire.
/// </summary>
public static class TacticalContactEvaluator
{
    public static IReadOnlyList<TacticalEvaluatedContact> Evaluate(
        TacticalBattle battle,
        TacticalGeometryMap geometry,
        DiplomacySystem diplomacy)
    {
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(diplomacy);

        if (!ReferenceEquals(geometry.Battle, battle))
        {
            throw new InvalidOperationException(
                "Geometry belongs to another tactical battle.");
        }

        // Première étape : utiliser exclusivement
        // les candidats produits par C2.2a.
        var candidates = TacticalContactDetector.Detect(
            battle,
            diplomacy);

        var results = new List<TacticalEvaluatedContact>(
            candidates.Count);

        foreach (var candidate in candidates)
        {
            CompanyDeployment first = candidate.First;
            CompanyDeployment second = candidate.Second;

            var firstPosition = geometry.GetPosition(first);
            var secondPosition = geometry.GetPosition(second);

            TacticalSpatialRelation? relation = null;

            // La mesure spatiale nécessite les deux
            // positions. Une position manquante n'est
            // pas interprétée comme une distance nulle.
            if (firstPosition is not null
                && secondPosition is not null)
            {
                relation = geometry.Measure(first, second);
            }

            // Évaluation du corridor depuis A vers B.
            TacticalSightResult firstToSecond =
                TacticalSightEvaluator.Evaluate(
                    geometry,
                    first,
                    second);

            // Évaluation indépendante de B vers A.
            // Cela préserve notamment la notion du
            // premier obstacle rencontré par chaque camp.
            TacticalSightResult secondToFirst =
                TacticalSightEvaluator.Evaluate(
                    geometry,
                    second,
                    first);

            results.Add(new TacticalEvaluatedContact(
                candidate,
                relation,
                firstToSecond,
                secondToFirst));
        }

        return results.AsReadOnly();
    }
}
