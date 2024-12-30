using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerEvents
{
    public class Race : Event, IOnEventCallback
    {
        public List<CheckPoint> checkpoints;
        private List<int> participantPositions;
        public Race()
        {
            PhotonNetwork.AddCallbackTarget(this);

            checkpoints = new List<CheckPoint>();
            participantPositions = new List<int>();

            for (int i = 0; i < participants.Count; i++)
            {
                participantPositions.Add(0);
            }
        }
        public void UpdateParticipantPosition(int participantIndex, int checkpointIndex)
        {
            if (participantIndex >= 0 && participantIndex < participants.Count)
            {
                participantPositions[participantIndex] = checkpointIndex;

                object[] content = new object[] { participantIndex, checkpointIndex };
                PhotonNetwork.RaiseEvent(66, content, new RaiseEventOptions
                {
                    Receivers = ReceiverGroup.All
                }, SendOptions.SendReliable);
            }
        }

        public int GetParticipantPosition(int participantIndex)
        {
            if (participantIndex >= 0 && participantIndex < participantPositions.Count)
            {
                return participantPositions[participantIndex];
            }
            return -1;
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            Utils.Log("Event " + photonEvent.Code + " received");

            if (photonEvent.Code == 65)
            {
                object[] data = (object[])photonEvent.CustomData;
                MessageType type = (MessageType)(int)data[0];
                EventState state = (EventState)(int)data[1];

                if (type == MessageType.EventState && state == EventState.Running)
                {
                    OnEventStart();
                }
            }

            if (photonEvent.Code == 66)
            {
                object[] data = (object[])photonEvent.CustomData;
                int participantIndex = (int)data[0];
                int checkpointIndex = (int)data[1];

                UpdateParticipantPosition(participantIndex, checkpointIndex);
            }

            if (photonEvent.Code == 67)
            {
                Utils.Log("Syncing all checkpoints");

                object[] data = (object[])photonEvent.CustomData;

                Utils.Log("data size: " + data.Length);

                int checkpointCount = data.Length / 2;
                checkpoints.Clear();

                for (int i = 0; i < checkpointCount; i++)
                {
                    Vector3 pointAPosition = (Vector3)data[2 * i];
                    Vector3 pointBPosition = (Vector3)data[2 * i + 1];

                    Point pointA = Utils.AddPoint();
                    pointA.transform.position = pointAPosition;
                    Point pointB = Utils.AddPoint();
                    pointB.transform.position = pointBPosition;

                    CheckPoint newCheckpoint = Utils.AddCheckPoint(pointA, pointB);
                    checkpoints.Add(newCheckpoint);
                }

                Utils.Log($"{checkpointCount} checkpoints synced.");
            }
        }

        void OnEventStart()
        {
            Main.tick.StartCountdown(10f);
        }

        public void AddNewCheckPoint(CheckPoint cp)
        {
            CheckPoint newC = Utils.AddCheckPoint(cp.pointA, cp.pointB);
            checkpoints.Add(newC);
        }

        public void SyncCheckPoints()
        {
            Utils.Log("Will sync " + checkpoints.Count + " checkpoints");

            List<object> checkpointData = new List<object>();

            for (int i = 0; i < checkpoints.Count; i++)
            {
                checkpointData.Add(checkpoints[i].pointA.transform.position);
                checkpointData.Add(checkpoints[i].pointB.transform.position);
            }

            Utils.Log("Will send " + checkpointData.Count);

            object[] content = checkpointData.ToArray();
            PhotonNetwork.RaiseEvent(67, content, new RaiseEventOptions
            {
                Receivers = ReceiverGroup.All
            }, SendOptions.SendReliable);
        }

        public void Disable()
        {
            PhotonNetwork.RemoveCallbackTarget(this);
        }
    }
}
