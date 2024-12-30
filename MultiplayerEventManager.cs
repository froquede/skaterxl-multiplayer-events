using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerEvents
{
    class MultiplayerEventManager : IOnEventCallback
    {
        public Event multiplayerEvent;
        public Race race;
        public GameOfSkate SKATE;
        public EventType eventType;
        public bool admin = false;

        public MultiplayerEventManager()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == 65)
            {
                Utils.Log("Event " + photonEvent.Code + " received as client");

                object[] data = (object[])photonEvent.CustomData;
                MessageType type = (MessageType)(int)data[0];
                EventState state = (EventState)(int)data[1];
                EventType eventType = (EventType)(int)data[2];
                string userid = (string)data[3];

                if (userid == "" || userid == MultiplayerManager.Instance.localPlayer.UserId)
                {
                    if (state == EventState.Running && !admin)
                    {
                        CreateEvent(eventType, data);
                    }

                    multiplayerEvent.state = state;
                }
            }
        }

        public void Disable()
        {
            Main.tick.GOSUI = false;

            if (SKATE != null) SKATE.Disable();
            if (race != null) race.Disable();

            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public void Reset()
        {
            SKATE = null;
            race = null;
            multiplayerEvent = null;
        }

        public void CreateEvent(EventType eventType, object[] data)
        {
            if (SKATE != null) SKATE.Disable();
            if (race != null) race.Disable();

            if (eventType == EventType.Race)
            {
                race = new Race();
                multiplayerEvent = race;
            }

            if (eventType == EventType.SKATE)
            {
                SKATE = new GameOfSkate();
                multiplayerEvent = SKATE;
                if (data.Length > 0) SKATE.opponent = (string)data[4];
            }

            foreach (KeyValuePair<int, NetworkPlayerController> entry in MultiplayerManager.Instance.networkPlayers)
            {
                if (entry.Value)
                {
                    multiplayerEvent.participants.Add(entry.Value);
                }
            }

            this.eventType = eventType;
            admin = data.Length == 0; // created locally, doesnt come from the network and data packet is empty
            Utils.ShowNotification("Event " + eventType + (admin ? " created" : " joined"), 2f);
        }

        public void StopEvent()
        {
            if (multiplayerEvent != null)
            {
                multiplayerEvent.ToggleEventState(EventState.Stopped, this.eventType);
                Utils.ShowNotification("Event stopped", 2f);
            }
        }

        public void StartEvent(string opponent = "")
        {
            if (multiplayerEvent != null)
            {
                multiplayerEvent.ToggleEventState(EventState.Running, this.eventType);

                if (eventType == EventType.SKATE) SKATE.StartEvent();
            }
        }
    }
}
