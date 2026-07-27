using SkaterXL.Core;
using UnityEngine;

namespace MultiplayerEvents
{
    // Sits on a checkpoint's trigger volume. When the LOCAL skater passes through, it tells the
    // active race which checkpoint (by order) was crossed. All race/ordering/lap logic lives in
    // Race; this just reports a local pass.
    class CheckpointTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer != LayerUtility.Character) return;

            Race race = Main.eventManager != null ? Main.eventManager.race : null;
            if (race == null || !race.running) return;

            // Only our own skater counts (each client reports its own progress).
            SkaterController skater = other.gameObject.GetComponentInParent<SkaterController>();
            if (skater == null || PlayerController.Instance == null) return;
            if (skater.gameObject.GetInstanceID() != PlayerController.Instance.skaterController.gameObject.GetInstanceID()) return;

            CheckPoint cp = GetComponentInParent<CheckPoint>();
            if (cp == null) return;

            race.OnLocalCheckpointPassed(cp.order);
        }
    }
}
