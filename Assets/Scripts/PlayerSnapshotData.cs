using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class PlayerSnapshotData : UdonSharpBehaviour
{
    public SyncManager manager;

    [UdonSynced] public int ownerPlayerId = -1;
    [UdonSynced] public bool hasSnapshot = false;
    [UdonSynced] public int snapshotSize = 0;
    
    // Sender fields
    [UdonSynced] public int targetReceiverId = 0;
    [UdonSynced] public int currentChunk = -1;
    [UdonSynced] public Color[] chunkPosData;
    [UdonSynced] public Color[] chunkVelData;

    // Receiver fields
    [UdonSynced] public int requestingFromId = 0;
    [UdonSynced] public int ackChunk = -1;

    void Start()
    {
        chunkPosData = new Color[128];
        chunkVelData = new Color[128];
    }

    public override void OnDeserialization()
    {
        if (manager != null)
        {
            manager.OnPlayerDataUpdated(this);
        }
    }
}
