using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Developer-only view override on the existing camera. Never controls either actor.
/// Runs after VillagePlayer.LateUpdate so its regular camera and input stay untouched.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(10000)]
public sealed class HansObserverCamera : MonoBehaviour
{
    [SerializeField] private bool enableObserver = true;
    [SerializeField] private Transform hans;
    [SerializeField, Min(1f)] private float distance = 8f;
    [SerializeField, Range(15f, 65f)] private float pitch = 30f;
    [SerializeField] private float targetHeight = 0.6f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Camera viewCamera;
    private bool observingHans;
    private bool hasOverride;
    private Vector3 playerCameraPosition;
    private Quaternion playerCameraRotation;

    private void Awake()
    {
        viewCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        if (!enableObserver || hans == null || !hans.gameObject.activeInHierarchy)
        {
            observingHans = false;
            return;
        }
        if (Application.isFocused && Keyboard.current != null &&
            Keyboard.current.f4Key.wasPressedThisFrame)
            observingHans = !observingHans;
    }

    private void LateUpdate()
    {
        // VillagePlayer has already calculated this frame's player view, even
        // while observing Hans. Returning therefore uses the current player pose.
        playerCameraPosition = transform.position;
        playerCameraRotation = transform.rotation;
        hasOverride = false;
        if (!enableObserver || !observingHans || hans == null ||
            !hans.gameObject.activeInHierarchy)
            return;

        Vector3 pivot = hans.position + Vector3.up * targetHeight;
        Quaternion rotation = Quaternion.Euler(pitch, hans.eulerAngles.y, 0f);
        Vector3 backwards = rotation * Vector3.back;
        float safeDistance = distance;

        // Keep the observer outside village walls; ignore Hans' own collider.
        float halfHeight = viewCamera.nearClipPlane *
            Mathf.Tan(viewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float radius = Mathf.Max(0.25f,
            halfHeight * Mathf.Sqrt(1f + viewCamera.aspect * viewCamera.aspect) + 0.02f);
        foreach (RaycastHit hit in Physics.SphereCastAll(pivot, radius, backwards,
                     distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform == hans || hit.transform.IsChildOf(hans)) continue;
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
        observingHans = false;
        hasOverride = false;
    }
#endif
}
