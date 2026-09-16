using System;
using NUnit.Framework;
using Village.Npc;

public sealed class NpcSimulationTests
{
    private static NpcSimulation Create(double length = 150d)
    {
        return new NpcSimulation(length, new WorkSchedule());
    }

    [Test]
    public void ShiftStartingBeforeEightAmNeverCollidesWithFirstDayBootstrap()
    {
        // A schedule starting before 08:00 (e.g. Justus/Jan/Michael/Florian's real 07:00
        // shifts) would collide with a naive "always sleep a full 8 hours from minute zero"
        // boot, since that session would only end at 08:00. Booting already rested (never
        // mid-sleep) avoids it entirely, on day one exactly as on every later day.
        var npc = new NpcSimulation(200d, new WorkSchedule { startHour = 7, endHour = 17 });
        Assert.That(npc.State, Is.Not.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(420d, 7.5d); // 07:00, shift start.
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingToWork));
    }

    [Test]
    public void WorkRequiresArrivalAndEndsWithSleepRightAfter()
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
        // Sleep is scheduled, not energy-triggered: the moment this NPC gets home after the
        // shift ends, it goes straight to sleep rather than sitting idle first.
        npc.AdvanceTo(1040d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
    }

    [Test]
    public void SleepAlwaysLastsExactlyEightHoursEveryTime()
    {
        var npc = Create();
        // Boots already rested (never mid-sleep at minute zero), so the very first shift runs
        // uninterrupted - the first sleep of the game only comes right after it.
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        npc.AdvanceTo(1040d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.SleptMinutes, Is.Zero);
        npc.AdvanceTo(1519d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(1520d, 7.5d);
        // Exactly 8 hours after arriving home (1040 + 480 = 1520): wakes, never early or late.
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.SleptMinutes, Is.EqualTo(480d).Within(1e-7));
        npc.AdvanceTo(2480d, 7.5d);
        // Home again right after day 2's shift: a second full session starts fresh from here.
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(2959d, 7.5d);
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        npc.AdvanceTo(2960d, 7.5d);
        // Exactly 8 hours after arriving home the second time (2480 + 480 = 2960): the second
        // session also lasts precisely 8 hours, not a minute more or less.
        Assert.That(npc.State, Is.EqualTo(NpcState.Home));
        Assert.That(npc.SleptMinutes, Is.EqualTo(960d).Within(1e-7));
    }

    [Test]
    public void ExtendForBreaksCatchUpDelaysSleepUntilTodaysTargetIsMet()
    {
        // A long commute (300m at 7.5m/min = 40 minutes) eats into the shift, so without
        // catch-up this NPC would only work 500 of its scheduled 540 minutes. extendForBreaks
        // must keep it working past the nominal end hour until the full target is met - sleep
        // must not cut that catch-up short just because the nominal shift has "ended".
        var npc = new NpcSimulation(300d, new WorkSchedule { startHour = 8, endHour = 17, extendForBreaks = true });
        npc.AdvanceTo(1020d, 7.5d);
        // 17:00: nominal shift end, but only 500 of 540 minutes worked - still working.
        Assert.That(npc.State, Is.EqualTo(NpcState.Working));
        Assert.That(npc.WorkedMinutes, Is.EqualTo(500d).Within(1e-7));
        npc.AdvanceTo(1060d, 7.5d);
        // 17:40: the full 540 minutes is now worked - only now does it head home.
        Assert.That(npc.State, Is.EqualTo(NpcState.GoingHome));
        Assert.That(npc.WorkedMinutes, Is.EqualTo(540d).Within(1e-7));
        npc.AdvanceTo(1100d, 7.5d);
        // 18:20, arrived home (40-minute walk): goes straight to sleep, as normal.
        Assert.That(npc.State, Is.EqualTo(NpcState.Sleeping));
        Assert.That(npc.DistanceFromHome, Is.Zero);
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
    public void RealisticCommuteSleepOnsetNeverDriftsAcrossManyWeeks()
    {
        // Mirrors a real Dorf1 day-shift worker: a 07:00-17:00 shift with a real (non-zero)
        // walk home. Sleep is scheduled off the work hours themselves, so - unlike the old
        // energy-threshold rule - there is nothing left that can drift: the onset time of day
        // must be identical on day zero and six and a half weeks later.
        NpcSimulation Build() => new NpcSimulation(220d, new WorkSchedule { startHour = 7, endHour = 17 });

        var early = Build();
        double earlyOnset = NextSleepOnset(early);

        // A single large jump most of the way there - the "große Zeitsprünge" case.
        var late = Build();
        late.AdvanceTo(45d * 1440d, 7.5d);
        double lateOnset = NextSleepOnset(late);

        Assert.That(lateOnset, Is.EqualTo(earlyOnset).Within(1e-6));
    }

    [Test]
    public void LisasOvernightShiftGetsAStableDaytimeSleepWindow()
    {
        // Lisa's 23:00-10:00 shift crosses midnight, so her free (and thus sleep-eligible)
        // hours are entirely during the day. Sleep must settle into a stable daytime window
        // that never touches 23:00-10:00, on day zero and just as reliably weeks later.
        NpcSimulation Build() => new NpcSimulation(150d, new WorkSchedule { startHour = 23, endHour = 10 });

        var early = Build();
        // Already on shift at minute zero (00:00 falls inside 23:00-10:00): no sleep yet.
        Assert.That(early.State, Is.Not.EqualTo(NpcState.Sleeping));
        double earlyOnset = NextSleepOnset(early);
        // The onset must fall strictly after the shift ends (10:00 = minute 600) and strictly
        // before it starts again (23:00 = minute 1380) - a real daytime window, not a sliver
        // overlapping either end of the shift.
        Assert.That(earlyOnset, Is.GreaterThan(600d));
        Assert.That(earlyOnset, Is.LessThan(1380d));

        var late = Build();
        late.AdvanceTo(45d * 1440d, 7.5d);
        double lateOnset = NextSleepOnset(late);
        Assert.That(lateOnset, Is.EqualTo(earlyOnset).Within(1e-6));

        // Across many weeks of ordinary ticking, sleep must never overlap the shift itself.
        // Each night's 23:00-10:00 window is checked hour by hour, in real chronological
        // order (23:00 of night N, then 00:00..09:00 of the following calendar day).
        var ticking = Build();
        for (int night = 0; night < 45; night++)
            for (int hour = 0; hour < 11; hour++)
            {
                double t = night * 1440d + 1380d + hour * 60d;
                ticking.AdvanceTo(t, 7.5d);
                Assert.That(ticking.State, Is.Not.EqualTo(NpcState.Sleeping),
                    $"asleep during the night shift, night {night}, {hour} hour(s) after 23:00");
            }
    }

    [Test]
    public void RealisticCommuteMultiDayWaitMatchesOrdinaryTicks()
    {
        // Same realistic shift/commute pairing as above, checking that a single large jump (as
        // a fast-forward control would issue) lands exactly where incremental per-tick
        // simulation does - the "große Zeitsprünge" case, not just ordinary per-frame ticking.
        NpcSimulation Build() => new NpcSimulation(220d, new WorkSchedule { startHour = 7, endHour = 17 });
        const double destination = 45d * 1440d + 611d;
        var jumped = Build();
        var ticking = Build();
        jumped.AdvanceTo(destination, 7.5d);
        for (double t = 3.7d; t < destination; t += 3.7d) ticking.AdvanceTo(t, 7.5d);
        ticking.AdvanceTo(destination, 7.5d);
        AssertEquivalent(jumped, ticking, 1e-5);
    }

    [Test]
    public void InvalidTimeAndSpeedDoNotChangeState()
    {
        var npc = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(-1d, 7.5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(double.NaN, 7.5d));
        Assert.Throws<ArgumentOutOfRangeException>(() => npc.AdvanceTo(60d, 0d));
        Assert.That(npc.TotalMinutes, Is.Zero);
    }

    // Advances minute-by-minute (a large jump may land mid-cycle) until the NPC has left any
    // sleep already in progress and then just entered the next one, returning the time of day
    // (minutes since midnight) that onset happened at.
    private static double NextSleepOnset(NpcSimulation npc)
    {
        while (npc.State == NpcState.Sleeping) npc.AdvanceTo(npc.TotalMinutes + 1d, 7.5d);
        while (npc.State != NpcState.Sleeping) npc.AdvanceTo(npc.TotalMinutes + 1d, 7.5d);
        return npc.TotalMinutes % 1440d;
    }

    private static void AssertEquivalent(NpcSimulation a, NpcSimulation b, double tolerance)
    {
        Assert.That(a.State, Is.EqualTo(b.State));
        Assert.That(a.TotalMinutes, Is.EqualTo(b.TotalMinutes));
        Assert.That(a.DistanceFromHome, Is.EqualTo(b.DistanceFromHome).Within(tolerance));
        Assert.That(a.WorkedMinutes, Is.EqualTo(b.WorkedMinutes).Within(tolerance));
        Assert.That(a.SleptMinutes, Is.EqualTo(b.SleptMinutes).Within(tolerance));
        Assert.That(a.TravelledMetres, Is.EqualTo(b.TravelledMetres).Within(tolerance));
    }
}
