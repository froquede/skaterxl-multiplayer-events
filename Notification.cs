
using UnityEngine;

namespace MultiplayerEvents
{
    public class Notification : MonoBehaviour
    {
        public string notificationText = "";
        private float displayTime;
        bool destroy = false;

        void OnGUI()
        {
            if (Time.time <= displayTime)
            {
                GUIStyle style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontSize = 20;
                style.normal.textColor = Color.white;
                float screenWidth = Screen.width;
                float screenHeight = Screen.height;

                Rect rect = new Rect(0, screenHeight - 50, screenWidth, 30);

                GUILayout.BeginArea(rect);
                GUILayout.Label(notificationText, style);
                GUILayout.EndArea();
            }
            else destroy = true;
        }

        void LateUpdate()
        {
            // Destroy the whole GameObject; Destroy(this) would only remove the
            // component and leak an empty GameObject for every notification shown.
            if (destroy) Destroy(gameObject);
        }

        public void ShowNotification(string message, float displayDuration)
        {
            notificationText = message;
            displayTime = Time.time + displayDuration;
        }
    }
}
