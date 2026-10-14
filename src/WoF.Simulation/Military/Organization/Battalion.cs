
using System.Collections.ObjectModel;
using WoF.Simulation.Military;
using WoF.Simulation.Military.Composition;

namespace WoF.Simulation.Military.Organization;

public sealed class Battalion
{
    private readonly List<Company> _companies = new();

    private readonly ReadOnlyCollection<Company> _companiesView;

    public int Id { get; }

    public string Name { get; }

    public BattalionType Type { get; }

    public Division? ParentDivision { get; private set; }

    public IReadOnlyList<Company> Companies =>
        _companiesView;

    public Battalion(
        int id,
        string name,
        BattalionType type)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Battalion name cannot be empty.",
                nameof(name));

        Id = id;
        Name = name;
        Type = type;

        _companiesView = _companies.AsReadOnly();
    }

    public void AddCompany(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (ParentDivision?.ResourceMode ==
        DivisionResourceMode.Hierarchical)
        {
            throw new InvalidOperationException(
                "Cannot add companies after resource activation.");
        }

        if (company.ParentBattalion != null)
        {
            throw new InvalidOperationException(
                "Company is already attached to a battalion.");
        }

        if (_companies.Any(c => c.Id == company.Id))
        {
            throw new InvalidOperationException(
                "Company ID already exists in this battalion.");
        }

        company.AttachTo(this);

        _companies.Add(company);
    }

    internal void AttachTo(Division division)
    {
        ArgumentNullException.ThrowIfNull(division);

        if (ParentDivision != null)
        {
            throw new InvalidOperationException(
                "Battalion already belongs to a division.");
        }

        ParentDivision = division;
    }


    
    public FormationResourceTotals GetResourceTotals()
    {
        if (_companies.Count == 0)
        {
            throw new InvalidOperationException(
                "Battalion has no companies.");
        }

        var totals = default(FormationResourceTotals);

        foreach (var company in _companies)
        {
            if (company.Composition is null)
            {
                throw new InvalidOperationException(
                    $"Company {company.Name} has no composition.");
            }

            totals += FormationResourceTotals.Capture(
                company.Composition
            );
        }

        return totals;
    }

}
