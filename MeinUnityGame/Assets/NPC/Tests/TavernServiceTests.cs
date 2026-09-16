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
        private readonly NpcSocialVenue venue;
        public World(bool drink = false, int stock = 1, double waiterHunger = 0, double guestSocial = 30, double waiterSocial = 100, double waiterSocialLoss = 0, bool selfSupply = false)
        {
            Food = Water = stock;
            venue = new NpcSocialVenue(new NpcPoint(50, 0, 0),
                new[] { new NpcPoint(100, 0, 0), new NpcPoint(110, 0, 0) });
            Service = new NpcTavernService((d, q) => (d ? Water : Food) >= q,
                d => { if ((d ? Water : Food) <= 0) return false;
                    if (d) Water--; else Food--; return true; },
                guest => new NpcPoint(guest.Position.X + 1.05, 0, 0), selfSupply);
            Guest = Create(venue, new SupplySettings {
                initialSatiation = drink ? 100 : 21, initialHydration = drink ? 21 : 100,
                satiationLossPerHour = drink ? 0 : 60, hydrationLossPerHour = drink ? 60 : 0 }, guestSocial);
            Waiter = Create(venue, new SupplySettings {
                initialSatiation = waiterHunger > 0 ? 22 : 100,
                satiationLossPerHour = waiterHunger, hydrationLossPerHour = 0,
                eatingMinutes = 1 }, waiterSocial, waiterSocialLoss);
            Group.Add(Guest, () => 1000); Group.Add(Waiter, () => 10);
            Service.Register(Guest, false); Service.Register(Waiter, true);
            Group.PrepareServices = Service.Update;
        }
        public NpcTavernService AddCoworker(out NpcSimulation coworker, out NpcSimulation secondGuest)
        {
            var other = new NpcTavernService((d, q) => (d ? Water : Food) >= q,
                d => { if ((d ? Water : Food) <= 0) return false;
                    if (d) Water--; else Food--; return true; },
                guest => new NpcPoint(guest.Position.X + 1.05, 0, 0), true);
            Service.LinkCoworker(other);
            coworker = Create(venue, new SupplySettings { satiationLossPerHour = 0, hydrationLossPerHour = 0 }, 100);
            secondGuest = Create(venue, new SupplySettings { initialSatiation = 21,
                satiationLossPerHour = 60, hydrationLossPerHour = 0 }, 30);
            Service.Register(coworker, false, false);
            Service.Register(secondGuest, false);
            other.Register(Guest, false, false); other.Register(Waiter, false, false);
            other.Register(secondGuest, false, false); other.Register(coworker, true);
            Group.Add(coworker, () => 10); Group.Add(secondGuest, () => 1000);
            Group.PrepareServices = () => { Service.Update(); other.Update(); };
            return other;
        }

        private NpcSimulation Create(NpcSocialVenue venue, SupplySettings needs, double social, double socialLoss = 0) =>
            new NpcSimulation(new Navigation(), new WorkSchedule { startHour = 0, endHour = 23 },
                needs,
                () => throw new InvalidOperationException("Legacy food consumption must not run."),
                () => throw new InvalidOperationException("Legacy drink consumption must not run."),
                null, null, null, venue, new NpcSocialSettings { initialValue = social,
                    lossPerHour = socialLoss, needThreshold = 35, satisfiedValue = 85, recoveryPerHour = 30 });
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
    public void WaiterMustCarryHisOwnPortionBackToHisReservedBankPlace()
    {
        var w = new World(stock: 1, waiterHunger: 60);
        w.Group.AdvanceTo(2);
        Assert.That(w.Waiter.NeedsFood, Is.True);
        Assert.That(w.Waiter.Target, Is.EqualTo(NpcPlace.Social));
        Assert.That(w.Waiter.SocialSlot, Is.GreaterThanOrEqualTo(0));
        Assert.That(w.Waiter.MealsCompleted, Is.Zero);
        w.Group.AdvanceTo(23); // Reaches the warehouse after visiting his own place.
        Assert.That(w.Waiter.Position.X, Is.EqualTo(0).Within(1e-6));
        Assert.That(w.Service.HasReservedPortion, Is.True);
        Assert.That(w.Waiter.MealsCompleted, Is.Zero);
        Assert.That(w.Food, Is.EqualTo(1));
        w.Group.AdvanceTo(34); // Physically back at bank place x=110.
        Assert.That(w.Waiter.Position.X, Is.EqualTo(110).Within(1e-6));
        Assert.That(w.Waiter.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Waiter.Satiation, Is.EqualTo(100).Within(1e-6));
        Assert.That(w.Guest.MealsCompleted, Is.Zero);
        Assert.That(w.Food, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HungerAndThirstGoDirectlyToBankEvenWithoutSocialNeed(bool drink)
    {
        var w = new World(drink, guestSocial: 100);
        w.Group.AdvanceTo(1);
        Assert.That(w.Guest.NeedsSocial, Is.False);
        Assert.That(w.Guest.Target, Is.EqualTo(NpcPlace.Social));
        Assert.That(w.Guest.SocialSlot, Is.GreaterThanOrEqualTo(0));
        Assert.That(w.Guest.State, Is.EqualTo(drink ? NpcState.GoingToDrink : NpcState.GoingToEat));
        w.Group.AdvanceTo(1.1);
        Assert.That(w.Guest.Position.X, Is.EqualTo(100).Within(1e-6));
        Assert.That(w.Guest.State, Is.EqualTo(NpcState.WaitingForService));
        w.Group.AdvanceTo(11.205);
        Assert.That(drink ? w.Guest.DrinksCompleted : w.Guest.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Guest.SocialSlot, Is.EqualTo(-1)); // Returns to normal work after service.
        Assert.That(drink ? w.Water : w.Food, Is.Zero);
    }

    [Test]
    public void LonelyInnkeeperKeepsWorkingDespiteSocialNeed()
    {
        var w = new World(waiterSocial: 30, waiterSocialLoss: 3);
        w.Service.Remove(w.Guest); w.Group.Remove(w.Guest);
        w.Group.AdvanceTo(60);
        Assert.That(w.Waiter.Social, Is.EqualTo(27).Within(1e-6));
        Assert.That(w.Waiter.NeedsSocial, Is.True);
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
        Assert.That(w.Waiter.State, Is.EqualTo(NpcState.Working));
        Assert.That(w.Waiter.Target, Is.EqualTo(NpcPlace.Work));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ArrivedGuestRestoresHostsSocialValueWithoutMakingHostAGuest(bool selfSupply)
    {
        var w = new World(stock: 0, waiterSocial: 30, waiterSocialLoss: 3, selfSupply: selfSupply);
        w.Group.AdvanceTo(0.05); // Guest is still walking to the bank.
        Assert.That(w.Waiter.Social, Is.LessThan(30));
        w.Group.AdvanceTo(60);
        Assert.That(w.Waiter.Social, Is.GreaterThan(30));
        Assert.That(w.Waiter.State, Is.EqualTo(NpcState.Working));
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
        Assert.That(w.Guest.Social, Is.EqualTo(30)); // Host does not count as a social guest.
        w.Group.AdvanceTo(180);
        Assert.That(w.Waiter.Social, Is.EqualTo(100).Within(1e-6));
        Assert.That(w.Waiter.NeedsSocial, Is.False);
        w.Group.AdvanceTo(240); // Staying at 100 must not create a zero-duration event loop.
        Assert.That(w.Waiter.Social, Is.EqualTo(100).Within(1e-6));
        w.Service.Remove(w.Guest); w.Group.Remove(w.Guest);
        w.Group.AdvanceTo(300);
        Assert.That(w.Waiter.Social, Is.EqualTo(97).Within(1e-6));
    }

    [Test]
    public void SocialNeedDoesNotBlockServingAWaitingGuest()
    {
        var w = new World(waiterSocial: 30, waiterSocialLoss: 3);
        w.Group.AdvanceTo(10);
        Assert.That(w.Waiter.NeedsSocial, Is.True);
        Assert.That(w.Waiter.State, Is.EqualTo(NpcState.ServingGuest));
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
        w.Group.AdvanceTo(11.105);
        Assert.That(w.Guest.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Food, Is.Zero);
    }

    [Test]
    public void InnkeeperSocialSkipMatchesSmallTicks()
    {
        var jump = new World(stock: 0, waiterSocial: 30, waiterSocialLoss: 3);
        var tick = new World(stock: 0, waiterSocial: 30, waiterSocialLoss: 3);
        jump.Group.AdvanceTo(1500);
        for (double t = 0.37; t < 1500; t += 0.37) tick.Group.AdvanceTo(t);
        tick.Group.AdvanceTo(1500);
        Assert.That(jump.Waiter.Social, Is.EqualTo(tick.Waiter.Social).Within(1e-5));
        Assert.That(jump.Waiter.State, Is.EqualTo(tick.Waiter.State));
        Assert.That(jump.Waiter.SocialSlot, Is.EqualTo(-1));
    }

    [Test]
    public void AdditionalWaiterEatsAtTavernWithoutReservingGuestSeat()
    {
        var w = new World(stock: 2, waiterHunger: 60, selfSupply: true);
        w.Group.AdvanceTo(2);
        Assert.That(w.Waiter.NeedsFood, Is.True);
        Assert.That(w.Waiter.Target, Is.EqualTo(NpcPlace.Tavern));
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
        Assert.That(w.Waiter.MealsCompleted, Is.Zero);
        w.Group.AdvanceTo(4);
        Assert.That(w.Waiter.Position.X, Is.EqualTo(0).Within(1e-6));
        Assert.That(w.Waiter.MealsCompleted, Is.EqualTo(1));
        Assert.That(w.Food, Is.EqualTo(1));
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
    }

    [TestCase(1)]
    [TestCase(2)]
    public void CoworkersDoNotDoubleBookGuestsOrDuplicateTheLastPortion(int stock)
    {
        var w = new World(stock: stock);
        var other = w.AddCoworker(out NpcSimulation coworker, out NpcSimulation secondGuest);
        w.Group.AdvanceTo(2);
        Assert.That(w.Food, Is.EqualTo(stock)); // Pickup is a reservation, not consumption.
        if (other.Guest != null) Assert.That(other.Guest, Is.Not.SameAs(w.Service.Guest));
        int carried = (other.HasReservedPortion ? 1 : 0) + (w.Service.HasReservedPortion ? 1 : 0);
        Assert.That(carried, Is.LessThanOrEqualTo(stock));
        w.Group.AdvanceTo(30);
        Assert.That(w.Guest.MealsCompleted + secondGuest.MealsCompleted, Is.EqualTo(stock));
        Assert.That(w.Food, Is.Zero);
    }

    [Test]
    public void AdditionalWaiterReturnsToOwnSleepPointAfterClosing()
    {
        var w = new World(stock: 0, selfSupply: true);
        w.Service.Remove(w.Guest); w.Group.Remove(w.Guest);
        w.Group.AdvanceTo(23 * 60);
        Assert.That(w.Waiter.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(w.Waiter.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(w.Waiter.SocialSlot, Is.EqualTo(-1));
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



