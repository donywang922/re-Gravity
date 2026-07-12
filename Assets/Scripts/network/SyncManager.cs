using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace network
{
    public class SyncManager : UdonSharpBehaviour
    {
        [Header("Dependencies")] public GravitySimulator simulator;
        public TrailManager trailManager;

        [Header("Player Data Pools")]
        public PlayerState[] playerStatePool;
        public TransferChannel[] transferChannelPool;

        [Header("UI References")] public GameObject playerRowPrefab;
        public Transform playerListContent;
        public GameObject loadingOverlay;
        public TextMeshProUGUI loadingText;
        public TextMeshProUGUI snapshotStatusText;

        // --- Player list UI ---
        private PlayerRow[] _playerRows = new PlayerRow[64];
        private int _currentRowCount = 0;

        // --- Local snapshot data ---
        private Color[] _localPosBuffer = new Color[65536];
        private Color[] _localVelBuffer = new Color[65536];
        private int _localSnapshotSize = 0;
        private int _localSnapshotMaxBodies = 0;
        private float _localSnapshotGravConst = 0f;

        // --- Cached local references ---
        private PlayerState _localPlayerState = null;
        private TransferChannel _localTransferChannel = null;

        // --- Sender state ---
        private bool _isSending = false;
        private int _sendTargetId = -1;
        private int _sendCurrentChunk = -1;
        private int _sendTotalChunks = 0;
        private float _sendLastAckTime = 0f;
        private int _sendRetryCount = 0;

        // --- Sender queue (for multiple requesters) ---
        private int[] _pendingReceivers = new int[16];
        private int _pendingCount = 0;

        // --- Receiver state ---
        private int _receivingFromId = -1;
        private int _receiveTotalSize = 0;
        private int _receiveCurrentChunk = -1;
        private Color[] _receivePosBuffer = new Color[65536];
        private Color[] _receiveVelBuffer = new Color[65536];
        private float _lastReceiveTime = 0f;

        // --- Constants ---
        private const int CHUNK_SIZE = 128;
        private const float SEND_TIMEOUT = 15f;
        private const float RECEIVE_TIMEOUT = 15f;
        private const int MAX_RETRIES = 3;

        // ====================================================================
        //  LIFECYCLE
        // ====================================================================

        private void Start()
        {
            loadingOverlay.SetActive(false);
            ClaimLocalSlot();
            UpdatePlayerList();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            UpdatePlayerList();
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            // 如果离开的玩家是我们的发送目标，终止发送
            if (_isSending && player.playerId == _sendTargetId)
            {
                AbortSend();
                StartNextQueued();
            }

            // 如果离开的玩家是我们的接收来源，取消接收
            if (player.playerId == _receivingFromId)
            {
                CancelReceive("对方已离开");
            }

            // 从待发送队列中移除
            RemoveFromQueue(player.playerId);

            UpdatePlayerList();
        }

        private void Update()
        {
            // 发送超时检测
            if (_isSending && Time.time - _sendLastAckTime > SEND_TIMEOUT)
            {
                _sendRetryCount++;
                if (_sendRetryCount > MAX_RETRIES)
                {
                    AbortSend();
                    StartNextQueued();
                }
                else
                {
                    // 重发当前 chunk
                    SendChunk(_sendCurrentChunk);
                    _sendLastAckTime = Time.time;
                }
            }

            // 接收超时检测
            if (_receivingFromId != -1 && Time.time - _lastReceiveTime > RECEIVE_TIMEOUT)
            {
                CancelReceive("接收超时");
            }
        }

        // ====================================================================
        //  SLOT CLAIMING
        // ====================================================================

        private void ClaimLocalSlot()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            // --- Claim PlayerState ---
            _localPlayerState = GetPlayerStateByPlayerId(local.playerId);
            if (_localPlayerState == null)
            {
                for (int i = 0; i < playerStatePool.Length; i++)
                {
                    int ownerId = playerStatePool[i].ownerPlayerId;
                    VRCPlayerApi owner = VRCPlayerApi.GetPlayerById(ownerId);
                    if (!Utilities.IsValid(owner) || owner.playerId != ownerId)
                    {
                        Networking.SetOwner(local, playerStatePool[i].gameObject);
                        playerStatePool[i].ownerPlayerId = local.playerId;
                        playerStatePool[i].ResetState();
                        playerStatePool[i].RequestSerialization();
                        _localPlayerState = playerStatePool[i];
                        break;
                    }
                }
            }

            // --- Claim TransferChannel ---
            _localTransferChannel = GetTransferChannelByPlayerId(local.playerId);
            if (_localTransferChannel == null)
            {
                for (int i = 0; i < transferChannelPool.Length; i++)
                {
                    int ownerId = transferChannelPool[i].ownerPlayerId;
                    VRCPlayerApi owner = VRCPlayerApi.GetPlayerById(ownerId);
                    if (!Utilities.IsValid(owner) || owner.playerId != ownerId)
                    {
                        Networking.SetOwner(local, transferChannelPool[i].gameObject);
                        transferChannelPool[i].ownerPlayerId = local.playerId;
                        transferChannelPool[i].ResetChannel();
                        transferChannelPool[i].RequestSerialization();
                        _localTransferChannel = transferChannelPool[i];
                        break;
                    }
                }
            }
        }

        // ====================================================================
        //  LOOKUP HELPERS
        // ====================================================================

        private PlayerState GetPlayerStateByPlayerId(int playerId)
        {
            for (int i = 0; i < playerStatePool.Length; i++)
            {
                if (playerStatePool[i].ownerPlayerId == playerId)
                    return playerStatePool[i];
            }
            return null;
        }

        private TransferChannel GetTransferChannelByPlayerId(int playerId)
        {
            for (int i = 0; i < transferChannelPool.Length; i++)
            {
                if (transferChannelPool[i].ownerPlayerId == playerId)
                    return transferChannelPool[i];
            }
            return null;
        }

        private PlayerState GetLocalPlayerState()
        {
            if (_localPlayerState != null) return _localPlayerState;
            ClaimLocalSlot();
            return _localPlayerState;
        }

        private TransferChannel GetLocalTransferChannel()
        {
            if (_localTransferChannel != null) return _localTransferChannel;
            ClaimLocalSlot();
            return _localTransferChannel;
        }

        // ====================================================================
        //  UI — PLAYER LIST
        // ====================================================================

        private void UpdatePlayerList()
        {
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            // Ensure enough rows
            while (_currentRowCount < players.Length)
            {
                GameObject row = Instantiate(playerRowPrefab, playerListContent);
                PlayerRow rowScript = (PlayerRow)row.GetComponent(typeof(UdonBehaviour));
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

                // 查找此玩家的 PlayerState，判断是否有快照
                PlayerState ps = GetPlayerStateByPlayerId(players[i].playerId);
                bool hasSnap = ps != null && ps.hasSnapshot;
                // bool isNotSelf = players[i].playerId != local.playerId;
                // row.syncButtonObj.SetActive(hasSnap && isNotSelf);
                row.syncButtonObj.SetActive(hasSnap);
            }
        }

        private void UpdatePlayerRowForState(PlayerState state)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            for (int i = 0; i < _currentRowCount; i++)
            {
                if (_playerRows[i].gameObject.activeSelf && _playerRows[i].playerId == state.ownerPlayerId)
                {
                    // bool isNotSelf = state.ownerPlayerId != local.playerId;
                    // _playerRows[i].syncButtonObj.SetActive(state.hasSnapshot && isNotSelf);
                    _playerRows[i].syncButtonObj.SetActive(state.hasSnapshot);
                    break;
                }
            }
        }

        // ====================================================================
        //  SNAPSHOT CAPTURE
        // ====================================================================

        public void OnBtnTakeSnapshot()
        {
            if (_isSending)
            {
                snapshotStatusText.text = "发送中，无法抓取";
                return;
            }
            simulator.StartSnapshot();
        }

        public void OnSnapshotComplete(int activeCount, Color[] posBuffer, Color[] velBuffer)
        {
            _localSnapshotSize = activeCount;
            _localSnapshotMaxBodies = simulator.ctrlPanel.activeMaxBodies;
            _localSnapshotGravConst = simulator.ctrlPanel.activeGravConst;
            for (int i = 0; i < activeCount; i++)
            {
                _localPosBuffer[i] = posBuffer[i];
                _localVelBuffer[i] = velBuffer[i];
            }

            // 更新 PlayerState 广播
            PlayerState ps = GetLocalPlayerState();
            if (ps != null)
            {
                ps.hasSnapshot = true;
                ps.snapshotSize = activeCount;
                ps.RequestSerialization();
            }

            if (activeCount > 0)
            {
                snapshotStatusText.text = $"抓取成功 ({System.DateTime.Now.ToString("HH:mm:ss")})";
            }
            else
            {
                snapshotStatusText.text = "抓取失败";
            }

            UpdatePlayerList();
        }

        // ====================================================================
        //  DESERIALIZATION CALLBACKS
        // ====================================================================

        /// <summary>
        /// 任何 PlayerState 变化时由 PlayerState.OnDeserialization() 调用。
        /// 处理：发送方检测请求 / ACK；UI 更新。
        /// </summary>
        public void OnPlayerStateUpdated(PlayerState state)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            int myId = local.playerId;

            // 更新 UI
            UpdatePlayerRowForState(state);

            // 忽略自己的 PlayerState 变化
            // if (state.ownerPlayerId == myId) return;

            // --- SENDER LOGIC ---

            // Case 1: 此玩家正在向我请求快照
            if (state.requestingFromId == myId)
            {
                int requesterId = state.ownerPlayerId;

                if (_isSending && _sendTargetId == requesterId)
                {
                    // 当前目标的 ACK — 发送下一个 chunk
                    if (state.ackChunk >= _sendCurrentChunk)
                    {
                        _sendRetryCount = 0;
                        _sendLastAckTime = Time.time;
                        int nextChunk = state.ackChunk + 1;
                        if (nextChunk >= _sendTotalChunks)
                        {
                            CompleteSend();
                        }
                        else
                        {
                            SendChunk(nextChunk);
                        }
                    }
                }
                else if (!_isSending && _localSnapshotSize > 0)
                {
                    // 空闲且有快照，开始发送
                    StartSending(requesterId);
                }
                else if (_isSending)
                {
                    // 正在发送给别人，排队
                    EnqueueReceiver(requesterId);
                }
            }

            // Case 2: 当前发送目标取消了请求
            if (_isSending && state.ownerPlayerId == _sendTargetId && state.requestingFromId != myId)
            {
                AbortSend();
                StartNextQueued();
            }
        }

        /// <summary>
        /// 任何 TransferChannel 变化时由 TransferChannel.OnDeserialization() 调用。
        /// 处理：接收方读取 chunk 数据。
        /// </summary>
        public void OnTransferChannelUpdated(TransferChannel channel)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;
            int myId = local.playerId;

            // 只关心发送给我的通道
            if (channel.receiverId != myId) return;

            // 确认是我正在等待的发送者
            if (channel.ownerPlayerId != _receivingFromId) return;

            if (channel.phase == 1) // Sending
            {
                int chunkIndex = channel.currentChunk;
                if (chunkIndex > _receiveCurrentChunk)
                {
                    // 接收新 chunk
                    _receiveCurrentChunk = chunkIndex;
                    _lastReceiveTime = Time.time;
                    _receiveTotalSize = channel.totalSize;

                    int startIndex = chunkIndex * CHUNK_SIZE;
                    int count = Mathf.Min(CHUNK_SIZE, _receiveTotalSize - startIndex);

                    for (int i = 0; i < count; i++)
                    {
                        _receivePosBuffer[startIndex + i] = channel.chunkPosData[i];
                        _receiveVelBuffer[startIndex + i] = channel.chunkVelData[i];
                    }

                    // 更新进度 UI
                    int received = startIndex + count;
                    if (loadingText != null)
                        loadingText.text = $"下载中... {received}/{_receiveTotalSize}";

                    // 发送 ACK（仅序列化 PlayerState，~30 bytes）
                    PlayerState ps = GetLocalPlayerState();
                    if (ps != null)
                    {
                        ps.ackChunk = chunkIndex;
                        ps.RequestSerialization();
                    }

                    // 检查是否接收完毕
                    if (received >= _receiveTotalSize)
                    {
                        ApplyReceivedSnapshot();
                    }
                }
            }
            else if (channel.phase == 2) // Complete
            {
                // 发送方确认完毕（冗余校验）
                if (_receivingFromId != -1)
                {
                    int totalReceived = (_receiveCurrentChunk + 1) * CHUNK_SIZE;
                    if (totalReceived >= _receiveTotalSize)
                    {
                        ApplyReceivedSnapshot();
                    }
                }
            }
        }

        // ====================================================================
        //  RECEIVER — REQUEST & RECEIVE
        // ====================================================================

        public void RequestSyncFromPlayer(int targetPlayerId)
        {
            if (_receivingFromId != -1) return; // 已经在接收中

            VRCPlayerApi local = Networking.LocalPlayer;
            if (!Utilities.IsValid(local)) return;

            // --- 自己下载自己的快照：跳过网络，直接本地应用 ---
            if (targetPlayerId == local.playerId)
            {
                if (_localSnapshotSize <= 0) return;

                loadingOverlay.SetActive(true);
                if (loadingText != null) loadingText.text = "正在应用本地快照...";

                ApplySnapshotSettings(_localSnapshotMaxBodies, _localSnapshotGravConst);
                simulator.ApplyDownloadedSnapshot(_localSnapshotSize, _localPosBuffer, _localVelBuffer);
                if (trailManager != null) trailManager.ClearTrails();
                return;
            }

            // --- 从其他玩家下载 ---
            PlayerState targetState = GetPlayerStateByPlayerId(targetPlayerId);
            if (targetState == null || !targetState.hasSnapshot) return;

            PlayerState localState = GetLocalPlayerState();
            if (localState == null) return;

            // 显示加载 UI
            loadingOverlay.SetActive(true);
            if (loadingText != null) loadingText.text = "请求同步中...";

            // 设置接收状态
            _receivingFromId = targetPlayerId;
            _receiveTotalSize = targetState.snapshotSize;
            _receiveCurrentChunk = -1;
            _lastReceiveTime = Time.time;

            // 通过 PlayerState 发出请求（轻量序列化）
            localState.requestingFromId = targetPlayerId;
            localState.ackChunk = -1;
            localState.RequestSerialization();
        }

        private void ApplyReceivedSnapshot()
        {
            // 清理接收状态
            int receivedFromId = _receivingFromId;
            _receivingFromId = -1;

            // 清除请求标记
            PlayerState ps = GetLocalPlayerState();
            if (ps != null)
            {
                ps.requestingFromId = 0;
                ps.ackChunk = -1;
                ps.RequestSerialization();
            }

            if (loadingText != null) loadingText.text = "正在应用数据...";

            // 应用快照配置
            TransferChannel senderChannel = GetTransferChannelByPlayerId(receivedFromId);
            if (senderChannel != null)
            {
                ApplySnapshotSettings(senderChannel.snapshotMaxBodies, senderChannel.snapshotGravConst);
            }

            // 应用快照数据
            simulator.ApplyDownloadedSnapshot(_receiveTotalSize, _receivePosBuffer, _receiveVelBuffer);
            if (trailManager != null) trailManager.ClearTrails();
        }

        private void ApplySnapshotSettings(int maxBodies, float gravConst)
        {
            simulator.ctrlPanel.activeMaxBodies = maxBodies;
            simulator.ctrlPanel.activeGravConst = gravConst;
            VRCShader.SetGlobalFloat(VRCShader.PropertyToID("_Udon_GravitationalConstant"), gravConst);

            // 同步滑条 UI
            simulator.ctrlPanel.maxBodiesSlider.SetValueAndRefresh(maxBodies / 256);
            simulator.ctrlPanel.gravConstSlider.SetValueAndRefresh(gravConst);
        }

        private void CancelReceive(string reason)
        {
            _receivingFromId = -1;

            PlayerState ps = GetLocalPlayerState();
            if (ps != null)
            {
                ps.requestingFromId = 0;
                ps.ackChunk = -1;
                ps.RequestSerialization();
            }

            if (loadingOverlay != null) loadingOverlay.SetActive(false);
            if (snapshotStatusText != null) snapshotStatusText.text = $"同步失败: {reason}";
        }

        // ====================================================================
        //  SENDER — SEND CHUNKS
        // ====================================================================

        private void StartSending(int receiverId)
        {
            TransferChannel tc = GetLocalTransferChannel();
            if (tc == null) return;

            _isSending = true;
            _sendTargetId = receiverId;
            _sendTotalChunks = (_localSnapshotSize + CHUNK_SIZE - 1) / CHUNK_SIZE;
            _sendCurrentChunk = -1;
            _sendRetryCount = 0;
            _sendLastAckTime = Time.time;

            // 设置传输通道元信息
            tc.phase = 1; // Sending
            tc.receiverId = receiverId;
            tc.totalSize = _localSnapshotSize;
            tc.totalChunks = _sendTotalChunks;
            tc.snapshotMaxBodies = _localSnapshotMaxBodies;
            tc.snapshotGravConst = _localSnapshotGravConst;

            // 发送第一个 chunk
            SendChunk(0);
        }

        private void SendChunk(int chunkIndex)
        {
            TransferChannel tc = GetLocalTransferChannel();
            if (tc == null) return;

            _sendCurrentChunk = chunkIndex;

            int startIndex = chunkIndex * CHUNK_SIZE;
            int count = Mathf.Min(CHUNK_SIZE, _localSnapshotSize - startIndex);

            for (int i = 0; i < count; i++)
            {
                tc.chunkPosData[i] = _localPosBuffer[startIndex + i];
                tc.chunkVelData[i] = _localVelBuffer[startIndex + i];
            }

            tc.currentChunk = chunkIndex;
            tc.RequestSerialization();
        }

        private void CompleteSend()
        {
            TransferChannel tc = GetLocalTransferChannel();
            if (tc != null)
            {
                tc.phase = 2; // Complete
                tc.RequestSerialization();
            }

            _isSending = false;
            _sendTargetId = -1;
            _sendCurrentChunk = -1;

            // 处理队列中的下一个请求
            StartNextQueued();
        }

        private void AbortSend()
        {
            TransferChannel tc = GetLocalTransferChannel();
            if (tc != null)
            {
                tc.ResetChannel();
                tc.RequestSerialization();
            }

            _isSending = false;
            _sendTargetId = -1;
            _sendCurrentChunk = -1;
            _sendRetryCount = 0;
        }

        // ====================================================================
        //  SENDER — REQUEST QUEUE
        // ====================================================================

        private void EnqueueReceiver(int receiverId)
        {
            // 检查是否已在队列中
            for (int i = 0; i < _pendingCount; i++)
            {
                if (_pendingReceivers[i] == receiverId) return;
            }

            if (_pendingCount < _pendingReceivers.Length)
            {
                _pendingReceivers[_pendingCount] = receiverId;
                _pendingCount++;
            }
        }

        private void RemoveFromQueue(int receiverId)
        {
            for (int i = 0; i < _pendingCount; i++)
            {
                if (_pendingReceivers[i] == receiverId)
                {
                    // Shift remaining elements
                    for (int j = i; j < _pendingCount - 1; j++)
                    {
                        _pendingReceivers[j] = _pendingReceivers[j + 1];
                    }
                    _pendingCount--;
                    return;
                }
            }
        }

        private void StartNextQueued()
        {
            while (_pendingCount > 0)
            {
                int nextId = _pendingReceivers[0];
                RemoveFromQueue(nextId);

                // 验证此玩家仍在线且仍在请求
                VRCPlayerApi nextPlayer = VRCPlayerApi.GetPlayerById(nextId);
                if (!Utilities.IsValid(nextPlayer)) continue;

                PlayerState nextState = GetPlayerStateByPlayerId(nextId);
                VRCPlayerApi local = Networking.LocalPlayer;
                if (nextState != null && Utilities.IsValid(local) && nextState.requestingFromId == local.playerId)
                {
                    StartSending(nextId);
                    return;
                }
            }
        }

        // ====================================================================
        //  APPLY CALLBACK
        // ====================================================================

        public void OnApplySnapshotComplete()
        {
            if (loadingOverlay != null) loadingOverlay.SetActive(false);
        }
    }
}