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
    class MultiplayerEventManager : IOnEventCallback, IInRoomCallbacks
    {
        public Event multiplayerEvent;
        public Race race;
        public GameOfSkate SKATE;
        public EventType eventType;
        public bool isEventOwner = false; // true if we created the event locally (vs. joined one from the network)

        public MultiplayerEventManager()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == NetCode.EventLifecycle)
            {

                object[] data = photonEvent.CustomData as object[];
                if (data == null || data.Length < 4) return;
                MessageType type = (MessageType)(int)data[0];
                EventState state = (EventState)(int)data[1];
                EventType eventType = (EventType)(int)data[2];
                string userid = (string)data[3];

                Utils.Log("Event " + photonEvent.Code + " received as client, event " + eventType + " state " + state + " userId: " + userid + " my id: " + MultiplayerManager.Instance.localPlayer.UserId);

                if (userid == "" || userid == MultiplayerManager.Instance.localPlayer.UserId)
                {
                    if (state == EventState.Running && !isEventOwner)
                    {
                        CreateEvent(eventType, data);
                    }

                    if (state == EventState.Stopped || state == EventState.End)
                    {
                        Disable(true);
                        Reset();
                    }

                    // Reset() above nulls multiplayerEvent on Stopped/End, so guard
                    // before touching it or we throw on every event that ends.
                    if (multiplayerEvent != null) multiplayerEvent.state = state;
                }
            }
        }

        public void Disable(bool soft = false)
        {
            Main.tick.GOSUI = false;

            if (SKATE != null) SKATE.Disable();
            if (race != null) race.Disable();

            if (!soft) PhotonNetwork.RemoveCallbackTarget(this);
        }

        public void Reset()
        {
            isEventOwner = false;
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
                // Network payload carries the opponent id at index 4; local creation passes an empty array.
                if (data.Length > 4) SKATE.opponent = (string)data[4];
            }

            foreach (KeyValuePair<int, NetworkPlayerController> entry in MultiplayerManager.Instance.networkPlayers)
            {
                if (entry.Value && entry.Value.UserId != MultiplayerManager.Instance.localPlayer.UserId)
                {
                    multiplayerEvent.participants.Add(entry.Value);
                }
            }

            this.eventType = eventType;
            isEventOwner = data.Length == 0; // created locally, doesnt come from the network and data packet is empty
            Utils.ShowNotification("Event " + eventType + (isEventOwner ? " created" : " joined"), 2f);
        }

        public void StopEvent()
        {
            if (multiplayerEvent != null)
            {
                multiplayerEvent.ToggleEventState(EventState.Stopped, this.eventType, lastOpponent);
                Utils.ShowNotification("Event stopped", 2f);

                Disable(true);
                Reset();
            }
        }

        public void EndEvent()
        {
            if (multiplayerEvent != null)
            {
                Utils.ShowNotification("Event ended - you " + (multiplayerEvent.isWinner ? "won" : "lost"), 2f);
                multiplayerEvent.ToggleEventState(EventState.End, this.eventType, lastOpponent);

                Disable(true);
                Reset();
            }
        }


        public string lastOpponent = "";
        public void StartEvent(string opponent = "")
        {
            if (multiplayerEvent != null)
            {
                lastOpponent = opponent;

                multiplayerEvent.ToggleEventState(EventState.Running, this.eventType, opponent);

                if (eventType == EventType.SKATE) SKATE.StartEvent();
            }
        }

        // IInRoomCallbacks - abort the current event if our opponent disconnects,
        // otherwise a S.K.A.T.E. game would hang forever waiting on someone who left.
        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (multiplayerEvent == null || otherPlayer == null) return;

            bool opponentLeft = SKATE != null && SKATE.opponentUserID != "" && SKATE.opponentUserID == otherPlayer.UserId;
            if (opponentLeft)
            {
                Utils.ShowNotification("Opponent left - event ended", 3f);
                Main.tick.trickConfirmation = null;
                Disable(true);
                Reset();
            }
        }

        public void OnPlayerEnteredRoom(Player newPlayer) { }
        public void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged) { }
        public void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) { }
        public void OnMasterClientSwitched(Player newMasterClient) { }
    }
}
