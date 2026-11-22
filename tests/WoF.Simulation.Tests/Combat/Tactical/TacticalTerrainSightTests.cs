using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World.Terrain;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

/// <summary>
/// Ligne de vue sur une grille : le sol interpolé et le couvert des
/// cases traversées masquent la vue, en plus des obstacles tracés.
/// Grille de 30 × 9 cases ; observateur et cible sur la rangée 4,
/// aux colonnes 3 et 26 (x = 600 m et x = 5 200 m).
/// </summary>
public sealed class TacticalTerrainSightTests
{
    private static (GridBattle B, CompanyDeployment Observer, CompanyDeployment Target)
        Scenario(
            Func<double, double, double> elevation,
            Func<double, double, TerrainCellFeature>? features = null,
            double offsetY = 0)
    {
        var b = CreateBattle(CreateGrid(30, 9, elevation, features));

        var observer = Place(b, b.French, 0, column: 3, row: 4, dy: offsetY);
        var target = Place(b, b.German, 0, column: 26, row: 4, dy: offsetY, facingDegrees: 270);

        return (b, observer, target);
    }

    [Fact]
    public void Mountain_BetweenTwoPlains_BlocksSightBothWays()
    {
        // Montagne de 300 m entre x = 2 500 et 3 300 m.
        var (b, observer, target) = Scenario(
            (x, _) => x is >= 2500 and <= 3300 ? 300 : 0);

        var forward = TacticalSightEvaluator.Evaluate(b.Map, observer, target);
        var backward = TacticalSightEvaluator.Evaluate(b.Map, target, observer);

        Assert.Equal(TacticalSightStatus.BlockedByTerrain, forward.Status);
        Assert.Equal(TacticalSightStatus.BlockedByTerrain, backward.Status);
        Assert.Null(forward.BlockingObstacle);

        // Le pied de la montagne commence à monter 200 m avant elle
        // (interpolation depuis le dernier centre de plaine).
        Assert.InRange(forward.ObstructionDistanceMeters!.Value, 1700, 1900);
        Assert.InRange(backward.ObstructionDistanceMeters!.Value, 1700, 1900);
    }

