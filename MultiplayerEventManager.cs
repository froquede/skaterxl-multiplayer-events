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

        // Invitations. Outgoing = we invited someone and wait for their answer.
        public string pendingInviteTo = "";   // opponent UserId we invited
        public string pendingInviteNick = "";
        public float pendingInviteExpiry = 0f;
        // Incoming = someone invited us; the in-world prompt (Tick) shows/answers it.
        public bool hasIncomingInvite = false;
        public string incomingInviteFrom = ""; // "Nick | UserId" of the inviter
        public EventType incomingInviteType = EventType.Null;
        public string incomingInviteWord = "";
        public float incomingInviteExpiry = 0f;

        public string agreedSkateWord = ""; // word both players use for the current/next S.K.A.T.E. game
        public string lastSkateOpponent = ""; // "Nick | UserId" for the rematch button

        public MultiplayerEventManager()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code == NetCode.Invitation)
            {
                object[] idata = photonEvent.CustomData as object[];
                if (idata == null || idata.Length < 4) return;
                HandleInvite(idata);
                return;
            }

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

            // Owner picks the word from its own settings; a joiner already adopted the
            // owner's word from the invite, so leave agreedSkateWord alone in that case.
            if (eventType == EventType.SKATE && data.Length == 0)
                agreedSkateWord = GameConfig.NormalizeSkateWord(Main.settings.skateWord);

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
                if (SKATE != null && SKATE.opponent != "") lastSkateOpponent = SKATE.opponent;
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
                if (SKATE != null && SKATE.opponent != "") lastSkateOpponent = SKATE.opponent;
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

        // --- Invitations -------------------------------------------------------

        void RaiseInvite(string key, string targetUserId, EventType type, string word)
        {
            object[] content = new object[] { key, (int)type, targetUserId, Utils.GetPlayerID(), word ?? "" };
            PhotonNetwork.RaiseEvent(NetCode.Invitation, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
        }

        // Owner: invite the opponent currently selected on the S.K.A.T.E. event.
        public void InviteOpponent()
        {
            if (SKATE == null || SKATE.opponentUserID == "") return;

            pendingInviteTo = SKATE.opponentUserID;
            pendingInviteNick = SKATE.opponentNickname;
            pendingInviteExpiry = UnityEngine.Time.time + GameConfig.InviteTimeoutSeconds;

            RaiseInvite(InviteMessage.Invite, pendingInviteTo, EventType.SKATE, agreedSkateWord);
            Utils.ShowNotification("Invited " + pendingInviteNick + " - waiting...", 2f);
        }

        public void CancelInvite(bool timedOut = false)
        {
            if (pendingInviteTo == "") return;

            RaiseInvite(InviteMessage.Cancel, pendingInviteTo, this.eventType, "");
            Utils.ShowNotification(timedOut ? "Invite timed out" : "Invite cancelled", 2f);
            pendingInviteTo = "";
            pendingInviteNick = "";
        }

        // Invitee: accept the pending in-world invite. The owner then starts the game,
        // which we join through the normal Running lifecycle event.
        public void AcceptIncomingInvite()
        {
            if (!hasIncomingInvite) return;

            agreedSkateWord = GameConfig.NormalizeSkateWord(incomingInviteWord); // adopt owner's word
            RaiseInvite(InviteMessage.Accept, Utils.UserIdOf(incomingInviteFrom), incomingInviteType, "");
            Utils.ShowNotification("Accepted - starting soon", 2f);
            hasIncomingInvite = false;
        }

        public void DeclineIncomingInvite(bool timedOut = false)
        {
            if (!hasIncomingInvite) return;

            RaiseInvite(InviteMessage.Decline, Utils.UserIdOf(incomingInviteFrom), incomingInviteType, "");
            Utils.ShowNotification(timedOut ? "Invite expired" : "Invite declined", 2f);
            hasIncomingInvite = false;
        }

        // Re-invite the last opponent to a fresh S.K.A.T.E. game.
        public void Rematch()
        {
            if (lastSkateOpponent == "" || !Utils.isOnline() || multiplayerEvent != null) return;

            CreateEvent(EventType.SKATE, new object[] { }); // become owner
            SKATE.opponent = lastSkateOpponent;
            InviteOpponent();
        }

        void HandleInvite(object[] data)
        {
            string key = data[0] as string;
            if (key == null) return;
            EventType type = (EventType)(int)data[1];
            string targetUserId = data[2] as string;
            string senderPlayerId = data[3] as string; // "Nick | UserId"
            string word = data.Length > 4 ? data[4] as string : "";

            if (targetUserId != MultiplayerManager.Instance.localPlayer.UserId) return; // not for us
            if (senderPlayerId == null) return;

            if (key == InviteMessage.Invite)
            {
                // Busy or already have a prompt up -> auto-decline so the inviter isn't left hanging.
                if (multiplayerEvent != null || hasIncomingInvite)
                {
                    RaiseInvite(InviteMessage.Decline, Utils.UserIdOf(senderPlayerId), type, "");
                    return;
                }

                hasIncomingInvite = true;
                incomingInviteFrom = senderPlayerId;
                incomingInviteType = type;
                incomingInviteWord = word;
                incomingInviteExpiry = UnityEngine.Time.time + GameConfig.InviteTimeoutSeconds;
                Utils.ShowNotification(Utils.NickOf(senderPlayerId) + " invited you to " + type, 3f);
            }
            else if (key == InviteMessage.Accept)
            {
                if (pendingInviteTo == "" || Utils.UserIdOf(senderPlayerId) != pendingInviteTo) return;

                string opponent = pendingInviteTo;
                pendingInviteTo = "";
                pendingInviteNick = "";
                StartEvent(opponent);
            }
            else if (key == InviteMessage.Decline)
            {
                if (pendingInviteTo == "" || Utils.UserIdOf(senderPlayerId) != pendingInviteTo) return;

                Utils.ShowNotification(Utils.NickOf(senderPlayerId) + " declined", 2f);
                pendingInviteTo = "";
                pendingInviteNick = "";
            }
            else if (key == InviteMessage.Cancel)
            {
                if (!hasIncomingInvite || Utils.UserIdOf(incomingInviteFrom) != Utils.UserIdOf(senderPlayerId)) return;

                hasIncomingInvite = false;
                Utils.ShowNotification("Invite cancelled", 2f);
            }
        }

        // IInRoomCallbacks - abort the current event if our opponent disconnects,
        // otherwise a S.K.A.T.E. game would hang forever waiting on someone who left.
        public void OnPlayerLeftRoom(Player otherPlayer)
        {
            if (otherPlayer == null) return;

            // Drop any invite involving the player who left.
            if (pendingInviteTo != "" && pendingInviteTo == otherPlayer.UserId)
            {
                pendingInviteTo = "";
                pendingInviteNick = "";
                Utils.ShowNotification("Invitee left", 2f);
            }
            if (hasIncomingInvite && Utils.UserIdOf(incomingInviteFrom) == otherPlayer.UserId)
            {
                hasIncomingInvite = false;
            }

            if (multiplayerEvent == null) return;

            bool opponentLeft = SKATE != null && SKATE.opponentUserID != "" && SKATE.opponentUserID == otherPlayer.UserId;
            if (opponentLeft)
            {
                Utils.ShowNotification("Opponent left - event ended", 3f);
                Main.tick.trickConfirmation = null;
                lastSkateOpponent = ""; // can't rematch someone who left
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
