using Cinemachine;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityModManagerNet;

namespace MultiplayerEvents
{
    public static class Utils
    {
        public static float map01(float value, float min, float max)
        {
            return (value - min) * 1f / (max - min);
        }

        public static float map(float value, float leftMin, float leftMax, float rightMin, float rightMax)
        {
            return rightMin + (value - leftMin) * (rightMax - rightMin) / (leftMax - leftMin);
        }

        public static Vector3 TranslateWithRotation(Vector3 input, Vector3 translation, Quaternion rotation)
        {
            Vector3 rotatedTranslation = rotation * translation;
            Vector3 output = input + rotatedTranslation;
            return output;
        }

        public static Quaternion SmoothDampQuaternion(Quaternion current, Quaternion target, ref Vector3 currentVelocity, float smoothTime)
        {
            Vector3 c = current.eulerAngles;
            Vector3 t = target.eulerAngles;
            return Quaternion.Euler(
              Mathf.SmoothDampAngle(c.x, t.x, ref currentVelocity.x, smoothTime),
              Mathf.SmoothDampAngle(c.y, t.y, ref currentVelocity.y, smoothTime),
              Mathf.SmoothDampAngle(c.z, t.z, ref currentVelocity.z, smoothTime)
            );
        }

        public static bool AlmostEquals(this float double1, float double2, float precision)
        {
            return (Mathf.Abs(double1 - double2) <= precision);
        }

        public static bool IsGrabbing()
        {
            return PlayerController.Instance.currentStateEnum == PlayerController.CurrentState.Grabs || EventManager.Instance.IsGrabbing;
        }

        public static void Log(object arg)
        {
            UnityModManager.Logger.Log("[ME] " + arg.ToString());
        }

        public static bool isAdmin()
        {
            return isOnline() && MultiplayerManager.Instance.IsMasterClient;
        }

        public static bool isOnline()
        {
            return MultiplayerManager.Instance != null && MultiplayerManager.Instance.networkPlayers != null && MultiplayerManager.Instance.InRoom;
        }

        public static void ShowNotification(object text, float duration) {
            try
            {
                // Destroy the GameObject, not the Transform (Unity refuses to destroy a Transform).
                if(Main.go.transform.childCount > 0) Main.tick.DelayDestroy(Main.go.transform.GetChild(0).gameObject);
            } catch { }

            GameObject notification = new GameObject();
            Notification n = notification.AddComponent<Notification>();
            // Parent to Main.go itself so the childCount de-dup above actually sees it.
            notification.transform.parent = Main.go.transform;
            n.ShowNotification(text.ToString(), duration);
        }

        // A short UI sound to make a turn change audible. Uses the game's own sounds:
        // a "major" cue when it's your move, a softer "minor" cue when you're waiting.
        public static void PlayTurnSound(bool yourTurn)
        {
            try
            {
                if (UISounds.Instance == null) return;
                if (yourTurn) UISounds.Instance.PlayOneShotSelectMajor();
                else UISounds.Instance.PlayOneShotSelectMinor();
            }
            catch { }
        }

        // Color an HDRP/Lit material as an OPAQUE, softly-glowing marker. Skater XL's build strips
        // HDRP/Lit's transparent pass (a transparent material renders only its shadow, no surface),
        // so we stay opaque - and lean on small/low geometry to avoid blocking the view. Emission
        // is part of the standard lit pass (not a stripped variant), so it renders reliably and
        // keeps a low marker readable even in shadow.
        public static void ApplyGateColor(Material mat, Color color)
        {
            Color opaque = new Color(color.r, color.g, color.b, 1f);
            mat.SetColor("_BaseColor", opaque);
            mat.SetColor("_Color", opaque);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissiveColor", new Color(color.r, color.g, color.b) * 1.5f);
            mat.SetFloat("_EmissiveExposureWeight", 0f); // emission independent of scene exposure
        }

        public static CheckPoint AddCheckPoint()
        {
            GameObject checkpoint = new GameObject("MECheckpoint");
            return checkpoint.AddComponent<CheckPoint>();
        }

        public static CheckPoint AddCheckPoint(Point pointA, Point pointB)
        {
            GameObject checkpoint = new GameObject("MECheckpoint");
            CheckPoint c = checkpoint.AddComponent<CheckPoint>();
            c.editing = false;
            c.pointA = pointA;
            c.pointB = pointB;
            c.UpdatePosition();

            return c;
        }

        public static Point AddPoint()
        {
            GameObject point = new GameObject("MEPoint");
            return point.AddComponent<Point>();
        }

