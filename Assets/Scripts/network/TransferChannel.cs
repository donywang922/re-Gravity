using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace network
{
    /// <summary>
    /// 数据传输通道。每人认领一个，仅发送方写入。
    /// 包含 chunk 数据（~4KB 每次序列化），接收方通过 OnDeserialization 被动读取。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TransferChannel : UdonSharpBehaviour
    {
        public SyncManager manager;

        public const int CHUNK_SIZE = 128;

        // --- 身份 ---
        [UdonSynced] public int ownerPlayerId = -1;

        // --- 传输控制 ---
        /// <summary>传输阶段。0=Idle, 1=Sending, 2=Complete</summary>
        [UdonSynced] public int phase = 0;

        /// <summary>接收者玩家 ID</summary>
        [UdonSynced] public int receiverId = 0;

        /// <summary>快照中天体总数</summary>
        [UdonSynced] public int totalSize = 0;

        /// <summary>当前正在发送的 chunk 索引</summary>
        [UdonSynced] public int currentChunk = -1;

        /// <summary>总 chunk 数</summary>
        [UdonSynced] public int totalChunks = 0;

        // --- 快照配置（随传输一起发送）---
        /// <summary>快照时的最大天体数</summary>
        [UdonSynced] public int snapshotMaxBodies = 0;

        /// <summary>快照时的引力常数</summary>
        [UdonSynced] public float snapshotGravConst = 0f;

        // --- Chunk 数据 ---
        [UdonSynced] public Color[] chunkPosData;
        [UdonSynced] public Color[] chunkVelData;

        void Start()
        {
            chunkPosData = new Color[CHUNK_SIZE];
            chunkVelData = new Color[CHUNK_SIZE];
        }

        public override void OnDeserialization()
        {
            if (manager != null)
            {
                manager.OnTransferChannelUpdated(this);
            }
        }

        /// <summary>
        /// 重置为空闲状态（认领或传输完成时调用）。
        /// </summary>
        public void ResetChannel()
        {
            phase = 0;
            receiverId = 0;
            totalSize = 0;
            currentChunk = -1;
            totalChunks = 0;
            snapshotMaxBodies = 0;
            snapshotGravConst = 0f;
        }
    }
}