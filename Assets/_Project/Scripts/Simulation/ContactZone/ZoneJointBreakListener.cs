using UnityEngine;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Приёмник OnJointBreak на GameObject детали: PhysX шлёт сообщение тому
    /// объекту, на котором стоит FixedJoint (наша деталь), поэтому прокси вешает
    /// этот компонент рядом со стыком и получает разрыв с индексом детали.
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
