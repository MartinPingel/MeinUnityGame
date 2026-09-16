using System;

namespace Village.Npc
{
    public enum NpcState { Home, GoingToWork, Working, GoingHome, Sleeping,
        GoingToEat, Eating, GoingToDrink, Drinking, GoingToDeliver, Delivering, GoingToCollect, Collecting, GoingToGetTool, CollectingTool, ReturningWithTool, UnloadingTool, GoingToSocial, WaitingForSocialPlace, WaitingForCompany, Socialising, WaitingForService, GoingToServicePickup, ServingGuest, GoingToChurch, WaitingForChurchPlace, AtChurch }

    [Serializable]
    public sealed class WorkSchedule
    {
        public int startHour = 8;
        public int endHour = 17;
        public bool commuteBeforeWork;
        // Opt-in: keep working past endHour, still bounded by higher-priority needs,
        // until today's WorkedMinutes reaches the full scheduled duration. Off by
        // default so every existing NPC keeps its current end-of-shift behavior.
        public bool extendForBreaks;
        // Opt-in: count travel to/from the workplace as work time too (WorkedMinutes,
        // the elevated working hunger/thirst rate, extendForBreaks). Off by default:
        // ordinary commuting keeps its existing, uncounted behavior.
        public bool travelCountsAsWork;
        // Opt-in: restrict which weekdays this schedule is active on. Bit 0 = Monday
        // ... bit 6 = Sunday. Default = every day, preserving existing behavior.
        public int workDaysMask = 0b1111111;
        // Opt-in: Sunday uses sundayStartHour/sundayEndHour instead of startHour/endHour,
        // and never extends for breaks (see NpcSimulation). Off by default: an NPC without
        // this keeps using startHour/endHour and its normal extendForBreaks on every day,
        // Sunday included, exactly as before.
        public bool sundayOverride;
        public int sundayStartHour = 8;
        public int sundayEndHour = 17;
        // Opt-in: a fixed one-hour breakfast at the tavern immediately before startHour,
        // Monday-Saturday only (Sunday keeps its separate church/Sunday rules, untouched).
        // Off by default: an NPC without this keeps its existing hunger/thirst-only tavern
        // visits, unaffected.
        public bool breakfastEnabled;
        // Opt-in: sleep at a fixed clock window (sleepStartHour-sleepEndHour) every day,
        // instead of the default "right after today's work concludes". Off by default: an
        // NPC without this keeps its existing schedule-driven sleep timing, unaffected. Only
        // meant for NPCs whose whole shift (every day it runs) already sits outside the fixed
        // window - it does not itself avoid a work/sleep collision.
        public bool fixedSleepSchedule;
        public int sleepStartHour = 22;
        public int sleepEndHour = 6;

        private static bool IsSunday(double minutes) => (int)Math.Floor(minutes / 1440d) % 7 == 6; // day 0 = Monday

        public bool IsWorkTime(double minutes)
        {
            if (workDaysMask != 0b1111111)
            {
                int weekdayBit = (int)Math.Floor(minutes / 1440d) % 7; // day 0 = Monday
                if ((workDaysMask & (1 << weekdayBit)) == 0) return false;
            }
            bool sunday = sundayOverride && IsSunday(minutes);
            int sh = sunday ? sundayStartHour : startHour;
            int eh = sunday ? sundayEndHour : endHour;
            double hour = (minutes % 1440d) / 60d;
            return sh < eh
                ? hour >= sh && hour < eh
                : hour >= sh || hour < eh;
        }

        // True while sundayOverride suppresses the normal extendForBreaks/elevated working
        // rate for today (Sunday): the shorter Sunday shift must never be extended to catch
        // up missed hours, and must never trigger a mid-shift meal break.
        internal bool SuppressesExtendedWork(double minutes) => sundayOverride && IsSunday(minutes);

        // The breakfast hour is always [startHour-1, startHour), regardless of any Sunday
        // override, and never applies on Sunday - Sunday keeps its own separate rules.
        internal bool IsBreakfastTime(double minutes)
        {
            if (!breakfastEnabled || IsSunday(minutes)) return false;
            int breakfastHour = (startHour - 1 + 24) % 24;
            double hour = (minutes % 1440d) / 60d;
            return breakfastHour < startHour
                ? hour >= breakfastHour && hour < startHour
                : hour >= breakfastHour || hour < startHour;
        }

        // Mirrors NextBoundary for the breakfast window's own start/end, so a large time
        // jump never skips over it unevaluated (same reasoning as churchSchedule.NextBoundary).
        internal double NextBreakfastBoundary(double minutes)
        {
            if (!breakfastEnabled) return double.PositiveInfinity;
            double day = Math.Floor(minutes / 1440d) * 1440d;
            double start = day + ((startHour - 1 + 24) % 24) * 60d;
            double end = day + startHour * 60d;
            if (start <= minutes) start += 1440d;
            if (end <= minutes) end += 1440d;
            return Math.Min(start, end);
        }

