using System;
using System.Collections.Generic;
using UnityEngine;
using UnityModManagerNet;

namespace MultiplayerEvents
{
    [Serializable]
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        public Color fontColor = Color.gray;
        public Color fontColorAccent = Color.white;
        public int maxRetries = GameConfig.DefaultRetries; // redo attempts allowed per setting turn
        public string skateWord = GameConfig.DefaultSkateWord; // characters spelled to lose (S.K.A.T.E.); letters or digits
        public float smallManualMaxSeconds = GameConfig.SmallManualMaxSeconds; // manuals shorter than this are ignored when matching tricks

        // Blocked players ("Nick | UserId"; UserId may be empty for a name-only block). A blocked
        // player can't invite you to any event, is hidden from opponent lists, and their event
        // traffic is ignored. See Utils.IsBlocked / BlockPlayer.
        public List<string> blockedPlayers = new List<string>();
        public void OnChange()
        {
            // Nothing to do on change, but must not throw: UMM may invoke this.
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save<Settings>(this, modEntry);
        }
    }
}
