using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerStateMachine))]
public class PlayerAttack : NetworkBehaviour {
    [Header("Close Attack")]
    [SerializeField] private float closeAttackMoveDistance = 1f; // 휘두르며 이동하는 거리
    [SerializeField] private float closeAttackValidTime = 0.2f; // 공격 판정이 유효한 시간
    [SerializeField] private float closeAttackDuration = 0.5f; // Idle로 전환되기까지의 시간
    [Header("Ranged Attack")]
    [SerializeField] private float rangedAttackDelay = 0.1f; // 휘두르는 모션까지의 시간 (입력 후 실제 투사체가 날아가기까지)

    private bool canRangedAttack = true;

    private PlayerMotor motor;
    private PlayerStateMachine stateMachine;


    private void Awake() {
        motor = GetComponent<PlayerMotor>();
        stateMachine = GetComponent<PlayerStateMachine>();
    }

    private void Update() {
        if (!IsServer) {
            return;
        }

        // check input (change state)

    }
}