using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class ToolBootstrapTests
{
    private sealed class Road : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(
            p == NpcPlace.Work ? 100 : p == NpcPlace.ToolPickup ? 200 :
            p == NpcPlace.Tavern ? 50 : 0, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Equipment : INpcToolSupply
    {
        public readonly WarehouseStock Stock = new WarehouseStock(), Market = new WarehouseStock();
        public readonly WorkplaceTools Tools;
        public Equipment() { Tools = new WorkplaceTools(Stock, 100, 100); }
        public bool CanWork => Tools.HasUsableTool || !Tools.HasEverHadTool;
        public double MinutesUntilBreak => Tools.MinutesUntilBreak;
        public void Wear(double minutes) => Tools.Wear(minutes);
        public bool NeedsDelivery => !Tools.HasUsableTool && Market.Has("Werkzeuge", 1);
        public bool TryCollect() => NeedsDelivery && Market.TryRemove("Werkzeuge", 1);
        public bool TryDeposit() { Stock.Add("Werkzeuge", 1); return true; }
    }
    private sealed class Production : INpcDeliveryInventory, INpcProductionGate
    {
        public bool Inputs = true;
        public bool CanProduce => Inputs;
        public bool TryProduceOne() => Inputs;
        public bool HasBatch(int n) => false;
        public bool TryPickUp(int n) => false;
        public bool TryDeliver(int n) => false;
    }
    private static NpcSimulation Create(Equipment equipment, Production job, bool thirst = false) =>
        new NpcSimulation(new Road(), new WorkSchedule { startHour = 8, endHour = 17 },
            new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 },
            new SupplySettings { initialHydration = thirst ? 20 + 505d * 4 / 60 : 100,
                hydrationLossPerHour = thirst ? 4 : 0, satiationLossPerHour = 0 },
            () => true, () => true, job,
            new WorkDeliverySettings { unitsPerWorkHour = 6, deliveryQuantity = 10 }, equipment);

    [Test]
    public void BootstrapEndsPermanentlyAfterFirstToolAndBreakPausesProduction()
    {
        var equipment = new Equipment(); var job = new Production(); var npc = Create(equipment, job);
        npc.AdvanceTo(520, 10);
        Assert.That(npc.ProducedUnits, Is.EqualTo(3));
        Assert.That(equipment.Tools.HasEverHadTool, Is.False);
        equipment.Stock.Add("Werkzeuge", 1);
        npc.AdvanceTo(580, 10);
        Assert.That(equipment.Tools.Quantity, Is.Zero);
        Assert.That(equipment.Tools.HasEverHadTool, Is.True);
        double produced = npc.ProducedUnits;
        npc.AdvanceTo(700, 10);
        Assert.That(npc.ProducedUnits, Is.EqualTo(produced));
        Assert.That(npc.WorkBlockedByTool, Is.True);
    }

    [Test]
    public void RealMarketWithdrawalTravelsAndSurvivesThirstInterruption()
    {
        var equipment = new Equipment(); equipment.Market.Add("Werkzeuge", 1);
        var npc = Create(equipment, new Production(), true);
        npc.AdvanceTo(499, 10);
        Assert.That(equipment.Market.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        npc.AdvanceTo(500, 10);
        Assert.That(equipment.Market.GetQuantity("Werkzeuge"), Is.Zero);
        Assert.That(npc.ToolCargoQuantity, Is.EqualTo(1));
        Assert.That(equipment.Tools.Quantity, Is.Zero);
        npc.AdvanceTo(520, 10);
        Assert.That(npc.ToolCargoQuantity, Is.EqualTo(1));
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        npc.AdvanceTo(540, 10);
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1));
        Assert.That(npc.ToolCargoQuantity, Is.Zero);
        Assert.That(equipment.Tools.Quantity, Is.EqualTo(1));
        Assert.That(equipment.Tools.HasEverHadTool, Is.True);
    }

    [Test]
    public void MissingRawMaterialDoesNotWearTool()
    {
        var equipment = new Equipment(); equipment.Stock.Add("Werkzeuge", 1);
        var npc = Create(equipment, new Production { Inputs = false });
        npc.AdvanceTo(1000, 10);
        Assert.That(npc.ProducedUnits, Is.Zero);
        Assert.That(equipment.Tools.CurrentDurability, Is.EqualTo(100));
    }

    [Test]
    public void SkippingDaysMatchesTicksIncludingToolPickupBreakAndNeeds()
    {
        var a = new Equipment(); var b = new Equipment();
        a.Market.Add("Werkzeuge", 4); b.Market.Add("Werkzeuge", 4);
        var jump = Create(a, new Production(), true);
        var tick = Create(b, new Production(), true);
        jump.AdvanceTo(4000, 10);
        for (double t = 0.37; t < 4000; t += 0.37) tick.AdvanceTo(t, 10);
        tick.AdvanceTo(4000, 10);
        Assert.That(jump.ProducedUnits, Is.EqualTo(tick.ProducedUnits));
        Assert.That(jump.ToolCargoQuantity, Is.EqualTo(tick.ToolCargoQuantity));
        Assert.That(a.Tools.Quantity, Is.EqualTo(b.Tools.Quantity));
        Assert.That(a.Market.GetQuantity("Werkzeuge"), Is.EqualTo(b.Market.GetQuantity("Werkzeuge")));
        Assert.That(a.Tools.CurrentDurability, Is.EqualTo(b.Tools.CurrentDurability).Within(1e-6));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-6));
    }
}
