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
        public int incomingInviteRetries = GameConfig.DefaultRetries;
        public float incomingInviteExpiry = 0f;

        // Per-sender invite timestamps (UserId -> recent invite times), for the spam rate limit.
        // A sender gets InviteMaxPerWindow invites per InviteWindowSeconds; extras are dropped.
        // In-memory / per session.
        Dictionary<string, List<float>> recentInvites = new Dictionary<string, List<float>>();

        public string agreedSkateWord = ""; // word both players use for the current/next S.K.A.T.E. game
        public int agreedRetries = GameConfig.DefaultRetries; // redo allowance both players use; owner-authoritative, synced via the invite
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
                // Sender ("Nick | UserId") is appended by ToggleEventState at index 4; drop the
                // event if they're blocked so a blocked player can't push events onto us.
                if (data.Length > 4 && Utils.IsBlocked(data[4] as string)) return;
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
                        // A normal End already shows the win/lose toast; only announce an opponent's
                        // manual Stop, and only if we were actually in a game.
                        if (state == EventState.Stopped && multiplayerEvent != null)
                            Utils.ShowNotification("Opponent quit", 2.5f);
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
            // Clear any open trick-confirm prompt here so it's covered on every teardown
            // path (Stop/End/opponent-left). Otherwise its HUD/input would dereference the
            // now-nulled SKATE next frame and throw every OnGUI/Update.
            Main.tick.trickConfirmation = null;

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

            // Owner picks the word and redo allowance from its own settings; a joiner already
            // adopted the owner's values from the invite, so leave them alone in that case.
            if (eventType == EventType.SKATE && data.Length == 0)
            {
                agreedSkateWord = GameConfig.NormalizeSkateWord(Main.settings.skateWord);
                agreedRetries = Main.settings.maxRetries < 0 ? 0 : Main.settings.maxRetries;
            }

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
                // A joiner is created from the owner's Running broadcast, so the match is already
                // live: turn on the HUD + accept game traffic. The owner stays "not running" here
                // and flips on in StartEvent once the invitee accepts.
                if (data.Length > 0) { SKATE.running = true; Main.tick.GOSUI = true; }
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
                // Route teardown to the actual opponent. lastOpponent is only set by the owner
                // (StartEvent), so a joiner would otherwise broadcast userid=="" and reset EVERY
                // event in the room. Both sides know the opponent via SKATE.opponentUserID.
                multiplayerEvent.ToggleEventState(EventState.Stopped, this.eventType, TeardownRoute());
                Utils.ShowNotification("Event stopped", 2f);

                Disable(true);
                Reset();
            }
        }

        // Who a Stopped/End lifecycle broadcast should target. For S.K.A.T.E. that's the bound
        // opponent (known on both sides); Race has no opponent and keeps its room-wide "" behavior.
        string TeardownRoute()
        {
            return (SKATE != null && SKATE.opponentUserID != "") ? SKATE.opponentUserID : lastOpponent;
        }

        public void EndEvent()
        {
            if (multiplayerEvent != null)
            {
                if (SKATE != null && SKATE.opponent != "") lastSkateOpponent = SKATE.opponent;
                Utils.ShowNotification(multiplayerEvent.isWinner ? "You won" : "You lost", 2f);
                multiplayerEvent.ToggleEventState(EventState.End, this.eventType, TeardownRoute());

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

        void RaiseInvite(string key, string targetUserId, EventType type, string word, int retries = 0)
        {
            object[] content = new object[] { key, (int)type, targetUserId, Utils.GetPlayerID(), word ?? "", retries };
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

            RaiseInvite(InviteMessage.Invite, pendingInviteTo, EventType.SKATE, agreedSkateWord, agreedRetries);
            Utils.ShowNotification("Waiting for " + pendingInviteNick, 2f);
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

            // If we had our own (unstarted) create-menu event open, tear it down first. Otherwise
            // isEventOwner stays true and the owner's Running broadcast never creates our joiner
            // game - we'd end up "playing" a ghost while the inviter faces a non-responsive us.
            if (multiplayerEvent != null) { Disable(true); Reset(); }

            agreedSkateWord = GameConfig.NormalizeSkateWord(incomingInviteWord); // adopt owner's word
            agreedRetries = incomingInviteRetries; // adopt owner's redo allowance
            RaiseInvite(InviteMessage.Accept, Utils.UserIdOf(incomingInviteFrom), incomingInviteType, "");
            Utils.ShowNotification("Starting...", 2f);
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

        // Records this invite attempt and returns false if the sender is over the rate limit
        // (InviteMaxPerWindow within InviteWindowSeconds). Only counts attempts that get through
        // here, so a dropped invite doesn't extend the window - the limit stays a true N-per-window.
        bool AllowInvite(string userId)
        {
            if (userId == "") return true;
            float now = UnityEngine.Time.time;

            List<float> times;
            if (!recentInvites.TryGetValue(userId, out times)) { times = new List<float>(); recentInvites[userId] = times; }
            times.RemoveAll(t => t <= now - GameConfig.InviteWindowSeconds);

            if (times.Count >= GameConfig.InviteMaxPerWindow) return false; // over the limit -> drop
            times.Add(now);
            return true;
        }

        void HandleInvite(object[] data)
        {
            string key = data[0] as string;
            if (key == null) return;
            EventType type = (EventType)(int)data[1];
            string targetUserId = data[2] as string;
            string senderPlayerId = data[3] as string; // "Nick | UserId"
            string word = data.Length > 4 ? data[4] as string : "";
            int retries = data.Length > 5 && data[5] is int ? (int)data[5] : GameConfig.DefaultRetries;

            if (targetUserId != MultiplayerManager.Instance.localPlayer.UserId) return; // not for us
            if (senderPlayerId == null) return;
            if (Utils.IsBlocked(senderPlayerId)) return; // blocked player -> silently ignore all invite traffic

            if (key == InviteMessage.Invite)
            {
                string senderId = Utils.UserIdOf(senderPlayerId);

                // Spam guard: allow a few invites per window (tolerates fat-fingers / legit
                // re-invites), then silently drop the rest - no popup, no reply, so a spammer
                // gets no feedback and their extra invites just time out on their end.
                if (!AllowInvite(senderId)) return;

                // Busy -> auto-decline so the inviter isn't left hanging. "Busy" means a game
                // is actually running or an invite handshake is already in flight (we have a
                // prompt up, or an outgoing invite pending). Merely having the S.K.A.T.E. menu
                // open (event created but not started, state == Stopped) does NOT count, so two
                // players opening the menu can still invite each other.
                bool inRunningEvent = multiplayerEvent != null && multiplayerEvent.state == EventState.Running;
                if (inRunningEvent || hasIncomingInvite || pendingInviteTo != "")
                {
                    RaiseInvite(InviteMessage.Decline, senderId, type, "");
                    return;
                }

                hasIncomingInvite = true;
                incomingInviteFrom = senderPlayerId;
                incomingInviteType = type;
                incomingInviteWord = word;
                incomingInviteRetries = retries < 0 ? 0 : retries;
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
                Utils.ShowNotification("Opponent left", 3f);
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
