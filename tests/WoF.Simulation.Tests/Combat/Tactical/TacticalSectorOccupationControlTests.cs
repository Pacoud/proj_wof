using WoF.Simulation.Military.Combat.Losses;
using WoF.Simulation.Military.Combat.Tactical;
using WoF.Simulation.World;
using static WoF.Simulation.Tests.Combat.Tactical.TacticalGridTestFactory;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalSectorOccupationControlTests
{
    private static GridBattle NewBattle() => CreateBattle(CreateGrid(8, 8, (_, _) => 0));

    [Theory]
    [InlineData(CompanyDeploymentRole.FrontLine)]
    [InlineData(CompanyDeploymentRole.Support)]
    [InlineData(CompanyDeploymentRole.Reserve)]
    public void EachRole_TakesOnlyCoveredCells_AndKeepsThemOnDeparture(CompanyDeploymentRole role)
    {
        var b = NewBattle();
        var province = b.Battle.Engagement.Province!;
        province.ChangeController(b.German.Country);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var company = Place(b, b.French, 0, 3, 2, facingDegrees: 0, frontageMeters: 300);
        company.ApplyMovementArrival(company.Sector, role);
        var shortFront = Place(b, b.French, 1, 6, 2, frontageMeters: 15);

        Assert.Same(b.German.Country, control.GetController(company.Sector));
        Assert.Equal(3, control.ApplyOccupation());
        company.SetInTransit(true);
        Assert.Equal(0, control.ApplyOccupation());
        for (int column = 2; column <= 4; column++)
            Assert.Same(b.French.Country, control.GetController(b.Layout.GetSector(Cell(b.Grid, column, 2))));

        Assert.Same(b.German.Country, control.GetController(shortFront.Sector));
        Assert.Same(b.German.Country, province.Controller);
    }

    [Fact]
    public void RoutedCompany_ContestsOnSite_AndCanRallyOnSite()
    {
        var b = NewBattle();
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var french = Place(b, b.French, 0, 3, 2);
        var sector = french.Sector;
        double cohesion = french.Company.Cohesion;
        Assert.False(french.IsRouted);

        french.SetRouted(true);
        Assert.False(french.IsInTransit);
        Assert.Equal(0, control.ApplyOccupation());
        var german = Place(b, b.German, 0, 3, 2);
        Assert.Equal(0, control.ApplyOccupation());
        Assert.Null(control.GetController(sector));

        german.SetInTransit(true);
        Assert.Equal(0, control.ApplyOccupation()); // Le Français est seul, toujours en déroute.
        french.SetRouted(false);
        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(b.French.Country, control.GetController(sector));
        Assert.False(french.IsInTransit);
        Assert.Equal(cohesion, french.Company.Cohesion);

        // Les deux sont à nouveau sur place. La déroute du détenteur ne suffit
        // pas à faire basculer la case. Son départ effectif libère la prise.
        german.SetInTransit(false);
        french.SetRouted(true);
        Assert.Equal(0, control.ApplyOccupation());
        french.SetInTransit(true);
        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(b.German.Country, control.GetController(sector));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneFitCompanyIsEnoughForItsCountry_InEitherOrder(bool routedFirst)
    {
        var b = NewBattle();
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        Place(b, b.French, 0, 3, 2).SetRouted(routedFirst);
        var other = Place(b, b.French, 1, 3, 2);
        other.SetRouted(!routedFirst);

        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(b.French.Country, control.GetController(other.Sector));
    }

    [Fact]
    public void ThreeCountries_KeepAlliedHolder_AndResolveJointEnemyCapture()
    {
        var b = NewBattle();
        var france = b.French.Country!;
        var germany = b.German.Country!;
        var holder = new Country(3, "Détenteur initial");
        b.Battle.Engagement.Province!.ChangeController(holder);
        b.Diplomacy.MakePeace(france, germany);
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);

        // Le pays de plus grand ID se déploie en premier.
        var german = Place(b, b.German, 0, 3, 2);
        Place(b, b.French, 0, 3, 2);
        Assert.Equal(0, control.ApplyOccupation());
        Assert.Same(holder, control.GetController(german.Sector));

        b.Diplomacy.DeclareWar(holder, germany);
        b.Diplomacy.DeclareWar(holder, france);
        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(france, control.GetController(german.Sector));
        Assert.Same(holder, b.Battle.Engagement.Province.Controller);
    }

    [Fact]
    public void DiplomacyAndManpower_AreReadAgainWithoutMovement()
    {
        var b = NewBattle();
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var french = Place(b, b.French, 0, 3, 2);
        var german = Place(b, b.German, 0, 3, 2);
        var sector = french.Sector;
        var personnel = german.Company.Composition!.Manpower;
        Assert.Equal(0, control.ApplyOccupation()); // Case initialement disputée.
        personnel.ApplyCasualties(new PersonnelCasualtyReport(0, personnel.Current, 0));
        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(b.French.Country, control.GetController(sector));

        french.SetInTransit(true);
        Assert.Equal(0, control.ApplyOccupation());
        b.Diplomacy.MakePeace(b.French.Country!, b.German.Country!);
        Assert.Equal(1, personnel.ReturnWoundedToDuty(1));
        Assert.Equal(0, control.ApplyOccupation()); // Un allié seul laisse la case au détenteur absent.
        Assert.Same(b.French.Country, control.GetController(sector));
        b.Diplomacy.DeclareWar(b.French.Country!, b.German.Country!);
        Assert.Equal(1, control.ApplyOccupation());
        Assert.Same(b.German.Country, control.GetController(sector));
    }

    [Fact]
    public void SparsePass_MatchesFullRecalculationAfterEveryChange()
    {
        var b = NewBattle();
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var sectors = b.Battle.Battlefield.Sectors;
        var expected = new Dictionary<BattleSector, Country?>();
        var french = Place(b, b.French, 0, 3, 2, facingDegrees: 0, frontageMeters: 300);
        var german = Place(b, b.German, 0, 4, 2);

        // Référence volontairement complète. Elle relit chaque case et ses
        // compagnies à chaque passe, sans cache, en gardant son propre historique.
        void Check()
        {
            var index = new TacticalSectorPresenceIndex(b.Battle, b.Map);
            int transfers = 0;
            foreach (var sector in sectors)
            {
                var owner = expected.GetValueOrDefault(sector);
                var troops = index.GetCovering(sector)
                    .Where(d => !d.IsInTransit && d.Company.Composition!.Manpower.Current > 0)
                    .ToArray();
                Country Side(CompanyDeployment d) => d.Company.ParentBattalion!.ParentDivision!.Country!;
                bool contested = troops.Any(a => troops.Any(c => b.Diplomacy.AreAtWar(Side(a), Side(c))));
                var claimant = contested ? null : troops.Where(d => !d.IsRouted).Select(Side)
                    .Where(c => owner is null || b.Diplomacy.AreAtWar(c, owner))
                    .OrderBy(c => c.Id).FirstOrDefault();
                if (claimant is null)
                    continue;

                expected[sector] = claimant;
                transfers++;
            }

            Assert.Equal(transfers, control.ApplyOccupation());
            foreach (var sector in sectors)
                Assert.Same(expected.GetValueOrDefault(sector), control.GetController(sector));
        }

        Check();
        Check();
        b.Diplomacy.MakePeace(b.French.Country!, b.German.Country!);
        Check();
        b.Diplomacy.DeclareWar(b.French.Country!, b.German.Country!);
        french.SetRouted(true);
        Check();
        var destination = b.Layout.GetSector(Cell(b.Grid, 6, 4));
        french.ApplyMovementArrival(destination, french.Role);
        b.Map.RelocateCompany(french, destination, b.Layout.GetCellCenter(destination));
        Check();
        french.SetRouted(false);
        Check();
        german.SetInTransit(true);
        Check();
        german.SetInTransit(false);
        Check();
    }

    [Fact]
    public void ForeignSectorWithSameId_IsRejected_AndDefaultIsCapturedOnce()
    {
        var b = NewBattle();
        var other = NewBattle();
        var control = new TacticalSectorOccupationControl(b.Map, b.Diplomacy);
        var sector = b.Layout.GetSector(0);
        var foreign = other.Layout.GetSector(0);
        Assert.Equal(sector.Id, foreign.Id);
        Assert.Throws<InvalidOperationException>(() => control.GetController(foreign));

        b.Battle.Engagement.Province!.ChangeController(b.French.Country);
        Assert.Equal(0, control.ApplyOccupation());
        Assert.Null(control.GetController(sector));
    }
}