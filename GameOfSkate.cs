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

        public void StartEvent()
        {
            double turn = rd.NextDouble();
            bool myTurn = true;
            if (turn < .5f) { myTurn = false; }

            if (myTurn) playerState = GOSState.Setting;
            else playerState = GOSState.Waiting;

            alreadyDone = new List<TrickCombo>();

            SyncTurn(myTurn);
        }

        public void SyncTurn(bool t)
        {
            object[] content = new object[] { "turn", !t };
            PhotonNetwork.RaiseEvent(70, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.Others
            }, SendOptions.SendReliable);
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

                    if (myTurn)
                    {
                        if (playerState != GOSState.Defending) playerState = GOSState.Setting;
                    }
                    else playerState = GOSState.Waiting;
                }

                if (key == "trickSet" && playerState == GOSState.Waiting)
                {
                    actualTrickCombo = (TrickCombo)data[1];
                    playerState = GOSState.Defending;
                    if (alreadyDone == null) alreadyDone = new List<TrickCombo>();
                    alreadyDone.Add(actualTrickCombo);
                }

                if (key == "letterSet")
                {
                    opponentLetters = (bool[])data[1];
                    if (Main.eventManager.admin) CheckEnd();
                }

                if (key == "defenseSuccess")
                {
                    playerState = GOSState.Setting;
                    actualTrickCombo = null;
                }

                if (key == "eventEnd")
                {
                    state = EventState.End;
                    isWinner = (bool)data[1];
                }
            }
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
                if(actualTrickCombo != null)
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
            if (a.Tricks.Count != b.Tricks.Count) return false;

            for (int i = 0; i < b.Tricks.Count; i++)
            {
                if (a.Tricks[i] != b.Tricks[i]) return false;
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

            if (Main.eventManager.admin) CheckEnd();
        }

        public char[] modeLetters = new char[] { 's', 'k', 'a', 't', 'e' };
        public void CheckEnd()
        {
            int letterCount = 0, opponentLetterCount = 0;
            for(int i = 0; i < letters.Length; i++)
            {
                if (letters[i]) letterCount++;
                if (opponentLetters[i]) opponentLetterCount++;
            }

            SendEventEnd(letterCount == modeLetters.Length);
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

            alreadyDone.Add(actualTrickCombo);
            PassTurn();
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
            myTurn = !myTurn;
            SyncTurn(!myTurn);
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
