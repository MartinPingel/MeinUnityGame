using System;
using NUnit.Framework;
using Village.Npc;

public sealed class NpcSupplyTests
{
    private sealed class TestRoad : INpcNavigation
    {
        private readonly double tavern, well;
        public TestRoad(double tavern = 75d, double well = 30d)
        { this.tavern = tavern; this.well = well; }
        public NpcPoint GetPlace(NpcPlace place)
        {
            return new NpcPoint(place == NpcPlace.Work ? 150d :
                place == NpcPlace.Tavern ? tavern : place == NpcPlace.Well ? well : 0d, 0d, 0d);
        }
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace to)
        {
            Assert.That(to, Is.Not.EqualTo(NpcPlace.Well), "Needs must never route to the well.");
            return new[] { from, GetPlace(to) };
        }
    }

    private static NpcSimulation Create(SupplySettings supplies = null, FatigueSettings fatigue = null,
        INpcNavigation roads = null, WorkSchedule schedule = null)
    {
        return new NpcSimulation(roads ?? new TestRoad(), schedule ?? new WorkSchedule(),
            fatigue ?? new FatigueSettings(), supplies ?? new SupplySettings());
    }

    [Test]
    public void EnergyAndSuppliesUseGameHoursDuringSleepAndWork()
    {
        var npc = Create();
        Assert.That(npc.Energy, Is.EqualTo(36d));
        Assert.That(npc.Satiation, Is.EqualTo(100d));
        Assert.That(npc.Hydration, Is.EqualTo(100d));
        npc.AdvanceTo(360d, 7.5d);
        Assert.That(npc.Energy, Is.EqualTo(84d).Within(1e-7));
        Assert.That(npc.Satiation, Is.EqualTo(88d).Within(1e-7));
        Assert.That(npc.Hydration, Is.EqualTo(76d).Within(1e-7));
        npc.AdvanceTo(600d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        Assert.That(npc.Energy, Is.EqualTo(68d).Within(1e-7));
        Assert.That(npc.Hydration, Is.EqualTo(60d).Within(1e-7));
    }

    [Test]
    public void CriticalWaterThenFoodThenSleepIncludesAllTravelAndServiceTime()
    {
        var npc = Create(new SupplySettings { initialSatiation = 20d, initialHydration = 20d },
            new FatigueSettings { initialFatigue = 80d });
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        npc.AdvanceTo(10d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        Assert.That(npc.Position.X, Is.EqualTo(75d));
        npc.AdvanceTo(19.9d, 7.5d);
        Assert.That(npc.DrinksCompleted, Is.Zero);
        npc.AdvanceTo(20d, 7.5d);
        Assert.That(npc.Hydration, Is.EqualTo(100d));
        // Drink first, then eat at the same tavern without another journey.
        Assert.That(npc.State, Is.EqualTo(NpcState.Eating));
        npc.AdvanceTo(50d, 7.5d);
        Assert.That(npc.Satiation, Is.EqualTo(100d));
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        npc.AdvanceTo(60d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.Energy, Is.EqualTo(16d).Within(1e-7));
    }

    [Test]
    public void CriticalThirstInterruptsEatingWithoutGrantingAnUnfinishedMeal()
    {
        var npc = Create(new SupplySettings { initialSatiation = 20d, initialHydration = 21d,
            hydrationLossPerHour = 60d }, roads: new TestRoad(tavern: 0d));
        Assert.That(npc.State, Is.EqualTo(NpcState.Eating));
        npc.AdvanceTo(1d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Drinking));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        Assert.That(npc.MealsCompleted, Is.Zero);
        Assert.That(npc.Satiation, Is.LessThan(20d));
        Assert.That(npc.Position.X, Is.Zero);
    }

    [Test]
    public void SupplyTripReturnsToWorkOnlyWhileShiftIsStillActive()
    {
        var npc = Create(new SupplySettings { initialHydration = 55d });
        npc.AdvanceTo(525d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        Assert.That(npc.Position.X, Is.EqualTo(150d));
        npc.AdvanceTo(545d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToWork));
        npc.AdvanceTo(555d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));

        var late = Create(new SupplySettings { initialSatiation = 20d + 1010d / 30d });
        late.AdvanceTo(1051d, 7.5d);
        Assert.That(late.MealsCompleted, Is.EqualTo(1d));
        Assert.That(late.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(late.IsWorkTime, Is.False);
    }

    [TestCase(7.5d, 8, 17)]
    [TestCase(3d, 8, 17)]
    [TestCase(7.5d, 10, 23)]
    [TestCase(3d, 10, 23)]
    [TestCase(7.5d, 8, 18)]
    [TestCase(3d, 8, 18)]
    public void MultiDayWaitMatchesTicksIncludingMealsDrinksAndPartialJourneys(double speed, int startHour, int endHour)
    {
        const double end = 8d * 1440d + 535.25d;
        var schedule = new WorkSchedule { startHour = startHour, endHour = endHour };
        var jumped = Create(schedule: schedule);
        var ticking = Create(schedule: schedule);
        jumped.AdvanceTo(end, speed);
        for (double t = 0.37d; t < end; t += 0.37d) ticking.AdvanceTo(t, speed);
        ticking.AdvanceTo(end, speed);
        Assert.That(jumped.State, Is.EqualTo(ticking.State));
        Assert.That(jumped.Target, Is.EqualTo(ticking.Target));
        Assert.That(jumped.Energy, Is.EqualTo(ticking.Energy).Within(1e-5));
        Assert.That(jumped.Satiation, Is.EqualTo(ticking.Satiation).Within(1e-5));
        Assert.That(jumped.Hydration, Is.EqualTo(ticking.Hydration).Within(1e-5));
        Assert.That(jumped.Position.X, Is.EqualTo(ticking.Position.X).Within(1e-5));
        Assert.That(jumped.WorkedMinutes, Is.EqualTo(ticking.WorkedMinutes).Within(1e-5));
        Assert.That(jumped.SleptMinutes, Is.EqualTo(ticking.SleptMinutes).Within(1e-5));
        Assert.That(jumped.TravelledMetres, Is.EqualTo(ticking.TravelledMetres).Within(1e-5));
        Assert.That(jumped.MealsCompleted, Is.EqualTo(ticking.MealsCompleted).And.GreaterThan(0d));
        Assert.That(jumped.DrinksCompleted, Is.EqualTo(ticking.DrinksCompleted).And.GreaterThan(0d));
    }

    [Test]
    public void DrinkingAcrossShiftEndReturnsHomeInsteadOfResumingWork()
    {
        var npc = Create(new SupplySettings { initialHydration = 20d + 1010d / 15d });
        npc.AdvanceTo(1011d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToDrink));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Tavern));
        npc.AdvanceTo(1031d, 7.5d);
        Assert.That(npc.DrinksCompleted, Is.EqualTo(1d));
        Assert.That(npc.Target, Is.EqualTo(NpcPlace.Home));
        Assert.That(npc.IsWorkTime, Is.False);
    }

    [Test]
    public void InvalidInputIsRejectedWithoutAdvancingNeeds()
    {
        Assert.Throws<ArgumentException>(() => Create(new SupplySettings { initialHydration = 101d }));
        Assert.Throws<ArgumentException>(() => Create(new SupplySettings { eatingMinutes = 0d }));
        var npc = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(double.NaN, 7.5d));
        Assert.That(npc.TotalMinutes, Is.Zero);
        Assert.That(npc.Satiation, Is.EqualTo(100d));
        Assert.That(npc.Hydration, Is.EqualTo(100d));
        Assert.That(npc.Energy, Is.EqualTo(36d));
    }
}
