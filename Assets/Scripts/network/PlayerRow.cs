using TMPro;
using UdonSharp;
using UnityEngine;

namespace network
{
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
}