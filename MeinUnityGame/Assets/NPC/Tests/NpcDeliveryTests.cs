using NUnit.Framework;
using Village.Npc;

public sealed class NpcDeliveryTests
{
    private sealed class Road : INpcNavigation
    {
        public double Pickup = 100d;
        public NpcPoint GetPlace(NpcPlace place) => new NpcPoint(
            place == NpcPlace.Pickup ? Pickup :
            place == NpcPlace.Work ? 100d : place == NpcPlace.Delivery ? 200d :
            place == NpcPlace.Tavern ? 50d : 0d, 0d, 0d);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination) =>
            new[] { from, GetPlace(destination) };
    }

    private sealed class Stores : INpcDeliveryInventory
    {
        public int Farm, Market, Produced;
        public bool AcceptDelivery = true;
        public bool TryProduceOne() { Farm++; Produced++; return true; }
        public bool HasBatch(int quantity) => Farm >= quantity;
        public bool TryPickUp(int quantity)
        {
            if (Farm < quantity) return false;
            Farm -= quantity;
            return true;
        }
        public bool TryDeliver(int quantity)
        {
            if (!AcceptDelivery) return false;
            Market += quantity;
            return true;
        }
    }

    private static NpcSimulation Create(Stores stores, SupplySettings supplies = null,
        FatigueSettings fatigue = null, double rate = 6d, INpcNavigation road = null)
    {
        return new NpcSimulation(road ?? new Road(), new WorkSchedule(),
            fatigue ?? new FatigueSettings { initialFatigue = 0d, gainPerAwakeHour = 0.1d },
            supplies ?? new SupplySettings { satiationLossPerHour = 0d, hydrationLossPerHour = 0d },
            () => true, () => true, stores,
            new WorkDeliverySettings { unitsPerWorkHour = rate, deliveryQuantity = 3 });
    }

    private static void Conserved(Stores stores, NpcSimulation npc)
    {
        Assert.That(stores.Farm + stores.Market + npc.CargoQuantity, Is.EqualTo(stores.Produced));
        Assert.That(stores.Farm, Is.GreaterThanOrEqualTo(0));
        Assert.That(stores.Market, Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void ProductionRequiresWorkAndCargoIsDepositedOnlyAfterPhysicalTravel()
    {
        var stores = new Stores();
        var npc = Create(stores);
        npc.AdvanceTo(489d, 10d);
        Assert.That(stores.Produced, Is.Zero); // Morning trip has not arrived yet.
        npc.AdvanceTo(519d, 10d);
        Assert.That(stores.Farm, Is.EqualTo(2));
        npc.AdvanceTo(520d, 10d);
        Assert.That(npc.CargoQuantity, Is.EqualTo(3));
        Assert.That(stores.Farm, Is.Zero);
        Assert.That(stores.Market, Is.Zero);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDeliver));
        npc.AdvanceTo(525d, 10d);
        Assert.That(npc.Position.X, Is.EqualTo(150d).Within(1e-7));
        Assert.That(stores.Market, Is.Zero);
        npc.AdvanceTo(530d, 10d);
        Assert.That(stores.Market, Is.EqualTo(3));
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToWork));
        Assert.That(stores.Produced, Is.EqualTo(3));
        npc.AdvanceTo(549d, 10d);
        Assert.That(stores.Produced, Is.EqualTo(3)); // Return trip produces nothing either.
        Conserved(stores, npc);
    }

    [Test]
    public void FarmStockIsWithdrawnOnlyAtItsPhysicalPickupPoint()
    {
        var stores = new Stores();
        var npc = Create(stores, road: new Road { Pickup = 125d });
        npc.AdvanceTo(520d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToCollect));
        Assert.That(stores.Farm, Is.EqualTo(3));
        Assert.That(npc.CargoQuantity, Is.Zero);
        npc.AdvanceTo(522d, 10d);
        Assert.That(stores.Farm, Is.EqualTo(3));
        npc.AdvanceTo(522.5d, 10d);
        Assert.That(npc.Position.X, Is.EqualTo(125d).Within(1e-7));
        Assert.That(stores.Farm, Is.Zero);
        Assert.That(npc.CargoQuantity, Is.EqualTo(3));
        npc.AdvanceTo(530d, 10d);
        Assert.That(stores.Market, Is.EqualTo(3));
        Conserved(stores, npc);
    }

    [Test]
    public void ThirstInterruptsDeliveryAndCargoSurvivesTheTavernVisit()
    {
        var stores = new Stores();
        var npc = Create(stores, new SupplySettings { initialHydration = 55d });
        npc.AdvanceTo(525d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        Assert.That(npc.CargoQuantity, Is.EqualTo(3));
        npc.AdvanceTo(540d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        Assert.That(stores.Market, Is.Zero);
        Assert.That(stores.Produced, Is.EqualTo(3));
        npc.AdvanceTo(560d, 10d);
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1d));
        Assert.That(stores.Market, Is.EqualTo(3));
        Conserved(stores, npc);
    }

    [Test]
    public void SleepInterruptsDeliveryAndAfterLateArrivalHansReturnsHome()
    {
        var stores = new Stores();
        var npc = Create(stores, fatigue: new FatigueSettings {
            initialFatigue = 0d, gainPerAwakeHour = 80d * 60d / 525d });
        npc.AdvanceTo(600d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.CargoQuantity, Is.EqualTo(3));
        Assert.That(stores.Produced, Is.EqualTo(3));
        npc.AdvanceTo(1100d, 10d);
        Assert.That(npc.IsWorkTime, Is.False);
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(stores.Market, Is.EqualTo(3));
        Assert.That(npc.CargoQuantity, Is.Zero);
        Conserved(stores, npc);
    }

    [Test]
    public void DestinationRejectionKeepsCargoAndSuccessfulRetryDepositsExactlyOnce()
    {
        var stores = new Stores { AcceptDelivery = false };
        var npc = Create(stores);
        npc.AdvanceTo(540d, 10d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Delivering));
        Assert.That(npc.CargoQuantity, Is.EqualTo(3));
        Assert.That(stores.Market, Is.Zero);
        stores.AcceptDelivery = true;
        npc.AdvanceTo(541d, 10d);
        npc.AdvanceTo(541d, 10d);
        Assert.That(stores.Market, Is.EqualTo(3));
        Assert.That(npc.CargoQuantity, Is.Zero);
        Conserved(stores, npc);
    }

    [Test]
    public void MultipleDayWaitMatchesTicksWithNeedsAndConservesEveryProducedUnit()
    {
        var jumpStores = new Stores();
        var tickStores = new Stores();
        var jump = Create(jumpStores, new SupplySettings(), new FatigueSettings());
        var tick = Create(tickStores, new SupplySettings(), new FatigueSettings());
        const double end = 8d * 1440d + 530.25d;
        jump.AdvanceTo(end, 10d);
        for (double t = 0.37d; t < end; t += 0.37d)
        {
            tick.AdvanceTo(t, 10d);
            Conserved(tickStores, tick);
        }
        tick.AdvanceTo(end, 10d);
        Conserved(jumpStores, jump);
        Conserved(tickStores, tick);
        Assert.That(jumpStores.Farm, Is.EqualTo(tickStores.Farm));
        Assert.That(jumpStores.Market, Is.EqualTo(tickStores.Market).And.GreaterThan(0));
        Assert.That(jump.CargoQuantity, Is.EqualTo(tick.CargoQuantity));
        Assert.That(jump.State, Is.EqualTo(tick.State));
        Assert.That(jump.Target, Is.EqualTo(tick.Target));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-5));
        Assert.That(jump.Energy, Is.EqualTo(tick.Energy).Within(1e-5));
        Assert.That(jump.Satiation, Is.EqualTo(tick.Satiation).Within(1e-5));
        Assert.That(jump.Hydration, Is.EqualTo(tick.Hydration).Within(1e-5));
        Assert.That(jump.WorkedMinutes, Is.EqualTo(tick.WorkedMinutes).Within(1e-5));
    }
}
