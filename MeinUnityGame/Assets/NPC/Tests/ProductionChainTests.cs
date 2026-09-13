using NUnit.Framework;
using Village.Npc;
using Village.Storage;

public sealed class ProductionChainTests
{
    private sealed class Road : INpcNavigation
    {
        private readonly double work, pickup, delivery;
        public Road(double work, double pickup, double delivery)
        { this.work = work; this.pickup = pickup; this.delivery = delivery; }
        public NpcPoint GetPlace(NpcPlace p) => new NpcPoint(p == NpcPlace.Work ? work :
            p == NpcPlace.Pickup ? pickup : p == NpcPlace.Delivery ? delivery : 0, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }

    // Real warehouse transactions exercised through the shared simulation's production/transport hooks.
    private sealed class Job : INpcDeliveryInventory, INpcProductionGate
    {
        public WarehouseStock Work, Source, Destination;
        public string Input, Output, Cargo;
        public int InputQuantity;
        public bool CanProduce => Output != null && (Input == null
            ? Work.GetQuantity(Output) < int.MaxValue : Work.CanConvert(Input, InputQuantity, Output, 1));
        public bool TryProduceOne()
        {
            if (!CanProduce) return false;
            if (Input != null) return Work.TryConvert(Input, InputQuantity, Output, 1);
            Work.Add(Output, 1); return true;
        }
        public bool HasBatch(int q) => Cargo != null && Source.Has(Cargo, q) &&
            (Cargo != "Eisen" || !Work.Has("Eisen", 2));
        public bool TryPickUp(int q) => Source.TryRemove(Cargo, q);
        public bool TryDeliver(int q) { Destination.Add(Cargo, q); return true; }
    }

    private static NpcSimulation Worker(Road road, Job job, int start, int end, double rate, int batch) =>
        new NpcSimulation(road, new WorkSchedule { startHour = start, endHour = end },
            new FatigueSettings(), new SupplySettings(), () => true, () => true, job,
            new WorkDeliverySettings { unitsPerWorkHour = rate, deliveryQuantity = batch });

    [Test]
    public void EmptyInputsNeverCreateIronOrTools()
    {
        var smelter = new WarehouseStock(); var smith = new WarehouseStock(); var market = new WarehouseStock();
        var janJob = new Job { Work = smelter, Input = "Eisenerz", InputQuantity = 2, Output = "Eisen" };
        var peterJob = new Job { Work = smith, Source = smelter, Destination = smith,
            Input = "Eisen", InputQuantity = 2, Output = "Werkzeuge", Cargo = "Eisen" };
        var marcelJob = new Job { Source = smith, Destination = market, Cargo = "Werkzeuge" };
        var jan = Worker(new Road(100, 100, 100), janJob, 7, 17, 3, 1);
        var peter = Worker(new Road(200, 100, 200), peterJob, 8, 18, 2, 10);
        var marcel = Worker(new Road(300, 200, 300), marcelJob, 9, 18, 1, 2);
        jan.AdvanceTo(4320, 10); peter.AdvanceTo(4320, 10); marcel.AdvanceTo(4320, 10);
        Assert.That(jan.ProducedUnits + peter.ProducedUnits, Is.Zero);
        Assert.That(smelter.GetQuantity("Eisen") + smith.GetQuantity("Werkzeuge") + market.GetQuantity("Werkzeuge"), Is.Zero);
        Assert.That(peter.CargoQuantity + marcel.CargoQuantity, Is.Zero);
    }

    [Test]
    public void CompleteChainConservesEveryExtractedUnitIncludingCargo()
    {
        var mine = new WarehouseStock(); var smelter = new WarehouseStock();
        var smith = new WarehouseStock(); var market = new WarehouseStock();
        var justus = Worker(new Road(100, 100, 200), new Job { Work = mine, Source = mine,
            Destination = smelter, Output = "Eisenerz", Cargo = "Eisenerz" }, 7, 17, 6, 10);
        var jan = Worker(new Road(200, 200, 200), new Job { Work = smelter,
            Input = "Eisenerz", InputQuantity = 2, Output = "Eisen" }, 7, 17, 3, 1);
        var peter = Worker(new Road(300, 200, 300), new Job { Work = smith, Source = smelter,
            Destination = smith, Input = "Eisen", InputQuantity = 2, Output = "Werkzeuge", Cargo = "Eisen" }, 8, 18, 2, 10);
        var marcel = Worker(new Road(400, 300, 400), new Job { Source = smith,
            Destination = market, Cargo = "Werkzeuge" }, 9, 18, 1, 2);
        bool sawOreCargo = false, sawIronCargo = false, sawToolCargo = false;
        for (double time = 0.25; time <= 6 * 1440; time += 0.25)
        {
            justus.AdvanceTo(time, 10); jan.AdvanceTo(time, 10);
            peter.AdvanceTo(time, 10); marcel.AdvanceTo(time, 10);
            sawOreCargo |= justus.CargoQuantity > 0;
            sawIronCargo |= peter.CargoQuantity > 0;
            sawToolCargo |= marcel.CargoQuantity > 0;
            double balance = mine.GetQuantity("Eisenerz") + justus.CargoQuantity + smelter.GetQuantity("Eisenerz") +
                2 * (smelter.GetQuantity("Eisen") + peter.CargoQuantity + smith.GetQuantity("Eisen")) +
                4 * (smith.GetQuantity("Werkzeuge") + marcel.CargoQuantity + market.GetQuantity("Werkzeuge"));
            Assert.That(balance, Is.EqualTo(justus.ProducedUnits), "Ore-equivalent balance at minute " + time);
        }
        Assert.That(market.GetQuantity("Werkzeuge"), Is.GreaterThan(0));
        Assert.That(sawOreCargo && sawIronCargo && sawToolCargo, Is.True);
        Assert.That(marcel.ProducedUnits, Is.Zero);
    }
}
