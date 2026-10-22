
using WoF.Simulation.World;

namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Etats defensifs reels des compagnies d'une bataille.
/// Ces informations restent internes au moteur.
/// </summary>
internal sealed class TacticalDefensiveState
{
    private readonly TacticalBattle _battle;

    private readonly Dictionary<
        CompanyDeployment,
        TacticalDefensiveDisposition> _dispositions =
            new(ReferenceEqualityComparer.Instance);

    internal TacticalDefensiveState(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    internal void Set(
        CompanyDeployment deployment,
        TacticalDefensiveDisposition disposition)
    {
        ValidateDeployment(deployment);

        if (!Enum.IsDefined(disposition.Stance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposition),
                "Invalid tactical stance.");
        }

        if (!Enum.IsDefined(disposition.Cover))
        {
            throw new ArgumentOutOfRangeException(
                nameof(disposition),
                "Invalid cover type.");
        }

        var sector = deployment.Sector;

        bool supported = disposition.Cover switch
        {
            TacticalCover.None => true,

            TacticalCover.Vegetation =>
                sector.DominantTerrain == TerrainType.Forest
                || sector.Features.HasFlag(SectorFeature.Woods),

            TacticalCover.Building =>
                sector.Features.HasFlag(SectorFeature.Buildings),

            TacticalCover.Entrenchment =>
                sector.Features.HasFlag(SectorFeature.Entrenchments),

            _ => false
        };

        if (!supported)
        {
            throw new InvalidOperationException(
                "Requested cover is not available in this sector.");
        }

        // Une seule disposition par vraie compagnie.
        _dispositions[deployment] = disposition;
    }

    internal TacticalDefensiveDisposition Get(
        CompanyDeployment deployment)
    {
        ValidateDeployment(deployment);

        return _dispositions.TryGetValue(
            deployment,
            out var disposition)
                ? disposition
                : new TacticalDefensiveDisposition(
                    TacticalStance.Standing,
                    TacticalCover.None);
    }

    private void ValidateDeployment(
        CompanyDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (!_battle.ContainsDeployment(deployment))
        {
            throw new InvalidOperationException(
                "Deployment belongs to another tactical battle.");
        }
    }
}