        public static bool lastColliderBool;
        public static void EnableCursor()
        {
            Main.cursor.transform.position = PlayerController.Instance.skaterController.skaterTransform.position;
            Main.cursor.lastHitPoint = Main.cursor.transform.position;
            Main.cursor.active = true;

            lastColliderBool = PlayerController.Instance.cameraController.gameObject.GetComponentInChildren<Cinemachine.CinemachineCollider>().enabled;
            DisableCameraCollider(false);
            PlayerController.Instance.cameraController._camRigidbody.isKinematic = true;
            PlayerController.Instance.cameraController.enabled = false;

            GameObject fallbackCamera = PlayerController.Instance.skaterController.transform.parent.parent.Find("Fallback Camera").gameObject;
            fallbackCamera.GetComponent<CinemachineFallbackCamera>().enabled = false;

            Main.cursor.camera = PlayerController.Instance.cameraController._actualCam.GetComponent<CinemachineVirtualCamera>();
        }

        public static void DisableCursor()
        {
            Main.cursor.active = false;
            DisableCameraCollider(lastColliderBool);

            PlayerController.Instance.cameraController._camRigidbody.isKinematic = false;
            PlayerController.Instance.cameraController.enabled = true;

            GameObject fallbackCamera = PlayerController.Instance.skaterController.transform.parent.parent.Find("Fallback Camera").gameObject;
            fallbackCamera.GetComponent<CinemachineFallbackCamera>().enabled = true;
            // Checkpoints stay local to the host during placement; they're sent to participants
            // only when the race starts (Race.StartAsHost), so nothing here to broadcast.
        }

        public static void DisableCameraCollider(bool enabled)
        {
            PlayerController.Instance.cameraController.gameObject.GetComponentInChildren<Cinemachine.CinemachineCollider>().enabled = enabled;
        }

        public static string GetPlayerID()
        {
            return MultiplayerManager.Instance.localPlayer.NickName + GameConfig.PlayerIdSeparator + MultiplayerManager.Instance.localPlayer.UserId;
        }

        public static string[] getListOfPlayers(bool moddedOnly = false)
        {
            List<string> names = new List<string>();
            HashSet<string> modded = moddedOnly ? GetModdedUserIds() : null;

            foreach (KeyValuePair<int, NetworkPlayerController> entry in MultiplayerManager.Instance.networkPlayers)
            {
                if (entry.Value && entry.Value.UserId != MultiplayerManager.Instance.localPlayer.UserId)
                {
                    if (moddedOnly && !modded.Contains(entry.Value.UserId)) continue;
                    if (IsBlocked(entry.Value.UserId, entry.Value.NickName)) continue; // hide blocked players
                    names.Add(entry.Value.NickName + GameConfig.PlayerIdSeparator + entry.Value.UserId);
                }
            }

            return names.ToArray();
        }

        // --- Player block list --------------------------------------------------

