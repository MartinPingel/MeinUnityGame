using NUnit.Framework;
using Village.Npc;

public sealed class SocialNeedTests
{
    private sealed class Navigation : INpcNavigation, INpcSocialNavigation
    {
        private readonly double home;
        private NpcPoint social;
        public Navigation(double home) { this.home = home; }
        public void SetSocialDestination(NpcPoint point) { social = point; }
        public NpcPoint GetPlace(NpcPlace p) => p == NpcPlace.Social ? social :
            new NpcPoint(p == NpcPlace.Home || p == NpcPlace.Tavern ? home :
                p == NpcPlace.Delivery ? 10000 : 100, 0, 0);
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to) => new[] { from, GetPlace(to) };
    }
    private sealed class Cargo : INpcDeliveryInventory, INpcProductionGate
    {
        public int Source = 10, Delivered;
        public bool CanProduce => false;
        public bool TryProduceOne() => false;
        public bool HasBatch(int q) => Source >= q;
        public bool TryPickUp(int q) { if (Source < q) return false; Source -= q; return true; }
        public bool TryDeliver(int q) { Delivered += q; return true; }
    }
    private static NpcSocialVenue Venue() => new NpcSocialVenue(new NpcPoint(50, 0, 0),
        new[] { new NpcPoint(100, 0, 0), new NpcPoint(110, 0, 0) });
    private static NpcSimulation Npc(NpcSocialVenue venue, double home = 0, double initial = 30,
        double loss = 0, double recovery = 30, SupplySettings needs = null, Cargo cargo = null,
        System.Func<bool> food = null, System.Func<bool> drink = null) =>
        new NpcSimulation(new Navigation(home), new WorkSchedule { startHour = 0, endHour = 23 },
            new FatigueSettings { initialFatigue = 0, gainPerAwakeHour = 0.1 },
            needs ?? new SupplySettings { satiationLossPerHour = 0, hydrationLossPerHour = 0 },
            food ?? (() => true), drink ?? (() => true), cargo,
            cargo == null ? null : new WorkDeliverySettings { unitsPerWorkHour = 1, deliveryQuantity = 10 },
            null, venue, new NpcSocialSettings { initialValue = initial, lossPerHour = loss,
                needThreshold = 35, satisfiedValue = 85, recoveryPerHour = recovery });

    [Test]
    public void AloneNeverRecoversAndTravellingPartnerDoesNotCount()
    {
        var venue = Venue(); var a = Npc(venue); var b = Npc(venue, home: -1000);
        var group = new NpcSimulationGroup(); group.Add(a, () => 10); group.Add(b, () => 10);
        group.AdvanceTo(100);
        Assert.That(a.State, Is.EqualTo(NpcState.WaitingForCompany));
        Assert.That(a.Social, Is.EqualTo(30));
        Assert.That(b.State, Is.EqualTo(NpcState.GoingToSocial));
        group.AdvanceTo(120);
        Assert.That(a.Social, Is.EqualTo(34.5).Within(1e-6));
        Assert.That(b.Social, Is.EqualTo(a.Social).Within(1e-6));
        Assert.That(a.State, Is.EqualTo(NpcState.Socialising));
    }

    [Test]
    public void RecoveryStopsAtTheExactDepartureOfTheOtherParticipant()
    {
        var venue = Venue(); var a = Npc(venue, initial: 34, recovery: 60);
        var b = Npc(venue, initial: 0, recovery: 60);
        var group = new NpcSimulationGroup(); group.Add(a, () => 100); group.Add(b, () => 100);
        group.AdvanceTo(60);
        Assert.That(a.Social, Is.EqualTo(85).Within(1e-6));
        Assert.That(a.NeedsSocial, Is.False);
        Assert.That(a.SocialSlot, Is.EqualTo(-1));
        Assert.That(b.Social, Is.EqualTo(51).Within(1e-6));
        Assert.That(b.State, Is.EqualTo(NpcState.WaitingForCompany));
        group.AdvanceTo(120);
        Assert.That(b.Social, Is.EqualTo(51).Within(1e-6));
    }

    [Test]
    public void SlotsAreExclusiveAndReleasedWhenNpcIsRemoved()
    {
        var venue = Venue(); var a = Npc(venue); var b = Npc(venue); var c = Npc(venue);
        var group = new NpcSimulationGroup();
        group.Add(a, () => 100); group.Add(b, () => 100); group.Add(c, () => 100);
        group.AdvanceTo(2);
        Assert.That(a.SocialSlot, Is.Not.EqualTo(b.SocialSlot));
        Assert.That(c.SocialSlot, Is.EqualTo(-1));
        Assert.That(c.State, Is.EqualTo(NpcState.WaitingForSocialPlace));
        group.Remove(a); group.AdvanceTo(3);
        Assert.That(c.SocialSlot, Is.GreaterThanOrEqualTo(0));
        Assert.That(c.SocialSlot, Is.Not.EqualTo(b.SocialSlot));
    }

    [Test]
    public void HungerInterruptsContactAndLeavesThePartnerWaiting()
    {
        var venue = Venue();
        var a = Npc(venue, needs: new SupplySettings { initialSatiation = 21,
            satiationLossPerHour = 60, hydrationLossPerHour = 0 });
        var b = Npc(venue);
        var group = new NpcSimulationGroup(); group.Add(a, () => 1000); group.Add(b, () => 1000);
        group.AdvanceTo(1);
        double value = b.Social;
        Assert.That(a.NeedsFood, Is.True);
        Assert.That(a.SocialSlot, Is.GreaterThanOrEqualTo(0));
        Assert.That(a.Position.X, Is.EqualTo(100));
        group.AdvanceTo(10);
        Assert.That(b.Social, Is.EqualTo(value).Within(1e-6));
        Assert.That(a.State, Is.EqualTo(NpcState.WaitingForService));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoWaiterMeansNoAutomaticBankConsumption(bool drinking)
    {
        int attempts = 0;
        System.Func<bool> consume = () => { attempts++; return true; };
        var venue = Venue();
        var a = Npc(venue, needs: new SupplySettings {
            initialSatiation = drinking ? 100 : 21, initialHydration = drinking ? 21 : 100,
            satiationLossPerHour = drinking ? 0 : 60, hydrationLossPerHour = drinking ? 60 : 0,
            eatingMinutes = 2, drinkingMinutes = 2 }, food: consume, drink: consume);
        var group = new NpcSimulationGroup(); group.Add(a, () => 1000);
        group.AdvanceTo(1);
        int slot = a.SocialSlot;
        Assert.That(slot, Is.GreaterThanOrEqualTo(0));
        Assert.That(a.Target, Is.EqualTo(NpcPlace.Social));
        Assert.That(a.State, Is.EqualTo(NpcState.WaitingForService));
        group.AdvanceTo(86);
        Assert.That(attempts, Is.Zero);
        Assert.That(drinking ? a.DrinksCompleted : a.MealsCompleted, Is.Zero);
        Assert.That(drinking ? a.Hydration : a.Satiation, Is.Zero);
        Assert.That(a.SocialSlot, Is.EqualTo(slot));
        Assert.That(a.Social, Is.EqualTo(30));
    }

    [Test]
    public void NeedUsesValueThresholdRatherThanClockTime()
    {
        var a = Npc(Venue(), initial: 100, loss: 60);
        var group = new NpcSimulationGroup(); group.Add(a, () => 10);
        group.AdvanceTo(64); Assert.That(a.NeedsSocial, Is.False);
        group.AdvanceTo(65); Assert.That(a.NeedsSocial, Is.True);
        Assert.That(a.Social, Is.EqualTo(35).Within(1e-6));
    }

    [Test]
    public void SocialDetourPreservesLoadedDelivery()
    {
        var venue = Venue(); var cargo = new Cargo();
        var a = Npc(venue, initial: 50, loss: 60, cargo: cargo);
        var b = Npc(venue);
        var group = new NpcSimulationGroup(); group.Add(a, () => 100); group.Add(b, () => 100);
        group.AdvanceTo(10);
        Assert.That(a.CargoQuantity, Is.EqualTo(10));
        group.AdvanceTo(30);
        Assert.That(a.NeedsSocial, Is.True);
        Assert.That(a.CargoQuantity, Is.EqualTo(10));
        Assert.That(cargo.Source + cargo.Delivered + a.CargoQuantity, Is.EqualTo(10));
        Assert.That(cargo.Delivered, Is.Zero);
    }

    [Test]
    public void SeveralDaySkipMatchesSmallTicks()
    {
        var va = Venue(); var vb = Venue();
        var a = Npc(va, initial: 36, loss: 3); var b = Npc(va, home: -100, initial: 37, loss: 3);
        var c = Npc(vb, initial: 36, loss: 3); var d = Npc(vb, home: -100, initial: 37, loss: 3);
        var jump = new NpcSimulationGroup(); jump.Add(a, () => 10); jump.Add(b, () => 10);
        var tick = new NpcSimulationGroup(); tick.Add(c, () => 10); tick.Add(d, () => 10);
        const double end = 3 * 1440;
        jump.AdvanceTo(end);
        for (double t = 0.37; t < end; t += 0.37) tick.AdvanceTo(t);
        tick.AdvanceTo(end);
        Assert.That(a.Social, Is.EqualTo(c.Social).Within(1e-5));
        Assert.That(b.Social, Is.EqualTo(d.Social).Within(1e-5));
        Assert.That(a.State, Is.EqualTo(c.State));
        Assert.That(a.Position.X, Is.EqualTo(c.Position.X).Within(1e-5));
        Assert.That(a.Energy, Is.EqualTo(c.Energy).Within(1e-5));
    }
}


