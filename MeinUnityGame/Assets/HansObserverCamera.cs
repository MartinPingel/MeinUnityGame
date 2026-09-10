using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// Developer-only view override on the existing camera. Discovers active NPCs without controlling them.
/// Runs after VillagePlayer.LateUpdate so its regular camera and input stay untouched.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(10000)]
public sealed class HansObserverCamera : MonoBehaviour
{
    [SerializeField] private bool enableObserver = true;
    [SerializeField, Min(1f)] private float distance = 8f;
    [SerializeField, Range(15f, 65f)] private float pitch = 30f;
    [SerializeField] private float targetHeight = 0.6f;

    /// <summary>The single NPC currently observed, or null for the player/release builds.</summary>
    public NpcAgent CurrentNpc
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (!isActiveAndEnabled || !enableObserver || viewCamera == null ||
                !viewCamera.isActiveAndEnabled)
                return null;
            return IsAvailable(observedNpc) ? observedNpc : null;
#else
            return null;
#endif
        }
    }

    public bool IsObserving(Transform target) => CurrentNpc != null && CurrentNpc.transform == target;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Camera viewCamera;
    private NpcAgent observedNpc; // null is the player's original camera view.
    private readonly List<NpcAgent> observedNpcs = new List<NpcAgent>();
    private bool hasOverride;
    private Vector3 playerCameraPosition;
    private Quaternion playerCameraRotation;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (!enableObserver)
        {
            observedNpc = null;
            return;
        }
        if (CurrentNpc == null) observedNpc = null;
        if (Application.isFocused && Keyboard.current != null &&
            Keyboard.current.f4Key.wasPressedThisFrame)
        {
            RefreshNpcs();
            int next = observedNpc != null ? observedNpcs.IndexOf(observedNpc) + 1 : 0;
            observedNpc = next < observedNpcs.Count ? observedNpcs[next] : null;
        }
    }

    private static bool IsAvailable(NpcAgent npc) => npc != null &&
        npc.gameObject.activeInHierarchy && npc.gameObject.scene.IsValid() &&
        npc.gameObject.scene.isLoaded;

    private void RefreshNpcs()
    {
        // Discover on every key press, including NPCs spawned or enabled since the last cycle.
        // Keep selection by object reference so insertions/removals cannot silently change targets.
        observedNpcs.Clear();
        foreach (NpcAgent npc in FindObjectsByType<NpcAgent>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (IsAvailable(npc)) observedNpcs.Add(npc);
        observedNpcs.Sort(CompareHierarchy);
    }

    private static int CompareHierarchy(NpcAgent a, NpcAgent b)
    {
        int sceneOrder = a.gameObject.scene.handle.CompareTo(b.gameObject.scene.handle);
        if (sceneOrder != 0) return sceneOrder;
        List<int> left = HierarchyPath(a.transform), right = HierarchyPath(b.transform);
        for (int i = 0; i < Mathf.Min(left.Count, right.Count); i++)
        {
            int order = left[i].CompareTo(right[i]);
            if (order != 0) return order;
        }
        return left.Count.CompareTo(right.Count);
    }

    private static List<int> HierarchyPath(Transform target)
    {
        var path = new List<int>();
        for (Transform current = target; current != null; current = current.parent)
            path.Add(current.GetSiblingIndex());
        path.Reverse();
        return path;
    }

    private void LateUpdate()
    {
        // VillagePlayer has already calculated this frame's player view, even
        // while observing an NPC. Returning therefore uses the current player pose.
        playerCameraPosition = transform.position;
        playerCameraRotation = transform.rotation;
        hasOverride = false;
        NpcAgent npc = CurrentNpc;
        if (npc == null) return;
        Transform target = npc.transform;

        Vector3 pivot = target.position + Vector3.up * targetHeight;
        Quaternion rotation = Quaternion.Euler(pitch, target.eulerAngles.y, 0f);
        Vector3 backwards = rotation * Vector3.back;
        float safeDistance = distance;

        // Keep the observer outside village walls; ignore the observed NPC's own collider.
        float halfHeight = viewCamera.nearClipPlane *
            Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float radius = Mathf.Max(0.25f,
            halfHeight * Mathf.Sqrt(1f + viewCamera.aspect * viewCamera.aspect) + 0.02f);
        foreach (RaycastHit hit in Physics.SphereCastAll(pivot, radius, backwards,
                     distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == target || hit.transform.IsChildOf(target)) continue;
            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.05f, hit.distance - 0.1f));
        }

        // Snap directly on every toggle and time skip; no interpolation across the village.
        transform.SetPositionAndRotation(pivot + backwards * safeDistance, rotation);
        hasOverride = true;
    }

    private void OnDisable()
    {
        if (hasOverride)
            transform.SetPositionAndRotation(playerCameraPosition, playerCameraRotation);
        observedNpc = null;
        observedNpcs.Clear();
        hasOverride = false;
    }
#endif
}
