using System;
using NUnit.Framework;
using Village.Npc;

public sealed class TavernServiceTests
{
    private sealed class Navigation : INpcNavigation, INpcSocialNavigation, INpcServiceNavigation
    {
        private NpcPoint social, service;
        public void SetSocialDestination(NpcPoint p) => social = p;
        public void SetServiceDestination(NpcPoint p) => service = p;
        public NpcPoint GetPlace(NpcPlace p) => p == NpcPlace.Social ? social :
            p == NpcPlace.Service ? service : new NpcPoint(0, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class World
    {
        public int Food, Water;
        public readonly NpcTavernService Service;
        public readonly NpcSimulationGroup Group = new NpcSimulationGroup();
        public readonly NpcSimulation Guest, Waiter;
        public World(bool drink = false, int stock = 1, double waiterHunger = 0)
        {
            Food = Water = stock;
            var venue = new NpcSocialVenue(new NpcPoint(50, 0, 0),
                new[] { new NpcPoint(100, 0, 0), new NpcPoint(110, 0, 0) });
            Service = new NpcTavernService((d, q) => (d ? Water : Food) >= q,
                d => { if ((d ? Water : Food) <= 0) return false;
                    if (d) Water--; else Food--; return true; },
                guest => new NpcPoint(guest.Position.X + 1.05, 0, 0));
            Guest = Create(venue, new SupplySettings {
                initialSatiation = drink ? 100 : 21, initialHydration = drink ? 21 : 100,
                satiationLossPerHour = drink ? 0 : 60, hydrationLossPerHour = drink ? 60 : 0 }, 30);
            Waiter = Create(venue, new SupplySettings {
                initialSatiation = waiterHunger > 0 ? 22 : 100,
                satiationLossPerHour = waiterHunger, hydrationLossPerHour = 0,
                eatingMinutes = 1 }, 100);
            Group.Add(Guest, () => 1000); Group.Add(Waiter, () => 10);
            Service.Register(Guest, false); Service.Register(Waiter, true);
            Group.PrepareServices = Service.Update;
        }
        private NpcSimulation Create(NpcSocialVenue venue, SupplySettings needs, double social) =>
            new NpcSimulation(new Navigation(), new WorkSchedule { startHour = 0, endHour = 23 },
                new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 }, needs,
                () => Service.TryConsumeUnreserved(false), () => Service.TryConsumeUnreserved(true),
                null, null, null, venue, new NpcSocialSettings { initialValue = social,
                    lossPerHour = 0, needThreshold = 35, satisfiedValue = 85, recoveryPerHour = 30 });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PickupReservesButOnlyPhysicalHandoverConsumesAndRestores(bool drink)
    {
        var w = new World(drink);
        w.Group.AdvanceTo(1);
        Assert.That(w.Guest.State, Is.EqualTo(NpcState.WaitingForService));
        Assert.That(w.Service.HasReservedPortion, Is.True);
        Assert.That(drink ? w.Water : w.Food, Is.EqualTo(1));
        Assert.That(w.Service.TryConsumeUnreserved(drink), Is.False);
        w.Group.AdvanceTo(10);
        Assert.That(drink ? w.Guest.Hydration : w.Guest.Satiation, Is.LessThanOrEqualTo(20));
        Assert.That(drink ? w.Water : w.Food, Is.EqualTo(1));
        Assert.That(w.Waiter.Position.X, Is.GreaterThan(0).And.LessThan(100));
        w.Group.AdvanceTo(11.105);
        Assert.That(drink ? w.Water : w.Food, Is.Zero);
        Assert.That(drink ? w.Guest.Hydration : w.Guest.Satiation, Is.EqualTo(100).Within(1e-6));
        Assert.That(drink ? w.Guest.DrinksCompleted : w.Guest.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Guest.Social, Is.EqualTo(30)); // Waiter is not a social participant.
    }

    [Test]
    public void EmptyStockCannotCreateServiceOrImproveNeed()
    {
        var w = new World(stock: 0);
        w.Group.AdvanceTo(60);
        Assert.That(w.Service.HasReservedPortion, Is.False);
        Assert.That(w.Guest.MealsCompleted, Is.Zero);
        Assert.That(w.Guest.Satiation, Is.Zero);
        Assert.That(w.Guest.State, Is.EqualTo(NpcState.WaitingForService));
    }

    [Test]
    public void LeavingGuestCancelsReservationWithoutConsumption()
    {
        var w = new World(); w.Group.AdvanceTo(2);
        w.Service.Remove(w.Guest); w.Group.Remove(w.Guest);
        w.Group.AdvanceTo(20);
        Assert.That(w.Service.HasReservedPortion, Is.False);
        Assert.That(w.Food, Is.EqualTo(1));
        Assert.That(w.Guest.MealsCompleted, Is.Zero);
    }

    [Test]
    public void WaitersHungerInterruptsTripAndReservationSurvives()
    {
        var w = new World(stock: 2, waiterHunger: 60);
        w.Group.AdvanceTo(2);
        Assert.That(w.Waiter.NeedsFood, Is.True);
        Assert.That(w.Service.HasReservedPortion, Is.True);
        Assert.That(w.Guest.MealsCompleted, Is.Zero);
        w.Group.AdvanceTo(4);
        Assert.That(w.Waiter.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Food, Is.EqualTo(1));
        Assert.That(w.Service.HasReservedPortion, Is.True);
        w.Group.AdvanceTo(15);
        Assert.That(w.Guest.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Food, Is.Zero);
    }

    [Test]
    public void LastPortionDoesNotDeadlockWaitersOwnUrgentHunger()
    {
        var w = new World(stock: 1, waiterHunger: 60);
        w.Group.AdvanceTo(4);
        Assert.That(w.Waiter.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Guest.MealsCompleted, Is.Zero);
        Assert.That(w.Food, Is.Zero);
        Assert.That(w.Service.HasReservedPortion, Is.False);
    }

    [Test]
    public void SkipAndSmallTicksProduceTheSameDeliveriesAndNeeds()
    {
        var jump = new World(stock: 20); var tick = new World(stock: 20);
        jump.Group.AdvanceTo(600);
        for (double t = 0.37; t < 600; t += 0.37) tick.Group.AdvanceTo(t);
        tick.Group.AdvanceTo(600);
        Assert.That(jump.Food, Is.EqualTo(tick.Food));
        Assert.That(jump.Guest.MealsCompleted, Is.EqualTo(tick.Guest.MealsCompleted));
        Assert.That(jump.Guest.Satiation, Is.EqualTo(tick.Guest.Satiation).Within(1e-5));
        Assert.That(jump.Waiter.Position.X, Is.EqualTo(tick.Waiter.Position.X).Within(1e-5));
    }
}
