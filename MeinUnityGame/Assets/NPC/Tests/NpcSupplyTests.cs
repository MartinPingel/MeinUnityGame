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
        INpcNavigation roads = null, WorkSchedule schedule = null,
        Func<bool> consumeFood = null, Func<bool> consumeDrink = null)
    {
        return new NpcSimulation(roads ?? new TestRoad(), schedule ?? new WorkSchedule(),
            fatigue ?? new FatigueSettings(), supplies ?? new SupplySettings(), consumeFood, consumeDrink);
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
    public void CompletedServicesConsumeOneUnitAndInterruptedServicesConsumeNone()
    {
        int food = 1, drinks = 1;
        var npc = Create(new SupplySettings { initialSatiation = 20d, initialHydration = 20d },
            consumeFood: () => food > 0 && --food >= 0,
            consumeDrink: () => drinks > 0 && --drinks >= 0);
        npc.AdvanceTo(19d, 7.5d);
        Assert.That(food, Is.EqualTo(1));
        Assert.That(drinks, Is.EqualTo(1));
        npc.AdvanceTo(20d, 7.5d);
        Assert.That(drinks, Is.Zero);
        Assert.That(npc.Hydration, Is.EqualTo(100d));
        npc.AdvanceTo(50d, 7.5d);
        Assert.That(food, Is.Zero);
        Assert.That(npc.Satiation, Is.EqualTo(100d));

        int meals = 0;
        var interrupted = Create(new SupplySettings { initialSatiation = 20d,
            initialHydration = 21d, hydrationLossPerHour = 60d },
            roads: new TestRoad(tavern: 0d),
            consumeFood: () => { meals++; return true; }, consumeDrink: () => false);
        interrupted.AdvanceTo(2d, 7.5d);
        Assert.That(meals, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void EmptyStockNeverRefillsTheCorrespondingNeed(bool food)
    {
        var npc = Create(new SupplySettings {
            initialSatiation = food ? 20d : 100d,
            initialHydration = food ? 100d : 20d,
            satiationLossPerHour = 0d, hydrationLossPerHour = 0d },
            consumeFood: () => false, consumeDrink: () => false);
        npc.AdvanceTo(180d, 7.5d);
        Assert.That(food ? npc.Satiation : npc.Hydration, Is.EqualTo(20d));
        Assert.That(npc.MealsCompleted, Is.Zero);
        Assert.That(npc.DrinksCompleted, Is.Zero);
    }

    [Test]
    public void TwoNpcsCannotConsumeTheSameLastUnit()
    {
        int available = 1;
        Func<bool> consume = () => available > 0 && --available >= 0;
        var supplies = new SupplySettings { initialSatiation = 20d,
            satiationLossPerHour = 0d, hydrationLossPerHour = 0d };
        var first = Create(supplies, roads: new TestRoad(tavern: 0d),
            consumeFood: consume, consumeDrink: () => false);
        var second = Create(supplies, roads: new TestRoad(tavern: 0d),
            consumeFood: consume, consumeDrink: () => false);
        first.AdvanceTo(30d, 7.5d);
        second.AdvanceTo(30d, 7.5d);
        Assert.That(available, Is.Zero);
        Assert.That(first.Satiation, Is.EqualTo(100d));
        Assert.That(second.Satiation, Is.EqualTo(20d));
        Assert.That(first.MealsCompleted + second.MealsCompleted, Is.EqualTo(1d));
    }

    [Test]
    public void LongWaitWithFiniteStockMatchesSmallTicksWithoutSkippingWithdrawals()
    {
        int jumpFood = 3, jumpDrinks = 4, tickFood = 3, tickDrinks = 4;
        var jumped = Create(consumeFood: () => jumpFood > 0 && --jumpFood >= 0,
            consumeDrink: () => jumpDrinks > 0 && --jumpDrinks >= 0);
        var ticking = Create(consumeFood: () => tickFood > 0 && --tickFood >= 0,
            consumeDrink: () => tickDrinks > 0 && --tickDrinks >= 0);
        const double end = 8d * 1440d;
        jumped.AdvanceTo(end, 7.5d);
        for (double t = 0.37d; t < end; t += 0.37d) ticking.AdvanceTo(t, 7.5d);
        ticking.AdvanceTo(end, 7.5d);
        Assert.That(jumpFood, Is.EqualTo(tickFood));
        Assert.That(jumpDrinks, Is.EqualTo(tickDrinks).And.EqualTo(0));
        Assert.That(jumped.MealsCompleted, Is.EqualTo(ticking.MealsCompleted));
        Assert.That(jumped.DrinksCompleted, Is.EqualTo(ticking.DrinksCompleted));
        Assert.That(jumped.Satiation, Is.EqualTo(ticking.Satiation).Within(1e-5));
        Assert.That(jumped.Hydration, Is.EqualTo(ticking.Hydration).Within(1e-5));
        Assert.That(jumped.Energy, Is.EqualTo(ticking.Energy).Within(1e-5));
        Assert.That(jumped.State, Is.EqualTo(ticking.State));
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
