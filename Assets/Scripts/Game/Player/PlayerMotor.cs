using UnityEngine;
using Unity.Netcode;

// ! 현재 이동 및 공격 -> 모든 플레이어의 행동 로직을 여기에서 담당 중
// ! 이전에는 PlayerAttack 클래스를 따로 두어 근/원거리 공격에 대한 동작을 여기에서 수행하고자 했음
// ! 클래스를 분리하더라도 의존성이 심해질 것 같아 우선 하나의 클래스에서 전부 구현하기로 함
// ! (근거리 공격의 경우, 전방으로 이동 + 공격 판정이 동시에 존재해서 두 로직을 분리 시에 불필요한 동일 연산이 반복됨)
// ! 단, 공격 판정 로직 자체는 별도의 클래스에 구현해두고, 이를 호출하는 방식으로 하기로 함(일종의 라이브러리 같은 느낌)
[RequireComponent(typeof(PlayerStateMachine))]
[RequireComponent(typeof(PlayerAttack))]
public class PlayerMotor : NetworkBehaviour {
    [Header("Collision")]
    [SerializeField] private CapsuleCollider capsule;
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float skinWidth = 0.02f;
    [Header("Move")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float maxSnapTurnDegreePerSec = 720f;
    [Header("Jump")]
    [SerializeField] private float jumpDistance = 4f;
    [SerializeField] private float jumpDuration = 0.4f;
    // [SerializeField] private float jumpCooldown = 0.6f;
    [Header("Close Attack")]
    [SerializeField] private float closeAttackMoveDistance = 3f; // 휘두르며 이동하는 거리
    [SerializeField] private float closeAttackMoveTime = 0.3f; // 공격 모션과 함께 실제 이동하는 시간
    [SerializeField] private float closeAttackDuration = 0.5f; // Idle로 전환되기까지의 시간 (공격 후딜레이에 관여)
    [SerializeField] private Vector2 closeAttackValidRange = new Vector2(0.1f, 0.3f); // 공격 판정을 발동할 시간 (시작 시점, 종료 시점)

    private PlayerStateMachine stateMachine;
    private PlayerAttack playerAttack;

    // caching input
    private Vector2 moveInput;
    private Vector2 aimInput;
    private bool jumpInput = false;
    private bool closeAttackInput = false;
    private bool rangedAttackInput = false;

    // jump
    private float jumpInverseDuration; // for calculation efficiency
    // close attack
    private float closeAttackInverseDuration;

    private Vector3 cachedPosition;
    private Vector3 cachedDirection;
    private float elapsedTime = 0f;

    // for collision detection
    private readonly RaycastHit[] castHits = new RaycastHit[8];
    private readonly Collider[] overlaps = new Collider[8];

    // lock vertical position
    private float fixedY;

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
        aimInput = command.Aim;
        jumpInput = (command.Buttons & ButtonBits.JumpDown) != 0;
        closeAttackInput = (command.Buttons & ButtonBits.CloseAttack) != 0;
        rangedAttackInput = (command.Buttons & ButtonBits.RangedAttack) != 0;
    }

    private void Awake() {
        stateMachine = GetComponent<PlayerStateMachine>();
        playerAttack = GetComponent<PlayerAttack>();
    }

    private void Start() {
        if (!IsServer) {
            return;
        }

        fixedY = transform.position.y;
        jumpInverseDuration = 1f / jumpDuration;
        closeAttackInverseDuration = 1f / closeAttackMoveTime;
    }

