using System;

namespace Village.Npc
{
    public enum NpcPlace { Home, Work, Tavern, Well, Delivery, Pickup }

    /// <summary>Unity-independent position used by the time simulation.</summary>
    public struct NpcPoint
    {
        public double X, Y, Z;
        public NpcPoint(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static double Distance(NpcPoint a, NpcPoint b)
        {
            double x = b.X - a.X, y = b.Y - a.Y, z = b.Z - a.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }
        public static NpcPoint Lerp(NpcPoint a, NpcPoint b, double t)
        {
            return new NpcPoint(a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }
    }

    /// <summary>Routes must start at the current road position and end at the requested place.</summary>
    public interface INpcNavigation
    {
        NpcPoint GetPlace(NpcPlace place);
        NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination);
    }

    // Preserves the original home/work-only constructor for existing callers/tests.
    internal sealed class HomeWorkNavigation : INpcNavigation
    {
        private readonly double length;
        public HomeWorkNavigation(double length)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length <= 0d)
                throw new ArgumentOutOfRangeException(nameof(length));
            this.length = length;
        }
        public NpcPoint GetPlace(NpcPlace place)
        {
            return new NpcPoint(place == NpcPlace.Work ? length : 0d, 0d, 0d);
        }
        public NpcPoint[] FindRoute(NpcPoint from, NpcPlace destination)
        {
            return new[] { from, GetPlace(destination) };
        }
    }
}
