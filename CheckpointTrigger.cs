using SkaterXL.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MultiplayerEvents
{
    class CheckpointTrigger : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            if (other.gameObject.layer == LayerUtility.Character)
            {
                SkaterController skater = other.gameObject.GetComponentInParent<SkaterController>();
                if(skater != null)
                {
                    if (skater.gameObject.GetInstanceID() == PlayerController.Instance.skaterController.gameObject.GetInstanceID())
                    {

                    }
                }
            }
        }
    }
}
