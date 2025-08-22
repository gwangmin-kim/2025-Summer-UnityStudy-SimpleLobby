using UnityEngine;
using Unity.Netcode;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class PlayerInputSender : NetworkBehaviour {
    [SerializeField] private float sendRate = 20f; // 20Hz
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
    }

    private void OnEnable() {
        moveAction?.Enable();
        jumpAction?.Enable();
        closeAttackAction?.Enable();
        rangedAttackAction?.Enable();
        useItemAction?.Enable();

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
        Vector2 move = moveAction.ReadValue<Vector2>();
        ButtonBits buttons = latchedButtons;
        // reset latched inputs
        latchedButtons = ButtonBits.None;

        var command = new PlayerCommand { Move = move, Buttons = buttons, Seq = ++seq };
        SendCommandServerRpc(command);
    }

    [ServerRpc]
    private void SendCommandServerRpc(PlayerCommand command) {
        motor.ApplyCommand(command, OwnerClientId);
    }
}