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
    [Header("Close Attack")]
    [SerializeField] private float closeAttackMoveDistance = 1f; // 휘두르며 이동하는 거리
    [SerializeField] private float closeAttackValidTime = 0.3f; // 공격 판정이 유효한 시간 (실제 이동 시간)
    [SerializeField] private float closeAttackDuration = 0.5f; // Idle로 전환되기까지의 시간 (공격 후딜레이에 관여)

    private PlayerStateMachine stateMachine;

    // caching input
    private Vector2 moveInput;
    private bool jumpInput = false;
    private bool closeAttackInput = false;
    private bool rangedAttackInput = false;

    // jump
    private float jumpInverseDuration; // for calculation efficiency
    public float JumpInverseDuration => jumpInverseDuration;
    // close attack
    private float closeAttackInverseDuration;
    public float CloseAttackInverseDuration => closeAttackInverseDuration;


    private Vector3 cachedPosition;
    private Vector3 cachedDirection;
    private float elapsedTime;

    // for collision detection
    RaycastHit[] castHits = new RaycastHit[8];
    Collider[] overlaps = new Collider[8];

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
        closeAttackInput = (command.Buttons & ButtonBits.CloseAttack) != 0;
        rangedAttackInput = (command.Buttons & ButtonBits.RangedAttack) != 0;
    }

    private void Awake() {
        stateMachine = GetComponent<PlayerStateMachine>();

        jumpInverseDuration = 1f / jumpDuration;
        closeAttackInverseDuration = 1f / closeAttackValidTime;
    }

    // server-side: logical movement
    private void Update() {
        if (!IsServer) {
            return;
        }

        // check input (change state)
        if (rangedAttackInput) {
            Debug.Log($"[Player{OwnerClientId}] PlayerAttack: RangedAttack");
            rangedAttackInput = false;
        }
        else if (closeAttackInput && stateMachine.TryCloseAttack()) {
            Debug.Log($"[Player{OwnerClientId}] PlayerAttack: CloseAttack");
            // enable attack effect
            CacheTransform();
            closeAttackInput = false;
        }
        else if (jumpInput && stateMachine.TryJump()) {
            Debug.Log($"[Player{OwnerClientId}] PlayerMotor: Jump");
            CacheTransform();
            jumpInput = false;
        }

        // move player
        switch (stateMachine.CurrentState) {
            case PlayerState.IdleOrMove:
                MovePlayer(Time.deltaTime);
                break;
            case PlayerState.Jump:
                JumpPlayer(Time.deltaTime);
                break;
            case PlayerState.Attack:
                CloseAttackPlayer(Time.deltaTime);
                break;

            default:
                break;
        }
    }

    private void CacheTransform() {
        if (!IsServer) {
            return;
        }

        // 방향키 입력 기준
        var direction = new Vector3(moveInput.x, 0f, moveInput.y);
        if (direction.sqrMagnitude < 1e-6f) {
            direction = new Vector3(transform.forward.x, 0, transform.forward.z);
            // direction.Normalize(); // transform can't rotate vertically
        }

        cachedPosition = transform.position;
        cachedDirection = direction;
        elapsedTime = 0f;
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
        elapsedTime += deltaTime;
        float t = Mathf.Clamp01(elapsedTime * jumpInverseDuration);
        // transform.position = jumpPosition + jumpDistance * SmoothStep(t) * jumpDirection;
        var targetPosition = cachedPosition + jumpDistance * SmoothStep(t) * cachedDirection;
        var delta = targetPosition - transform.position;
        KinematicMove(delta.normalized, delta.magnitude, 0);

        // rotation
        var target = Quaternion.LookRotation(cachedDirection, Vector3.up);
        float maxStep = maxSnapTurnDegreePerSec * deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxStep);

        if (elapsedTime >= jumpDuration) {
            stateMachine.EndJump();
        }
    }

    private void CloseAttackPlayer(float deltaTime) {
        elapsedTime += deltaTime;
        float t = Mathf.Clamp01(elapsedTime * closeAttackInverseDuration);
        var targetPosition = cachedPosition + closeAttackMoveDistance * SmoothStep(t) * cachedDirection;
        var delta = targetPosition - transform.position;
        KinematicMove(delta.normalized, delta.magnitude, 0);

        // rotation
        var target = Quaternion.LookRotation(cachedDirection, Vector3.up);
        float maxStep = maxSnapTurnDegreePerSec * deltaTime;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxStep);

        if (elapsedTime >= closeAttackDuration) {
            stateMachine.EndCloseAttack();
        }
        else if (elapsedTime >= closeAttackValidTime) {
            // disable attack effect
        }
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

        // (optional) apply skinWidth
        float radius = Mathf.Max(0f, r - skinWidth);

        int overlapCount = Physics.OverlapCapsuleNonAlloc(
            p0, p1, radius, overlaps, collisionMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++) {
            var col = overlaps[i];
            if (col == null || IsSelf(col)) {
                continue;
            }

            if (Physics.ComputePenetration(
                    capsule, capsule.transform.position, capsule.transform.rotation,
                    col, col.transform.position, col.transform.rotation,
                    out Vector3 direction, out float distance)) {
                // 살짝만 밀어냄 (skin 너머로)
                Vector3 delta = direction * (distance + skinWidth);
                transform.position += delta;
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
    /// <param name="slideDepth">미끄러짐 판정 횟수</param>
    private void KinematicMove(Vector3 direction, float distance, int slideDepth = 2) {
        Vector3 remaining = distance * direction;
        for (int i = 0; i <= slideDepth; i++) {
            // prevent oscilation
            if (remaining.sqrMagnitude <= 1e-6f) {
                return;
            }

            float castDistance = distance + skinWidth;

            if (!CustomCapsuleCast(direction, castDistance, out var hit)) {
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

    bool IsSelf(Collider col) => col.transform.root == transform.root;
    bool IsSelf(RaycastHit hit) => hit.collider != null && IsSelf(hit.collider);

    /// <summary>
    /// 현재 이동하려는 지점에 대한 플레이어 또는 환경과의 충돌을 검사
    /// collisionMask가 Player | Environment로 설정되어 있어야 함
    /// 플레이어 자기 자신을 제외
    /// 최대 8개의 충돌 후보군 비교 가능
    /// </summary>
    /// <param name="direction"></param>
    /// <param name="distance"></param>
    /// <param name="bestHit"></param>
    /// <returns></returns>
    private bool CustomCapsuleCast(Vector3 direction, float distance, out RaycastHit bestHit) {
        bestHit = default;
        GetCapsule(out var p0, out var p1, out var r);

        // (optional) apply skinWidth
        float radius = Mathf.Max(0f, r - skinWidth);

        int hitCount = Physics.CapsuleCastNonAlloc(
            p0, p1, radius, direction, castHits, distance, collisionMask, QueryTriggerInteraction.Ignore);

        // find best hit
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++) {
            var hit = castHits[i];
            if (IsSelf(hit)) {
                continue; // except myself
            }

            if (hit.distance < bestDistance) {
                bestDistance = hit.distance;
                bestHit = hit;
            }
        }

        return bestDistance < float.PositiveInfinity;
    }

    private static float SmoothStep(float t) => t * t * (3f - 2f * t);
}