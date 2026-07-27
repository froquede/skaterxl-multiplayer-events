using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MultiplayerEvents
{
    // Per-racer progress, keyed by UserId (indices are NOT stable across clients, so we never
    // identify racers by list position).
    public class RaceProgress
    {
        public string userId;
        public int lapsDone;        // laps fully completed
        public int nextCp;          // next checkpoint index to cross (0..cpCount)
        public int lastServerTime;  // server time (ms) of the last pass, for tie-breaking
        public bool finished;
        public int totalMs;         // finish time once finished
    }

    // Race is fully self-scoped by raceId: every network message carries it and anyone whose
    // active raceId doesn't match ignores the message. Non-participants never build checkpoints
    // and never react to race traffic, so a race can't overwrite or disturb other players' events.
    // Lobby (open/join/start/stop) is orchestrated by MultiplayerEventManager; Race handles the
    // in-race telemetry (progress/finish) and owns the checkpoints, ordering, laps and ranking.
    public class Race : Event, IOnEventCallback
    {
        public string raceId = "";
        public int laps = GameConfig.DefaultRaceLaps;
        public bool running = false;              // true once started (armed triggers); gate the HUD
        public int startServerTime = 0;           // race clock origin (Photon ServerTimestamp, ms)

        public List<CheckPoint> checkpoints = new List<CheckPoint>();
        public List<string> participantIds = new List<string>();          // UserIds racing
        public Dictionary<string, RaceProgress> progress = new Dictionary<string, RaceProgress>();

        bool finishedLocally = false;
        string myUserId => MultiplayerManager.Instance.localPlayer.UserId;

        public Race()
        {
            PhotonNetwork.AddCallbackTarget(this);
        }

        // --- Host-side setup -------------------------------------------------

        // Give a freshly-created host race its id + lap count (called when the host creates it).
        public void SetupHost(int lapCount)
        {
            raceId = System.Guid.NewGuid().ToString("N");
            laps = Mathf.Clamp(lapCount, 1, GameConfig.MaxRaceLaps);
        }

        // Host places a checkpoint (from the cursor tool). Order = current count. We COPY the
        // points into ones the race owns, so the cursor destroying its own placement objects can
        // never corrupt a committed checkpoint (they used to share Point objects).
        public void AddNewCheckPoint(CheckPoint cp)
        {
            Point a = Utils.AddPoint(); a.transform.position = cp.pointA.transform.position;
            Point b = Utils.AddPoint(); b.transform.position = cp.pointB.transform.position;
            CheckPoint newC = Utils.AddCheckPoint(a, b);
            newC.order = checkpoints.Count;
            checkpoints.Add(newC);
        }

        // Host: broadcast Start (with the checkpoint geometry + participant list) and begin locally.
        public void StartAsHost(List<string> participants, int startTime)
        {
            participantIds = new List<string>(participants);
            if (!participantIds.Contains(myUserId)) participantIds.Add(myUserId); // host races too
            startServerTime = startTime;

            // [Start, raceId, laps, startServerTime, participantsCsv, cpCount, A0,B0,A1,B1,...]
            List<object> content = new List<object>
            {
                RaceMessage.Start, raceId, laps, startServerTime,
                string.Join(GameConfig.RaceParticipantSeparator, participantIds.ToArray()),
                checkpoints.Count
            };
            for (int i = 0; i < checkpoints.Count; i++)
            {
                content.Add(checkpoints[i].pointA.transform.position);
                content.Add(checkpoints[i].pointB.transform.position);
            }
            PhotonNetwork.RaiseEvent(NetCode.RaceSession, content.ToArray(),
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);

            BeginRace();
        }

        // --- Joiner-side setup ----------------------------------------------

        // Participant: adopt the host's race parameters + geometry and begin.
        public void StartAsParticipant(string id, int lapCount, string participantsCsv, int startTime, List<Vector3> points)
        {
            raceId = id;
            laps = Mathf.Clamp(lapCount, 1, GameConfig.MaxRaceLaps);
            participantIds = participantsCsv.Split(new[] { GameConfig.RaceParticipantSeparator }, System.StringSplitOptions.RemoveEmptyEntries).ToList();
            startServerTime = startTime;
            BuildCheckpoints(points);
            BeginRace();
        }

        void BuildCheckpoints(List<Vector3> points)
        {
            DestroyCheckpoints();
            int count = points.Count / 2; // (A, B) pairs
            for (int i = 0; i < count; i++)
            {
                Point a = Utils.AddPoint(); a.transform.position = points[2 * i];
                Point b = Utils.AddPoint(); b.transform.position = points[2 * i + 1];
                CheckPoint cp = Utils.AddCheckPoint(a, b);
                cp.order = i;
                checkpoints.Add(cp);
            }
        }

        void BeginRace()
        {
            progress.Clear();
            foreach (string id in participantIds)
                progress[id] = new RaceProgress { userId = id, lapsDone = 0, nextCp = 0 };

            finishedLocally = false;
            running = true;
            state = EventState.Running;

            TeleportToStart();

            float secs = Mathf.Max(0f, (startServerTime - PhotonNetwork.ServerTimestamp) / 1000f);
            Main.tick.StartCountdown(secs);
            Utils.ShowNotification("Race starting", 2f);
        }

        // Put the local player a few metres behind the first gate, facing the course, so crossing
        // checkpoint 0 is the real start. Each client teleports only its own player (no collision
        // between players, so a shared start line is fine).
        void TeleportToStart()
        {
            if (checkpoints.Count == 0 || PlayerController.Instance == null) return;
            CheckPoint c0 = checkpoints[0];
            if (c0.pointA == null || c0.pointB == null) return;

            Vector3 mid = Vector3.Lerp(c0.pointA.transform.position, c0.pointB.transform.position, 0.5f);

            Vector3 dir;
            if (checkpoints.Count >= 2 && checkpoints[1].pointA != null && checkpoints[1].pointB != null)
            {
                Vector3 next = Vector3.Lerp(checkpoints[1].pointA.transform.position, checkpoints[1].pointB.transform.position, 0.5f);
                dir = next - mid; // aim toward the next gate
            }
            else
            {
                dir = Vector3.Cross(Vector3.up, (c0.pointB.transform.position - c0.pointA.transform.position)); // gate normal
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir.Normalize();

            Vector3 spawnPos = mid - dir * 3f;
            Quaternion spawnRot = Quaternion.LookRotation(dir, Vector3.up);

            try
            {
                Respawn r = PlayerController.Instance.respawn;
                r.SetSpawnPos(spawnPos, spawnRot, false);
                r.ForceRespawn();
            }
            catch (System.Exception e) { Utils.Log("Race teleport failed: " + e); }
        }

        public bool RaceStarted => running && PhotonNetwork.ServerTimestamp >= startServerTime;

        // --- In-race progress -----------------------------------------------

        public void OnLocalCheckpointPassed(int order)
        {
            if (!RaceStarted || finishedLocally) return;
            if (!progress.TryGetValue(myUserId, out RaceProgress me)) return;
            if (order != me.nextCp) return; // must be crossed in sequence; ignore out-of-order gates

            int now = PhotonNetwork.ServerTimestamp;
            me.nextCp++;
            me.lastServerTime = now;
            SetCheckpointRespawn(); // bail after here -> respawn at this gate, not the start

            if (me.nextCp >= checkpoints.Count)
            {
                me.lapsDone++;
                me.nextCp = 0;

                if (me.lapsDone >= laps)
                {
                    me.finished = true;
                    me.totalMs = now - startServerTime;
                    finishedLocally = true;
                    BroadcastFinish(me.totalMs);
                    Utils.ShowNotification("Finished - " + FormatTime(me.totalMs), 4f);
                    return;
                }

                Utils.ShowNotification("Lap " + (me.lapsDone + 1) + " / " + laps, 2f);
            }

            BroadcastProgress(me);
        }

        // Move the local player's respawn point to where they crossed the gate, so a bail sends
        // them back to their last checkpoint instead of the start line.
        void SetCheckpointRespawn()
        {
            try
            {
                PlayerController pc = PlayerController.Instance;
                if (pc == null) return;
                Transform t = pc.skaterController.skaterTransform;
                pc.respawn.SetSpawnPos(t.position, t.rotation, pc.IsSwitch);
            }
            catch (System.Exception e) { Utils.Log("Race checkpoint respawn set failed: " + e); }
        }

        void BroadcastProgress(RaceProgress me)
        {
            object[] content = { RaceMessage.Progress, raceId, myUserId, me.lapsDone, me.nextCp, me.lastServerTime };
            PhotonNetwork.RaiseEvent(NetCode.RaceSession, content,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
        }

        void BroadcastFinish(int totalMs)
        {
            object[] content = { RaceMessage.Finish, raceId, myUserId, totalMs };
            PhotonNetwork.RaiseEvent(NetCode.RaceSession, content,
                new RaiseEventOptions { Receivers = ReceiverGroup.Others }, SendOptions.SendReliable);
        }

        void IOnEventCallback.OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != NetCode.RaceSession) return;

            object[] data = photonEvent.CustomData as object[];
            if (data == null || data.Length < 2) return;
            string key = data[0] as string;
            if ((data[1] as string) != raceId) return; // not our race

            if (key == RaceMessage.Progress)
            {
                string userId = data[2] as string;
                if (userId == null || !progress.ContainsKey(userId)) return;
                RaceProgress p = progress[userId];
                p.lapsDone = (int)data[3];
                p.nextCp = (int)data[4];
                p.lastServerTime = (int)data[5];
            }
            else if (key == RaceMessage.Finish)
            {
                string userId = data[2] as string;
                if (userId == null || !progress.ContainsKey(userId)) return;
                RaceProgress p = progress[userId];
                p.finished = true;
                p.totalMs = (int)data[3];
            }
        }

        // --- Ranking + display ----------------------------------------------

        // Racers ordered best-first: finishers (by time) above everyone still going (by furthest
        // progress, earliest crossing wins ties).
        public List<RaceProgress> Ranking()
        {
            List<RaceProgress> list = progress.Values.ToList();
            list.Sort((a, b) =>
            {
                if (a.finished != b.finished) return a.finished ? -1 : 1;
                if (a.finished && b.finished) return a.totalMs.CompareTo(b.totalMs);
                if (a.lapsDone != b.lapsDone) return b.lapsDone.CompareTo(a.lapsDone);
                if (a.nextCp != b.nextCp) return b.nextCp.CompareTo(a.nextCp);
                return a.lastServerTime.CompareTo(b.lastServerTime);
            });
            return list;
        }

        public static string FormatTime(int ms)
        {
            if (ms < 0) ms = 0;
            int totalSec = ms / 1000;
            return (totalSec / 60) + ":" + (totalSec % 60).ToString("00") + "." + ((ms % 1000) / 10).ToString("00");
        }

        // Nick for a UserId (local player, else the room), falling back to a short id.
        public string NickFor(string userId)
        {
            if (userId == myUserId) return MultiplayerManager.Instance.localPlayer.NickName;
            NetworkPlayerController npc = Utils.GetNetworkPlayer(userId);
            if (npc != null) return npc.NickName;
            return userId.Length > 6 ? userId.Substring(0, 6) : userId;
        }

        // --- Teardown --------------------------------------------------------

        // Remove a racer who left the room from the ranking.
        public void RemoveParticipant(string userId)
        {
            participantIds.Remove(userId);
            progress.Remove(userId);
        }

        public void DestroyCheckpoints()
        {
            for (int i = 0; i < checkpoints.Count; i++)
            {
                CheckPoint cp = checkpoints[i];
                if (cp == null) continue;
                if (cp.pointA != null) Object.Destroy(cp.pointA.gameObject);
                if (cp.pointB != null) Object.Destroy(cp.pointB.gameObject);
                Object.Destroy(cp.gameObject);
            }
            checkpoints.Clear();
        }

        public void Disable()
        {
            running = false;
            DestroyCheckpoints();
            if (Main.cursor != null) Main.cursor.ClearPlacement(); // drop any in-progress placement objects
            PhotonNetwork.RemoveCallbackTarget(this);
        }
    }
}
