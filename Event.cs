using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerEvents
{
    public class Event
    {
        public EventState state = EventState.Stopped;
        public List<NetworkPlayerController> participants = new List<NetworkPlayerController>();
        public bool isWinner = false;

        public void ToggleEventState(EventState newState, EventType type, string UserID = "")
        {
            Utils.Log("Toggling event to " + newState);
            object[] content = new object[] { (int)MessageType.EventState, (int)newState, (int)type, UserID, Utils.GetPlayerID() };
            PhotonNetwork.RaiseEvent(65, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            }, SendOptions.SendReliable);

            state = newState;
        }
    }
}
