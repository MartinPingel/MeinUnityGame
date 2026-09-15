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
    public void SleepAlwaysLastsEightHoursAndEndsAtExactlyFullEnergy()
    {
        var npc = Create();
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.Fatigue, Is.EqualTo(64d));
        npc.AdvanceTo(360d, 7.5d);
        // Still short of the fixed 8 hours: regenerating continuously, but not yet full.
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.Energy, Is.EqualTo(84d).Within(1e-7));
        npc.AdvanceTo(480d, 7.5d);
        // Exactly 8 hours after going to sleep with Energy 36: exactly 100 now, never early.
        Assert.That(npc.Energy, Is.EqualTo(100d).Within(1e-7));
        Assert.That(npc.SleptMinutes, Is.EqualTo(480d).Within(1e-7));
        npc.AdvanceTo(1680d, 7.5d);
        // A second sleep, from a much lower rest value (20), still runs the full fixed
        // 8 hours rather than any shorter recovery-rate-based duration.
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.Energy, Is.EqualTo(20d).Within(1e-7));
        npc.AdvanceTo(2160d, 7.5d);
        Assert.That(npc.Energy, Is.EqualTo(100d).Within(1e-7));
        Assert.That(npc.SleptMinutes, Is.EqualTo(960d).Within(1e-7));
    }

    [Test]
    public void SevereFatigueOverridesWorkAndSleepRequiresGettingHome()
    {
        var npc = Create(fatigue: new FatigueSettings { gainPerAwakeHour = 12d });
        npc.AdvanceTo(890d, 7.5d);
        Assert.That(npc.IsWorkTime, Is.True);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.DistanceFromHome, Is.GreaterThan(0d));
        npc.AdvanceTo(900d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
        npc.AdvanceTo(1380d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.Fatigue, Is.EqualTo(0d).Within(1e-6));
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
        npc.AdvanceTo(1200d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.WorkedMinutes, Is.Zero);
        Assert.That(npc.DistanceFromHome, Is.GreaterThan(0d));
        npc.AdvanceTo(1680d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
    }

    [Test, Timeout(5000)]
    public void VeryLargeWaitPreservesRepeatedDailyTotals()
    {
        const double days = 1000000d;
        var direct = Create();
        direct.AdvanceTo(days * 1440d, 7.5d);
        // Every completed sleep is exactly 8 hours by construction, so however many sessions
        // a million days contain, the running total must always land on an exact multiple.
        Assert.That(direct.SleptMinutes % 480d, Is.EqualTo(0d).Within(1e-6));

        // A single huge jump must match reaching the same point via several staged calls,
        // proving the multi-day cycle-skip optimization stays correct under the fixed-duration
        // sleep rule (not just fast).
        var staged = Create();
        for (double d = 250000d; d <= days; d += 250000d) staged.AdvanceTo(d * 1440d, 7.5d);
        Assert.That(staged.State, Is.EqualTo(direct.State));
        Assert.That(staged.WorkedMinutes, Is.EqualTo(direct.WorkedMinutes).Within(1e-5));
        Assert.That(staged.SleptMinutes, Is.EqualTo(direct.SleptMinutes).Within(1e-5));
        Assert.That(staged.TravelledMetres, Is.EqualTo(direct.TravelledMetres).Within(1e-5));
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
