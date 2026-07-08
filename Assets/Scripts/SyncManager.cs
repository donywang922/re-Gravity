
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using TMPro;
using UnityEngine.UI;

public class SyncManager : UdonSharpBehaviour
{
    [Header("Dependencies")] public GravitySimulator simulator;
    public TrailManager trailManager;

    [Header("Player Data Pool")] public PlayerSnapshotData[] playerDataPool;

    [Header("UI References")] public PlayerRow playerRowPrefab;
    public Transform playerListContent;
    public GameObject loadingOverlay;
    public TextMeshProUGUI loadingText;
    public Slider progressBar;
    public TextMeshProUGUI snapshotStatusText;

    private PlayerRow[] _playerRows = new PlayerRow[64];
    private int _currentRowCount = 0;

    // Local snapshot data
    private Color[] _localPosBuffer = new Color[65536];
    private Color[] _localVelBuffer = new Color[65536];
    private int _localSnapshotSize = 0;
    // private bool _hasLocalSnapshot = false;

    // Receive state
    private int _receivingFromId = -1;
    private int _receiveTotalSize = 0;
    private int _receiveCurrentChunk = -1;
    private Color[] _receivePosBuffer = new Color[65536];
    private Color[] _receiveVelBuffer = new Color[65536];

    // private float _listUpdateTimer = 0;

    private void Start()
    {
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
        UpdatePlayerList();
    }

