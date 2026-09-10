using System;

namespace Village.Npc
{
    public enum NpcState { Home, GoingToWork, Working, GoingHome, Sleeping,
        GoingToEat, Eating, GoingToDrink, Drinking }

    [Serializable]
    public sealed class WorkSchedule
    {
        public int startHour = 8;
        public int endHour = 17;
        public bool commuteBeforeWork;

        public bool IsWorkTime(double minutes)
        {
            double hour = (minutes % 1440d) / 60d;
            return startHour < endHour
                ? hour >= startHour && hour < endHour
                : hour >= startHour || hour < endHour;
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

        public void Validate()
        {
            if (startHour < 0 || startHour > 23 || endHour < 0 || endHour > 23 || startHour == endHour)
                throw new ArgumentException("Work hours must be distinct hours between 0 and 23.");
        }
    }

    // Existing scene data stays compatible; simulation converts these to Energy = 100 - Fatigue.
    [Serializable]
    public sealed class FatigueSettings
    {
        public double initialFatigue = 64d;
        public double gainPerAwakeHour = 4d;
        public double recoveryPerSleepHour = 8d;
        public double sleepThreshold = 80d;
        public double wakeThreshold = 16d;

        public void Validate()
        {
            double[] values = { initialFatigue, gainPerAwakeHour, recoveryPerSleepHour, sleepThreshold, wakeThreshold };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("Fatigue settings must be finite.");
            if (initialFatigue < 0d || initialFatigue > 100d || gainPerAwakeHour <= 0d ||
                recoveryPerSleepHour <= 0d || wakeThreshold < 0d || sleepThreshold > 100d ||
                wakeThreshold >= sleepThreshold)
                throw new ArgumentException("Invalid fatigue rates or sleep/wake thresholds.");
        }
    }

    [Serializable]
    public sealed class SupplySettings
    {
        public double initialSatiation = 100d;
        public double initialHydration = 100d;
        public double satiationLossPerHour = 2d;
        public double hydrationLossPerHour = 4d;
        public double hungerThreshold = 20d;
        public double thirstThreshold = 20d;
        public double eatingMinutes = 30d;
        public double drinkingMinutes = 10d;

        public void Validate()
        {
            double[] values = { initialSatiation, initialHydration, satiationLossPerHour,
                hydrationLossPerHour, hungerThreshold, thirstThreshold, eatingMinutes, drinkingMinutes };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value))
                    throw new ArgumentException("Supply settings must be finite.");
            if (initialSatiation < 0d || initialSatiation > 100d ||
                initialHydration < 0d || initialHydration > 100d ||
                satiationLossPerHour < 0d || hydrationLossPerHour < 0d ||
                hungerThreshold < 0d || hungerThreshold >= 100d ||
                thirstThreshold < 0d || thirstThreshold >= 100d ||
                eatingMinutes <= 0d || drinkingMinutes <= 0d)
                throw new ArgumentException("Invalid supply levels, rates, thresholds or service duration.");
        }
    }
}
