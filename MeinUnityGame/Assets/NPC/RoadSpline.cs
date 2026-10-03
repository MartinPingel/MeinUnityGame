using System;
using System.Collections.Generic;
using UnityEngine;

namespace Village.Npc
{
    /// <summary>
    /// A road defined by a handful of control points instead of axis-aligned boxes. Resampled
    /// at regular intervals along a Catmull-Rom curve through them, with every sample's height
    /// taken live from the terrain - so both the visible mesh and the NPC navigation graph
    /// (see SplineRoadRouter) follow a continuously sloped, curved line with no height steps.
    /// Read-only with respect to the terrain: only ever samples its height, never edits it.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RoadSpline : MonoBehaviour
    {
        [SerializeField] private Transform[] controlPoints;
        [Tooltip("Target distance between generated samples, in metres.")]
        [SerializeField, Min(0.25f)] private float sampleSpacing = 2f;
        [SerializeField, Min(0.1f)] private float width = 4f;
        [Tooltip("How far above the sampled terrain height the road surface sits.")]
        [SerializeField] private float surfaceClearance = 0.05f;
        [Tooltip("Optional explicit terrain; defaults to Terrain.activeTerrain.")]
        [SerializeField] private Terrain terrain;

        private Vector3[] samples;

        public float Width => width;

        /// <summary>World-space, terrain-snapped points along the curve, evenly spaced at
        /// roughly sampleSpacing. Built on first access and cached until a field changes.</summary>
        public Vector3[] Samples => samples ?? (samples = BuildSamples());

        private void Awake() => TryRebuildMesh();
        private void OnValidate()
        {
            samples = null;
            TryRebuildMesh();
        }

        [ContextMenu("Weg neu erzeugen")]
        public void TryRebuildMesh()
        {
            try
            {
                RebuildMesh();
            }
            catch (InvalidOperationException exception)
            {
                Debug.LogWarning($"{name}: {exception.Message}", this);
            }
        }

        public Vector3[] BuildSamples()
        {
            if (controlPoints == null || controlPoints.Length < 2)
                throw new InvalidOperationException("RoadSpline needs at least two control points.");
            foreach (Transform point in controlPoints)
                if (point == null)
                    throw new InvalidOperationException("RoadSpline has a missing control point.");

            Terrain activeTerrain = terrain != null ? terrain : Terrain.activeTerrain;
            if (activeTerrain == null)
                throw new InvalidOperationException(
                    "RoadSpline needs a Terrain to sample height from (run the terrain generator first).");

            var controls = new Vector3[controlPoints.Length];
            for (int i = 0; i < controls.Length; i++) controls[i] = controlPoints[i].position;
            int segments = controls.Length - 1;

            var result = new List<Vector3> { SnapToTerrain(controls[0], activeTerrain) };
            for (int seg = 0; seg < segments; seg++)
            {
                Vector3 p0 = controls[Mathf.Max(seg - 1, 0)];
                Vector3 p1 = controls[seg];
                Vector3 p2 = controls[seg + 1];
                Vector3 p3 = controls[Mathf.Min(seg + 2, segments)];
                // Step count from the straight chord length: an approximation of true arc
                // length, close enough to keep samples roughly sampleSpacing apart.
                float chord = Vector3.Distance(p1, p2);
                int steps = Mathf.Max(1, Mathf.RoundToInt(chord / sampleSpacing));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    Vector3 flat = CatmullRom(p0, p1, p2, p3, t);
                    result.Add(SnapToTerrain(flat, activeTerrain));
                }
            }
            return result.ToArray();
        }

        private void RebuildMesh()
        {
            Vector3[] points = BuildSamples();
            samples = points;
            var meshFilter = GetComponent<MeshFilter>();
            var mesh = new Mesh { name = "RoadSplineMesh" };
            int count = points.Length;
            var vertices = new Vector3[count * 2];
            var uvs = new Vector2[count * 2];
            var triangles = new int[(count - 1) * 6];
            float half = width * 0.5f;
            float cumulative = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 forward = i < count - 1 ? points[i + 1] - points[i] : points[i] - points[i - 1];
                forward.y = 0f;
                if (forward.sqrMagnitude < 1e-8f) forward = Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward.normalized) * half;
                vertices[i * 2] = transform.InverseTransformPoint(points[i] - right);
                vertices[i * 2 + 1] = transform.InverseTransformPoint(points[i] + right);
                if (i > 0) cumulative += Vector3.Distance(points[i - 1], points[i]);
                uvs[i * 2] = new Vector2(0f, cumulative);
                uvs[i * 2 + 1] = new Vector2(1f, cumulative);
            }
            for (int i = 0; i < count - 1; i++)
            {
                int vertexIndex = i * 2, triangleIndex = i * 6;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = vertexIndex + 2;
                triangles[triangleIndex + 2] = vertexIndex + 1;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = vertexIndex + 2;
                triangles[triangleIndex + 5] = vertexIndex + 3;
            }
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private Vector3 SnapToTerrain(Vector3 worldPoint, Terrain t)
        {
            float y = t.SampleHeight(worldPoint) + t.GetPosition().y + surfaceClearance;
            return new Vector3(worldPoint.x, y, worldPoint.z);
        }
    }
}