    public override void OnPlayerJoined(VRCPlayerApi player)
    {
        UpdatePlayerList();
    }

    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        UpdatePlayerList();
    }

    private void UpdatePlayerList()
    {
        VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
        VRCPlayerApi.GetPlayers(players);

        // Ensure enough rows
        while (_currentRowCount < players.Length)
        {
            PlayerRow rowScript = Instantiate(playerRowPrefab);
            rowScript.transform.SetParent(playerListContent, false);
            rowScript.manager = this;
            _playerRows[_currentRowCount] = rowScript;
            _currentRowCount++;
        }

        // Hide all first
        for (int i = 0; i < _currentRowCount; i++)
        {
            _playerRows[i].gameObject.SetActive(false);
        }

        // Populate valid players
        for (int i = 0; i < players.Length; i++)
        {
            if (!Utilities.IsValid(players[i])) continue;

            PlayerRow row = _playerRows[i];

            row.gameObject.SetActive(true);
            row.playerId = players[i].playerId;
            row.playerNameText.text = players[i].displayName;

            // Find if this player has a snapshot
            PlayerSnapshotData data = GetPlayerDataById(players[i].playerId);
            bool hasSnap = data.hasSnapshot;
            row.syncButtonObj.SetActive(hasSnap && players[i].playerId != Networking.LocalPlayer.playerId);
        }
    }

    private PlayerSnapshotData GetPlayerDataById(int playerId)
    {
        for (int i = 0; i < playerDataPool.Length; i++)
        {
            if (playerDataPool[i].ownerPlayerId == playerId)
                return playerDataPool[i];
        }

        return null;
    }

    public PlayerSnapshotData GetLocalPlayerData()
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return null;

        PlayerSnapshotData existing = GetPlayerDataById(local.playerId);
        if (existing != null) return existing;

        // Try claim one
        for (int i = 0; i < playerDataPool.Length; i++)
        {
            int owner = playerDataPool[i].ownerPlayerId;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(owner);
            if (!Utilities.IsValid(p) || p.playerId != owner)
            {
                Networking.SetOwner(local, playerDataPool[i].gameObject);
                playerDataPool[i].ownerPlayerId = local.playerId;
                playerDataPool[i].hasSnapshot = false;
                playerDataPool[i].snapshotSize = 0;
                playerDataPool[i].targetReceiverId = 0;
                playerDataPool[i].requestingFromId = 0;
                playerDataPool[i].currentChunk = -1;
                playerDataPool[i].RequestSerialization();
                return playerDataPool[i];
            }
        }

        return null;
    }

    public void OnBtnTakeSnapshot()
    {
        simulator.StartSnapshot();
    }

    public void OnSnapshotComplete(int activeCount, Color[] posBuffer, Color[] velBuffer)
    {
        _localSnapshotSize = activeCount;
        for (int i = 0; i < activeCount; i++)
        {
            _localPosBuffer[i] = posBuffer[i];
            _localVelBuffer[i] = velBuffer[i];
        }

        // _hasLocalSnapshot = true;
        if (loadingOverlay != null) loadingOverlay.SetActive(false);

        PlayerSnapshotData localData = GetLocalPlayerData();
        if (localData != null)
        {
            localData.hasSnapshot = true;
            localData.snapshotSize = activeCount;
            localData.RequestSerialization();
        }

        UpdatePlayerList();
        
        if (snapshotStatusText != null)
        {
            if (activeCount > 0)
            {
                snapshotStatusText.text = $"抓取成功 ({System.DateTime.Now.ToString("HH:mm:ss")})";
            }
            else
            {
                snapshotStatusText.text = "抓取失败";
            }
        }
    }

    public void RequestSyncFromPlayer(int targetPlayerId)
    {
        PlayerSnapshotData localData = GetLocalPlayerData();
        if (localData == null) return;

        PlayerSnapshotData targetData = GetPlayerDataById(targetPlayerId);
        if (targetData == null || !targetData.hasSnapshot) return;

        loadingOverlay.SetActive(true);
        loadingText.text = "Requesting Sync...";
        progressBar.value = 0;

        _receivingFromId = targetPlayerId;
        _receiveTotalSize = targetData.snapshotSize;
        _receiveCurrentChunk = -1;

        localData.requestingFromId = targetPlayerId;
        localData.ackChunk = -1;
        localData.RequestSerialization();
    }

    public void OnPlayerDataUpdated(PlayerSnapshotData data)
    {
        VRCPlayerApi local = Networking.LocalPlayer;
        if (!Utilities.IsValid(local)) return;

        // Update button for this player
        for (int i = 0; i < _currentRowCount; i++)
        {
            if (_playerRows[i].gameObject.activeSelf && _playerRows[i].playerId == data.ownerPlayerId)
            {
                _playerRows[i].syncButtonObj.SetActive(data.hasSnapshot && data.ownerPlayerId != local.playerId);
                break;
            }
        }

        PlayerSnapshotData localData = GetLocalPlayerData();
        if (localData == null) return;

        // If I am the SENDER
        if (data.ownerPlayerId != local.playerId && data.requestingFromId == local.playerId)
        {
            // Someone is requesting data from me
            int targetId = data.ownerPlayerId;
            int ack = data.ackChunk;

            if (localData.targetReceiverId != targetId)
            {
                // New request
                localData.targetReceiverId = targetId;
                localData.currentChunk = 0;
                SendChunkData(localData, 0);
            }
            else if (ack == localData.currentChunk)
            {
                // They ack'd my current chunk, move to next
                int nextChunk = localData.currentChunk + 1;
                if (nextChunk * 128 >= _localSnapshotSize)
                {
                    // Finished sending!
                    localData.targetReceiverId = 0;
                    localData.currentChunk = -1;
                    localData.RequestSerialization();
                }
                else
                {
                    localData.currentChunk = nextChunk;
                    SendChunkData(localData, nextChunk);
                }
            }
        }
        else if (data.ownerPlayerId != local.playerId && data.requestingFromId != local.playerId &&
                 localData.targetReceiverId == data.ownerPlayerId)
        {
            // The person I was sending to stopped requesting or switched
            localData.targetReceiverId = 0;
            localData.currentChunk = -1;
            localData.RequestSerialization();
        }

        // If I am the RECEIVER
        if (data.ownerPlayerId == _receivingFromId && data.targetReceiverId == local.playerId)
        {
            int chunkIndex = data.currentChunk;
            if (chunkIndex > _receiveCurrentChunk)
            {
                // Received new chunk
                _receiveCurrentChunk = chunkIndex;
                int startIndex = chunkIndex * 128;
                int count = Mathf.Min(128, _receiveTotalSize - startIndex);

                for (int i = 0; i < count; i++)
                {
                    _receivePosBuffer[startIndex + i] = data.chunkPosData[i];
                    _receiveVelBuffer[startIndex + i] = data.chunkVelData[i];
                }

                // Update Progress
                if (progressBar != null) progressBar.value = (float)startIndex / _receiveTotalSize;
                if (loadingText != null) loadingText.text = $"Downloading... {startIndex}/{_receiveTotalSize}";

                // Ack the chunk
                localData.ackChunk = chunkIndex;
                localData.RequestSerialization();

                if (startIndex + count >= _receiveTotalSize)
                {
                    // Finished receiving
                    _receivingFromId = -1;
                    localData.requestingFromId = 0;
                    localData.RequestSerialization();

                    if (loadingText != null) loadingText.text = "Applying Data...";
                    simulator.ApplyDownloadedSnapshot(_receiveTotalSize, _receivePosBuffer, _receiveVelBuffer);
                    if (trailManager != null) trailManager.ClearTrails();
                }
            }
        }
    }

    private void SendChunkData(PlayerSnapshotData localData, int chunkIndex)
    {
        int startIndex = chunkIndex * 128;
        int count = Mathf.Min(128, _localSnapshotSize - startIndex);

        for (int i = 0; i < count; i++)
        {
            localData.chunkPosData[i] = _localPosBuffer[startIndex + i];
            localData.chunkVelData[i] = _localVelBuffer[startIndex + i];
        }

        localData.RequestSerialization();
    }

    public void OnApplySnapshotComplete()
    {
        if (loadingOverlay != null) loadingOverlay.SetActive(false);
    }
}
