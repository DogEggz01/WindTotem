using System.Collections;
using UnityEngine;

namespace WindTotem
{
    // Composition preserves all private ShipItem lifecycle methods and boat/save behavior.
    public sealed class PortableTotem : MonoBehaviour
    {
        private IEnumerator Start()
        {
            var item = GetComponent<ShipItem>();
            var saveable = GetComponent<SaveablePrefab>();
            // Allow vanilla load restoration to populate the instance ID and boat parent first.
            while (!SaveLoadManager.instance || GameState.currentlyLoading || GameState.justStarted)
                yield return null;
            yield return new WaitForEndOfFrame();
            if (item.sold) saveable.RegisterToSave();

            var lod = GetComponent<LODGroup>();
            if (lod)
            {
                var levels = lod.GetLODs();
                if (levels.Length > 0)
                {
                    levels[0].renderers = GetComponentsInChildren<Renderer>(true);
                    lod.SetLODs(levels);
                    lod.RecalculateBounds();
                }
            }
        }
    }
}
