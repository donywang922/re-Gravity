using UdonSharp;

namespace network
{
    /// <summary>
    /// 轻量级玩家状态广播。每人认领一个，序列化开销 ~30 bytes。
    /// 包含快照可用性信息和同步请求/ACK 字段。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class PlayerState : UdonSharpBehaviour
    {
        public SyncManager manager;

        // --- 身份 ---
        [UdonSynced] public int ownerPlayerId = -1;

        // --- 快照可用性 ---
        [UdonSynced] public bool hasSnapshot = false;
        [UdonSynced] public int snapshotSize = 0;

        // --- 接收者字段（轻量 ACK 通道）---
        /// <summary>我正在向谁请求快照。0 = 无请求。</summary>
        [UdonSynced] public int requestingFromId = 0;

        /// <summary>我已确认接收到的最新 chunk 索引。-1 = 未开始。</summary>
        [UdonSynced] public int ackChunk = -1;

        public override void OnDeserialization()
        {
            if (manager != null)
            {
                manager.OnPlayerStateUpdated(this);
            }
        }

        /// <summary>
        /// 重置所有字段为初始状态（认领时调用）。
        /// </summary>
        public void ResetState()
        {
            hasSnapshot = false;
            snapshotSize = 0;
            requestingFromId = 0;
            ackChunk = -1;
        }
    }
}