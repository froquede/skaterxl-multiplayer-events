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
        public bool[] letters = new bool[GameConfig.SkateLetterCount];
        public bool[] opponentLetters = new bool[GameConfig.SkateLetterCount];

        public GOSState playerState;
        public bool myTurn = false;
        public TrickCombo actualTrickCombo;
        public int retriesValue = GameConfig.DefaultRetries;

        public void StartEvent()
        {
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
            Main.tick.GOSUI = true;
            retriesValue = Main.settings.maxRetries < 0 ? 0 : Main.settings.maxRetries;
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            Player sender = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
            if (sender == null) return; // sender may have already left the room
            if (opponentUserID != "" && opponentUserID != sender.UserId) return;

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
        }

        public void OnComboEnded(TrickCombo trickC)
        {
            if (playerState == GOSState.Setting && actualTrickCombo == null)
            {
                if (trickC.Landed)
                {
                    // Like a real game of S.K.A.T.E., a trick already used this game
                    // can't be set again - keep setting until a fresh trick or a bail.
                    if (IsAlreadyDone(trickC))
                    {
                        Utils.ShowNotification("Trick already used this game - try another", 2f);
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

            if (playerState == GOSState.Defending)
            {
                if (actualTrickCombo != null)
                {
                    if (trickC.Landed)
                    {
                        if (CompareCombos(trickC, actualTrickCombo))
                        {
                            SendDefenseSuccess();
                        }
                        else AddLetter();
                    }
                    else
                    {
                        AddLetter();
                    }
                }
            }
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
            Utils.Log(a.Tricks.Count + " " + b.Tricks.Count);
            if (a.Tricks.Count != b.Tricks.Count) return false;

            for (int i = 0; i < b.Tricks.Count; i++)
            {
                Utils.Log(a.Tricks[i] + " " + b.Tricks[i]);
                if (a.Tricks[i].ToString() != b.Tricks[i].ToString()) return false;
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

            letters[next] = true;

            SetLetter();
            playerState = GOSState.Waiting;

            if (Main.eventManager.isEventOwner) CheckEnd();
        }

        public char[] modeLetters = new char[] { 's', 'k', 'a', 't', 'e' };
        public void CheckEnd()
        {
            int letterCount = 0, opponentLetterCount = 0;
            for (int i = 0; i < letters.Length; i++)
            {
                if (letters[i]) letterCount++;
                if (opponentLetters[i]) opponentLetterCount++;
            }

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
