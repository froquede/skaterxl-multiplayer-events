using UnityEngine;

namespace MultiplayerEvents
{
    public class CheckPoint : MonoBehaviour
    {
        public Point pointA, pointB;
        public int order = -1; // position in the race sequence (0-based)

        GameObject visual;   // rendered marker: low + thin so it never blocks the view; no collider
        GameObject trigger;  // invisible tall/deep box; the actual detection volume
        Material mat;

        void Start()
        {
            // Visual marker: a low, wide, thin bar. Collider removed so it can't obstruct skating.
            visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.transform.parent = transform;
            Destroy(visual.GetComponent<BoxCollider>());
            mat = new Material(Shader.Find("HDRP/Lit"));
            Utils.ApplyGateColor(mat, new Color(0f, 1f, 0.2f)); // opaque low bar - doesn't block the view
            visual.GetComponent<Renderer>().material = mat;

            // Detection volume: invisible (renderer removed), tall + deep so a skater at speed can't
            // tunnel through it. Carries the trigger component that reports passes to the race.
            trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
            trigger.transform.parent = transform;
            Destroy(trigger.GetComponent<MeshRenderer>());
            trigger.GetComponent<BoxCollider>().isTrigger = true;
            trigger.AddComponent<CheckpointTrigger>();

            UpdatePosition();
        }

        public bool editing = true;
        void Update()
        {
            if (editing && pointA != null && pointB != null) UpdatePosition();
        }

        public void UpdatePosition()
        {
            if (pointA == null || pointB == null) return;

            Vector3 mid = Vector3.Lerp(pointA.transform.position, pointB.transform.position, .5f);
            float width = Vector3.Distance(pointA.transform.position, pointB.transform.position);
            Vector3 dir = (pointB.transform.position - pointA.transform.position).normalized;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 90, 0);

            if (visual)
            {
                visual.transform.position = mid + new Vector3(0f, 0.1f, 0f); // just above the ground
                visual.transform.localScale = new Vector3(width, 0.2f, 0.03f); // wide, low, thin
                visual.transform.rotation = rot;
            }
            if (trigger)
            {
                trigger.transform.position = mid + new Vector3(0f, 1.5f, 0f); // centered ~waist height
                trigger.transform.localScale = new Vector3(width, 3f, 1.5f);  // gate-wide x 3m tall x 1.5m deep
                trigger.transform.rotation = rot;
            }
        }
    }

    public class Point : MonoBehaviour
    {

    }
}
