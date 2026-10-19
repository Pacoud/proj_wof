
using WoF.Simulation.Military;
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Combat.Tactical;

public sealed partial class TacticalCommunicationNetwork
{
    private sealed class RadioWatch
    {
        public Company Operator { get; }
        public Action Handler { get; }

        public int BindingCount { get; set; }
        public bool Ready { get; set; }

        public RadioWatch(
            Company radioOperator,
            Action handler)
        {
            Operator = radioOperator;
            Handler = handler;
            Ready = HasUsableRadio(radioOperator);
        }
    }

    private readonly Dictionary<
        TacticalCommunicationNode, Company>
        _radioOperators = new();

    private readonly Dictionary<Company, RadioWatch>
        _radioWatches =
            new(ReferenceEqualityComparer.Instance);

    private bool _physicalRadioChecksEnabled;
    private bool _disposed;

    public bool PhysicalRadioChecksEnabled =>
        _physicalRadioChecksEnabled;

    /// <summary>
    /// Active les exigences physiques des liaisons
    /// radio. Une liaison sans operateurs valides
    /// devient alors indisponible.
    /// </summary>
    /// 
    public void EnablePhysicalRadioChecks()
    {
        ThrowIfDisposed();

        if (_physicalRadioChecksEnabled)
            return;

        _physicalRadioChecksEnabled = true;
        Version++;
    }

    /// <summary>
    /// Associe un noeud radio a une compagnie
    /// possedant le personnel et le materiel
    /// qui servent physiquement cette liaison.
    ///
    /// Aucun stock de radios n'est duplique.
    /// </summary>
    public void BindRadioOperator(
        TacticalCommunicationNode node,
        Company radioOperator)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(radioOperator);

        ThrowIfDisposed();
        ValidateNode(node);
        ValidateAssignment(node, radioOperator);

        if (_radioOperators.TryGetValue(
            node, out var previous))
        {
            if (ReferenceEquals(previous, radioOperator))
                return;

            Detach(previous);
        }

        _radioOperators[node] = radioOperator;
        Attach(radioOperator);

        // Un changement d'operateur peut modifier
        // les chemins utilisables.
        Version++;
    }

    private static void ValidateAssignment(
        TacticalCommunicationNode node,
        Company radioOperator)
    {
        if (radioOperator.Composition is null)
        {
            throw new InvalidOperationException(
                "Radio operator has no physical composition.");
        }

        Battalion? parentBattalion =
            radioOperator.ParentBattalion;

        Division? parentDivision =
            parentBattalion?.ParentDivision;

        if (parentBattalion is null || parentDivision is null)
        {
            throw new InvalidOperationException(
                "Radio operator is not attached to a division.");
        }

        bool valid = node.Kind switch
        {
            TacticalCommunicationNodeKind.Company =>
                ReferenceEquals(
                    ((CompanyDeployment)node.Formation).Company,
                    radioOperator),

            TacticalCommunicationNodeKind.Battalion =>
                ValidBattalionAssignment(
                    (Battalion)node.Formation,
                    radioOperator,
                    parentBattalion,
                    parentDivision),

            TacticalCommunicationNodeKind.Division =>
                ReferenceEquals(
                    node.Formation, parentDivision)
                && radioOperator.Type is
                    CompanyType.Signals or
                    CompanyType.Headquarters,

            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                "Radio operator is not authorized for this node.");
        }
    }

    private static bool ValidBattalionAssignment(
        Battalion battalion,
        Company radioOperator,
        Battalion parentBattalion,
        Division parentDivision)
    {
        // Radio organique d'une compagnie du bataillon.
        if (ReferenceEquals(battalion, parentBattalion))
            return true;

        // Service de transmissions centralise :
        // compagnie Signals de la meme division.
        return ReferenceEquals(
                battalion.ParentDivision,
                parentDivision)
            && radioOperator.Type == CompanyType.Signals;
    }

    private static bool HasUsableRadio(Company company)
    {
        var composition = company.Composition;

        return composition is not null
            && composition.Radios.Current > 0
            && composition.Manpower.Current > 0;
    }

    private void Attach(Company company)
    {
        if (_radioWatches.TryGetValue(
            company, out var existing))
        {
            existing.BindingCount++;
            return;
        }

        void Handler() => OnOperatorResourcesChanged(company);

        var watch = new RadioWatch(company, Handler)
        {
            BindingCount = 1
        };

        _radioWatches.Add(company, watch);

        company.Composition!.Radios.Changed += Handler;
        company.Composition.Manpower.Changed += Handler;
    }

    private void Detach(Company company)
    {
        var watch = _radioWatches[company];
        watch.BindingCount--;

        if (watch.BindingCount != 0)
            return;

        company.Composition!.Radios.Changed -= watch.Handler;
        company.Composition.Manpower.Changed -= watch.Handler;

        _radioWatches.Remove(company);
    }

    /// <summary>
    /// Callback appele uniquement lors d'une
    /// modification des ressources surveillees.
    /// Invalide le cache seulement si une capacite
    /// bascule entre disponible et indisponible.
    /// </summary>
    private void OnOperatorResourcesChanged(Company company)
    {
        if (_disposed
            || !_radioWatches.TryGetValue(
                company, out var watch))
        {
            return;
        }

        bool readyNow = HasUsableRadio(company);

        if (watch.Ready == readyNow)
            return;

        watch.Ready = readyNow;

        if (_physicalRadioChecksEnabled)
            Version++;
    }

    private bool IsRadioNodeReady(
        TacticalCommunicationNode node)
    {
        return _radioOperators.TryGetValue(
                node, out var radioOperator)
            && _radioWatches.TryGetValue(
                radioOperator, out var watch)
            && watch.Ready;
    }

    /// <summary>
    /// Le telephone de campagne et les messagers
    /// restent geres par l'etat administratif
    /// de la liaison dans cette iteration.
    /// </summary>
    private bool IsPhysicallyAvailable(
        TacticalCommunicationLink link)
    {
        ThrowIfDisposed();

        if (!_physicalRadioChecksEnabled
            || link.Medium != TacticalCommunicationMedium.Radio)
        {
            return true;
        }

        return IsRadioNodeReady(link.From)
            && IsRadioNodeReady(link.To);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Supprime les abonnements aux pools physiques.
    /// A appeler lorsque le reseau n'est plus utilise.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        foreach (var watch in _radioWatches.Values)
        {
            var composition = watch.Operator.Composition!;

            composition.Radios.Changed -= watch.Handler;
            composition.Manpower.Changed -= watch.Handler;
        }

        _radioWatches.Clear();
        _radioOperators.Clear();

        _disposed = true;
        Version++;
    }
}