    // server-side: logical movement
    private void Update() {
        if (!IsServer) {
            return;
        }

        // check input (change state)
        if (rangedAttackInput && stateMachine.TryRangedAttack()) {
            // Debug.Log($"[Player{OwnerClientId}] PlayerAttack: RangedAttack");
            rangedAttackInput = false;
        }
        else if (closeAttackInput && stateMachine.TryCloseAttack()) {
            // Debug.Log($"[Player{OwnerClientId}] PlayerAttack: CloseAttack");
            CacheTransform(true);
            playerAttack.InitCloseAttack();
            closeAttackInput = false;
        }
        else if (jumpInput && stateMachine.TryJump()) {
            // Debug.Log($"[Player{OwnerClientId}] PlayerMotor: Jump");
            CacheTransform();
            jumpInput = false;
        }

        // move player
        switch (stateMachine.CurrentState) {
            case PlayerState.Idle:
            case PlayerState.Move:
                MovePlayer(Time.deltaTime);
                break;
            case PlayerState.Jump:
                JumpPlayer(Time.deltaTime);
                break;
            case PlayerState.CloseAttack:
                CloseAttackPlayer(Time.deltaTime);
                break;

            default:
                break;
        }
    }

    private void CacheTransform(bool towardMouse = false) {
        if (!IsServer) {
            return;
        }

        // 방향키 입력 기준
        var direction = towardMouse ? new Vector3(aimInput.x, 0f, aimInput.y) : new Vector3(moveInput.x, 0f, moveInput.y);
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

        ResolveOverlaps();

        // translation
        var direction = new Vector3(moveInput.x, 0f, moveInput.y);
        var distance = moveSpeed * deltaTime;
        KinematicMove(direction, distance);
        stateMachine.SetMove(direction.sqrMagnitude > 0f);
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
        if (!IsServer) {
            return;
        }

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
            stateMachine.EndJump(moveInput.sqrMagnitude > 0f);
        }
    }

    private void CloseAttackPlayer(float deltaTime) {
        if (!IsServer) {
            return;
        }

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
            // exit state
            stateMachine.EndCloseAttack(moveInput.sqrMagnitude > 0f);
        }
        else if (elapsedTime >= closeAttackValidRange.x && elapsedTime < closeAttackValidRange.y) {
            // ? 호출 횟수를 줄여도 될 것 같음 (매프레임은 다소 과할 수 있음)
            playerAttack.CloseAttack();
        }
    }

    // helper functions
    /// <summary>
    /// 캡슐 콜라이더의 월드 좌표 계산
    /// </summary>
    /// <param name="capsule">계산할 캡슐 콜라이더</param>
    /// <param name="p0">위쪽 구 중심</param>
    /// <param name="p1">아래쪽 구 중심</param>
    /// <param name="radius">반지름</param>
    // private static void GetCapsule(CapsuleCollider capsule, out Vector3 p0, out Vector3 p1, out float radius) {
    //     Transform tr = capsule.transform;
    //     Vector3 c = tr.TransformPoint(capsule.center);
    //     float height = Mathf.Max(capsule.height * Mathf.Abs(tr.lossyScale.y), capsule.radius * 2f);
    //     radius = capsule.radius * Mathf.Max(Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.z));

    //     float half = (height * 0.5f) - radius;
    //     Vector3 up = tr.up;
    //     p0 = c + up * half;
    //     p1 = c - up * half;
    // }

    // helper function
    private void FixTransformY() {
        var currentPosition = transform.position;
        transform.position = new Vector3(currentPosition.x, fixedY, currentPosition.z);
    }

    /// <summary>
    /// 환경과 플레이어가 겹치는 상황에서 탈출
    /// </summary>
    private void ResolveOverlaps() {
        if (!capsule.TryGetCapsuleWorld(out var p0, out var p1, out var r)) {
            return;
        }

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
                // 수직 좌표 고정
                direction.y = 0f;
                // 살짝만 밀어냄 (skin 너머로)
                Vector3 delta = direction * (distance + skinWidth);
                transform.position += delta;
                // 겹침이 연쇄될 수 있어 1~2회 더 반복하고 싶다면 여기에 루프를 추가
            }
        }

        FixTransformY();
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

        // FixTransformY();
    }

    // helper functions
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
        // GetCapsule(capsule, out var p0, out var p1, out var r);
        if (!capsule.TryGetCapsuleWorld(out var p0, out var p1, out var r)) {
            return false;
        }

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