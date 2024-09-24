using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Some.Utility.AddressableManager.Simple
{
    public class SimpleObjectReference : SimpleObjectReference<Object> { protected override void SetLoadedAsset(Object obj) { } protected override void ClearAssetReferences() { }  }
    //Simplifies most of the Loading/Unloading functionality so you can just inherit from it and handle the callbacks
    public abstract class SimpleObjectReference<T> : MonoBehaviour, IAssetRequestCallback where T : Object
    {
        [SerializeField] private AssetReferenceT<T> m_defaultReference;
        [SerializeField] private bool m_autoLoadReference = false;
        [Tooltip("Ideally it's always \"FullInit\", but if you're not sure what that entails you can leave it to just \"Init\".")]
        [SerializeField] private ManagerService m_autoLoadRequiresServices = ManagerService.FullInit;
        [Tooltip("Will also reload the last loaded assetref (if applicable) OnEnable")]
        [SerializeField] private bool m_unloadOnDisable = false;
        [SerializeField] private UnityEngine.Events.UnityEvent<T> m_onAssetLoad = new UnityEngine.Events.UnityEvent<T>();
        [SerializeField] private UnityEngine.Events.UnityEvent m_onAssetUnload = new UnityEngine.Events.UnityEvent();

        //IAssetRequest
        public int RequestID { get; set; } = 0;

        protected AssetReference m_lastReference = null;
        protected AssetReference m_activeReference = null;
        public AssetReference ActiveReference { get => m_activeReference; private set { if (m_activeReference != null) m_lastReference = m_activeReference; m_activeReference = value; } }
        public T ActiveAsset => ActiveReference != null && ActiveReference.IsDone ? ActiveReference.Asset as T : null;
        public bool IsLoading { get; private set; } = false;

        private System.Action<T> onLoadCallback;

        public string LoadedGUID => ActiveReference != null ? ActiveReference.AssetGUID : "";

        //called after load, for any component-level functionality you might to extend
        protected abstract void SetLoadedAsset(T asset);
        //called before unload, use to ensure that no references to the previously loaded asset exists
        protected abstract void ClearAssetReferences();

        protected virtual void Awake()
        {
            if (m_autoLoadReference && !m_unloadOnDisable)
            {
                if (ServicesReadyForAutoLoad())
                    AttemptAutoLoadAsset();
                else
                    AddressableManager.AwaitServices(m_autoLoadRequiresServices, AttemptAutoLoadAsset);
            }
            else if (m_autoLoadReference)
                m_lastReference = m_defaultReference;
        }

        protected virtual void OnEnable()
        {
            if (m_lastReference != null && m_unloadOnDisable)
            {
                if (ServicesReadyForAutoLoad())
                    AttemptAutoLoadAsset();
                else
                    AddressableManager.AwaitServices(m_autoLoadRequiresServices, AttemptAutoLoadAsset);
            }
        }

        protected virtual void OnDisable()
        {
            if (m_unloadOnDisable)
            {
                if (!ServicesReadyForAutoLoad())
                    AddressableManager.IgnoreServices(m_autoLoadRequiresServices, AttemptAutoLoadAsset);

                UnloadAssetInternal();
            }
        }

        protected virtual void OnDestroy() { if (!AddressableManager.HasInit) AddressableManager.IgnoreServices(ManagerService.Init, AttemptAutoLoadAsset); UnloadAssetInternal(); }

        protected bool ServicesReadyForAutoLoad() { return AddressableManager.HasInstance && AddressableManager.ServicesComplete(m_autoLoadRequiresServices); }
        protected void AttemptAutoLoadAsset()
        {
            if (ServicesReadyForAutoLoad())
                AutoLoadDefaultAsset();
        }

        protected virtual void AutoLoadDefaultAsset(bool defaultToLast = true) { LoadAsset(defaultToLast && m_lastReference != null ? m_lastReference : m_defaultReference); }

        //TODO: Add LoadAsync function
        public void LoadAsset(System.Action<T> onFinishLoad = null) { LoadAsset(m_defaultReference, onFinishLoad);  }
        public virtual void LoadAsset(AssetReference assetReference, System.Action<T> onFinishLoad = null)
        {
            //passed empty, assuming an unload
            if(assetReference == null || string.IsNullOrEmpty(assetReference.AssetGUID))
            {
                UnloadAsset();
                if (onFinishLoad != null)
                    onFinishLoad.Invoke(ActiveAsset);
                return;
            }

            //requesting repeat of current load
            if(LoadedGUID == assetReference.AssetGUID)
            {
                if(onFinishLoad != null)
                {
                    if (!IsLoading)
                        onFinishLoad.Invoke(ActiveAsset);
                    else
                        onLoadCallback += onFinishLoad;
                }
            }

            UnloadAssetInternal();
            IsLoading = true;
            ActiveReference = assetReference;
            onLoadCallback = onFinishLoad;

            AddressableManager.BaseInstance.LoadAsset(assetReference, this);
        }

        //IAssetRequestWithCallback
        public virtual void OnAssetLoaded(AssetReference assetReference)
        {
            IsLoading = false;
            if (ActiveReference == null || string.IsNullOrEmpty(ActiveReference.AssetGUID) || assetReference.Asset == null)
                return;

            ActiveReference = assetReference;
            SetLoadedAsset(ActiveAsset);
            m_onAssetLoad.Invoke(ActiveAsset);

            if (onLoadCallback != null)
                onLoadCallback.Invoke(ActiveAsset);

            onLoadCallback = null;
        }

        public virtual void OnAssetFailToLoad(AssetReference assetReference)
        {
            if (assetReference.AssetGUID == LoadedGUID)
                onLoadCallback = null;
        }

        //seperating from Internal function to null out the reference to the m_lastReference to help disable the m_unloadOnDisable reload functionality (if an External Unload call is made, we likely don't want it to reload OnEnable), might want to revisit later
        public void UnloadAsset()
        {
            m_lastReference = null;
            UnloadAssetInternal();
        }

        protected virtual void UnloadAssetInternal()
        {
            m_onAssetUnload.Invoke();
            ClearAssetReferences();

            if (AddressableManager.HasInit && !string.IsNullOrEmpty(LoadedGUID))
                AddressableManager.BaseInstance.UnloadAsset(ActiveReference, this);

            ActiveReference = null;
            onLoadCallback = null;
        }
    }
}
