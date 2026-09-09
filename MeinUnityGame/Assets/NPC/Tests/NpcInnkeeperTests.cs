using NUnit.Framework;
using Village.Npc;

public sealed class NpcInnkeeperTests
{
    private sealed class TavernNavigation : INpcNavigation
    {
        public NpcPoint GetPlace(NpcPlace place)
        {
            return new NpcPoint(place == NpcPlace.Well ? 84.12d : 0d, 0d, 0d);
        }

        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination)
        {
            NpcPoint to = GetPlace(destination);
            // Like RoadRouter, co-located home/work/food returns one point.
            return NpcPoint.Distance(from, to) < 1e-9 ? new[] { to } : new[] { from, to };
        }
    }

    private static NpcSimulation Create(bool suppliesEnabled = true)
    {
        return new NpcSimulation(new TavernNavigation(),
            new WorkSchedule { startHour = 10, endHour = 23 },
            new FatigueSettings { initialFatigue = 72d },
            suppliesEnabled ? new SupplySettings() :
                new SupplySettings { satiationLossPerHour = 0d, hydrationLossPerHour = 0d });
    }

    [Test]
    public void CoLocatedHomeAndWorkUseInnkeeperHoursWithoutAnArtificialCommute()
    {
        var npc = Create(false);
        Assert.That(npc.RouteLength, Is.Zero);
        npc.AdvanceTo(420d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.Energy, Is.EqualTo(84d).Within(1e-7));
        npc.AdvanceTo(600d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        npc.AdvanceTo(1379d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        npc.AdvanceTo(1380d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.WorkedMinutes, Is.EqualTo(780d).Within(1e-7));
        Assert.That(npc.TravelledMetres, Is.Zero);
        npc.AdvanceTo(1860d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
    }

    [Test]
    public void CriticalThirstInterruptsTavernWorkAndThenReturns()
    {
        var npc = Create();
        npc.AdvanceTo(1200d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Well));
        npc.AdvanceTo(1233d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Work));
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1d));
        Assert.That(npc.Position.X, Is.Zero);
    }

    [Test]
    public void MultiDayWaitingMatchesTicksForAnInnkeeperLivingAtWork()
    {
        var jumped = Create();
        var ticking = Create();
        const double destination = 6d * 1440d + 600.25d;
        jumped.AdvanceTo(destination, 7.5d);
        for (double t = 0.37d; t < destination; t += 0.37d) ticking.AdvanceTo(t, 7.5d);
        ticking.AdvanceTo(destination, 7.5d);
        Assert.That(jumped.State, Is.EqualTo(ticking.State));
        Assert.That(jumped.Target, Is.EqualTo(ticking.Target));
        Assert.That(jumped.Position.X, Is.EqualTo(ticking.Position.X).Within(1e-5));
        Assert.That(jumped.Energy, Is.EqualTo(ticking.Energy).Within(1e-5));
        Assert.That(jumped.Satiation, Is.EqualTo(ticking.Satiation).Within(1e-5));
        Assert.That(jumped.Hydration, Is.EqualTo(ticking.Hydration).Within(1e-5));
        Assert.That(jumped.WorkedMinutes, Is.EqualTo(ticking.WorkedMinutes).Within(1e-5));
        Assert.That(jumped.SleptMinutes, Is.EqualTo(ticking.SleptMinutes).Within(1e-5));
        Assert.That(jumped.TravelledMetres, Is.EqualTo(ticking.TravelledMetres).Within(1e-5));
        Assert.That(jumped.MealsCompleted, Is.EqualTo(ticking.MealsCompleted).And.GreaterThan(0d));
        Assert.That(jumped.DrinksCompleted, Is.EqualTo(ticking.DrinksCompleted).And.GreaterThan(0d));
    }
}
