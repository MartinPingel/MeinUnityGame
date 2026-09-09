using System;

namespace Village.Npc
{
    public enum NpcState { Home, GoingToWork, Working, GoingHome, Sleeping }

    [Serializable]
    public sealed class WorkSchedule
    {
        public int startHour = 8;
        public int endHour = 17;

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
}
