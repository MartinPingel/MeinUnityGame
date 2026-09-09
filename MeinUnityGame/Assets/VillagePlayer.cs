using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Walking capsule and third-person camera for exploring the test village.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public sealed class VillagePlayer : MonoBehaviour
{
    [SerializeField] private Camera followCamera;
    [SerializeField] private GameClock clock;

    [Header("Laufen")]
    [SerializeField, Min(0.1f)] private float walkSpeed = 3.5f;
    [SerializeField, Min(0.1f)] private float acceleration = 12f;
    [SerializeField, Min(0.1f)] private float turnSpeed = 540f;

    [Header("Kamera - rechte Maustaste halten")]
    [SerializeField, Min(1f)] private float cameraDistance = 8f;
    [SerializeField, Range(15f, 65f)] private float pitch = 30f;
    [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float yaw;
    private float currentCameraDistance;
    private bool rotatingCamera;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (followCamera == null)
            followCamera = Camera.main;
        if (followCamera == null)
        {
            Debug.LogError("VillagePlayer needs the scene's Main Camera.", this);
            enabled = false;
            return;
        }

        yaw = transform.eulerAngles.y;
        currentCameraDistance = cameraDistance;
        UpdateCamera(true);
    }

    private void Update()
    {
        bool canControl = Application.isFocused && (clock == null || !clock.IsPaused);
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (!canControl || mouse == null || !mouse.rightButton.isPressed ||
            (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
        {
            ReleaseMouse();
        }
        else if (!rotatingCamera && mouse.rightButton.wasPressedThisFrame &&
                 (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
        {
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            rotatingCamera = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (rotatingCamera)
        {
            // Mouse delta is already a per-frame displacement; do not multiply by deltaTime.
            Vector2 delta = mouse.delta.ReadValue();
            yaw = Mathf.Repeat(yaw + delta.x * mouseSensitivity, 360f);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, 15f, 65f);
        }

        Vector2 input = Vector2.zero;
        if (canControl && keyboard != null)
        {
            input.x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            input.y = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            input = Vector2.ClampMagnitude(input, 1f);
        }

        Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
        horizontalVelocity = canControl
            ? Vector3.MoveTowards(horizontalVelocity, direction * walkSpeed, acceleration * Time.deltaTime)
            : Vector3.zero;

        if (direction.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;
        verticalVelocity = Mathf.Max(verticalVelocity - 20f * Time.deltaTime, -30f);

        CollisionFlags collisions = controller.Move(
            (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        if ((collisions & CollisionFlags.Below) != 0)
            verticalVelocity = -2f;
        if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
    }

    private void LateUpdate()
    {
        if (followCamera != null)
            UpdateCamera(false);
    }

    private void UpdateCamera(bool snap)
    {
        Vector3 pivot = transform.position + Vector3.up * 0.6f;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 backwards = rotation * Vector3.back;

        // Cover the camera's near-plane corners, including wide aspect ratios.
        float halfHeight = followCamera.nearClipPlane * Mathf.Tan(followCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float radius = Mathf.Max(0.25f, halfHeight * Mathf.Sqrt(1f + followCamera.aspect * followCamera.aspect) + 0.02f);
        float safeDistance = cameraDistance;
        foreach (RaycastHit hit in Physics.SphereCastAll(pivot, radius, backwards,
                     cameraDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == controller || hit.transform.IsChildOf(transform))
                continue;
            safeDistance = Mathf.Min(safeDistance, Mathf.Max(0.05f, hit.distance - 0.1f));
        }

        // Retract immediately at walls; move back out gently after clearing them.
        currentCameraDistance = snap || safeDistance < currentCameraDistance
            ? safeDistance
            : Mathf.MoveTowards(currentCameraDistance, safeDistance, 5f * Time.deltaTime);
        followCamera.transform.SetPositionAndRotation(pivot + backwards * currentCameraDistance, rotation);
    }

    private void ReleaseMouse()
    {
        if (!rotatingCamera)
            return;
        rotatingCamera = false;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused)
        {
            horizontalVelocity = Vector3.zero;
            ReleaseMouse();
        }
    }

    private void OnDisable()
    {
        horizontalVelocity = Vector3.zero;
        ReleaseMouse();
    }
}
