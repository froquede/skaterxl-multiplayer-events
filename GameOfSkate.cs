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
    class GameOfSkate : Event, IOnEventCallback
    {
        System.Random rd = new System.Random();
        public string opponent = "";
        public string opponentUserID => opponent != "" ? opponent.Split(new string[] { GameConfig.PlayerIdSeparator }, StringSplitOptions.None)[1] : "";
        public string opponentNickname => opponent != "" ? opponent.Split(new string[] { GameConfig.PlayerIdSeparator }, StringSplitOptions.None)[0] : "";
        public char[] modeLetters;         // the word to spell, e.g. S.K.A.T.E.
        public bool[] letters;
        public bool[] opponentLetters;

        // Default to Waiting (not the enum's zero-value Setting): a joiner is constructed from
        // the Running lifecycle event before the owner's SyncTurn arrives, and must not treat
        // a landed trick as its own turn in that gap. The owner sets its state in StartEvent.
        public GOSState playerState = GOSState.Waiting;
        // True only once the match is actually live (owner: after StartEvent; joiner: on join).
        // Gates the in-world HUD and rejects stray game traffic while just sitting in the menu.
        public bool running = false;
        public bool myTurn = false;
        public TrickCombo actualTrickCombo;
        public string lastRegisteredTrick = ""; // what the game registered for our last attempt (HUD feedback)
        int defenseTriesLeft = 1;               // remaining attempts at the current set trick (>1 only at match point)
        public int retriesValue = GameConfig.DefaultRetries;

        public void StartEvent()
        {
            running = true;            // match is now live: show the HUD and accept game traffic
            Main.tick.GOSUI = true;

            double turn = rd.NextDouble();
            myTurn = turn >= .5f; // assign the field, not a shadowing local

            if (myTurn) SetSetting();
            else playerState = GOSState.Waiting;

            alreadyDone = null;

            SyncTurn(myTurn);
        }

        public void SyncTurn(bool t)
        {
            object[] content = new object[] { SkateMessage.Turn, !t };
            PhotonNetwork.RaiseEvent(NetCode.SkateGame, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            Utils.Log("Send sync turn: " + !t);
        }

        public GameOfSkate()
        {
            PhotonNetwork.AddCallbackTarget(this);
            // NB: GOSUI (the in-world HUD) is intentionally NOT enabled here. It should appear only
            // once the match is actually running - owner via StartEvent, joiner via CreateEvent on
            // join - not the moment the owner opens the "create game" menu.
            // Redo allowance is owner-authoritative and agreed via the invite (like the word),
            // so both players show the same "Redo (N)". Don't read local settings here or the
            // two machines desync (owner's setting vs. joiner's).
            retriesValue = Main.eventManager.agreedRetries < 0 ? 0 : Main.eventManager.agreedRetries;

            // Both players must use the same word; the owner's word is agreed via the invite
            // and stored on the manager before this event is created.
            string word = GameConfig.NormalizeSkateWord(Main.eventManager.agreedSkateWord);
            modeLetters = word.ToCharArray();
            letters = new bool[modeLetters.Length];
            opponentLetters = new bool[modeLetters.Length];
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            if (!running) return; // not a live match yet (menu open) - ignore all game traffic
            Player sender = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
            if (sender == null) return; // sender may have already left the room
            if (opponentUserID != "" && opponentUserID != sender.UserId) return;
            if (Utils.IsBlocked(sender.UserId, sender.NickName)) return; // ignore a blocked player's game traffic

            if (photonEvent.Code == NetCode.SkateGame)
            {
                object[] data = photonEvent.CustomData as object[];
                if (data == null || data.Length < 1) return;
                string key = data[0] as string;
                if (key == null) return;

                if (key == SkateMessage.Turn)
                {
                    myTurn = (bool)data[1];

                    Utils.Log("Received turn sync, my turn? " + myTurn);
                    actualTrickCombo = null;

                    if (myTurn)
                    {
                        SetSetting();
                    }
                    else playerState = GOSState.Waiting;
                }

                if (key == SkateMessage.TrickSet && playerState == GOSState.Waiting)
                {
                    actualTrickCombo = (TrickCombo)data[1];

                    if (alreadyDone == null) alreadyDone = new List<TrickCombo>();
                    alreadyDone.Add(actualTrickCombo);

                    playerState = GOSState.Defending;
                    lastRegisteredTrick = ""; // clear until we attempt a defense
                    // On match point the losing letter only counts after several misses.
                    defenseTriesLeft = IsMatchPoint() ? GameConfig.LastLetterTries : 1;
                }

                if (key == SkateMessage.LetterSet)
                {
                    opponentLetters = (bool[]) data[1];
                    actualTrickCombo = null;
                    SetSetting();

                    if (Main.eventManager.isEventOwner) CheckEnd();
                }

                if (key == SkateMessage.DefenseSuccess)
                {
                    actualTrickCombo = null;
                    SetSetting();
                }

                if (key == SkateMessage.EventEnd)
                {
                    isWinner = (bool)data[1];
                    EndEvent();
                }
            }
        }

        void SetSetting()
        {
            playerState = GOSState.Setting;
            retries = retriesValue;
            lastRegisteredTrick = ""; // fresh turn - clear the "You: ..." HUD feedback
        }

        public void OnComboEnded(TrickCombo trickC)
        {
            // Remember what the game registered for our own attempt so the HUD can
            // show it - lets the defender see exactly what they landed vs the target.
            lastRegisteredTrick = Utils.ComboName(trickC);

            if (playerState == GOSState.Setting && actualTrickCombo == null)
            {
                if (trickC.Landed)
                {
                    // A trick that normalizes to nothing (e.g. only a small manual)
                    // isn't a real set - keep setting instead of locking in nothing.
                    if (Utils.NormalizedTrickNames(trickC).Count == 0) return;

                    // Like a real game of S.K.A.T.E., a trick already used this game
                    // can't be set again - keep setting until a fresh trick or a bail.
                    if (IsAlreadyDone(trickC))
                    {
                        Utils.ShowNotification("Trick already used", 2f);
                        return;
                    }

                    actualTrickCombo = trickC;
                    if (retries > 0) ConfirmTrick();
                    else
                    {
                        SetTrick();
                    }
                }
                else
                {
                    PassTurn();
                }
            }

            if (playerState == GOSState.Defending && actualTrickCombo != null)
            {
                bool matched = trickC.Landed && CompareCombos(trickC, actualTrickCombo);
                if (matched)
                {
                    SendDefenseSuccess();
                }
                else
                {
                    // Match point: give the extra attempt(s) at the same set trick
                    // before the game-losing letter actually counts (issue #7).
                    defenseTriesLeft--;
                    if (defenseTriesLeft > 0)
                    {
                        Utils.ShowNotification("Last letter - " + defenseTriesLeft + (defenseTriesLeft == 1 ? " try" : " tries") + " left!", 2f);
                        return; // stay defending on the same trick
                    }

                    AddLetter();
                }
            }
        }

        // True when one more letter would end the game for us (match point).
        bool IsMatchPoint()
        {
            int myLetters = 0;
            for (int i = 0; i < letters.Length; i++) if (letters[i]) myLetters++;
            return myLetters == modeLetters.Length - 1;
        }

        // Explicitly pass your setting turn without having to bail (issue #3).
        public void TryPassTurn()
        {
            if (playerState != GOSState.Setting || actualTrickCombo != null) return;
            Utils.ShowNotification("Turn passed", 1.5f);
            PassTurn();
        }

        bool IsAlreadyDone(TrickCombo combo)
        {
            if (alreadyDone == null) return false;
            for (int i = 0; i < alreadyDone.Count; i++)
            {
                if (CompareCombos(combo, alreadyDone[i])) return true;
            }
            return false;
        }

        bool CompareCombos(TrickCombo a, TrickCombo b)
        {
            // Compare on the normalized trick names (small manuals ignored) so both
            // sides agree with what the HUD shows.
            List<string> na = Utils.NormalizedTrickNames(a);
            List<string> nb = Utils.NormalizedTrickNames(b);

            Utils.Log("Compare [" + string.Join(", ", na) + "] vs [" + string.Join(", ", nb) + "]");
            if (na.Count != nb.Count) return false;

            for (int i = 0; i < na.Count; i++)
            {
                if (na[i] != nb[i]) return false;
            }

            return true;
        }

        void AddLetter()
        {
            int next = 0;
            for(int i = 0; i < letters.Length; i++)
            {
                if (letters[i]) next++;
            }

            // CheckEnd (which ends the game at a full word) only runs on the event owner, so a
            // non-owner relies on the owner's EventEnd round-trip to stop. Under packet loss or
            // desync a client can be asked to defend with the word already full; guard so we
            // don't index past the array. The owner's EventEnd will still tear the game down.
            if (next >= letters.Length) return;

            letters[next] = true;

            SetLetter();
            playerState = GOSState.Waiting;

            if (Main.eventManager.isEventOwner) CheckEnd();
        }

        public void CheckEnd()
        {
            int letterCount = 0, opponentLetterCount = 0;
            // opponentLetters is replaced wholesale from network payloads (LetterSet), so don't
            // assume it matches letters.Length; bound each array by its own length.
            for (int i = 0; i < letters.Length; i++) if (letters[i]) letterCount++;
            for (int i = 0; i < opponentLetters.Length; i++) if (opponentLetters[i]) opponentLetterCount++;

            if (letterCount == modeLetters.Length || opponentLetterCount == modeLetters.Length)
            {
                isWinner = opponentLetterCount == modeLetters.Length;

                SendEventEnd(isWinner);
                EndEvent();
            }
        }

        public void EndEvent()
        {
            Main.tick.trickConfirmation = null;
            Main.tick.GOSUI = false;
            Main.eventManager.EndEvent(); // already calls Disable(true) + Reset() internally

            Disable();
        }

        public void ConfirmTrick()
        {
            Main.tick.trickConfirmation = actualTrickCombo;
        }

        public void SendDefenseSuccess()
        {
            object[] content = new object[] { SkateMessage.DefenseSuccess };
            PhotonNetwork.RaiseEvent(NetCode.SkateGame, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            playerState = GOSState.Waiting;
        }

        public void SendEventEnd(bool winner)
        {
            object[] content = new object[] { SkateMessage.EventEnd, !winner };
            PhotonNetwork.RaiseEvent(NetCode.SkateGame, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
        }

        public List<TrickCombo> alreadyDone;

        public void SetTrick()
        {
            object[] content = new object[] { SkateMessage.TrickSet, actualTrickCombo };
            PhotonNetwork.RaiseEvent(NetCode.SkateGame, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            if (alreadyDone == null) alreadyDone = new List<TrickCombo>();
            alreadyDone.Add(actualTrickCombo);
            playerState = GOSState.Waiting;
        }

        public void SetLetter()
        {
            object[] content = new object[] { SkateMessage.LetterSet, letters };
            PhotonNetwork.RaiseEvent(NetCode.SkateGame, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
        }

        public void PassTurn()
        {
            retries = GameConfig.DefaultRetries;
            playerState = GOSState.Waiting;
            actualTrickCombo = null;
            myTurn = false;

            SyncTurn(myTurn);
        }

        public void Disable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        public int retries = GameConfig.DefaultRetries; // redos left this setting turn (drives the "Redo (N)" label)
        public void OnConfirmEvent(bool confirm)
        {
            if (confirm)
            {
                SetTrick();
            }
            else
            {
                actualTrickCombo = null;
                retries--;
            }
        }

        public enum GOSState
        {
            Setting,
            Waiting,
            Defending
        }
    }
}
