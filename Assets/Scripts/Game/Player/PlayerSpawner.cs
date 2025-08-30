using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;

using System.Collections.Generic;

public class PlayerSpawner : NetworkBehaviour {
    [SerializeField] private NetworkObject playerPrefab;
    [SerializeField] private Transform[] spawnPoints;

    // clientId -> spawned player object
    private readonly Dictionary<ulong, NetworkObject> spawnedPlayers = new();
    // clientId -> spawn index
    private readonly Dictionary<ulong, int> assignedIndices = new();
    // empty indices pool
    private readonly SortedSet<int> freeIndices = new();

    private void Awake() {
        // initialize indices pool
        for (int i = 0; i < spawnPoints.Length; i++) {
            freeIndices.Add(i);
        }
    }

    private void OnEnable() {
        var nm = NetworkManager.Singleton;
        if (nm == null) {
            return;
        }
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        NetworkManager.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
    }

    private void OnDisable() {
        var nm = NetworkManager.Singleton;
        if (nm == null) {
            return;
        }
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;

        NetworkManager.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
    }

    private void OnClientConnected(ulong clientId) {
        // to cover late-join
        if (!IsServer || SceneManager.GetActiveScene().name != "Game") {
            return;
        }

        // 네트워크 씬 매니저 없이 직접 씬 로드했다면, 여기서도 스폰할 수 있음.
        // 다만 권장은 OnLoadEventCompleted에서 스폰 타이밍을 맞추는 것.
        TrySpawnPlayer(clientId);
    }

    private void OnClientDisconnected(ulong clientId) {
        if (!IsServer) {
            return;
        }

        if (spawnedPlayers.TryGetValue(clientId, out var networkObject) && networkObject && networkObject.IsSpawned) {
            networkObject.Despawn(true);
        }
        spawnedPlayers.Remove(clientId);

        if (assignedIndices.TryGetValue(clientId, out int idx)) {
            assignedIndices.Remove(clientId);
            freeIndices.Add(idx); // return index
        }
    }

    private void OnLoadEventCompleted(string sceneName, LoadSceneMode mode,
                                      List<ulong> clientsCompleted,
                                      List<ulong> clientsTimedOut) {
        if (!IsServer || sceneName != "Game") {
            return;
        }

        // spawn all clients who loaded Game scene
        foreach (var clientId in clientsCompleted) {
            TrySpawnPlayer(clientId);
        }
    }

    private void TrySpawnPlayer(ulong clientId) {
        // prevent double spawn
        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out var networkClient)) {
            if (networkClient.PlayerObject != null && networkClient.PlayerObject.IsSpawned) {
                return;
            }
        }
        if (spawnedPlayers.ContainsKey(clientId)) {
            return;
        }

        Transform spawnPoint = GetSpawnPoint(clientId);

        NetworkObject instance = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);
        // 이 줄이 핵심: 해당 clientId의 "PlayerObject"로 지정되며 오너십도 설정됨
        instance.SpawnAsPlayerObject(clientId, true);

        spawnedPlayers[clientId] = instance;
    }

    private Transform GetSpawnPoint(ulong clientId) {
        // if already assigned
        if (assignedIndices.TryGetValue(clientId, out int index)) {
            return spawnPoints[index];
        }

        // assign new free index
        if (freeIndices.Count > 0) {
            index = Min(freeIndices);
            freeIndices.Remove(index);
            return spawnPoints[index];
        }

        // fallback
        Debug.LogWarning($"GetSpawnPoint: no free spawn point for {clientId}. spawn at origin.");
        return new GameObject($"Spawn_{clientId}").transform;
    }

    private static int Min(SortedSet<int> set) => set.Min;
}