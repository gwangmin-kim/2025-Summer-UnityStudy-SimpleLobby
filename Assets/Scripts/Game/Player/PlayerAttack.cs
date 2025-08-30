using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(PlayerStateMachine))]
public class PlayerAttack : NetworkBehaviour {
    [Header("Close Attack")]
    [SerializeField] private Collider closeAttackHitbox;
    [Header("Ranged Attack")]
    [SerializeField] private Boomerang boomerang;
}