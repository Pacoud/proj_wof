
namespace WoF.Simulation.Military.Combat.Tactical;

public enum TacticalActivity
{
    Stationary,
    Moving,
    Firing
}

public enum TacticalConcealment
{
    None,
    Partial,
    Strong
}

/// <summary>
/// Etat observable d'une compagnie pendant une passe.
/// Ce n'est ni un niveau de connaissance ennemi,
/// ni une ressource physique supplémentaire.
/// </summary>
public sealed record TacticalUnitSignature
{
    public TacticalActivity Activity { get; }
    public TacticalConcealment Concealment { get; }

    public TacticalUnitSignature(
        TacticalActivity activity,
        TacticalConcealment concealment)
    {
        if (!Enum.IsDefined(activity))
            throw new ArgumentOutOfRangeException(nameof(activity));

        if (!Enum.IsDefined(concealment))
            throw new ArgumentOutOfRangeException(nameof(concealment));

        Activity = activity;
        Concealment = concealment;
    }
}

/// <summary>
/// Entree temporaire du systeme de detection.
/// Chaque passe fournit ses propres signatures.
/// Une signature absente signifie : etat inconnu.
/// </summary>
public sealed class TacticalSignatureFrame
{
    private readonly TacticalBattle _battle;

    internal TacticalBattle Battle => _battle;

    private readonly Dictionary<
        CompanyDeployment,
        TacticalUnitSignature>
        _signatures = new(ReferenceEqualityComparer.Instance);

    public TacticalSignatureFrame(TacticalBattle battle)
    {
        _battle = battle
            ?? throw new ArgumentNullException(nameof(battle));
    }

    public void Set(
        CompanyDeployment deployment,
        TacticalUnitSignature signature)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(signature);

        if (!_battle.Engagement.IsActive)
            throw new InvalidOperationException(
                "Cannot update signatures of an ended battle.");

        if (!_battle.ContainsDeployment(deployment))
            throw new InvalidOperationException(
                "Deployment belongs to another battle.");

        _signatures[deployment] = signature;
    }

    public bool TryGet(
        CompanyDeployment deployment,
        out TacticalUnitSignature? signature)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (!_battle.ContainsDeployment(deployment))
            throw new InvalidOperationException(
                "Deployment belongs to another battle.");

        return _signatures.TryGetValue(
            deployment,
            out signature);
    }
}
