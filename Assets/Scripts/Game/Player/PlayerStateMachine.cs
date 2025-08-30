using UnityEngine;
using Unity.Netcode;

public enum PlayerState {
    IdleOrMove,
    Jump,
    Attack,
    // ...
}

public struct PlayerNetState : INetworkSerializable {
    public PlayerState State;
    public double StartServerTime;
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref State);
        serializer.SerializeValue(ref StartServerTime);
    }
}

public class PlayerStateMachine : NetworkBehaviour {
    public NetworkVariable<PlayerNetState> netState = new NetworkVariable<PlayerNetState>(writePerm: NetworkVariableWritePermission.Server);
    public PlayerState CurrentState => netState.Value.State;

    private void SetState(PlayerState newState) {
        if (CurrentState == newState) {
            return;
        }

        Debug.Log($"[Player{OwnerClientId}] SetState: from {CurrentState} to {newState}");

        netState.Value = new PlayerNetState {
            State = newState,
            StartServerTime = NetworkManager.ServerTime.Time,
        };
    }

    public bool TryJump() {
        if (!IsServer) {
            return false;
        }

        switch (CurrentState) {
            case PlayerState.IdleOrMove:
                SetState(PlayerState.Jump);
                return true;

            default:
                return false;
        }
    }

    public void EndJump() {
        if (!IsServer || CurrentState != PlayerState.Jump) {
            return;
        }

        SetState(PlayerState.IdleOrMove);
    }

    public bool TryCloseAttack() {
        if (!IsServer) {
            return false;
        }

        switch (CurrentState) {
            case PlayerState.IdleOrMove:
                SetState(PlayerState.Attack);
                return true;

            default:
                return false;
        }
    }

    public void EndCloseAttack() {
        if (!IsServer || CurrentState != PlayerState.Attack) {
            return;
        }

        SetState(PlayerState.IdleOrMove);
    }
}