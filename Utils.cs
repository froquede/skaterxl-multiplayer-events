using Cinemachine;
using HarmonyLib;
using System.Collections;
using System.Collections.Generic;
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
                if(Main.go.transform.childCount > 0) Main.tick.DelayDestroy(Main.go.transform.GetChild(0));
            } catch { }

            GameObject notification = new GameObject();
            Notification n = notification.AddComponent<Notification>();
            notification.transform.parent = Main.go.transform.parent;
            n.ShowNotification(text.ToString(), duration);
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

            Main.eventManager.race.SyncCheckPoints();
        }

        public static void DisableCameraCollider(bool enabled)
        {
            PlayerController.Instance.cameraController.gameObject.GetComponentInChildren<Cinemachine.CinemachineCollider>().enabled = enabled;
        }

        public static string GetPlayerID()
        {
            return MultiplayerManager.Instance.localPlayer.NickName + " | " + MultiplayerManager.Instance.localPlayer.UserId;
        }

        public static string[] getListOfPlayers()
        {
            string[] names = new string[MultiplayerManager.Instance.networkPlayers.Count];
            int i = 0;
            foreach (KeyValuePair<int, NetworkPlayerController> entry in MultiplayerManager.Instance.networkPlayers)
            {
                if (entry.Value)
                {
                    names[i] = entry.Value.NickName + " | " + entry.Value.UserId;
                    i++;
                }
            }

            return names;
        }

        public static string ComboName()
        {
            string fullName = "";
            if (Main.eventManager.SKATE != null && Main.eventManager.SKATE.actualTrickCombo != null)
            {

                string lastTrick = "";
                for (int i = 0; i < Main.eventManager.SKATE.actualTrickCombo.Tricks.Count; i++)
                {
                    Trick t = Main.eventManager.SKATE.actualTrickCombo.Tricks[i];

                    if (i >= 1 && t.ToString() != "Ollie" && lastTrick != "" && lastTrick != "Nollie" && lastTrick != "Fakie" && lastTrick != "Switch") fullName += "to ";

                    string trickName = t.ToString();
                    if (Main.eventManager.SKATE.actualTrickCombo.Tricks.Count > 1)
                    {
                        if (trickName == "Fakie Ollie") trickName = "Fakie";
                        if (trickName == "Switch Ollie") trickName = "Switch";
                        if (trickName == "Ollie") trickName = "";
                    }

                    fullName += trickName + " ";

                    lastTrick = trickName;
                }
            }

            return fullName;
        }
    }
}
