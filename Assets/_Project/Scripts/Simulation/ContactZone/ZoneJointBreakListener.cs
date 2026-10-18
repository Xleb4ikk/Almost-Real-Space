using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// OnJointBreak receiver on the part's GameObject: PhysX messages the object
    /// holding the FixedJoint (our part), so the proxy attaches this component next
    /// to the joint and receives the break with the part index.
    /// </summary>
    public sealed class ZoneJointBreakListener : MonoBehaviour
    {
        private ContactZoneProxy proxy;
        private int partIndex = -1;

        public void Bind(ContactZoneProxy owner, int index)
        {
            proxy = owner;
            partIndex = index;
        }

        private void OnJointBreak(float breakForce)
        {
            if (proxy != null)
            {
                proxy.OnPartJointBreak(partIndex, breakForce);
            }
        }
    }
}
