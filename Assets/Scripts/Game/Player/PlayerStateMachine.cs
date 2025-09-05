using UnityEngine;
using Unity.Netcode;
using UnityEditor.XR;

public enum PlayerState {
    Idle,
    Move,
    Jump,
    CloseAttack,
    RangedAttack
    // ...
}

public struct StatePacket : INetworkSerializable {
    public PlayerState State;
    public uint seq; // 몇 번째 상태인지(애니메이션에서 동일 상태 반복 확인 용으로 필요)
                     // 현재 일회성 상태는 종료 시 반드시 Idle/Walk 중 하나로 전환되도록 로직이 짜여 있음.
                     // 다만, 클라이언트에서는 Update에서 이를 확인하기 때문에 Jump->Idle->Jump로 즉각 전환이 이루어지면 놓칠 수 있다.
                     // 따라서 seq를 두어 현재 상태가 이전과 별개로 진입한 상태인지 확인할 수 있도록 한다.
    public double StartServerTime; // 상태 전환 시점(late join) 핸들링
                                   // 본 게임에선 게임 시작 후 추가 입장이 불가능하게 로직이 짜여져 있음 (late join 없음)
                                   // 이 파라미터가 반드시 필요하지는 않지만, 연습 삼아 + 추후 확장성을 위해 추가
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref seq);
        serializer.SerializeValue(ref StartServerTime);
    }
}

public class PlayerStateMachine : NetworkBehaviour {
    public NetworkVariable<StatePacket> statePacket = new NetworkVariable<StatePacket>(writePerm: NetworkVariableWritePermission.Server);
    public PlayerState CurrentState => statePacket.Value.State;

    private uint currentSeq = 0;

    private void SetState(PlayerState newState) {
        if (CurrentState == newState) {
            return;
        }

        statePacket.Value = new StatePacket {
            State = newState,
            seq = ++currentSeq,
            StartServerTime = NetworkManager.ServerTime.Time,
        };

        // Debug.Log($"[Player{OwnerClientId}] SetState: from {CurrentState} to {newState}; seq = {currentSeq}");
    }

    public void SetMove(bool isMoving) {
        if (!IsServer) {
            return;
        }

        var nextState = isMoving ? PlayerState.Move : PlayerState.Idle;
        SetState(nextState);
    }

    public bool TryJump() {
        if (!IsServer) {
            return false;
        }

        switch (CurrentState) {
            case PlayerState.Idle:
            case PlayerState.Move:
                SetState(PlayerState.Jump);
                return true;

            default:
                return false;
        }
    }

    public void EndJump(bool isMoving) {
        if (!IsServer || CurrentState != PlayerState.Jump) {
            return;
        }

        // SetState(PlayerState.Idle);
        SetMove(isMoving);
    }

    public bool TryCloseAttack() {
        if (!IsServer) {
            return false;
        }

        switch (CurrentState) {
            case PlayerState.Idle:
            case PlayerState.Move:
                SetState(PlayerState.CloseAttack);
                return true;

            default:
                return false;
        }
    }

    public void EndCloseAttack(bool isMoving) {
        if (!IsServer || CurrentState != PlayerState.CloseAttack) {
            return;
        }

        // SetState(PlayerState.Idle);
        SetMove(isMoving);
    }

    public bool TryRangedAttack() {
        return true;
    }
}