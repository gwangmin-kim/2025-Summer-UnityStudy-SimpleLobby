using UnityEngine;
using Unity.Netcode;

public static class AnimatorParameters {
    public static readonly int Idle = Animator.StringToHash("Base.Idle");
    public static readonly int Walk = Animator.StringToHash("Base.Walk");
    public static readonly int Jump = Animator.StringToHash("Base.Jump");
    public static readonly int Swing = Animator.StringToHash("Base.Swing");

    public static int PlayerStateToAnimatorStateHash(PlayerState playerState) => playerState switch {
        PlayerState.Idle => Idle,
        PlayerState.Move => Walk,
        PlayerState.Jump => Jump,
        PlayerState.CloseAttack => Swing,
        PlayerState.RangedAttack => Swing,
        _ => -1 // invalid
    };
}

[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(Animator))]
// [RequireComponent(typeof(PlayerMotor))]
public class PlayerAnimation : NetworkBehaviour {
    Animator animator;

    [Header("Mesh")]
    [SerializeField] Transform visualRoot;

    [Header("Animation")]
    [SerializeField] private float fadeTime = 0.08f;

    // [Header("Jump")]
    // [SerializeField] private float jumpVisualHeight = 1f;

    private PlayerStateMachine stateMachine;
    private StatePacket cachedState;

    // private PlayerMotor motor;

    private void Awake() {
        animator = GetComponent<Animator>();
        stateMachine = GetComponent<PlayerStateMachine>();
        // motor = GetComponent<PlayerMotor>();
    }

    // client-side: visual movement
    private void LateUpdate() {
        if (visualRoot == null) {
            return;
        }

        UpdateAnimator();

        // if (stateMachine.CurrentState == PlayerState.Jump) {
        //     double from = stateMachine.statePacket.Value.StartServerTime;
        //     double now = NetworkManager.ServerTime.Time;
        //     float elapsed = Mathf.Max(0f, (float)(now - from));
        //     float t = Mathf.Clamp01(elapsed * motor.JumpInverseDuration);

        //     float height = JumpHeight(t);

        //     Vector3 localPosition = visualRoot.localPosition;
        //     localPosition.y = height;
        //     visualRoot.localPosition = localPosition;
        // }
        // else {
        //     Vector3 localPosition = visualRoot.localPosition;
        //     localPosition.y = 0f;
        //     visualRoot.localPosition = localPosition;
        // }
    }

    private void UpdateAnimator() {
        StatePacket currentState = stateMachine.statePacket.Value;

        if (!IsStateChanged(cachedState, currentState)) {
            return;
        }

        int animatorStateHash = AnimatorParameters.PlayerStateToAnimatorStateHash(currentState.State);
        animator.CrossFade(animatorStateHash, fadeTime, 0);
        cachedState = currentState;
    }

    // helper function: detect state changes
    private static bool IsStateChanged(StatePacket cached, StatePacket current) => cached.seq != current.seq;

    // helper function: parameterized curves
    // private float JumpHeight(float t) => jumpVisualHeight * 4f * t * (1f - t);
}