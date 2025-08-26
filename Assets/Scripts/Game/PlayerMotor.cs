using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerStateMachine))]
public class PlayerMotor : NetworkBehaviour {
    [Header("Collision")]
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float skinWidth = 0.02f;
    [Header("Move")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float maxSnapTurnDegreePerSec = 720f;
    [Header("Jump")]
    [SerializeField] private float jumpDistance = 3f;
    [SerializeField] private float jumpDuration = 0.3f;
    // [SerializeField] private float jumpCooldown = 0.5f;

    private PlayerStateMachine stateMachine;

    // caching input
    private Vector2 moveInput;
    private bool jumpInput;

    // jump
    private Vector3 jumpPosition;
    private Vector3 jumpDirection;
    private float jumpElapsed;
    private float jumpInverseDuration; // for calculation efficiency
    public float JumpInverseDuration => jumpInverseDuration;

    private void Awake() {
        stateMachine = GetComponent<PlayerStateMachine>();

        jumpInverseDuration = 1f / jumpDuration;
    }

    // server-side: logical movement
    private void Update() {
        if (!IsServer) {
            return;
        }

        // check jump
        if (jumpInput && stateMachine.TryJump()) {
            Debug.Log("Jump");
            InitializeJump();
            jumpInput = false;
        }
        if (stateMachine.CurrentState == PlayerState.Jump) {
            JumpPlayer(Time.deltaTime);
        }
        else {
            MovePlayer(Time.deltaTime);
        }
    }

    private void InitializeJump() {
        if (!IsServer) {
            return;
        }

        var direction = new Vector3(moveInput.x, 0f, moveInput.y);
        if (direction.sqrMagnitude < 1e-6f) {
            direction = new Vector3(transform.forward.x, 0, transform.forward.z);
            // direction.Normalize(); // transform can't rotate vertically
        }

        jumpPosition = transform.position;
        jumpDirection = direction;
        jumpElapsed = 0f;
    }

    private void MovePlayer(float deltaTime) {
        if (!IsServer) {
            return;
        }

        // translation
        var direction = new Vector3(moveInput.x, 0f, moveInput.y);
        var distance = moveSpeed * deltaTime;
        KinematicMove(direction, distance);
        // var delta = moveSpeed * deltaTime * direction;
        // transform.position += delta;


        // rotation
        if (direction.sqrMagnitude > 0f) {
            var target = Quaternion.LookRotation(direction, Vector3.up);
            float maxStep = maxSnapTurnDegreePerSec * deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxStep);
        }
    }

    private void JumpPlayer(float deltaTime) {
        jumpElapsed += deltaTime;
        float t = Mathf.Clamp01(jumpElapsed * jumpInverseDuration);
        // transform.position = jumpPosition + jumpDistance * SmoothStep(t) * jumpDirection;
        var targetPosition = jumpPosition + jumpDistance * SmoothStep(t) * jumpDirection;
        var delta = targetPosition - transform.position;
        KinematicMove(delta.normalized, delta.magnitude);

        // rotation
        var target = Quaternion.LookRotation(jumpDirection, Vector3.up);
        float maxStep = maxSnapTurnDegreePerSec * deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxStep);

        if (jumpElapsed >= jumpDuration) {
            stateMachine.EndJump();
        }
    }

    /// <summary>
    /// 클라이언트가 서버로 보낸 입력을 받아 캐싱
    /// </summary>
    /// <param name="command">입력 정보 구조체</param>
    /// <param name="senderId">클라이언트 Id</param>
    public void ApplyCommand(PlayerCommand command, ulong senderId) {
        if (!IsServer || senderId != OwnerClientId) {
            return;
        }
        moveInput = command.Move;
        jumpInput = (command.Buttons & ButtonBits.JumpDown) != 0;
    }

    // helper functions
    /// <summary>
    /// 캡슐 콜라이더의 월드 좌표 계산
    /// </summary>
    /// <param name="p0">위쪽 구 중심</param>
    /// <param name="p1">아래쪽 구 중심</param>
    /// <param name="radius">반지름</param>
    private void GetCapsule(out Vector3 p0, out Vector3 p1, out float radius) {
        Transform tr = capsule.transform;
        Vector3 c = tr.TransformPoint(capsule.center);
        float height = Mathf.Max(capsule.height * Mathf.Abs(tr.lossyScale.y), capsule.radius * 2f);
        radius = capsule.radius * Mathf.Max(Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.z));

        float half = (height * 0.5f) - radius;
        Vector3 up = tr.up;
        p0 = c + up * half;
        p1 = c - up * half;
    }

    /// <summary>
    /// 환경과 플레이어가 겹치는 상황에서 탈출
    /// </summary>
    private void ResolveOverlaps() {
        GetCapsule(out var p0, out var p1, out var r);
        Collider[] hits = Physics.OverlapCapsule(p0, p1, r, collisionMask, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits) {
            if (hit.attachedRigidbody != null && !hit.attachedRigidbody.isKinematic) continue; // 동적인 건 스킵(상황 따라 조정)
            if (Physics.ComputePenetration(
                    capsule, capsule.transform.position, capsule.transform.rotation,
                    hit, hit.transform.position, hit.transform.rotation,
                    out Vector3 direction, out float distance)) {
                // 살짝만 밀어냄 (skin 너머로)
                Vector3 depen = direction * (distance + skinWidth);
                transform.position += depen;
                // 겹침이 연쇄될 수 있어 1~2회 더 반복하고 싶다면 여기에 루프를 추가
            }
        }
    }

    /// <summary>
    /// 충돌 방지가 적용된 이동
    /// 벽을 만나면 표면을 따라 미끄러짐
    /// </summary>
    /// <param name="direction">이동 방향, 정규화 필요</param>
    /// <param name="distance">이동 거리</param>
    private void KinematicMove(Vector3 direction, float distance) {
        Vector3 remaining = distance * direction;
        for (int i = 0; i < 3; i++) {
            // prevent oscilation
            if (remaining.sqrMagnitude <= 1e-6f) {
                return;
            }

            GetCapsule(out var p0, out var p1, out var r);
            float castDistance = distance + skinWidth;

            if (!Physics.CapsuleCast(p0, p1, r - skinWidth, direction, out RaycastHit hit, castDistance, collisionMask, QueryTriggerInteraction.Ignore)) {
                // no collision
                transform.position += remaining;
                return;
            }

            float moveDistance = Mathf.Max(hit.distance - skinWidth, 0f);
            transform.position += moveDistance * direction;

            distance = Mathf.Max(distance - moveDistance, 0f);
            direction = Vector3.ProjectOnPlane(direction, hit.normal).normalized;
            remaining = distance * direction;
        }
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}