using UnityEngine;
using Unity.Netcode;

public class PlayerMotor : NetworkBehaviour {
    [Header("Move")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float maxSnapTurnDegreePerSec = 720f;

    private float fixedDeltaTime;
    private float timer;

    private Vector2 moveInput;

    public void ApplyCommand(PlayerCommand command, ulong senderId) {
        if (!IsServer || senderId != OwnerClientId) {
            return;
        }
        moveInput = command.Move;
    }

    private void Update() {
        if (!IsServer) {
            return;
        }

        // translation
        Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y);
        Vector3 delta = direction * moveSpeed * Time.deltaTime;
        transform.position += delta;

        // rotation
        if (direction.sqrMagnitude > 0f) {
            Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
            float maxStep = maxSnapTurnDegreePerSec * Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, maxStep);
        }
    }
}