        // True if a player (by stable UserId or by nickname) is on the block list.
        public static bool IsBlocked(string userId, string nick)
        {
            List<string> list = Main.settings != null ? Main.settings.blockedPlayers : null;
            if (list == null) return false;

            for (int i = 0; i < list.Count; i++)
            {
                string bId = UserIdOf(list[i]);
                string bNick = NickOf(list[i]);
                if (!string.IsNullOrEmpty(bId) && !string.IsNullOrEmpty(userId) && bId == userId) return true;
                if (!string.IsNullOrEmpty(bNick) && !string.IsNullOrEmpty(nick)
                    && string.Equals(bNick, nick, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Overload for a "Nick | UserId" string.
        public static bool IsBlocked(string playerId)
        {
            return IsBlocked(UserIdOf(playerId), NickOf(playerId));
        }

        public static void BlockPlayer(string nick, string userId)
        {
            nick = (nick ?? "").Trim();
            userId = (userId ?? "").Trim();
            if (nick == "" && userId == "") return;
            if (IsBlocked(userId, nick)) return; // already blocked

            Main.settings.blockedPlayers.Add(nick + GameConfig.PlayerIdSeparator + userId);
            Main.settings.Save(Main.modEntry);
            ShowNotification("Blocked " + (nick != "" ? nick : userId), 2f);
        }

        public static void UnblockPlayer(string entry)
        {
            if (Main.settings.blockedPlayers.Remove(entry)) Main.settings.Save(Main.modEntry);
        }

        // --- Mod presence: advertise ourselves and detect other modded players ---

        static string presenceRoom = null; // name of the room we last advertised in

        // Advertise that we run the mod via a Photon player custom property, so others can
        // tell who they can invite. Cheap and idempotent; call it while online.
        public static void PublishPresence()
        {
            if (!isOnline()) { presenceRoom = null; return; }

            Room current = PhotonNetwork.CurrentRoom;
            string room = current != null ? current.Name : null;
            // Publish once per room. The old flag-based guard also re-checked ContainsKey, which
            // stays false until the server round-trips the property, so it re-sent every frame
            // for the first several frames after joining. Gating on the room name avoids that.
            if (room != null && room == presenceRoom) return;

            PhotonNetwork.LocalPlayer.SetCustomProperties(new ExitGames.Client.Photon.Hashtable
            {
                { GameConfig.PresencePropertyKey, Main.modEntry.Info.Version }
            });
            presenceRoom = room;
        }

        public static HashSet<string> GetModdedUserIds()
        {
            HashSet<string> set = new HashSet<string>();
            if (PhotonNetwork.CurrentRoom == null) return set;

            foreach (Player p in PhotonNetwork.CurrentRoom.Players.Values)
            {
                if (p != null && p.UserId != null && p.CustomProperties != null
                    && p.CustomProperties.ContainsKey(GameConfig.PresencePropertyKey))
                {
                    set.Add(p.UserId);
                }
            }
            return set;
        }

        // "Nick | UserId" helpers.
        public static string NickOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "";
            return playerId.Split(new string[] { GameConfig.PlayerIdSeparator }, StringSplitOptions.None)[0];
        }

        public static string UserIdOf(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "";
            string[] parts = playerId.Split(new string[] { GameConfig.PlayerIdSeparator }, StringSplitOptions.None);
            return parts.Length > 1 ? parts[1] : "";
        }

        // The trick this player is currently setting/defending, as a readable name.
        public static string ComboName()
        {
            if (Main.eventManager.SKATE == null) return "";
            return ComboName(Main.eventManager.SKATE.actualTrickCombo);
        }

        // Human-readable name for a combo, normalized the same way matching is:
        // small manuals are dropped and the "to Fakie"/stance quirks are handled so
        // the connector never doubles up (e.g. the old "to to Fakie" bug).
        public static string ComboName(TrickCombo combo)
        {
            List<string> tricks = NormalizedTrickNames(combo);
            if (tricks.Count == 0) return "";

            StringBuilder sb = new StringBuilder();
            string last = "";
            for (int i = 0; i < tricks.Count; i++)
            {
                string name = tricks[i];

                // In a multi-trick combo a standalone pop collapses to its stance:
                // "Fakie Ollie" -> "Fakie", "Switch Ollie" -> "Switch", "Ollie" -> "" (implied).
                if (tricks.Count > 1)
                {
                    if (name == "Fakie Ollie") name = "Fakie";
                    else if (name == "Switch Ollie") name = "Switch";
                    else if (name == "Ollie") name = "";
                }
                if (name == "") continue; // dropped Ollie: no token and no connector

                // Join tricks with "to", except: the first token, right after a bare
                // stance (Fakie/Switch/Nollie), or when the token already carries its
                // own "to" (e.g. "to Fakie") - that last guard fixes the doubled "to".
                bool afterStance = last == "Fakie" || last == "Switch" || last == "Nollie";
                bool startsWithTo = name.StartsWith("to ");
                if (sb.Length > 0 && !afterStance && !startsWithTo) sb.Append("to ");

                sb.Append(name).Append(' ');
                last = name;
            }

            return sb.ToString().TrimEnd();
        }

        // Trick names for a combo with incidental (short) manuals removed, so an
        // intentional trick popped right after a tiny manual matches the clean trick.
        // Used for both display and set/defense comparison so they always agree.
        public static List<string> NormalizedTrickNames(TrickCombo combo)
        {
            List<string> names = new List<string>();
            if (combo == null || combo.Tricks == null) return names;

            float manualMax = Main.settings != null ? Main.settings.smallManualMaxSeconds : GameConfig.SmallManualMaxSeconds;

            foreach (Trick t in combo.Tricks)
            {
                Manual m = t as Manual;
                if (m != null && m.duration < manualMax) continue; // ignore small manual
                string s = t.ToString();
                if (!string.IsNullOrEmpty(s)) names.Add(s); // AirTrick.ToString() can be null
            }
            return names;
        }

        // The network controller for a given UserId, or null if not in the room.
        public static NetworkPlayerController GetNetworkPlayer(string userId)
        {
            if (string.IsNullOrEmpty(userId) || !isOnline()) return null;

            foreach (KeyValuePair<int, NetworkPlayerController> entry in MultiplayerManager.Instance.networkPlayers)
            {
                if (entry.Value != null && entry.Value.UserId == userId) return entry.Value;
            }
            return null;
        }
    }
}
