using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerAttack : NetworkBehaviour {
    [SerializeField] private LayerMask hitMask;
    [Header("Close Attack")]
    [SerializeField] private BoxCollider swingHitbox;

    [Header("Ranged Attack")]
    [SerializeField] private Boomerang boomerang;

    // close attack hit detection
    private readonly Collider[] swingHits = new Collider[8];
    // for redundancy checking
    private readonly HashSet<ulong> hitThisSwing = new HashSet<ulong>(8);

    // helper functions
    bool IsSelf(Collider col) => col.transform.root == transform.root;

    public void InitCloseAttack() {
        hitThisSwing.Clear();
    }

    public void CloseAttack() {
        if (!IsServer) {
            return;
        }

        if (!swingHitbox.TryGetBoxWorld(out var center, out var halfExtents, out var orientation)) {
            return;
        }

        int hitCount = Physics.OverlapBoxNonAlloc(center, halfExtents, swingHits, orientation, hitMask, QueryTriggerInteraction.Ignore);
        Debug.Log(hitCount);

        for (int i = 0; i < hitCount; i++) {
            var hit = swingHits[i];

            if (IsSelf(hit) || !hit.TryGetComponent<NetworkObject>(out var no)) {
                continue;
            }

            if (hitThisSwing.Contains(no.NetworkObjectId)) {
                continue;
            }

            // 피격 로직
            Debug.Log($"[Player{OwnerClientId}] Close Attack hit: {hit.name}");
            // 피격이 받아들여졌다면 셋에 추가
            hitThisSwing.Add(no.NetworkObjectId);
        }
    }
}