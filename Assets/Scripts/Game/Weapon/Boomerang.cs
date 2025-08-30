using UnityEngine;
using Unity.Netcode;

public enum BoomerangState {
    Holded,
    OnAir,
    Grounded
}

public class Boomerang : NetworkBehaviour {
    public BoomerangState State { get; private set; }
}