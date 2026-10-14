
namespace WoF.Simulation.Military.Organization;

public sealed class Company
{
    public int Id { get; }

    public string Name { get; }

    public CompanyType Type { get; }

    public Battalion? ParentBattalion { get; private set; }

    public Company(
        int id,
        string name,
        CompanyType type)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Company name cannot be empty.",
                nameof(name));

        Id = id;
        Name = name;
        Type = type;
    }

    internal void AttachTo(Battalion battalion)
    {
        ArgumentNullException.ThrowIfNull(battalion);

        if (ParentBattalion != null)
        {
            throw new InvalidOperationException(
                "Company already belongs to a battalion.");
        }

        ParentBattalion = battalion;
    }
}
