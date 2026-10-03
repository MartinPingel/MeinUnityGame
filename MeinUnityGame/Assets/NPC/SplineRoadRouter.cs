using System;
using UnityEngine;

namespace Village.Npc
{
    /// <summary>
    /// Read-only routing along a single RoadSpline's dense, terrain-snapped sample points -
    /// the curved/sloped counterpart to RoadRouter's axis-aligned box strips. Only used when
    /// an NpcAgent's road root carries a RoadSpline (see NpcAgent.SceneNavigation); every
    /// existing BoxCollider-based road is untouched and keeps using RoadRouter exactly as
    /// before. No changes to the terrain, roads, or scene navigation settings.
    /// </summary>
    public static class SplineRoadRouter
    {
        // Horizontal only: height always comes from the spline's own terrain sample, never
        // from whatever Y a hand-placed marker happens to have.
        private const float Tolerance = 0.5f;

        public static int NearestIndex(Vector3[] samples, Vector3 point)
        {
            int best = 0;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < samples.Length; i++)
            {
                float distance = HorizontalDistance(samples[i], point);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        /// <summary>Snaps a point to its nearest sample's exact position (including terrain
        /// height), so a place defined "on" the spline never drifts from the route's own
        /// endpoints by even a tiny height mismatch.</summary>
        public static Vector3 SnapToNearestSample(RoadSpline spline, Vector3 point)
        {
            if (spline == null) throw new ArgumentNullException(nameof(spline));
            Vector3[] samples = spline.Samples;
            return samples[NearestIndex(samples, point)];
        }

        public static Vector3[] FindRoute(RoadSpline spline, Vector3 from, Vector3 to)
        {
            if (spline == null) throw new ArgumentNullException(nameof(spline));
            Vector3[] samples = spline.Samples;
            if (samples.Length < 2) throw new InvalidOperationException("Road spline has too few samples.");

            int fromIndex = NearestIndex(samples, from);
            int toIndex = NearestIndex(samples, to);
            if (HorizontalDistance(samples[fromIndex], from) > Tolerance ||
                HorizontalDistance(samples[toIndex], to) > Tolerance)
                throw new InvalidOperationException("NPC access point must lie on the road spline.");

            int step = fromIndex <= toIndex ? 1 : -1;
            int count = Mathf.Abs(toIndex - fromIndex) + 1;
            var path = new Vector3[count];
            for (int i = 0; i < count; i++) path[i] = samples[fromIndex + i * step];
            return path;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
