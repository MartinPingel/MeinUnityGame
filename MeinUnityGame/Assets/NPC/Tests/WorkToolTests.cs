using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class WorkToolTests
{
    private sealed class Road : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(p == NpcPlace.Home ? 0 :
            p == NpcPlace.Tavern ? 50 : p == NpcPlace.Delivery ? 200 : 100, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Equipment : INpcWorkEquipment
    {
        public readonly WarehouseStock Stock = new WarehouseStock();
        public readonly WorkplaceTools Tools;
        public Equipment(int count = 1, double max = 100, double wear = 100)
        {
            if (count > 0) Stock.Add(WorkplaceTools.GoodsType, count);
            Tools = new WorkplaceTools(Stock, max, wear);
        }
        public bool CanWork => Tools.HasUsableTool;
        public double MinutesUntilBreak => Tools.MinutesUntilBreak;
        public void Wear(double minutes) => Tools.Wear(minutes);
    }
    private sealed class Farm : INpcDeliveryInventory
    {
        public int Food, Delivered;
        public bool Deliveries;
        public bool TryProduceOne() { Food++; return true; }
        public bool HasBatch(int quantity) => Deliveries && Food >= quantity;
        public bool TryPickUp(int quantity) { if (Food < quantity) return false; Food -= quantity; return true; }
        public bool TryDeliver(int quantity) { Delivered += quantity; return true; }
    }
    private static NpcSimulation Create(Equipment equipment, Farm farm = null, SupplySettings supplies = null,
        FatigueSettings fatigue = null) => new NpcSimulation(new Road(),
            new WorkSchedule { startHour = 8, endHour = 17 },
            fatigue ?? new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 },
            supplies ?? new SupplySettings { satiationLossPerHour = 0, hydrationLossPerHour = 0 },
            () => true, () => true, farm,
            farm == null ? null : new WorkDeliverySettings { unitsPerWorkHour = 6, deliveryQuantity = 2 }, equipment);

    [Test]
    public void TravelDoesNotWearAndBreakStopsProductionAtTheExactWorkBoundary()
    {
        var tool = new Equipment(); var farm = new Farm(); var npc = Create(tool, farm);
        npc.AdvanceTo(490, 10);
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(100));
        npc.AdvanceTo(520, 10);
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(50).Within(1e-7));
        npc.AdvanceTo(550, 10);
        Assert.That(tool.Tools.Quantity, Is.Zero);
        Assert.That(tool.Tools.CurrentDurability, Is.Zero);
        Assert.That(farm.Food, Is.EqualTo(6));
        npc.AdvanceTo(800, 10);
        Assert.That(farm.Food, Is.EqualTo(6));
        Assert.That(npc.WorkedMinutes, Is.EqualTo(60).Within(1e-7));
        tool.Stock.Add(WorkplaceTools.GoodsType, 1);
        npc.AdvanceTo(810, 10);
        Assert.That(farm.Food, Is.EqualTo(7));
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(100d * 5 / 6).Within(1e-7));
    }

    [Test]
    public void MissingToolDoesNotBankFreeProductionAndTheScheduleContinues()
    {
        var tool = new Equipment(0); var farm = new Farm(); var npc = Create(tool, farm);
        npc.AdvanceTo(800, 10);
        Assert.That(farm.Food, Is.Zero);
        Assert.That(npc.Position.X, Is.EqualTo(100));
        tool.Stock.Add(WorkplaceTools.GoodsType, 1);
        npc.AdvanceTo(809, 10); Assert.That(farm.Food, Is.Zero);
        npc.AdvanceTo(810, 10); Assert.That(farm.Food, Is.EqualTo(1));
        npc.AdvanceTo(1100, 10);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(npc.Position.X, Is.Zero);
    }

    [Test]
    public void PartialProductionSurvivesBreakButRequiresRemainingRealWork()
    {
        var tool = new Equipment(max: 15); var farm = new Farm(); var npc = Create(tool, farm);
        npc.AdvanceTo(600, 10);
        Assert.That(farm.Food, Is.Zero);
        Assert.That(npc.WorkedMinutes, Is.EqualTo(9).Within(1e-7));
        tool.Stock.Add(WorkplaceTools.GoodsType, 1);
        npc.AdvanceTo(601, 10);
        Assert.That(farm.Food, Is.EqualTo(1));
    }

    [Test]
    public void DeliveryAndDrinkPauseDoNotWearTheBuildingTool()
    {
        var tool = new Equipment(); var farm = new Farm { Deliveries = true }; var npc = Create(tool, farm);
        npc.AdvanceTo(510, 10); // Two units produced, departing with cargo.
        double durability = tool.Tools.CurrentDurability;
        npc.AdvanceTo(530, 10); // Arrived back at work after delivery.
        Assert.That(farm.Delivered, Is.EqualTo(2));
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(durability).Within(1e-7));

        tool = new Equipment();
        npc = Create(tool, supplies: new SupplySettings { initialHydration = 20 + 505d / 15,
            satiationLossPerHour = 0 });
        npc.AdvanceTo(505, 10); durability = tool.Tools.CurrentDurability;
        npc.AdvanceTo(525, 10); // Tavern walk, ten-minute drink, return walk.
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1));
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(durability).Within(1e-7));
    }

    [Test]
    public void WorkplaceWithoutProductionStillWearsToolsOnlyWhileWorking()
    {
        var tool = new Equipment(); var npc = Create(tool);
        npc.AdvanceTo(520, 10);
        Assert.That(tool.Tools.CurrentDurability, Is.EqualTo(50).Within(1e-7));
        npc.AdvanceTo(1100, 10);
        Assert.That(tool.Tools.Quantity, Is.Zero);
        Assert.That(npc.ProducedUnits, Is.Zero);
        Assert.That(npc.Position.X, Is.Zero);
    }

    [Test]
    public void MultipleDaysAndNeedInterruptionsMatchSmallTicks()
    {
        var a = new Equipment(3, wear: 10); var b = new Equipment(3, wear: 10);
        var fa = new Farm { Deliveries = true }; var fb = new Farm { Deliveries = true };
        var jump = Create(a, fa, new SupplySettings(), new FatigueSettings());
        var tick = Create(b, fb, new SupplySettings(), new FatigueSettings());
        const double end = 10 * 1440 + 800;
        jump.AdvanceTo(end, 10);
        for (double t = 0.37; t < end; t += 0.37) tick.AdvanceTo(t, 10);
        tick.AdvanceTo(end, 10);
        Assert.That(jump.WorkedMinutes, Is.EqualTo(tick.WorkedMinutes).Within(1e-5));
        Assert.That(jump.WorkedMinutes, Is.LessThanOrEqualTo(1800.00001));
        Assert.That(a.Tools.Quantity, Is.EqualTo(b.Tools.Quantity));
        Assert.That(a.Tools.CurrentDurability, Is.EqualTo(b.Tools.CurrentDurability).Within(1e-5));
        Assert.That(fa.Food, Is.EqualTo(fb.Food));
        Assert.That(fa.Delivered, Is.EqualTo(fb.Delivered));
        Assert.That(jump.CargoQuantity, Is.EqualTo(tick.CargoQuantity));
        Assert.That(jump.Energy, Is.EqualTo(tick.Energy).Within(1e-5));
        Assert.That(jump.State, Is.EqualTo(tick.State));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-5));
    }
}
