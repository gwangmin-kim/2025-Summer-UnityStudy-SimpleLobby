using UnityEngine;
using Unity.Netcode;

using System;

[Flags]
public enum ButtonBits : byte {
    None = 0,
    JumpDown = 1 << 0, // Space
    CloseAttack = 1 << 1, // F
    RangedAttack = 1 << 2, // Left Click
    UseItem = 1 << 3, // Right Click
    // 필요 시 Up 엣지도 별도 비트로 추가 가능 (e.g., JumpUp = 1<<4)
}

public struct PlayerCommand : INetworkSerializable {
    public Vector2 Move; // WASD, Hold
    public ButtonBits Buttons;
    public uint Seq; // 순서(나중에 보정/중복 제거에 유용)

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter {
        serializer.SerializeValue(ref Move);
        serializer.SerializeValue(ref Buttons);
        serializer.SerializeValue(ref Seq);
    }
}