using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

public class SnapshotManager : UdonSharpBehaviour
{
    public GravitySimulator simulator;

    [UdonSynced] private string syncedSnapshotData = "";

    private bool isCapturing = false;

    public void CaptureSnapshot()
    {
        if (isCapturing) return;
        isCapturing = true;

        CustomRenderTexture currPosMass = simulator.posMassA;
        VRCAsyncGPUReadback.Request(currPosMass, 0, TextureFormat.RGBAFloat, (IUdonEventReceiver)this);
    }

    public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            isCapturing = false;
            return;
        }

        Color[] posData = new Color[65536];
        if (!request.TryGetData(posData, 0))
        {
            isCapturing = false;
            return;
        }

        string data = "";
        int count = 0;
        for (int i = 0; i < posData.Length; i++)
        {
            data += $"{i},{posData[i].r:F1},{posData[i].g:F1},{posData[i].b:F1},{posData[i].a:F1}|";
            count++;
            if (count > 200) break;
        }

        if (Networking.IsOwner(gameObject))
        {
            syncedSnapshotData = data;
            RequestSerialization();
        }

        isCapturing = false;
        Debug.Log("Snapshot captured and broadcasted.");
    }

    public override void OnDeserialization()
    {
        Debug.Log("Snapshot received with length: " + syncedSnapshotData.Length);
    }
}