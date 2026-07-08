using System;
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
