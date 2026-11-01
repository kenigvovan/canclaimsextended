using Atlas.Api;
using Atlas.XUnit;
using canclaimsextended.scenarios.Infrastructure;
using canclaimsextended.src.part.structure;

namespace canclaimsextended.scenarios;

/// <summary>
/// Plots owned by citizens (PlotCommandOwnership): /plot fs and nfs and who may use them (F1),
/// /plot claim with the price typed back and the money it moves (F2, F3), and the ways a plot goes
/// back to the city or to another owner (F5). Section F of TESTING_canclaimsextended.md.
/// </summary>
[Trait("Category", "E2E")]
public class PlotTradeScenarios : CxScenarioBase
{
    /// <summary>The economy account of a player: the mod uses the player's uid.</summary>
    private static string Account(ITestPlayer player) => player.Player.PlayerUID;

    private static bool Owns(ITestPlayer player, Plot plot) => Info(player).Equals(plot.getPlotOwner());

    /// <summary>The mayor puts the city plot up for sale, standing in it.</summary>
    private async Task PutForSale(CityScene s, Plot plot, int price)
    {
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);
        CommandResult fs = await s.Mayor.ExecuteCommand($"{PlotCmd} fs {price}");
        Assert.True(fs.Ok && plot.Price == price, $"precondition: /plot fs {price}: {fs.Message}");
    }

    // ---- F1: putting up for sale -------------------------------------------------------------

    // F1: the mayor sells city land
    [AtlasScenario]
    public async Task ForSale_Should_SetThePrice_When_MayorSellsCityLand()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult fs = await s.Mayor.ExecuteCommand($"{PlotCmd} fs 50");

        Assert.True(fs.Ok, "/plot fs 50: " + fs.Message);
        Assert.Equal(50, plot.Price);
        Assert.True(plot.IsForSale, "the plot is not for sale");
        AssertInvariants();
    }

    // F1: a negative price is refused and the plot stays as it was
    [AtlasScenario]
    public async Task ForSale_Should_BeRefused_When_PriceIsNegative()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);

        CommandResult fs = await s.Mayor.ExecuteCommand($"{PlotCmd} fs -1");

        Assert.False(fs.Ok, "/plot fs -1 went through: " + fs.Message);
        Assert.Equal(-1, plot.Price);
        AssertInvariants();
    }

    // F1: a plain citizen cannot sell the city's land
    [AtlasScenario]
    public async Task ForSale_Should_BeRefused_When_CitizenSellsCityLand()
    {
        CityScene s = await GivenCity("citizen");
        ITestPlayer citizen = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await StandIn(citizen, plot.Shape.Boxes[0]);

        CommandResult fs = await citizen.ExecuteCommand($"{PlotCmd} fs 50");

        Assert.False(fs.Ok, "a citizen put city land up for sale: " + fs.Message);
        Assert.Equal(-1, plot.Price);
    }

    // F1: nor the plot of another citizen
    [AtlasScenario]
    public async Task ForSale_Should_BeRefused_When_CitizenSellsSomeoneElsesPlot()
    {
        CityScene s = await GivenCity("owner", "other");
        ITestPlayer owner = s.Citizens[0], other = s.Citizens[1];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await StandIn(other, plot.Shape.Boxes[0]);

        CommandResult fs = await other.ExecuteCommand($"{PlotCmd} fs 50");

        Assert.False(fs.Ok, "a citizen put someone else's plot up for sale: " + fs.Message);
        Assert.Equal(-1, plot.Price);
        Assert.True(Owns(owner, plot), "the plot changed hands");
    }

    // F1, F4: /plot nfs takes the plot off sale, and then nobody can buy it
    [AtlasScenario]
    public async Task NotForSale_Should_StopTheSale()
    {
        // Arrange
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 50);

        // Act
        CommandResult nfs = await s.Mayor.ExecuteCommand($"{PlotCmd} nfs");
        await StandIn(buyer, plot.Shape.Boxes[0]);
        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim 50");

        // Assert
        Assert.True(nfs.Ok, "/plot nfs: " + nfs.Message);
        Assert.Equal(-1, plot.Price);
        Assert.False(claim.Ok, "a plot taken off sale was bought: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        AssertInvariants();
    }

    // F1: /plot nfs on a plot that is not for sale is an error
    [AtlasScenario]
    public async Task NotForSale_Should_BeRefused_When_PlotIsNotForSale()
    {
        CityScene s = await GivenCity();
        await GivenPlot(s);

        CommandResult nfs = await s.Mayor.ExecuteCommand($"{PlotCmd} nfs");

        Assert.False(nfs.Ok, "/plot nfs on a plot not for sale: " + nfs.Message);
    }

    // ---- F2: buying --------------------------------------------------------------------------

    // F2: without the price the command only shows it
    [AtlasScenario]
    public async Task Claim_Should_OnlyShowThePrice_When_PriceIsNotTyped()
    {
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 50);
        await StandIn(buyer, plot.Shape.Boxes[0]);

        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim");

        Assert.False(claim.Ok, "/plot claim without a price bought the plot: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.Equal(50, plot.Price);
    }

    // F2: a typed price other than the plot's is refused - the buyer never pays a price changed after they looked
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_TypedPriceIsWrong()
    {
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 50);
        await StandIn(buyer, plot.Shape.Boxes[0]);

        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim 49");

        Assert.False(claim.Ok, "/plot claim 49 bought a plot priced 50: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.Equal(50, plot.Price);
        AssertInvariants();
    }

    // F2: a player of no city cannot buy
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_BuyerIsNoCitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 0);
        await StandIn(stranger, plot.Shape.Boxes[0]);

        CommandResult claim = await stranger.ExecuteCommand($"{PlotCmd} claim 0");

        Assert.False(claim.Ok, "a stranger bought a city plot: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.Empty(Info(stranger).PlayerPlots);
        AssertInvariants();
    }

    // F2: the mayor buying the city's own land would only move coins in a circle
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_MayorBuysCityLand()
    {
        CityScene s = await GivenCity();
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 0);

        CommandResult claim = await s.Mayor.ExecuteCommand($"{PlotCmd} claim 0");

        Assert.False(claim.Ok, "the mayor bought city land: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        AssertInvariants();
    }

    // F2: an owner who put their plot up for sale cannot buy it from themselves
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_PlotIsAlreadyYours()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await owner.ExecuteCommand($"{PlotCmd} fs 10");
        Assert.Equal(10, plot.Price);

        CommandResult claim = await owner.ExecuteCommand($"{PlotCmd} claim 10");

        Assert.False(claim.Ok, "the owner bought their own plot: " + claim.Message);
        Assert.True(Owns(owner, plot), "the owner lost the plot");
        AssertInvariants();
    }

    // F2: not enough money - nothing moves and the plot stays the city's
    [AtlasScenario]
    public async Task Claim_Should_BeRefused_When_BuyerHasNotEnoughMoney()
    {
        // Arrange: price 40, the buyer has 39
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 40);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(buyer), 39);
        await StandIn(buyer, plot.Shape.Boxes[0]);

        // Act
        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim 40");

        // Assert
        Assert.False(claim.Ok, "a buyer with 39 bought a plot for 40: " + claim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.Equal(40, plot.Price);
        Assert.Equal(39m, money.GetBalance(Account(buyer)));
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.DoesNotContain(money.Log, line => line.StartsWith("Transfer"));
        AssertInvariants();
    }

    // F2: a purchase of city land moves the price from the buyer to the city treasury
    [AtlasScenario]
    public async Task Claim_Should_PayTheCityTreasury_When_CitizenBuysCityLand()
    {
        // Arrange
        CityScene s = await GivenCity("buyer");
        ITestPlayer buyer = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await PutForSale(s, plot, 40);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(buyer), 100);
        decimal totalBefore = money.Total();
        await StandIn(buyer, plot.Shape.Boxes[0]);

        // Act
        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim 40");

        // Assert
        Assert.True(claim.Ok, "/plot claim 40: " + claim.Message);
        Assert.True(Owns(buyer, plot), "the buyer does not own the plot");
        Assert.Contains(plot, Info(buyer).PlayerPlots);
        Assert.Equal(-1, plot.Price);
        Assert.Equal(60m, money.GetBalance(Account(buyer)));
        Assert.Equal(40m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Contains($"Transfer {Account(buyer)}->{s.City.MoneyAccountName} 40: Success", money.Log);
        Assert.Equal(totalBefore, money.Total());
        AssertInvariants();
    }

    // ---- F3: resale --------------------------------------------------------------------------

    // F3: a citizen's plot bought by another citizen pays the seller, not the city
    [AtlasScenario]
    public async Task Resale_Should_PayTheSeller()
    {
        // Arrange: the seller owns the plot and puts it up for 80
        CityScene s = await GivenCity("seller", "buyer");
        ITestPlayer seller = s.Citizens[0], buyer = s.Citizens[1];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, seller);
        FakeMoneyProvider money = UseFakeEconomy();
        money.SetBalance(Account(buyer), 100);
        CommandResult fs = await seller.ExecuteCommand($"{PlotCmd} fs 80");
        Assert.True(fs.Ok && plot.Price == 80, "precondition: the owner put the plot up for 80: " + fs.Message);
        await StandIn(buyer, plot.Shape.Boxes[0]);

        // Act
        CommandResult claim = await buyer.ExecuteCommand($"{PlotCmd} claim 80");

        // Assert
        Assert.True(claim.Ok, "/plot claim 80: " + claim.Message);
        Assert.True(Owns(buyer, plot), "the buyer does not own the plot");
        Assert.DoesNotContain(plot, Info(seller).PlayerPlots);
        Assert.Equal(20m, money.GetBalance(Account(buyer)));
        Assert.Equal(80m, money.GetBalance(Account(seller)));
        Assert.Equal(0m, money.GetBalance(s.City.MoneyAccountName));
        Assert.Contains($"Transfer {Account(buyer)}->{Account(seller)} 80: Success", money.Log);
        AssertInvariants();
    }

    // F3: the owner may sell their own plot - the mayor's right is not needed
    [AtlasScenario]
    public async Task ForSale_Should_SetThePrice_When_OwnerSellsOwnPlot()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);

        CommandResult fs = await owner.ExecuteCommand($"{PlotCmd} fs 80");

        Assert.True(fs.Ok, "/plot fs 80 by the owner: " + fs.Message);
        Assert.Equal(80, plot.Price);
        Assert.True(Owns(owner, plot), "putting up for sale changed the owner");
        AssertInvariants();
    }

    // ---- F5: giving up and taking back ---------------------------------------------------------

    // F5: the owner gives the plot back to the city
    [AtlasScenario]
    public async Task Unclaim_Should_ReturnPlotToCity_When_OwnerGivesItUp()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);

        CommandResult unclaim = await owner.ExecuteCommand($"{PlotCmd} unclaim");

        Assert.True(unclaim.Ok, "/plot unclaim: " + unclaim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.DoesNotContain(plot, Info(owner).PlayerPlots);
        Assert.True(s.City.Equals(plot.getCity()), "the plot left the city");
        AssertInvariants();
    }

    // F5: the mayor takes a citizen's plot back
    [AtlasScenario]
    public async Task Unclaim_Should_TakePlotBack_When_MayorDoesIt()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);

        CommandResult unclaim = await s.Mayor.ExecuteCommand($"{PlotCmd} unclaim");

        Assert.True(unclaim.Ok, "/plot unclaim by the mayor: " + unclaim.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.DoesNotContain(plot, Info(owner).PlayerPlots);
        AssertInvariants();
    }

    // F5: another citizen cannot take someone's plot from them
    [AtlasScenario]
    public async Task Unclaim_Should_BeRefused_When_CitizenIsNeitherOwnerNorMayor()
    {
        CityScene s = await GivenCity("owner", "other");
        ITestPlayer owner = s.Citizens[0], other = s.Citizens[1];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        await StandIn(other, plot.Shape.Boxes[0]);

        CommandResult unclaim = await other.ExecuteCommand($"{PlotCmd} unclaim");

        Assert.False(unclaim.Ok, "another citizen took the plot from its owner: " + unclaim.Message);
        Assert.True(Owns(owner, plot), "the owner lost the plot");
        AssertInvariants();
    }

    // F5: /cadmin plot takeback is /plot unclaim as the city's mayor
    [AtlasScenario]
    public async Task AdminTakeBack_Should_ReturnPlotToCity()
    {
        // Arrange: an admin of no city stands in a citizen's plot
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, owner);
        ITestPlayer admin = await GivenStranger(s, "admin");
        AllowAdminCommands(admin);
        await StandIn(admin, plot.Shape.Boxes[0]);

        // Act
        CommandResult takeback = await admin.ExecuteCommand($"{CadminCmd} plot takeback");

        // Assert
        Assert.True(takeback.Ok, "/cadmin plot takeback: " + takeback.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.DoesNotContain(plot, Info(owner).PlayerPlots);
        Assert.False(s.City.isMayor(Info(admin)), "the admin stayed mayor after the command");
        AssertInvariants();
    }

    // F5: /cadmin plot owner gives the plot to a citizen without a purchase
    [AtlasScenario]
    public async Task AdminOwner_Should_GiveThePlot_When_TargetIsCitizen()
    {
        CityScene s = await GivenCity("owner");
        ITestPlayer owner = s.Citizens[0];
        Plot plot = await GivenPlot(s);
        AllowAdminCommands(s.Mayor);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} plot owner {Name(owner)}");

        Assert.True(set.Ok, "/cadmin plot owner: " + set.Message);
        Assert.True(Owns(owner, plot), "the citizen does not own the plot");
        Assert.Contains(plot, Info(owner).PlayerPlots);
        Assert.Equal(-1, plot.Price);
        AssertInvariants();
    }

    // F5: the previous owner loses the plot to the new one
    [AtlasScenario]
    public async Task AdminOwner_Should_TakePlotFromPreviousOwner()
    {
        CityScene s = await GivenCity("first", "second");
        ITestPlayer first = s.Citizens[0], second = s.Citizens[1];
        Plot plot = await GivenPlot(s);
        await GivenOwnedPlot(s, plot, first);
        AllowAdminCommands(s.Mayor);
        await StandIn(s.Mayor, plot.Shape.Boxes[0]);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} plot owner {Name(second)}");

        Assert.True(set.Ok, "/cadmin plot owner: " + set.Message);
        Assert.True(Owns(second, plot), "the new owner does not own the plot");
        Assert.DoesNotContain(plot, Info(first).PlayerPlots);
        AssertInvariants();
    }

    // F5: an owner must be a citizen of the plot's city
    [AtlasScenario]
    public async Task AdminOwner_Should_BeRefused_When_TargetIsNoCitizen()
    {
        CityScene s = await GivenCity();
        ITestPlayer stranger = await GivenStranger(s);
        Plot plot = await GivenPlot(s);
        AllowAdminCommands(s.Mayor);

        CommandResult set = await s.Mayor.ExecuteCommand($"{CadminCmd} plot owner {Name(stranger)}");

        Assert.False(set.Ok, "a player of no city became owner: " + set.Message);
        Assert.Null(plot.getPlotOwner());
        Assert.Empty(Info(stranger).PlayerPlots);
        AssertInvariants();
    }
}
