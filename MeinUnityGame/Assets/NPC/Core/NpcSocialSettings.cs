using System;

namespace Village.Npc
{
    [Serializable]
    public sealed class NpcSocialSettings
    {
        public double initialValue = 100d;
        public double lossPerHour = 3d;
        public double needThreshold = 35d;
        public double recoveryPerHour = 30d;
        public double satisfiedValue = 85d;
        public void Validate()
        {
            foreach (double v in new[] { initialValue, lossPerHour, needThreshold, recoveryPerHour, satisfiedValue })
                if (double.IsNaN(v) || double.IsInfinity(v)) throw new ArgumentException("Social values must be finite.");
            if (initialValue < 0 || initialValue > 100 || lossPerHour < 0 ||
                needThreshold < 0 || satisfiedValue > 100 || satisfiedValue <= needThreshold || recoveryPerHour <= 0)
                throw new ArgumentException("Invalid social levels or rates.");
        }
    }
}
