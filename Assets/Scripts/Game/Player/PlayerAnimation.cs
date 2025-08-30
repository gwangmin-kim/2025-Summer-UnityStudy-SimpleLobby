using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimation : NetworkBehaviour {
    [Header("Mesh")]
    [SerializeField] Transform visualRoot;
    [SerializeField] Transform leftHand;
    [SerializeField] Transform rightHand;
    [SerializeField] Transform leftFoot;
    [SerializeField] Transform rightFoot;

    [Header("Jump")]
    [SerializeField] private float jumpVisualHeight = 1f;

    private PlayerStateMachine stateMachine;
    private PlayerMotor motor;

    private void Awake() {
        stateMachine = GetComponent<PlayerStateMachine>();
        motor = GetComponent<PlayerMotor>();
    }

    // client-side: visual movement
    private void LateUpdate() {
        if (visualRoot == null) {
            return;
        }

        if (stateMachine.CurrentState == PlayerState.Jump) {
            AnimateJump();
        }
        else {
            Vector3 localPosition = visualRoot.localPosition;
            localPosition.y = 0f;
            visualRoot.localPosition = localPosition;
        }
    }

    private void AnimateJump() {
        double from = stateMachine.netState.Value.StartServerTime;
        double now = NetworkManager.ServerTime.Time;
        float elapsed = Mathf.Max(0f, (float)(now - from));
        float t = Mathf.Clamp01(elapsed * motor.JumpInverseDuration);

        float height = JumpHeight(t);

        Vector3 localPosition = visualRoot.localPosition;
        localPosition.y = height;
        visualRoot.localPosition = localPosition;
    }

    // helper function: parameterized curves
    private float JumpHeight(float t) => jumpVisualHeight * 4f * t * (1f - t);
}