namespace WoF.Simulation.World.Terrain;

/// <summary>Intervalle entier fermé [Min, Max].</summary>
public readonly record struct IntRange
{
    public int Min { get; }
    public int Max { get; }

    public IntRange(int min, int max)
    {
        if (min < 0 || max < min)
            throw new ArgumentOutOfRangeException(nameof(max));

        Min = min;
        Max = max;
    }

    public bool Contains(int value) => value >= Min && value <= Max;
}

/// <summary>
/// Consignes du générateur pour un type de province.
///
/// Les fractions portent sur le nombre total de cases :
/// - collines et montagne : seuils sur le relief lui-même (cases les plus hautes) ;
/// - marais : seuil sur le relief (cases les plus basses) ;
/// - bois : seuil sur un second champ de bruit, hors marais.
///
/// ReliefAmplitudeMeters : écart entre la case la plus basse et la plus haute.
/// Valeurs de travail, validées le 22 novembre 2026 sauf mention contraire.
/// </summary>
public sealed record TerrainGenerationProfile
{
    public TerrainType BaseTerrain { get; init; } = TerrainType.Plains;

    public double BaseElevationMeters { get; init; }

    public double ReliefAmplitudeMeters { get; init; }

    public double WoodsFraction { get; init; }

    public double MarshFraction { get; init; }

    public double HillsFraction { get; init; }

    public double MountainFraction { get; init; }

    public IntRange Villages { get; init; }

    public IntRange Towns { get; init; }

    public IntRange Cities { get; init; }

    public IntRange VillageCells { get; init; } = new(1, 3);

    public IntRange TownCells { get; init; } = new(5, 10);

    /// <summary>Taille d'une ville. Valeur provisoire, à confirmer.</summary>
    public IntRange CityCells { get; init; } = new(300, 500);

    /// <summary>
    /// Refuse un profil que le générateur appliquerait faux sans erreur.
    ///
    /// - Terrain de base : Plains ou Desert. Les cinq autres types sont produits
    ///   par les seuils (Hills, Mountain, Marsh), les bois (Forest) ou les localités (Urban).
    /// - Altitude de base finie ; amplitude finie et strictement positive,
    ///   sinon les seuils de relief n'auraient pas de sens.
    /// - Fractions finies dans [0,1] ; collines + montagne + marais ≤ 1,
    ///   car ces seuils se partagent les mêmes cases ; bois + marais ≤ 1,
    ///   car les bois sont exclus des marais.
    /// - Tailles de localités d'au moins une case (la case centrale).
    /// </summary>
    internal void Validate()
    {
        if (BaseTerrain is not (TerrainType.Plains or TerrainType.Desert))
            throw Invalid(nameof(BaseTerrain), "must be Plains or Desert");

        if (!double.IsFinite(BaseElevationMeters))
            throw Invalid(nameof(BaseElevationMeters), "must be finite");

        if (!double.IsFinite(ReliefAmplitudeMeters) || ReliefAmplitudeMeters <= 0)
            throw Invalid(nameof(ReliefAmplitudeMeters), "must be finite and positive");

        ValidateFraction(WoodsFraction, nameof(WoodsFraction));
        ValidateFraction(MarshFraction, nameof(MarshFraction));
        ValidateFraction(HillsFraction, nameof(HillsFraction));
        ValidateFraction(MountainFraction, nameof(MountainFraction));

        if (HillsFraction + MountainFraction + MarshFraction > 1)
            throw Invalid(nameof(HillsFraction),
                "hills, mountain and marsh fractions must not exceed 1 together");

        if (WoodsFraction + MarshFraction > 1)
            throw Invalid(nameof(WoodsFraction),
                "woods and marsh fractions must not exceed 1 together");

        ValidateSize(VillageCells, nameof(VillageCells));
        ValidateSize(TownCells, nameof(TownCells));
        ValidateSize(CityCells, nameof(CityCells));
    }

    private static void ValidateFraction(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1)
            throw Invalid(name, "must be a finite fraction in [0, 1]");
    }

    private static void ValidateSize(IntRange size, string name)
    {
        if (size.Min < 1)
            throw Invalid(name, "a locality has at least one cell");
    }

    private static ArgumentException Invalid(string property, string reason)
    {
        return new ArgumentException(
            $"Invalid terrain generation profile: {property} {reason}.",
            "profile");
    }

    public static TerrainGenerationProfile For(TerrainType terrain)
    {
        return terrain switch
        {
            TerrainType.Plains => new()
            {
                BaseElevationMeters = 120,
                ReliefAmplitudeMeters = 30,
                WoodsFraction = 0.15,
                MarshFraction = 0.03,
                HillsFraction = 0.10,
                Villages = new(15, 25),
                Towns = new(1, 1)
            },

            // Amplitude non fixée par le créateur : 80 m, provisoire.
            TerrainType.Forest => new()
            {
                BaseElevationMeters = 200,
                ReliefAmplitudeMeters = 80,
                WoodsFraction = 0.60,
                MarshFraction = 0.05,
                HillsFraction = 0.15,
                Villages = new(4, 6)
            },

            TerrainType.Hills => new()
            {
                BaseElevationMeters = 250,
                ReliefAmplitudeMeters = 150,
                WoodsFraction = 0.25,
                MarshFraction = 0.02,
                HillsFraction = 0.50,
                Villages = new(8, 12)
            },

            TerrainType.Mountain => new()
            {
                BaseElevationMeters = 600,
                ReliefAmplitudeMeters = 800,
                WoodsFraction = 0.30,
                MarshFraction = 0,
                HillsFraction = 0.35,
                MountainFraction = 0.40,
                Villages = new(3, 5)
            },

            TerrainType.Marsh => new()
            {
                BaseElevationMeters = 20,
                ReliefAmplitudeMeters = 10,
                WoodsFraction = 0.20,
                MarshFraction = 0.35,
                HillsFraction = 0,
                Villages = new(3, 5)
            },

            // Amplitude non fixée par le créateur : 60 m, provisoire.
            TerrainType.Desert => new()
            {
                BaseTerrain = TerrainType.Desert,
                BaseElevationMeters = 300,
                ReliefAmplitudeMeters = 60,
                WoodsFraction = 0,
                MarshFraction = 0,
                HillsFraction = 0.15,
                Villages = new(1, 2)
            },

            // Amplitude non fixée par le créateur : 30 m, provisoire.
            TerrainType.Urban => new()
            {
                BaseElevationMeters = 100,
                ReliefAmplitudeMeters = 30,
                WoodsFraction = 0.05,
                MarshFraction = 0,
                HillsFraction = 0.05,
                Villages = new(6, 10),
                Cities = new(1, 1)
            },

            _ => throw new ArgumentOutOfRangeException(nameof(terrain))
        };
    }
}