        public double NextBoundary(double minutes)
        {
            double day = Math.Floor(minutes / 1440d) * 1440d;
            double start = day + startHour * 60d;
            double end = day + endHour * 60d;
            if (start <= minutes) start += 1440d;
            if (end <= minutes) end += 1440d;
            return Math.Min(start, end);
        }

        // Opt-in (fixedSleepSchedule): true during the fixed daily sleep window, regardless
        // of work hours - callers are responsible for only enabling this where that window
        // never overlaps the NPC's own shift (see NpcSimulation).
        internal bool IsFixedSleepTime(double minutes)
        {
            if (!fixedSleepSchedule) return false;
            double hour = (minutes % 1440d) / 60d;
            return sleepStartHour < sleepEndHour
                ? hour >= sleepStartHour && hour < sleepEndHour
                : hour >= sleepStartHour || hour < sleepEndHour;
        }

        // Mirrors NextBoundary for the fixed sleep window's own start/end, so a large time
        // jump never skips over it unevaluated.
        internal double NextFixedSleepBoundary(double minutes)
        {
            if (!fixedSleepSchedule) return double.PositiveInfinity;
            double day = Math.Floor(minutes / 1440d) * 1440d;
            double start = day + sleepStartHour * 60d;
            double end = day + sleepEndHour * 60d;
            if (start <= minutes) start += 1440d;
            if (end <= minutes) end += 1440d;
            return Math.Min(start, end);
        }

        public void Validate()
        {
            if (startHour < 0 || startHour > 23 || endHour < 0 || endHour > 23 || startHour == endHour)
                throw new ArgumentException("Work hours must be distinct hours between 0 and 23.");
            if (workDaysMask <= 0 || workDaysMask > 0b1111111)
                throw new ArgumentException("Work days mask must select at least one weekday.");
            if (sundayOverride && (sundayStartHour < 0 || sundayStartHour > 23 ||
                sundayEndHour < 0 || sundayEndHour > 23 || sundayStartHour == sundayEndHour))
                throw new ArgumentException("Sunday work hours must be distinct hours between 0 and 23.");
            if (fixedSleepSchedule && (sleepStartHour < 0 || sleepStartHour > 23 ||
                sleepEndHour < 0 || sleepEndHour > 23 || sleepStartHour == sleepEndHour))
                throw new ArgumentException("Fixed sleep hours must be distinct hours between 0 and 23.");
        }
    }

    [Serializable]
    public sealed class SupplySettings
    {
        public double initialSatiation = 100d;
        public double initialHydration = 100d;
        public double satiationLossPerHour = 2d;
        // Opt-in keeps older scenes/callers unchanged; configured per NPC in the Inspector.
        public double productiveSatiationMultiplier = 1d;
        // Opt-in additional rate while State == Working, regardless of production gating
        // (unlike productiveSatiationMultiplier). Zero keeps existing NPCs unchanged.
        public double workingSatiationLossPerHour;
        public double hydrationLossPerHour = 4d;
        // Opt-in hydration equivalent of workingSatiationLossPerHour. Zero by default.
        public double workingHydrationLossPerHour;
        public double hungerThreshold = 20d;
        public double thirstThreshold = 20d;
        public double eatingMinutes = 30d;
        public double drinkingMinutes = 10d;

        public void Validate()
        {
            double[] values = { initialSatiation, initialHydration, satiationLossPerHour, productiveSatiationMultiplier,
                workingSatiationLossPerHour, hydrationLossPerHour, workingHydrationLossPerHour,
                hungerThreshold, thirstThreshold, eatingMinutes, drinkingMinutes };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("Supply settings must be finite.");
            if (initialSatiation < 0d || initialSatiation > 100d ||
                initialHydration < 0d || initialHydration > 100d ||
                satiationLossPerHour < 0d || productiveSatiationMultiplier < 1d || workingSatiationLossPerHour < 0d ||
                double.IsInfinity(satiationLossPerHour * productiveSatiationMultiplier) ||
                hydrationLossPerHour < 0d || workingHydrationLossPerHour < 0d ||
                hungerThreshold < 0d || hungerThreshold >= 100d ||
                thirstThreshold < 0d || thirstThreshold >= 100d ||
                eatingMinutes <= 0d || drinkingMinutes <= 0d)
                throw new ArgumentException("Invalid supply levels, rates, thresholds or service duration.");
        }
    }
}

