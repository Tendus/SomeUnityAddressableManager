using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Some.Utility.AddressableManager.Sample
{
    public class SimpleUIManager : MonoBehaviour
    {
        public GameObject m_splashScreen;
        public SimpleLoadingScreen m_loadScreen;

        private void Awake()
        {
            m_splashScreen.SetActive(true);
            m_loadScreen.gameObject.SetActive(false);
        }

        public void StartGame()
        {
            m_loadScreen.StartLoadingSequence(this, false);
            foreach (UnityEngine.UI.Graphic g in m_splashScreen.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                g.raycastTarget = false;
        }

        public void ClearCache()
        {
            UnityEngine.Caching.ClearCache();
            UnityEngine.AddressableAssets.Addressables.ClearResourceLocators();
        }
    }
}
