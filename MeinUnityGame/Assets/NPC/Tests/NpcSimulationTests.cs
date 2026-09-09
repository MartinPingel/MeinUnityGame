using System;
using NUnit.Framework;
using Village.Npc;

public sealed class NpcSimulationTests
{
    private static NpcSimulation Create(double length = 150d, FatigueSettings fatigue = null)
    {
        return new NpcSimulation(length, new WorkSchedule(), fatigue ?? new FatigueSettings());
    }

    [Test]
    public void WorkRequiresArrivalAndEndsWithReturnHome()
    {
        var npc = Create();
        npc.AdvanceTo(480d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToWork));
        Assert.That(npc.WorkedMinutes, Is.Zero);
        npc.AdvanceTo(490d, 7.5d);
        Assert.That(npc.DistanceFromHome, Is.EqualTo(75d).Within(1e-7));
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToWork));
        npc.AdvanceTo(500d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        npc.AdvanceTo(1020d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.WorkedMinutes, Is.EqualTo(520d).Within(1e-7));
        npc.AdvanceTo(1040d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.DistanceFromHome, Is.Zero);
    }

    [Test]
    public void MidnightSleepAndWakeAreAccountedFor()
    {
        var npc = Create();
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(360d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.Fatigue, Is.EqualTo(16d).Within(1e-7));
        npc.AdvanceTo(1320d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(1440d, 7.5d);
        Assert.That(npc.Fatigue, Is.EqualTo(64d).Within(1e-7));
        Assert.That(npc.SleptMinutes, Is.EqualTo(480d).Within(1e-7));
        npc.AdvanceTo(1800d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.Fatigue, Is.EqualTo(16d).Within(1e-7));
    }

    [Test]
    public void SevereFatigueOverridesWorkAndSleepRequiresGettingHome()
    {
        var npc = Create(fatigue: new FatigueSettings { gainPerAwakeHour = 12d });
        npc.AdvanceTo(680d, 7.5d);
        Assert.That(npc.IsWorkTime, Is.True);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        npc.AdvanceTo(690d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.DistanceFromHome, Is.GreaterThan(0d));
        npc.AdvanceTo(700d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
        npc.AdvanceTo(1210d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.Fatigue, Is.EqualTo(16d).Within(1e-6));
    }

    [TestCase(7.5d)]
    [TestCase(3d)]
    public void MultiDayWaitMatchesOrdinaryTicksIncludingPartialTravel(double speed)
    {
        const double destination = 7d * 1440d + 491.25d;
        var jumped = Create(148.58d);
        var ticking = Create(148.58d);
        jumped.AdvanceTo(destination, speed);
        for (double t = 0.37d; t < destination; t += 0.37d) ticking.AdvanceTo(t, speed);
        ticking.AdvanceTo(destination, speed);
        AssertEquivalent(jumped, ticking, 1e-5);
    }

    [Test]
    public void LongJourneyCanBeAbandonedForSleepWithoutReachingWork()
    {
        var npc = Create(100000d);
        npc.AdvanceTo(1320d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.WorkedMinutes, Is.Zero);
        Assert.That(npc.DistanceFromHome, Is.GreaterThan(0d));
        npc.AdvanceTo(2160d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
    }

    [Test, Timeout(5000)]
    public void VeryLargeWaitPreservesRepeatedDailyTotals()
    {
        var npc = Create();
        const double days = 1000000d;
        npc.AdvanceTo(days * 1440d, 7.5d);
        Assert.That(npc.WorkedMinutes, Is.EqualTo(days * 520d).Within(0.01d));
        Assert.That(npc.SleptMinutes, Is.EqualTo(days * 480d).Within(0.01d));
        Assert.That(npc.TravelledMetres, Is.EqualTo(days * 300d).Within(0.01d));
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
    }

    [Test]
    public void InvalidTimeAndSpeedDoNotChangeState()
    {
        var npc = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(-1d, 7.5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(double.NaN, 7.5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(60d, 0d));
        Assert.That(npc.TotalMinutes, Is.Zero);
        Assert.That(npc.Fatigue, Is.EqualTo(64d));
    }

    private static void AssertEquivalent(NpcSimulation a, NpcSimulation b, double tolerance)
    {
        Assert.That(a.State, Is.EqualTo(b.State));
        Assert.That(a.TotalMinutes, Is.EqualTo(b.TotalMinutes));
        Assert.That(a.Fatigue, Is.EqualTo(b.Fatigue).Within(tolerance));
        Assert.That(a.DistanceFromHome, Is.EqualTo(b.DistanceFromHome).Within(tolerance));
        Assert.That(a.WorkedMinutes, Is.EqualTo(b.WorkedMinutes).Within(tolerance));
        Assert.That(a.SleptMinutes, Is.EqualTo(b.SleptMinutes).Within(tolerance));
        Assert.That(a.TravelledMetres, Is.EqualTo(b.TravelledMetres).Within(tolerance));
    }
}
