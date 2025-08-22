using UnityEngine;
using Unity.Netcode;

[DefaultExecutionOrder(-10000)] // highest order
public class NetworkManagerPersist : MonoBehaviour {
    void Awake() {
        // singleton
        var nm = GetComponent<NetworkManager>();
        if (NetworkManager.Singleton != null && NetworkManager.Singleton != nm) {
            Destroy(gameObject);
            return;
        }

        DontDestroyOnLoad(gameObject);
    }
}
