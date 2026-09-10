using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class SmithFlowTests
{
    private sealed class Road : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace place) => new NpcPoint(
            place == NpcPlace.Pickup ? 300d :
            place == NpcPlace.Work || place == NpcPlace.Delivery ? 100d :
            place == NpcPlace.Tavern ? 50d : 0d, 0d, 0d);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }

    private sealed class Stores : INpcDeliveryInventory, INpcProductionGate
    {
        public readonly WarehouseStock Smelter = new WarehouseStock();
        public readonly WarehouseStock Smith = new WarehouseStock();
        public bool AcceptDelivery = true;
        public Stores(int iron = 20) { if (iron > 0) Smelter.Add("Eisen", iron); }
        public bool CanProduce => Smith.CanConvert("Eisen", 2, "Werkzeuge", 1);
        public bool HasBatch(int quantity) => !Smith.Has("Eisen", 2) && Smelter.Has("Eisen", quantity);
        public bool TryPickUp(int quantity) => Smelter.TryRemove("Eisen", quantity);
        public bool TryDeliver(int quantity)
        {
            if (!AcceptDelivery) return false;
            Smith.Add("Eisen", quantity);
            return true;
        }
        public bool TryProduceOne() => Smith.TryConvert("Eisen", 2, "Werkzeuge", 1);
    }

    private static NpcSimulation Create(Stores stores, SupplySettings supplies = null, FatigueSettings fatigue = null)
    {
        return new NpcSimulation(new Road(), new WorkSchedule { startHour = 8, endHour = 18 },
            fatigue ?? new FatigueSettings { initialFatigue = 0d, gainPerAwakeHour = 0.1d },
            supplies ?? new SupplySettings { satiationLossPerHour = 0d, hydrationLossPerHour = 0d },
            () => true, () => true, stores,
            new WorkDeliverySettings { deliveryQuantity = 10, unitsPerWorkHour = 2d });
    }

    private static void Conserved(Stores stores, NpcSimulation npc, int initial = 20)
    {
        Assert.That(stores.Smelter.GetQuantity("Eisen") + stores.Smith.GetQuantity("Eisen") +
            npc.CargoQuantity + 2 * stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(initial));
    }

    [Test]
    public void PhysicalPickupAndArrivalPrecedeTheFirstThirtyMinutesOfProcessing()
    {
        var stores = new Stores();
        var npc = Create(stores);
        npc.AdvanceTo(509d, 10d);
        Assert.That(stores.Smelter.GetQuantity("Eisen"), Is.EqualTo(20));
        Assert.That(npc.CargoQuantity, Is.Zero);
        npc.AdvanceTo(510d, 10d);
        Assert.That(stores.Smelter.GetQuantity("Eisen"), Is.EqualTo(10));
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        npc.AdvanceTo(520d, 10d);
        Assert.That(npc.Position.X, Is.EqualTo(200d).Within(1e-7));
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.Zero);
        npc.AdvanceTo(530d, 10d);
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.EqualTo(10));
        Assert.That(npc.CargoQuantity, Is.Zero);
        npc.AdvanceTo(559d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        npc.AdvanceTo(560d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.EqualTo(8));
        npc.AdvanceTo(1000d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(10));
        Conserved(stores, npc);
    }

    [Test]
    public void TimeWithoutIronDoesNotAccumulateFreeProcessingProgress()
    {
        var stores = new Stores(0);
        var npc = Create(stores);
        npc.AdvanceTo(600d, 10d);
        stores.Smith.Add("Eisen", 2);
        npc.AdvanceTo(629d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        npc.AdvanceTo(630d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        Conserved(stores, npc, 2);
    }

    [Test]
    public void ThirstDetourKeepsCargoAndDoesNotProcessItOnTheRoad()
    {
        var stores = new Stores();
        var npc = Create(stores, new SupplySettings { initialHydration = 20d + 515d / 15d });
        npc.AdvanceTo(516d, 10d);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        npc.AdvanceTo(540d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.Zero);
        npc.AdvanceTo(550d, 10d);
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.EqualTo(10));
        npc.AdvanceTo(580d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        Conserved(stores, npc);
    }

    [Test]
    public void ADrinkPausePreservesPartialWorkWithoutProcessingInTheTavern()
    {
        var stores = new Stores(0);
        stores.Smith.Add("Eisen", 2);
        var npc = Create(stores, new SupplySettings { initialHydration = 20d + 505d / 15d });
        npc.AdvanceTo(505d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        npc.AdvanceTo(518d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.EqualTo(2));
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        npc.AdvanceTo(539d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        npc.AdvanceTo(540d, 10d);
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(1));
        Conserved(stores, npc, 2);
    }

    [Test]
    public void SleepDetourKeepsCargoAndNoProcessingHappensAfterShiftEnd()
    {
        var stores = new Stores();
        var npc = Create(stores, fatigue: new FatigueSettings {
            initialFatigue = 0d, gainPerAwakeHour = 80d * 60d / 515d });
        npc.AdvanceTo(600d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        npc.AdvanceTo(1100d, 10d);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.EqualTo(10));
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        Conserved(stores, npc);
    }

    [Test]
    public void LateDeliveryReturnsHomeAndRejectedDeliveryRetainsTheFullLoad()
    {
        var stores = new Stores(0);
        var npc = Create(stores);
        npc.AdvanceTo(1050d, 10d);
        stores.Smelter.Add("Eisen", 10);
        npc.AdvanceTo(1070d, 10d);
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        stores.AcceptDelivery = false;
        npc.AdvanceTo(1095d, 10d);
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        Assert.That(stores.Smith.GetQuantity("Eisen"), Is.Zero);
        stores.AcceptDelivery = true;
        npc.AdvanceTo(1096d, 10d);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(stores.Smith.GetQuantity("Werkzeuge"), Is.Zero);
        Conserved(stores, npc, 10);
    }

    [Test]
    public void LongWaitMatchesTicksAndConservesIronIncludingToolsAndCargo()
    {
        var jumpedStores = new Stores();
        var tickingStores = new Stores();
        var jumped = Create(jumpedStores, new SupplySettings(), new FatigueSettings());
        var ticking = Create(tickingStores, new SupplySettings(), new FatigueSettings());
        const double end = 4d * 1440d + 550.25d;
        jumped.AdvanceTo(end, 10d);
        for (double t = 0.37d; t < end; t += 0.37d)
        {
            ticking.AdvanceTo(t, 10d);
            Conserved(tickingStores, ticking);
        }
        ticking.AdvanceTo(end, 10d);
        Conserved(jumpedStores, jumped);
        Assert.That(jumpedStores.Smith.GetQuantity("Werkzeuge"), Is.EqualTo(tickingStores.Smith.GetQuantity("Werkzeuge")));
        Assert.That(jumped.CargoQuantity, Is.EqualTo(ticking.CargoQuantity));
        Assert.That(jumped.State, Is.EqualTo(ticking.State));
        Assert.That(jumped.Position.X, Is.EqualTo(ticking.Position.X).Within(1e-5));
        Assert.That(jumped.Energy, Is.EqualTo(ticking.Energy).Within(1e-5));
        Assert.That(jumped.WorkedMinutes, Is.EqualTo(ticking.WorkedMinutes).Within(1e-5));
    }
}
