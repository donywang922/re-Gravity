using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerRow : UdonSharpBehaviour
{
    public TextMeshProUGUI playerNameText;
    public GameObject syncButtonObj;
    
    public SyncManager manager;
    public int playerId = -1;

    public void OnSyncButtonClicked()
    {
        if (playerId != -1)
        {
            manager.RequestSyncFromPlayer(playerId);
        }
    }
}
