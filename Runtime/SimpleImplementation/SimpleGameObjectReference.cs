using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Some.Utility.AddressableManager.Simple
{
    public class SimpleGameObjectReference : SimpleObjectReference<GameObject>
    {
        [SerializeField] private Transform m_overrideParent;

        public GameObject InstantiatedObject { get; protected set; }

        protected override void SetLoadedAsset(GameObject asset)
        {
            InstantiatedObject = Instantiate(asset, m_overrideParent != null ? m_overrideParent : transform);
        }

        protected override void ClearAssetReferences()
        {
            if(InstantiatedObject != null)
                Destroy(InstantiatedObject);

            InstantiatedObject = null;
        }
    }
}
