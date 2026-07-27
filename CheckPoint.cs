using UnityEngine;

namespace MultiplayerEvents
{
    public class CheckPoint : MonoBehaviour
    {
        public Point pointA, pointB;
        public int order = -1; // position in the race sequence (0-based)

        GameObject visual;   // rendered marker: low + thin so it never blocks the view; no collider
        GameObject trigger;  // invisible tall/deep box; the actual detection volume
        GameObject beacon;   // tall bright pillar shown only over the racer's current target gate
        Material mat, beaconMat;

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

            // Beacon: a tall thin bright pillar, hidden until this is the racer's next target gate.
            beacon = GameObject.CreatePrimitive(PrimitiveType.Cube);
            beacon.transform.parent = transform;
            Destroy(beacon.GetComponent<BoxCollider>());
            beaconMat = new Material(Shader.Find("HDRP/Lit"));
            Utils.ApplyGateColor(beaconMat, new Color(1f, 0.85f, 0f)); // amber
            beacon.GetComponent<Renderer>().material = beaconMat;
            beacon.SetActive(false);

            UpdatePosition();
        }

        // Highlight (or un-highlight) this as the next gate to head for.
        public void SetNext(bool on)
        {
            if (beacon != null) beacon.SetActive(on);
            if (mat != null) Utils.ApplyGateColor(mat, on ? new Color(1f, 0.85f, 0f) : new Color(0f, 1f, 0.2f));
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
            if (beacon)
            {
                beacon.transform.position = mid + new Vector3(0f, 2.5f, 0f); // rises from the gate
                beacon.transform.localScale = new Vector3(0.15f, 5f, 0.15f);  // thin tall pillar
                beacon.transform.rotation = Quaternion.identity;
            }
        }
    }

    public class Point : MonoBehaviour
    {

    }
}
