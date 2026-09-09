using System;
using System.Collections.Generic;
using System.Globalization;

namespace Village.Npc
{
    /// <summary>
    /// Deterministic, Unity-independent simulation of work, rest and travel along one
    /// reversible home/work route. Advances at decision boundaries, never frame by frame.
    /// Add future needs to this model's boundary calculation and state transitions.
    /// </summary>
    public sealed class NpcSimulation
    {
        private const double Epsilon = 1e-9;
        private readonly WorkSchedule work;
        private readonly FatigueSettings fatigue;
        private bool seekingRest;

        public double TotalMinutes { get; private set; }
        public double Fatigue { get; private set; }
        public double RouteLength { get; }
        public double DistanceFromHome { get; private set; }
        public NpcState State { get; private set; }
        public double WorkedMinutes { get; private set; }
        public double SleptMinutes { get; private set; }
        public double TravelledMetres { get; private set; }
        public bool IsWorkTime => work.IsWorkTime(TotalMinutes);
        public bool NeedsRest => seekingRest;

        public NpcSimulation(double routeLength, WorkSchedule schedule, FatigueSettings settings)
        {
            schedule.Validate();
            settings.Validate();
            if (double.IsNaN(routeLength) || double.IsInfinity(routeLength) || routeLength <= 0d)
                throw new ArgumentOutOfRangeException(nameof(routeLength));
            // Own copies keep an in-progress simulation stable if inspector data changes.
            work = new WorkSchedule { startHour = schedule.startHour, endHour = schedule.endHour };
            fatigue = new FatigueSettings
            {
                initialFatigue = settings.initialFatigue,
                gainPerAwakeHour = settings.gainPerAwakeHour,
                recoveryPerSleepHour = settings.recoveryPerSleepHour,
                sleepThreshold = settings.sleepThreshold,
                wakeThreshold = settings.wakeThreshold
            };
            RouteLength = routeLength;
            Fatigue = fatigue.initialFatigue;
            State = NpcState.Sleeping; // The baseline is day 1 at midnight, at home.
            seekingRest = true;
            Decide();
        }

        public void AdvanceTo(double targetMinutes, double metresPerGameMinute)
        {
            if (double.IsNaN(targetMinutes) || double.IsInfinity(targetMinutes) || targetMinutes < TotalMinutes)
                throw new ArgumentOutOfRangeException(nameof(targetMinutes));
            if (double.IsNaN(metresPerGameMinute) || double.IsInfinity(metresPerGameMinute) || metresPerGameMinute <= 0d)
                throw new ArgumentOutOfRangeException(nameof(metresPerGameMinute));

            // Exact repeated midnight states can be fast-forwarded, including all totals.
            // Future needs must extend the key and accumulated totals, or disable this optimization.
            Dictionary<string, Snapshot> midnights = targetMinutes - TotalMinutes >= 2880d
                ? new Dictionary<string, Snapshot>() : null;
            while (targetMinutes - TotalMinutes > Epsilon)
            {
                Decide();
                if (midnights != null && TotalMinutes % 1440d == 0d)
                {
                    string key = ((int)State) + "|" + seekingRest + "|" +
                        Fatigue.ToString("R", CultureInfo.InvariantCulture) + "|" +
                        DistanceFromHome.ToString("R", CultureInfo.InvariantCulture);
                    if (midnights.TryGetValue(key, out Snapshot previous))
                    {
                        double period = TotalMinutes - previous.time;
                        double cycles = period > 0d ? Math.Floor((targetMinutes - TotalMinutes) / period) : 0d;
                        if (cycles > 0d)
                        {
                            WorkedMinutes += (WorkedMinutes - previous.worked) * cycles;
                            SleptMinutes += (SleptMinutes - previous.slept) * cycles;
                            TravelledMetres += (TravelledMetres - previous.travelled) * cycles;
                            TotalMinutes += period * cycles;
                            midnights.Clear();
                            continue;
                        }
                    }
                    else
                    {
                        midnights.Add(key, new Snapshot
                        {
                            time = TotalMinutes, worked = WorkedMinutes,
                            slept = SleptMinutes, travelled = TravelledMetres
                        });
                    }
                }

                double nextMidnight = (Math.Floor(TotalMinutes / 1440d) + 1d) * 1440d;
                double step = Math.Min(targetMinutes - TotalMinutes,
                    Math.Min(work.NextBoundary(TotalMinutes), nextMidnight) - TotalMinutes);
                double arrivalIn = double.PositiveInfinity;
                double fatigueIn = double.PositiveInfinity;
                bool sleeping = State == NpcState.Sleeping;
                bool toWork = State == NpcState.GoingToWork;
                bool toHome = State == NpcState.GoingHome;

                if (toWork) arrivalIn = (RouteLength - DistanceFromHome) / metresPerGameMinute;
                if (toHome) arrivalIn = DistanceFromHome / metresPerGameMinute;
                if (sleeping)
                    fatigueIn = (Fatigue - fatigue.wakeThreshold) / (fatigue.recoveryPerSleepHour / 60d);
                else if (!seekingRest)
                    fatigueIn = (fatigue.sleepThreshold - Fatigue) / (fatigue.gainPerAwakeHour / 60d);

                step = Math.Min(step, Math.Min(arrivalIn, fatigueIn));
                if (step <= 0d)
                    throw new InvalidOperationException("NPC simulation could not advance.");

                if (State == NpcState.Working) WorkedMinutes += step;
                if (sleeping) SleptMinutes += step;
                Fatigue = Math.Max(0d, Math.Min(100d, Fatigue + step *
                    (sleeping ? -fatigue.recoveryPerSleepHour : fatigue.gainPerAwakeHour) / 60d));
                if (toWork || toHome)
                {
                    double distance = Math.Min(metresPerGameMinute * step,
                        toWork ? RouteLength - DistanceFromHome : DistanceFromHome);
                    DistanceFromHome += toWork ? distance : -distance;
                    TravelledMetres += distance;
                    if (step == arrivalIn) DistanceFromHome = toWork ? RouteLength : 0d;
                }
                if (step == fatigueIn)
                    Fatigue = sleeping ? fatigue.wakeThreshold : fatigue.sleepThreshold;
                TotalMinutes += step;
            }

            TotalMinutes = targetMinutes;
            Decide();
        }

        private void Decide()
        {
            if (State == NpcState.Sleeping && Fatigue > fatigue.wakeThreshold + Epsilon)
                return;
            if (State == NpcState.Sleeping)
                seekingRest = false;
            if (Fatigue >= fatigue.sleepThreshold - Epsilon)
                seekingRest = true;

            if (DistanceFromHome < Epsilon) DistanceFromHome = 0d;
            if (RouteLength - DistanceFromHome < Epsilon) DistanceFromHome = RouteLength;

            if (seekingRest)
                State = DistanceFromHome == 0d ? NpcState.Sleeping : NpcState.GoingHome;
            else if (work.IsWorkTime(TotalMinutes))
                State = DistanceFromHome == RouteLength ? NpcState.Working : NpcState.GoingToWork;
            else
                State = DistanceFromHome == 0d ? NpcState.Home : NpcState.GoingHome;
        }

        private struct Snapshot
        {
            public double time, worked, slept, travelled;
        }
    }
}
