using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PlayerMotor))]
public class PlayerInputSender : NetworkBehaviour {
    [SerializeField] private float sendRate = 20f; // 20Hz

    [Header("Aim Settings")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float aimDeadzone = 0.05f;

    private float sendInterval;
    private float sendTimer = 0f;
    private uint seq;

    private ButtonBits latchedButtons;
    private PlayerMotor motor;

    // action lists
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction closeAttackAction;
    private InputAction rangedAttackAction;
    private InputAction useItemAction;
    private InputAction aimAction;

    // // cached aim input
    // private Vector3 lastAimDirection = Vector3.zero;

    private void Awake() {
        sendInterval = 1f / sendRate;
        motor = GetComponent<PlayerMotor>();
        FindActions();
    }

    private void FindActions() {
        moveAction = InputSystem.actions.FindAction("Move", true);
        jumpAction = InputSystem.actions.FindAction("Jump", true);
        closeAttackAction = InputSystem.actions.FindAction("CloseAttack", true);
        rangedAttackAction = InputSystem.actions.FindAction("RangedAttack", true);
        useItemAction = InputSystem.actions.FindAction("UseItem", true);
        aimAction = InputSystem.actions.FindAction("Aim", true);
    }

    private void OnEnable() {
        moveAction?.Enable();
        jumpAction?.Enable();
        closeAttackAction?.Enable();
        rangedAttackAction?.Enable();
        useItemAction?.Enable();
        aimAction?.Enable();

        if (jumpAction != null) jumpAction.performed += OnJump;
        if (closeAttackAction != null) closeAttackAction.performed += OnCloseAttack;
        if (rangedAttackAction != null) rangedAttackAction.performed += OnRangedAttack;
        if (useItemAction != null) useItemAction.performed += OnUseItem;
    }

    private void OnDisable() {
        if (jumpAction != null) jumpAction.performed -= OnJump;
        if (closeAttackAction != null) closeAttackAction.performed -= OnCloseAttack;
        if (rangedAttackAction != null) rangedAttackAction.performed -= OnRangedAttack;
        if (useItemAction != null) useItemAction.performed -= OnUseItem;

        moveAction?.Disable();
        jumpAction?.Disable();
        closeAttackAction?.Disable();
        rangedAttackAction?.Disable();
        useItemAction?.Disable();
        aimAction?.Disable();
    }

    private void OnJump(InputAction.CallbackContext ctx) => latchedButtons |= ButtonBits.JumpDown;
    private void OnCloseAttack(InputAction.CallbackContext ctx) => latchedButtons |= ButtonBits.CloseAttack;
    private void OnRangedAttack(InputAction.CallbackContext ctx) => latchedButtons |= ButtonBits.RangedAttack;
    private void OnUseItem(InputAction.CallbackContext ctx) => latchedButtons |= ButtonBits.UseItem;

    private void Update() {
        if (!IsOwner || !IsClient) {
            return;
        }

        sendTimer += Time.unscaledDeltaTime;
        if (sendTimer >= sendInterval) {
            sendTimer = 0f;
            HandleInput();
        }
    }

    private void HandleInput() {
        // get inputs
        TryGetMoveDirection(out var move);
        TryGetPointerDirection(out var aim, out var _);
        ButtonBits buttons = latchedButtons;

        // reset latched inputs
        latchedButtons = ButtonBits.None;

        var command = new PlayerCommand { Move = move, Aim = aim, Buttons = buttons, Seq = ++seq };
        SendCommandServerRpc(command);
    }

    private bool TryGetMoveDirection(out Vector2 direction) {
        direction = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
        return moveAction != null;
    }

    private bool TryGetPointerDirection(out Vector2 direction, out Vector3 worldPoint) {
        direction = Vector2.zero; worldPoint = transform.position;

        if (mainCamera == null) {
            mainCamera = Camera.main;
            if (mainCamera == null) {
                return false;
            }
        }

        Vector2 screenPosition = aimAction != null ? aimAction.ReadValue<Vector2>() : Vector2.zero;
        // Debug.Log(screenPosition);
        Ray ray = mainCamera.ScreenPointToRay(screenPosition);

        float planeY = transform.position.y;
        float denom = ray.direction.y;

        if (Mathf.Abs(denom) < 1e-6) {
            return false;
        }

        float t = (planeY - ray.origin.y) / denom;
        if (t <= 0f || !float.IsFinite(t)) {
            return false;
        }

        worldPoint = ray.origin + ray.direction * t;

        Vector3 flat = worldPoint - transform.position;
        flat.y = 0f;

        if (flat.sqrMagnitude < aimDeadzone * aimDeadzone) {
            return false;
        }

        direction = new Vector2(flat.x, flat.z).normalized;
        return true;
    }

    [ServerRpc]
    private void SendCommandServerRpc(PlayerCommand command) {
        motor.ApplyCommand(command, OwnerClientId);
    }
}