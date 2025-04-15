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
        public string opponentUserID => opponent != "" ? opponent.Split(new string[] { " | " }, StringSplitOptions.None)[1] : "";
        public string opponentNickname => opponent != "" ? opponent.Split(new string[] { " | " }, StringSplitOptions.None)[0] : "";
        public bool[] letters = new bool[5];
        public bool[] opponentLetters = new bool[5];

        public GOSState playerState;
        public bool myTurn = false;
        public TrickCombo actualTrickCombo;
        public int retriesValue = 1;

        public void StartEvent()
        {
            double turn = rd.NextDouble();
            bool myTurn = true;
            if (turn < .5f) { myTurn = false; }

            if (myTurn) SetSetting();
            else playerState = GOSState.Waiting;

            alreadyDone = null;

            SyncTurn(myTurn);
        }

        public void SyncTurn(bool t)
        {
            object[] content = new object[] { "turn", !t };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            Utils.Log("Send sync turn: " + !t);
        }

        public GameOfSkate()
        {
            PhotonNetwork.AddCallbackTarget(this);
            Main.tick.GOSUI = true;
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            Player sender = PhotonNetwork.CurrentRoom.GetPlayer(photonEvent.Sender);
            if (opponentUserID != "" && opponentUserID != sender.UserId) return;

            if (photonEvent.Code == 70)
            {
                object[] data = (object[])photonEvent.CustomData;
                string key = (string)data[0];

                if (key == "turn")
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

                if (key == "trickSet" && playerState == GOSState.Waiting)
                {
                    actualTrickCombo = (TrickCombo)data[1];

                    if (alreadyDone == null) alreadyDone = new List<TrickCombo>();
                    alreadyDone.Add(actualTrickCombo);

                    playerState = GOSState.Defending;
                }

                if (key == "letterSet")
                {
                    opponentLetters = (bool[]) data[1];
                    actualTrickCombo = null;
                    SetSetting();

                    if (Main.eventManager.admin) CheckEnd();
                }

                if (key == "defenseSuccess")
                {
                    actualTrickCombo = null;
                    SetSetting();
                }

                if (key == "eventEnd")
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

            if (Main.eventManager.admin) CheckEnd();
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
            Main.eventManager.EndEvent();
            Main.eventManager.Reset();

            Disable();
        }

        public void ConfirmTrick()
        {
            Main.tick.trickConfirmation = actualTrickCombo;
        }

        public void SendDefenseSuccess()
        {
            object[] content = new object[] { "defenseSuccess" };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            playerState = GOSState.Waiting;
        }

        public void SendEventEnd(bool winner)
        {
            object[] content = new object[] { "eventEnd", !winner };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
        }

        public List<TrickCombo> alreadyDone;

        public void SetTrick()
        {
            object[] content = new object[] { "trickSet", actualTrickCombo };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);

            if (alreadyDone == null) alreadyDone = new List<TrickCombo>();
            alreadyDone.Add(actualTrickCombo);
            playerState = GOSState.Waiting;
        }

        public void SetLetter()
        {
            object[] content = new object[] { "letterSet", letters };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
        }

        public void PassTurn()
        {
            retries = 1;
            playerState = GOSState.Waiting;
            actualTrickCombo = null;
            myTurn = false;

            SyncTurn(myTurn);
        }

        public void Disable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }

        int retries = 1;
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