    [Fact]
    public void FlatPlain_KeepsSightClear()
    {
        var (b, observer, target) = Scenario((_, _) => 0);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            TacticalSightEvaluator.Evaluate(b.Map, observer, target).Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(-45)]
    public void RegularSlope_DoesNotMaskTheView(double offsetY)
    {
        // Pente de 5 % vers le nord, en travers de la ligne de vue,
        // et de 3 % vers l'est, dans son axe. Décalée de la rangée des
        // centres, la ligne traverse des cases dont le centre est plus
        // haut qu'elle : une altitude unique par case la masquerait.
        var (b, observer, target) = Scenario(
            (x, y) => 0.03 * x + 0.05 * y,
            offsetY: offsetY);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            TacticalSightEvaluator.Evaluate(b.Map, observer, target).Status);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            TacticalSightEvaluator.Evaluate(b.Map, target, observer).Status);
    }

    [Fact]
    public void Woods_BlockTheView_ExceptInTheObserverAndTargetCells()
    {
        // Une bande de bois au milieu (x = 3 000 m).
        var (wooded, observer, target) = Scenario(
            (_, _) => 0,
            (x, _) => Math.Abs(x - 3000) < 150 ? TerrainCellFeature.Woods : TerrainCellFeature.None);

        Assert.Equal(
            TacticalSightStatus.BlockedByTerrain,
            TacticalSightEvaluator.Evaluate(wooded.Map, observer, target).Status);

        // Bois seulement dans les cases de l'observateur et de la cible :
        // leur propre couvert relève de leur dissimulation.
        var (ends, endObserver, endTarget) = Scenario(
            (_, _) => 0,
            (x, _) => Math.Abs(x - 600) < 150 || Math.Abs(x - 5200) < 150
                ? TerrainCellFeature.Woods
                : TerrainCellFeature.None);

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            TacticalSightEvaluator.Evaluate(ends.Map, endObserver, endTarget).Status);
    }

    [Fact]
    public void Buildings_AreLowerThanWoods()
    {
        // Deux plateaux à 12 m séparés par une vallée de 900 m de large ;
        // l'œil est à 13,7 m. Bâtiments (8 m) ou bois (15 m) au fond.
        static double Valley(double x, double _) => Math.Abs(x - 3000) < 450 ? 0 : 12;

        static Func<double, double, TerrainCellFeature> Bottom(TerrainCellFeature feature) =>
            (x, _) => Math.Abs(x - 3000) < 150 ? feature : TerrainCellFeature.None;

        var (buildings, bo, bt) = Scenario(Valley, Bottom(TerrainCellFeature.Buildings));
        var (woods, wo, wt) = Scenario(Valley, Bottom(TerrainCellFeature.Woods));

        Assert.Equal(
            TacticalSightStatus.ClearOfKnownObstacles,
            TacticalSightEvaluator.Evaluate(buildings.Map, bo, bt).Status);

        Assert.Equal(
            TacticalSightStatus.BlockedByTerrain,
            TacticalSightEvaluator.Evaluate(woods.Map, wo, wt).Status);
    }

    [Fact]
    public void TheNearestObstruction_Wins()
    {
        var y = 4 * ProvinceHexGrid.SpacingMeters * HexCoord.RowSpacingFactor;

        // Obstacle tracé près de l'observateur, montagne plus loin.
        var (far, fo, ft) = Scenario((x, _) => x is >= 4000 and <= 4400 ? 300 : 0);
        far.Map.AddLineObstacle(new TacticalLineObstacle(
            1, "Mur", TacticalLineObstacleKind.Wall, 1500, y - 200, 1500, y + 200, 50));

        var obstacleFirst = TacticalSightEvaluator.Evaluate(far.Map, fo, ft);

        Assert.Equal(TacticalSightStatus.BlockedByObstacle, obstacleFirst.Status);
        Assert.Equal(900, obstacleFirst.ObstructionDistanceMeters!.Value, 6);

        // Montagne près de l'observateur, obstacle tracé plus loin.
        var (near, no, nt) = Scenario((x, _) => x is >= 1200 and <= 1600 ? 300 : 0);
        near.Map.AddLineObstacle(new TacticalLineObstacle(
            1, "Mur", TacticalLineObstacleKind.Wall, 4500, y - 200, 4500, y + 200, 50));

        var terrainFirst = TacticalSightEvaluator.Evaluate(near.Map, no, nt);

        Assert.Equal(TacticalSightStatus.BlockedByTerrain, terrainFirst.Status);
        Assert.True(terrainFirst.ObstructionDistanceMeters < 900);
    }

    [Fact]
    public void Detection_ReadsTheTerrain()
    {
        // Deux cases voisines (colonnes 10 et 11 de la rangée 4), compagnies
        // décalées de 80 m vers le nord : la ligne passe près de la case
        // commune de la rangée 5 (colonne 10). Une colline de 300 m y masque
        // la vue ; sans elle, l'observateur localise la cible.
        static bool Detects(double hill)
        {
            var b = CreateBattle(CreateGrid(30, 9, (x, y) =>
                Math.Abs(x - 2100) < 1 && Math.Abs(y - 5 * 173.20508) < 1 ? hill : 0));

            var observer = Place(b, b.French, 0, column: 10, row: 4, dy: 80);
            var target = Place(b, b.German, 0, column: 11, row: 4, dy: 80, facingDegrees: 270);

            var perception = new TacticalPerceptionState(b.Battle);
            var signatures = new TacticalSignatureFrame(b.Battle);

            signatures.Set(target, new TacticalUnitSignature(
                TacticalActivity.Stationary, TacticalConcealment.None));

            TacticalDetectionSystem.Update(
                b.Battle, b.Map, b.Diplomacy, perception, signatures,
                currentHour: 2,
                new TacticalDetectionProfile(
                    maxVisualRangeMeters: 1000,
                    maxAcousticRangeMeters: 0,
                    fieldOfViewDegrees: 360));

            return perception.GetReports(observer).Any(r =>
                r.CurrentObservationLevel == TacticalKnowledgeLevel.Located);
        }

        Assert.True(Detects(hill: 0));
        Assert.False(Detects(hill: 300));
    }
}
