using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class WaterSupplyTests
{
    private sealed class Road : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(p == NpcPlace.Home ? 0 :
            p == NpcPlace.Pickup || p == NpcPlace.Well ? 300 : 100, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Water : INpcDeliveryInventory, INpcProductionGate, INpcWaterSupply
    {
        public readonly WarehouseStock Stock = new WarehouseStock();
        public int Drawn;
        public bool Accept = true;
        public bool IsDrinkStockEmpty => Stock.GetQuantity("Getränke") == 0;
        public bool CanProduce => false;
        public bool TryProduceOne() { Assert.Fail("Water must never be produced at work."); return false; }
        public bool HasBatch(int q) => Stock.GetQuantity("Getränke") < 20;
        public bool TryPickUp(int q) { if (!HasBatch(q)) return false; Drawn += q; return true; }
        public bool TryDeliver(int q) { if (!Accept) return false; Stock.Add("Getränke", q); return true; }
    }
    private static NpcSimulation Create(Water water, SupplySettings supplies = null, FatigueSettings energy = null) =>
        new NpcSimulation(new Road(), new WorkSchedule { startHour = 10, endHour = 23 },
            energy ?? new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 },
            supplies ?? new SupplySettings { satiationLossPerHour = 0, hydrationLossPerHour = 0 },
            () => true, () => water.Stock.TryRemove("Getränke", 1), water,
            new WorkDeliverySettings { deliveryQuantity = 40, unitsPerWorkHour = 1 });

    [Test]
    public void StartsAtTavernAndCreditsOnlyAfterTheReturnJourney()
    {
        var water = new Water(); var npc = Create(water);
        npc.AdvanceTo(610, 10);
        Assert.That(npc.Position.X, Is.EqualTo(100));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Pickup));
        npc.AdvanceTo(629, 10);
        Assert.That(water.Drawn, Is.Zero);
        npc.AdvanceTo(630, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.Zero);
        npc.AdvanceTo(649, 10);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.Zero);
        npc.AdvanceTo(650, 10);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(40));
        Assert.That(npc.CargoQuantity, Is.Zero);
        npc.AdvanceTo(1300, 10);
        Assert.That(water.Drawn, Is.EqualTo(40));
        Assert.That(npc.ProducedUnits, Is.Zero);
    }

    [Test]
    public void ThresholdIsStrictAndNoPickupStartsOutsideShift()
    {
        var water = new Water(); water.Stock.Add("Getränke", 20); var npc = Create(water);
        npc.AdvanceTo(1380, 10);
        Assert.That(water.Drawn, Is.Zero);
        water.Stock.TryRemove("Getränke", 1);
        npc.AdvanceTo(2000, 10); // Next shift begins at 2040.
        Assert.That(water.Drawn, Is.Zero);
        npc.AdvanceTo(2090, 10);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(59));
    }

    [Test]
    public void ThirstWithAnEmptyTavernFetchesWaterAndUnloadsBeforeDrinking()
    {
        var water = new Water(); var npc = Create(water, new SupplySettings {
            initialHydration = 20, satiationLossPerHour = 0, hydrationLossPerHour = 0 });
        npc.AdvanceTo(649, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        Assert.That(npc.DrinksCompleted, Is.Zero);
        npc.AdvanceTo(660, 10);
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1));
        Assert.That(npc.Hydration, Is.EqualTo(100));
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(39));
        Assert.That(water.Drawn, Is.EqualTo(40));
    }

    [Test]
    public void SleepInterruptionKeepsCargoAndFailedDepositDoesNotLoseIt()
    {
        var water = new Water(); var npc = Create(water, energy: new FatigueSettings {
            initialFatigue = 0, gainPerAwakeHour = 80d * 60 / 635 });
        npc.AdvanceTo(700, 10);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.Zero);
        water.Accept = false;
        npc.AdvanceTo(1250, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        water.Accept = true;
        npc.AdvanceTo(1251, 10);
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(40));
        Assert.That(water.Drawn, Is.EqualTo(40));
    }

    [Test]
    public void HungerDetourUnloadsAtTavernAndResumesWorkAfterEating()
    {
        var water = new Water(); var npc = Create(water, new SupplySettings {
            initialSatiation = 20 + 635d / 30d, satiationLossPerHour = 2,
            hydrationLossPerHour = 0 });
        npc.AdvanceTo(636, 10);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        npc.AdvanceTo(650, 10);
        Assert.That(npc.State, Is.EqualTo(NpcState.Eating));
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(40));
        npc.AdvanceTo(680, 10);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        Assert.That(npc.MealsCompleted, Is.EqualTo(1));
    }

    [Test]
    public void LateLoadedTripFinishesThenReturnsHome()
    {
        var water = new Water(); water.Stock.Add("Getränke", 20); var npc = Create(water);
        npc.AdvanceTo(1355, 10);
        water.Stock.TryRemove("Getränke", 1);
        npc.AdvanceTo(1375, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(40));
        npc.AdvanceTo(1395, 10);
        Assert.That(water.Stock.GetQuantity("Getränke"), Is.EqualTo(59));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
    }

    [Test]
    public void MultiDayWaitMatchesTicksAndEveryDrawnUnitIsAccountedFor()
    {
        var a = new Water(); var b = new Water();
        var jump = Create(a, new SupplySettings(), new FatigueSettings());
        var tick = Create(b, new SupplySettings(), new FatigueSettings());
        const double end = 1440 * 5 + 700;
        jump.AdvanceTo(end, 10);
        for (double t = 0.37; t < end; t += 0.37) tick.AdvanceTo(t, 10);
        tick.AdvanceTo(end, 10);
        Assert.That(a.Drawn, Is.EqualTo(a.Stock.GetQuantity("Getränke") + jump.CargoQuantity + jump.DrinksCompleted));
        Assert.That(b.Drawn, Is.EqualTo(b.Stock.GetQuantity("Getränke") + tick.CargoQuantity + tick.DrinksCompleted));
        Assert.That(a.Drawn, Is.EqualTo(b.Drawn));
        Assert.That(jump.State, Is.EqualTo(tick.State));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-5));
        Assert.That(jump.Hydration, Is.EqualTo(tick.Hydration).Within(1e-5));
        Assert.That(jump.Energy, Is.EqualTo(tick.Energy).Within(1e-5));
    }
}
