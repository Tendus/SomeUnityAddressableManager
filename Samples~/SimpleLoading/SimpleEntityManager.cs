using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Some.Utility.AddressableManager.Sample
{
    public class SimpleEntityManager : MonoBehaviour
    {
        public void LoadAllAssets()
        {
            foreach (var assetRef in GetComponentsInChildren<Simple.SimpleGameObjectReference>())
                assetRef.LoadAsset();
        }
    }
}
