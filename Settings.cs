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
        public void OnChange()
        {
            throw new NotImplementedException();
        }

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save<Settings>(this, modEntry);
        }
    }
}
