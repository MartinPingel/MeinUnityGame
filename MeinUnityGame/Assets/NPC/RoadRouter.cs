using System;
using System.Collections.Generic;
using UnityEngine;

namespace Village.Npc
{
    /// <summary>
    /// Read-only routing over the existing axis-aligned road strips.
    /// No changes to roads, building colliders or the scene's navigation settings.
    /// </summary>
    public static class RoadRouter
    {
        private const float Tolerance = 0.001f;

        public static Vector3[] FindRoute(Transform roadsRoot, Vector3 home, Vector3 workplace)
        {
            if (roadsRoot == null) throw new ArgumentNullException(nameof(roadsRoot));
            var strips = new List<Strip>();
            foreach (BoxCollider collider in roadsRoot.GetComponentsInChildren<BoxCollider>())
            {
                Bounds b = collider.bounds;
                if (!collider.enabled || collider.isTrigger) continue;
                Vector3 a = b.center, end = b.center;
                a.y = end.y = b.max.y;
                bool horizontal = b.size.x >= b.size.z;
                if (horizontal) { a.x = b.min.x; end.x = b.max.x; }
                else { a.z = b.min.z; end.z = b.max.z; }
                strips.Add(new Strip { a = a, b = end, horizontal = horizontal });
            }
            if (strips.Count == 0) throw new InvalidOperationException("No road strips found.");

            var nodes = new List<Vector3>();
            foreach (Strip s in strips) { Add(nodes, s.a); Add(nodes, s.b); }
            for (int i = 0; i < strips.Count; i++)
            for (int j = i + 1; j < strips.Count; j++)
            {
                Strip a = strips[i], b = strips[j];
                if (a.horizontal == b.horizontal) continue;
                Strip h = a.horizontal ? a : b, v = a.horizontal ? b : a;
                Vector3 cross = new Vector3(v.a.x, h.a.y, h.a.z);
                if (Contains(h, cross) && Contains(v, cross)) Add(nodes, cross);
            }

            int start = Attach(nodes, strips, home);
            int goal = Attach(nodes, strips, workplace);
            int count = nodes.Count;
            var edges = new List<int>[count];
            for (int i = 0; i < count; i++) edges[i] = new List<int>();
            // All endpoints split overlapping collinear strips as well as junctions.
            foreach (Strip strip in strips)
            {
                var indices = new List<int>();
                for (int i = 0; i < count; i++) if (Contains(strip, nodes[i])) indices.Add(i);
                indices.Sort((i, j) => Vector3.SqrMagnitude(nodes[i] - strip.a)
                    .CompareTo(Vector3.SqrMagnitude(nodes[j] - strip.a)));
                for (int i = 1; i < indices.Count; i++)
                {
                    int a = indices[i - 1], b = indices[i];
                    if (!edges[a].Contains(b)) edges[a].Add(b);
                    if (!edges[b].Contains(a)) edges[b].Add(a);
                }
            }

            var distance = new float[count];
            var previous = new int[count];
            var visited = new bool[count];
            for (int i = 0; i < count; i++) { distance[i] = float.PositiveInfinity; previous[i] = -1; }
            distance[start] = 0f;
            for (int pass = 0; pass < count; pass++)
            {
                int current = -1;
                for (int i = 0; i < count; i++)
                    if (!visited[i] && (current < 0 || distance[i] < distance[current])) current = i;
                if (current < 0 || float.IsPositiveInfinity(distance[current])) break;
                if (current == goal) break;
                visited[current] = true;
                foreach (int next in edges[current])
                {
                    float candidate = distance[current] + Vector3.Distance(nodes[current], nodes[next]);
                    if (candidate >= distance[next]) continue;
                    distance[next] = candidate;
                    previous[next] = current;
                }
            }

            if (float.IsPositiveInfinity(distance[goal]))
                throw new InvalidOperationException("Home and workplace are not connected by roads.");
            var path = new List<Vector3>();
            for (int at = goal; at >= 0; at = previous[at]) path.Add(nodes[at]);
            path.Reverse();
            return path.ToArray();
        }

        private static int Attach(List<Vector3> nodes, List<Strip> strips, Vector3 point)
        {
            foreach (Strip strip in strips)
                if (Contains(strip, point)) return Add(nodes, point);
            throw new InvalidOperationException("NPC access point must lie on a road centreline.");
        }

        private static bool Contains(Strip strip, Vector3 p)
        {
            if (Mathf.Abs(p.y - strip.a.y) > Tolerance) return false;
            return strip.horizontal
                ? Mathf.Abs(p.z - strip.a.z) <= Tolerance && p.x >= strip.a.x - Tolerance && p.x <= strip.b.x + Tolerance
                : Mathf.Abs(p.x - strip.a.x) <= Tolerance && p.z >= strip.a.z - Tolerance && p.z <= strip.b.z + Tolerance;
        }

        private static int Add(List<Vector3> nodes, Vector3 p)
        {
            for (int i = 0; i < nodes.Count; i++)
                if ((nodes[i] - p).sqrMagnitude <= Tolerance * Tolerance) return i;
            nodes.Add(p);
            return nodes.Count - 1;
        }

        private struct Strip { public Vector3 a, b; public bool horizontal; }
    }
}
