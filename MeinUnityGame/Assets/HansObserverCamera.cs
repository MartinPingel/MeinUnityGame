using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Developer-only view override on the existing camera. Cycles player / configured NPCs without controlling them.
/// Runs after VillagePlayer.LateUpdate so its regular camera and input stay untouched.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(10000)]
public sealed class HansObserverCamera : MonoBehaviour
{
    [SerializeField] private bool enableObserver = true;
    [SerializeField] private NpcAgent[] observedNpcs = new NpcAgent[0];
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
                !viewCamera.isActiveAndEnabled || observedNpcs == null ||
                observedIndex < 0 || observedIndex >= observedNpcs.Length)
                return null;
            NpcAgent npc = observedNpcs[observedIndex];
            return npc != null && npc.gameObject.activeInHierarchy ? npc : null;
#else
            return null;
#endif
        }
    }

    public bool IsObserving(Transform target) => CurrentNpc != null && CurrentNpc.transform == target;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Camera viewCamera;
    private int observedIndex = -1; // -1 is the player's original camera view.
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
            observedIndex = -1;
            return;
        }
        if (observedIndex >= 0 && CurrentNpc == null) observedIndex = -1;
        if (Application.isFocused && Keyboard.current != null &&
            Keyboard.current.f4Key.wasPressedThisFrame)
        {
            int next = observedIndex + 1;
            while (observedNpcs != null && next < observedNpcs.Length)
            {
                NpcAgent candidate = observedNpcs[next];
                if (candidate != null && candidate.gameObject.activeInHierarchy) break;
                next++;
            }
            observedIndex = observedNpcs != null && next < observedNpcs.Length ? next : -1;
        }
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
        observedIndex = -1;
        hasOverride = false;
    }
#endif
}
