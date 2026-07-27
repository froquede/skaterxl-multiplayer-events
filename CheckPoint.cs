using SkaterXL.Core;
using UnityEngine;

namespace MultiplayerEvents
{
    public class CheckPoint : MonoBehaviour
    {
        public Point pointA, pointB;
        public int order = -1; // position in the race sequence (0-based); set when added to a race
        GameObject cube;
        Material cubeMaterial;

        void Start()
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.parent = transform;
            BoxCollider box = cube.GetComponent<BoxCollider>();
            box.isTrigger = true;
            // The visual stays a thin wall (localScale.z ~ 0.01), but the trigger box is made
            // much deeper/taller in local units so a skater at speed can't tunnel through it:
            // world size = localScale * box.size => ~gate-wide x ~3m tall x ~1.5m deep.
            box.size = new Vector3(1f, 6f, 150f);
            cube.AddComponent<CheckpointTrigger>();

            cubeMaterial = new Material(Shader.Find("HDRP/Lit"));

            Color transparentGreen = new Color(0f, 1f, 0f, 0.2f);
            cubeMaterial.SetColor("_BaseColor", transparentGreen);

            // Set surface type to Transparent
            cubeMaterial.SetFloat("_SurfaceType", 1); // 1 = Transparent

            // Set the blend mode to Alpha for smooth transparency
            cubeMaterial.SetFloat("_BlendMode", 0); // 0 = Alpha

            // Set Depth Test to "LEqual" so that transparent objects render correctly
            cubeMaterial.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.LessEqual);

            // Ensure ZWrite is disabled for transparent materials
            cubeMaterial.SetFloat("_ZWrite", 0);

            //cubeMaterial.renderQueue = 3000;
            cubeMaterial.SetFloat("_AlphaClip", 1f);

            cube.GetComponent<Renderer>().material = cubeMaterial;

            UpdatePosition();
        }

        public bool editing = true;
        void Update()
        {
            if (editing)
            {
                if (pointA != null && pointB != null)
                {
                    UpdatePosition();
                }
            }
        }

        public void UpdatePosition()
        {
            if (cube)
            {
                cube.transform.position = Vector3.Lerp(pointA.transform.position, pointB.transform.position, .5f);
                cube.transform.localScale = new Vector3(Vector3.Distance(pointA.transform.position, pointB.transform.position), .5f, .01f);
                Vector3 direction = (pointB.transform.position - pointA.transform.position).normalized;
                cube.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
                cube.transform.Rotate(0, 90, 0);
            }
        }
    }

    public class Point : MonoBehaviour
    {

    }

}
