using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Construit la grille d'une province à partir de son descripteur.
/// Pur et déterministe : même descripteur, même grille. Ne lit aucune autre province.
///
/// Étapes :
/// 1. relief : somme d'octaves de bruit, ramenée à l'amplitude du profil ;
/// 2. collines, montagne et marais : seuils sur le relief (cases les plus hautes, les plus basses) ;
/// 3. bois : seuil sur un second champ de bruit, hors marais ;
/// 4. frontières : arcs du cadre et points de passage ;
/// 5. rivières ; 6. localités ; 7. routes et ponts ;
/// 8. terrain final de chaque case.
/// </summary>
public static class ProvinceTerrainGenerator
{
    private const long ReliefSeedDomain = 0x52454C49; // « RELI »
    private const long WoodsSeedDomain = 0x574F4F44;  // « WOOD »

    private const double ReliefWavelengthMeters = 6000;
    private const int ReliefOctaves = 5;
    private const double WoodsWavelengthMeters = 2000;
    private const int WoodsOctaves = 4;

    public static ProvinceHexGrid Generate(ProvinceTerrainDescriptor descriptor)
    {
        return Generate(
            descriptor,
            TerrainGenerationProfile.For(descriptor.DominantTerrain));
    }

    public static ProvinceHexGrid Generate(
        ProvinceTerrainDescriptor descriptor,
        TerrainGenerationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(profile);

        // Avant toute allocation : un profil incohérent serait appliqué faux sans erreur.
        profile.Validate();

        var grid = new ProvinceHexGrid(
            descriptor,
            ProvinceHexGrid.StandardColumns,
            ProvinceHexGrid.StandardRows);

        double[] normalized = GenerateRelief(grid, profile);

        ClassifyRelief(grid, profile, normalized);
        PlaceWoods(grid, profile);

        ProvinceBorderLayout.Apply(grid);
        RiverTracer.Apply(grid, normalized);
        LocalityPlacer.Apply(grid, profile, normalized);
        RoadNetworkBuilder.Apply(grid);

        FinalizeTerrain(grid, profile);

        return grid;
    }

    /// <summary>Relief normalisé dans [0,1], puis altitude = base + amplitude × valeur.</summary>
    private static double[] GenerateRelief(
        ProvinceHexGrid grid,
        TerrainGenerationProfile profile)
    {
        ulong seed = DeterministicRandom.Hash(
            grid.Descriptor.ProvinceSeed, ReliefSeedDomain);

        var values = new double[grid.CellCount];
        double min = double.PositiveInfinity;
        double max = double.NegativeInfinity;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            double value = TerrainNoise.Fractal(
                seed,
                grid.GetCenterXMeters(cell) / ReliefWavelengthMeters,
                grid.GetCenterYMeters(cell) / ReliefWavelengthMeters,
                ReliefOctaves);

            values[cell] = value;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        double range = max - min;

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            double normalized = range > 0
                ? (values[cell] - min) / range
                : 0;

            values[cell] = normalized;

            grid.SetElevation(cell, (float)(
                profile.BaseElevationMeters
                + profile.ReliefAmplitudeMeters * normalized));
        }

        return values;
    }

    private static void ClassifyRelief(
        ProvinceHexGrid grid,
        TerrainGenerationProfile profile,
        double[] normalized)
    {
        int count = grid.CellCount;

        // Du plus bas au plus haut ; à altitude égale, par indice.
        int[] order = Enumerable.Range(0, count)
            .OrderBy(c => normalized[c])
            .ThenBy(c => c)
            .ToArray();

        int marsh = FractionToCount(profile.MarshFraction, count);
        int mountain = FractionToCount(profile.MountainFraction, count);
        int hills = FractionToCount(profile.HillsFraction, count);

        for (int rank = 0; rank < count; rank++)
        {
            int fromTop = count - 1 - rank;

            TerrainType terrain =
                fromTop < mountain ? TerrainType.Mountain
                : fromTop < mountain + hills ? TerrainType.Hills
                : rank < marsh ? TerrainType.Marsh
                : profile.BaseTerrain;

            grid.SetTerrain(order[rank], terrain);
        }
    }

    private static void PlaceWoods(
        ProvinceHexGrid grid,
        TerrainGenerationProfile profile)
    {
        int woods = FractionToCount(profile.WoodsFraction, grid.CellCount);

        if (woods == 0)
            return;

        ulong seed = DeterministicRandom.Hash(
            grid.Descriptor.ProvinceSeed, WoodsSeedDomain);

        var density = new double[grid.CellCount];

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            density[cell] = TerrainNoise.Fractal(
                seed,
                grid.GetCenterXMeters(cell) / WoodsWavelengthMeters,
                grid.GetCenterYMeters(cell) / WoodsWavelengthMeters,
                WoodsOctaves);
        }

        var selected = Enumerable.Range(0, grid.CellCount)
            .Where(c => grid.GetTerrain(c) != TerrainType.Marsh)
            .OrderByDescending(c => density[c])
            .ThenBy(c => c)
            .Take(woods);

        foreach (int cell in selected)
            grid.AddFeature(cell, TerrainCellFeature.Woods);
    }

    /// <summary>
    /// Bâtiments → Urban ; marais, montagne et collines conservés ;
    /// sinon bois → Forest ; sinon terrain de base du profil.
    /// </summary>
    private static void FinalizeTerrain(
        ProvinceHexGrid grid,
        TerrainGenerationProfile profile)
    {
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            var features = grid.GetFeatures(cell);
            var terrain = grid.GetTerrain(cell);

            if ((features & TerrainCellFeature.Buildings) != 0)
                terrain = TerrainType.Urban;
            else if (terrain == profile.BaseTerrain
                     && (features & TerrainCellFeature.Woods) != 0)
                terrain = TerrainType.Forest;

            grid.SetTerrain(cell, terrain);
        }
    }

    private static int FractionToCount(double fraction, int count)
    {
        return (int)Math.Round(
            fraction * count,
            MidpointRounding.AwayFromZero);
    }
}
