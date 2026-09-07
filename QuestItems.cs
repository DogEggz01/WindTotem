using System;
using System.Collections;
using UnityEngine;

namespace WindTotem
{
    internal static class QuestItems
    {
        internal static ShipItem SpawnAndCommit(int index, Vector3 position, Func<int, bool> commit)
        {
            GameObject root = null;
            SaveablePrefab save = null;
            try
            {
                root = UnityEngine.Object.Instantiate(PrefabsDirectory.instance.directory[index], position,
                    Quaternion.identity, FloatingOriginManager.instance.transform);
                var item = root.GetComponent<ShipItem>();
                save = root.GetComponent<SaveablePrefab>();
                if (!item || !save) throw new InvalidOperationException("Quest prefab is missing item/save components.");
                item.sold = true;
                item.nailed = false;
                save.RegisterToSave();
                if (!commit(save.instanceId)) throw new InvalidOperationException("Quest transition rejected; removing uncommitted item.");
                return item;
            }
            catch
            {
                try { if (save && SaveLoadManager.instance) save.Unregister(); }
                finally { if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); } }
                throw;
            }
        }
        internal static IEnumerator ReleasePhysics(ShipItem item)
        {
            // Vanilla creates a physics proxy/collider asynchronously and holds it
            // kinematic for the first six physics frames. Never make the visual body dynamic.
            for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
            if (!item || !item.itemRigidbodyC || item.held || item.nailed || GameState.currentlyLoading) yield break;
            var proxy = item.itemRigidbodyC;
            proxy.attached = false;
            proxy.disableCol = false;
            proxy.inStove = false;
            var rb = proxy.GetBody();
            if (rb && proxy.GetComponent<Collider>()) { rb.useGravity = true; rb.isKinematic = false; rb.WakeUp(); }
        }
    }
}
