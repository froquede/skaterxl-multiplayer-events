using Cinemachine;
using SkaterXL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MultiplayerEvents
{
    public class Cursor : MonoBehaviour
    {
        public bool active = false;
        public MeshRenderer renderer;
        public CinemachineVirtualCamera camera;
        public CheckPoint checkPoint;
        // Placement-camera orbit (right stick) + zoom (triggers). Defaults reproduce the old
        // fixed (0,4,-4) view: pitch 45, yaw 0, distance ~5.66.
        float camYaw = 0f, camPitch = 45f, camDist = 5.66f;

        void Start()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sphere.transform.localScale = new Vector3(.1f, 0.01f, .1f);
            sphere.transform.parent = transform;
            sphere.transform.localPosition = new Vector3(0, 0.01f, 0);

            Destroy(sphere.GetComponent<CapsuleCollider>());

            Material material = new Material(Shader.Find("HDRP/Lit"));
            Utils.ApplyGateColor(material, new Color(0f, 0.4f, 1f));

            renderer = sphere.GetComponent<MeshRenderer>();
            renderer.material = material;

            renderer.enabled = false;
        }

        public Vector3 lastHitPoint = Vector3.zero;
        void Update()
        {
            if (active)
            {
                if (markDestroy)
                {
                    AddPoint();
                    markDestroy = false;
                }

                if (!renderer.enabled) renderer.enabled = true;

                RaycastHit hit;
                if (Physics.Raycast(transform.position + new Vector3(0, 1f, 0), Vector3.down, out hit, Mathf.Infinity, LayerUtility.GroundMask))
                {
                    lastHitPoint = hit.point;
                }

                // Zoom the placement camera with the triggers (RT in, LT out).
                float rt = PlayerController.Instance.inputController.player.GetAxis(InputBinding.RT);
                float lt = PlayerController.Instance.inputController.player.GetAxis(InputBinding.LT);
                camDist = Mathf.Clamp(camDist + (lt - rt) * 12f * Time.deltaTime, 2f, 20f);

                // Move the cursor relative to where the camera is looking (so "up" is always away
                // from the camera, even after orbiting). Speed scales with zoom for fine placement.
                float lsx = PlayerController.Instance.inputController.LeftStick.rawInput.pos.x;
                float lsy = PlayerController.Instance.inputController.LeftStick.rawInput.pos.y;
                Vector3 move = Quaternion.Euler(0f, camYaw, 0f) * new Vector3(lsx, 0f, lsy);
                lastHitPoint += move * (camDist / 5.66f) / 4f;

                transform.position = Vector3.Lerp(transform.position, lastHitPoint, Time.deltaTime * 12f);

                // Orbit the placement camera around the cursor with the right stick (X = yaw, Y = pitch).
                float rsx = PlayerController.Instance.inputController.RightStick.rawInput.pos.x;
                float rsy = PlayerController.Instance.inputController.RightStick.rawInput.pos.y;
                camYaw += rsx * 120f * Time.deltaTime;
                camPitch = Mathf.Clamp(camPitch - rsy * 60f * Time.deltaTime, 10f, 80f);
                Vector3 offset = Quaternion.Euler(camPitch, camYaw, 0f) * new Vector3(0f, 0f, -camDist);
                camera.transform.position = transform.position + offset;
                camera.transform.LookAt(transform.position);

                if (temporaryPoint != null) temporaryPoint.transform.position = transform.position;

                if (PlayerController.Instance.inputController.player.GetButtonUp(InputBinding.Confirm))
                {
                    AddPoint();
                }

                if (PlayerController.Instance.inputController.player.GetButton(InputBinding.Cancel))
                {
                    Utils.DisableCursor();
                }
            }
            else if (renderer.enabled) renderer.enabled = false;
        }

        Point pointA;
        Point pointB;
        Point temporaryPoint;
        bool markDestroy = false;
        void AddPoint()
        {
            if (checkPoint != null && pointB != null)
            {
                markDestroy = true;
                return;
            }

            if (checkPoint == null) checkPoint = Utils.AddCheckPoint();
            if (pointA == null)
            {
                pointA = Utils.AddPoint();
                pointA.transform.position = transform.position;
                checkPoint.pointA = pointA;
                temporaryPoint = Utils.AddPoint();
                checkPoint.pointB = temporaryPoint;
                checkPoint.editing = true;
            }
            else if (pointB == null)
            {
                checkPoint.editing = false;
                pointB = Utils.AddPoint();
                pointB.transform.position = transform.position;
                checkPoint.pointB = pointB;

                Main.eventManager.race.AddNewCheckPoint(checkPoint);
            }
        }

        void LateUpdate()
        {
            if (markDestroy)
            {
                Destroy(checkPoint.gameObject);
                Destroy(pointA.gameObject);
                Destroy(pointB.gameObject);
                Destroy(temporaryPoint.gameObject);
                checkPoint = null;
                pointA = pointB = temporaryPoint = null;
                Utils.Log("Destroyed");
            }
        }

        // Destroy any in-progress placement objects and stop the cursor. Safe to call anytime -
        // used by "Clear Checkpoints" and on race teardown so nothing is left in the scene.
        public void ClearPlacement()
        {
            if (active) Utils.DisableCursor(); // restores the camera + sets active = false
            markDestroy = false;
            if (temporaryPoint != null) Destroy(temporaryPoint.gameObject);
            if (checkPoint != null) Destroy(checkPoint.gameObject);
            if (pointA != null) Destroy(pointA.gameObject);
            if (pointB != null) Destroy(pointB.gameObject);
            checkPoint = null;
            pointA = pointB = temporaryPoint = null;
            if (renderer != null) renderer.enabled = false;
        }
    }
}