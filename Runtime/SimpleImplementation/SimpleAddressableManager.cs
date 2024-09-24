using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Some.Utility.AddressableManager.Simple
{
    //TODO: Get Priority implemented either as a check for Autocaching, or when dumping the cache on approaching memory overload
    public abstract class SimpleAbstractAssetRefLink : AssetRefLink
    {
        public abstract int Priority { get; }

        public abstract void SetPriority(IAssetRequest forRequester, int priority);
    }

    //Seperating from base Priority class above in case someone wants to override and store the Requesters as direct references
    public class SimpleAssetRefLink : SimpleAbstractAssetRefLink
    {
        protected int m_basePriority = 0;
        protected Dictionary<int,int> m_allRequesters = new Dictionary<int, int>();

        public override bool HasRequests => m_allRequesters.Count > 0;

        public override int Priority
        {
            get
            {
                int retPriority = m_basePriority;
                foreach (int key in m_allRequesters.Keys)
                    retPriority = Mathf.Max(retPriority, m_allRequesters[key]);

                return retPriority;
            }
        }

        public override void LinkRequestID(IAssetRequest requester) { if (!m_allRequesters.ContainsKey(requester.RequestID)) m_allRequesters.Add(requester.RequestID, 0); }
        public override void DelinkRequestID(IAssetRequest requester) { m_allRequesters.Remove(requester.RequestID); }
        public override void DelinkAllRequestIDs() { m_allRequesters.Clear(); }
        public override bool HasRequestID(int requestID) { return m_allRequesters.ContainsKey(requestID); }

        public override void SetPriority(IAssetRequest forRequester, int priority)
        {
            if (forRequester == null)
                m_basePriority = priority;
            else if (HasRequestID(forRequester.RequestID))
                m_allRequesters[forRequester.RequestID] = priority;
        }
    }

    //Wrapper for SimpleTemplate version in case someone wants to just extend this "Simple" manager
    public class SimpleAddressableManager : SimpleAddressableManagerTemplate<SimpleAddressableManager, SimpleAddressableManagerSettingsBase, SimpleAssetRefLink>
    {
        protected override SimpleAssetRefLink CreateNewAssetLink(AssetReference assetRef) { return new SimpleAssetRefLink(); }
    }

    public abstract class SimpleAddressableManagerTemplate<M,S,A> : AddressableManager<M,S,A>
        where M : SimpleAddressableManagerTemplate<M,S,A>
        where S : SimpleAddressableManagerSettingsTemplate<M,S,A> 
        where A : SimpleAbstractAssetRefLink
    {
        public bool DoAutoCacheBundlesInMemory { get; set; }

        protected override async UniTask<bool> InitSelfAsync()
        {
            bool hasInit = await base.InitSelfAsync();
            if (!hasInit)
                return false;

            hasInit = await Settings.FinalizeInit(this);

            return hasInit;
        }

        public override bool ApplySettings(AddressableManagerSettings settings)
        {
            if (settings is S tempSettings)
                PrintLogs = tempSettings.PrintManagerLogs;

            return base.ApplySettings(settings);
        }

        //public wrappers for the internal Load functions
        public override AssetRefLink LoadAsset(AssetReference assetRef, IAssetRequestCallback requester) { return LoadAsset(assetRef, requester, 0); }
        public AssetRefLink LoadAsset(AssetReference assetRef, IAssetRequestCallback requester, int priority) { UpdateAssetRefPriority(assetRef, requester, priority, true); return LoadAssetInternal(assetRef, requester); }
        public async UniTask<Object> LoadAssetAsync(AssetReference assetRef, IAssetRequestCallback requester, int priority = 0)
        {
            UpdateAssetRefPriority(assetRef, requester, priority, true);
            var loadHandle = await LoadAssetInternalAsync(assetRef, requester);

            if (loadHandle.IsAssetLoaded)
                return loadHandle.LoadedObject;
            else
                return null;
        }

        public void UpdateAssetRefPriority(AssetReference assetRef, IAssetRequestCallback requester, int priority, bool createIfNull = true)
        {
            SimpleAbstractAssetRefLink refLink = ValidateTemplatedAssetLink(assetRef, requester, createIfNull);
            if(refLink != null)
                refLink.SetPriority(requester, priority);
        }

        //Autocaching logic implementation
        protected override bool PostLoadCheck_AssetLoadStillRequired(AssetReference assetRef) { return DoAutoCacheBundlesInMemory || base.PostLoadCheck_AssetLoadStillRequired(assetRef); }
        protected override bool ShouldReleaseAsset(AssetRefLink refLink) { return !DoAutoCacheBundlesInMemory && base.ShouldReleaseAsset(refLink); }

        //TODO: Expand on Debug visuals and options, I like having a full HUD display of whats going on and part of why I'm making this project was to eventually do that with AssetRefLink tracking
        private void OnGUI()
        {
            //replace with proper option select later
            if (!PrintLogs)
                return;

            string log = "Simple Addressable Manager Status:";

            log += $"\n\tHas Instance: {HasInstance}";
            log += $"\n\tInit Manager: {InitStatus.ActiveState}";
            log += $"\n\tCatalogs Updated: {CatalogsStatus.ActiveState}";
            log += $"\n\tDownloaded Asset: {DownloadStatus.ActiveState}";
            log += $"\n\tPreCached Assets: {PreCachingStatus.ActiveState}";

            GUI.Label(new Rect(15, 15, 500, 500), log);
        }
    }
}
