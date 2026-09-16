using NUnit.Framework;
using Village.Npc;

public sealed class ProductiveHungerTests
{
    private sealed class Road : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(p == NpcPlace.Home ? 0 : 100, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Job : INpcDeliveryInventory, INpcProductionGate
    {
        public bool Inputs = true;
        public bool CanProduce => Inputs;
        public bool TryProduceOne() => Inputs;
        public bool HasBatch(int quantity) => false;
        public bool TryPickUp(int quantity) => false;
        public bool TryDeliver(int quantity) => false;
    }
    private sealed class Equipment : INpcWorkEquipment
    {
        public bool Available = true;
        public bool CanWork => Available;
        public double MinutesUntilBreak => double.PositiveInfinity;
        public void Wear(double minutes) { }
    }
    private static NpcSimulation Create(Job job, Equipment equipment = null) =>
        new NpcSimulation(new Road(), new WorkSchedule { startHour = 8, endHour = 17 },
            new SupplySettings { satiationLossPerHour = 5, productiveSatiationMultiplier = 2,
                hydrationLossPerHour = 0 }, () => true, () => true, job,
            new WorkDeliverySettings { unitsPerWorkHour = 6, deliveryQuantity = 10 }, equipment);

    [Test]
    public void OnlyActualProductionDoublesConsumption()
    {
        var job = new Job(); var gear = new Equipment(); var npc = Create(job, gear);
        npc.AdvanceTo(490, 10); // First work arrival, no productive time yet.
        double before = npc.Satiation;
        npc.AdvanceTo(550, 10);
        Assert.That(before - npc.Satiation, Is.EqualTo(10).Within(1e-6));
        job.Inputs = false; before = npc.Satiation;
        npc.AdvanceTo(610, 10);
        Assert.That(before - npc.Satiation, Is.EqualTo(5).Within(1e-6));
        job.Inputs = true; gear.Available = false; before = npc.Satiation;
        npc.AdvanceTo(670, 10);
        Assert.That(before - npc.Satiation, Is.EqualTo(5).Within(1e-6));
    }

    [Test]
    public void HungerBoundaryAndMealsMatchWhenSkippingTime()
    {
        var jump = Create(new Job()); var tick = Create(new Job());
        const double end = 7 * 1440;
        jump.AdvanceTo(end, 10);
        for (double t = 0.37; t < end; t += 0.37) tick.AdvanceTo(t, 10);
        tick.AdvanceTo(end, 10);
        Assert.That(jump.MealsCompleted, Is.EqualTo(tick.MealsCompleted));
        Assert.That(jump.MealsCompleted / 7d, Is.InRange(1.7, 2.3));
        Assert.That(jump.Satiation, Is.EqualTo(tick.Satiation).Within(1e-6));
        Assert.That(jump.ProducedUnits, Is.EqualTo(tick.ProducedUnits));
        Assert.That(jump.Position.X, Is.EqualTo(tick.Position.X).Within(1e-6));
    }

    [Test]
    public void InvalidWorkMultiplierIsRejected()
    {
        Assert.Throws<System.ArgumentException>(() =>
            new SupplySettings { productiveSatiationMultiplier = double.NaN }.Validate());
        Assert.Throws<System.ArgumentException>(() =>
            new SupplySettings { productiveSatiationMultiplier = 0.5 }.Validate());
    }
}
