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

        void Start()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sphere.transform.localScale = new Vector3(.1f, 0.01f, .1f);
            sphere.transform.parent = transform;
            sphere.transform.localPosition = new Vector3(0, 0.01f, 0);

            Destroy(sphere.GetComponent<CapsuleCollider>());

            Material material = new Material(Shader.Find("HDRP/Lit"));
            Utils.ApplyHDRPTransparency(material, new Color(0f, 0f, 1f, 0.25f));

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

                lastHitPoint += new Vector3(PlayerController.Instance.inputController.LeftStick.rawInput.pos.x / 4f, 0f, PlayerController.Instance.inputController.LeftStick.rawInput.pos.y / 4f);

                transform.position = Vector3.Lerp(transform.position, lastHitPoint, Time.deltaTime * 12f);
                camera.transform.position = transform.position + new Vector3(0, 4f, -4f);
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
    }
}