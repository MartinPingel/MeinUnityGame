using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class WagonTransportTests
{
    private sealed class Roads : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(
            p == NpcPlace.Work || p == NpcPlace.Pickup ? 150 :
            p == NpcPlace.Delivery ? 300 : p == NpcPlace.Tavern ? 50 : 0, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Haul : INpcDeliveryInventory, INpcProductionGate, INpcTravelSpeed
    {
        public readonly WarehouseStock Mine = new WarehouseStock(), Smelter = new WarehouseStock();
        public bool RejectDeposit;
        public bool CanProduce => false;
        public double GetTravelSpeedMultiplier(int cargo) => cargo > 0 ? 0.75 : 1.5;
        public bool TryProduceOne() => false;
        public bool HasBatch(int q) => q <= 10 && Mine.Has("Eisenerz", q);
        public bool TryPickUp(int q) => HasBatch(q) && Mine.TryRemove("Eisenerz", q);
        public bool TryDeliver(int q)
        {
            if (RejectDeposit) return false;
            Smelter.Add("Eisenerz", q); return true;
        }
    }
    private static NpcSimulation Create(Haul job, bool thirst = false) =>
        new NpcSimulation(new Roads(), new WorkSchedule { startHour = 7, endHour = 17 },
            new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 },
            new SupplySettings { initialHydration = thirst ? 20 + 435d * 4 / 60 : 100,
                hydrationLossPerHour = thirst ? 4 : 0, satiationLossPerHour = 0 },
            () => true, () => true, job,
            new WorkDeliverySettings { unitsPerWorkHour = 1, deliveryQuantity = 10 });

    [Test]
    public void WithdrawalAndDepositHappenOnlyAtArrivalWithDifferentTravelSpeeds()
    {
        var job = new Haul(); job.Mine.Add("Eisenerz", 10); var npc = Create(job);
        npc.AdvanceTo(429, 10);
        Assert.That(job.Mine.GetQuantity("Eisenerz"), Is.EqualTo(10));
        Assert.That(npc.Position.X, Is.EqualTo(135).Within(1e-6));
        npc.AdvanceTo(430, 10);
        Assert.That(job.Mine.GetQuantity("Eisenerz"), Is.Zero);
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        Assert.That(job.Smelter.GetQuantity("Eisenerz"), Is.Zero);
        npc.AdvanceTo(440, 10);
        Assert.That(npc.Position.X, Is.EqualTo(225).Within(1e-6));
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        npc.AdvanceTo(450, 10);
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(job.Smelter.GetQuantity("Eisenerz"), Is.EqualTo(10));
        Assert.That(npc.ProducedUnits, Is.Zero);
    }

    [Test]
    public void InsufficientOreWaitsWithoutCreatingCargo()
    {
        var job = new Haul(); job.Mine.Add("Eisenerz", 9); var npc = Create(job);
        npc.AdvanceTo(800, 10);
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(job.Smelter.GetQuantity("Eisenerz"), Is.Zero);
        Assert.That(job.Mine.GetQuantity("Eisenerz"), Is.EqualTo(9));
        job.Mine.Add("Eisenerz", 1);
        npc.AdvanceTo(810, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
    }

    [Test]
    public void ThirstDetourAndRejectedUnloadKeepTheSameWagonCargo()
    {
        var job = new Haul { RejectDeposit = true }; job.Mine.Add("Eisenerz", 10);
        var npc = Create(job, true);
        npc.AdvanceTo(480, 10);
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1));
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        Assert.That(job.Mine.GetQuantity("Eisenerz") + job.Smelter.GetQuantity("Eisenerz"), Is.Zero);
        npc.AdvanceTo(510, 10);
        Assert.That(npc.CargoQuantity, Is.EqualTo(10));
        job.RejectDeposit = false;
        npc.AdvanceTo(511, 10);
        Assert.That(npc.CargoQuantity, Is.Zero);
        Assert.That(job.Smelter.GetQuantity("Eisenerz"), Is.EqualTo(10));
    }

    [Test]
    public void TimeSkipMatchesTicksAndConservesAllOre()
    {
        var a = new Haul(); var b = new Haul();
        a.Mine.Add("Eisenerz", 100); b.Mine.Add("Eisenerz", 100);
        var jump = Create(a, true); var tick = Create(b, true);
        jump.AdvanceTo(4000, 10);
        for (double t = 0.37; t < 4000; t += 0.37) tick.AdvanceTo(t, 10);
        tick.AdvanceTo(4000, 10);
        Assert.That(a.Mine.GetQuantity("Eisenerz") + jump.CargoQuantity +
            a.Smelter.GetQuantity("Eisenerz"), Is.EqualTo(100));
        Assert.That(jump.DeliveredUnits, Is.EqualTo(tick.DeliveredUnits));
        Assert.That(jump.CargoQuantity, Is.EqualTo(tick.CargoQuantity));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-6));
        Assert.That(jump.Energy, Is.EqualTo(tick.Energy).Within(1e-6));
    }
}
