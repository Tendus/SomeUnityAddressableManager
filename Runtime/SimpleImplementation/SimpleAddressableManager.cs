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
        public const int CachePriority_Default = 0;
        public static int CachePriority_Required { get; set; } = 100;

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

        //public wrappers for caching functions
        public sealed override UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, params AssetReference[] assetReferences) { return PrecacheBundlesAsync(progressTracker, CachePriority_Default, assetReferences); }
        public virtual async UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, int priority, params AssetReference[] assetReferences)
        {
            UpdateAssetLinksPriority(this, priority, true, assetReferences);
            bool ret = await base.PrecacheBundlesAsync(progressTracker, assetReferences);

            return ret;
        }

        public sealed override UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, params string[] bundleIds) { return PrecacheBundlesAsync(progressTracker, CachePriority_Default, bundleIds); }
        //need to essentially copy/paste the full precache function from the base class for optimization, at least for now, may revisit and simplify the neccessary functions later
        public virtual async UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, int priority, params string[] bundleIds)
        {
            PreCachingStatus.OnStart();

            AssetCacheProgress progressData = AssetCacheProgress.Create(AssetCacheProgress.State.RetrievingCatalog, bundleIds);

            //Before precaching we need to get a list of all the assetguids to create the asset refs for the AssetRefLinks
            List<string> allGuids = await CacheFunc_RetrieveGUIDsFromBundleIdsAsync(progressData, progressTracker, bundleIds);
            AssetReference[] refParams = new AssetReference[allGuids.Count];
            for (int i = 0; i < allGuids.Count; i++)
                refParams[i] = allGuids[i].GUIDtoAssetReference();

            UpdateAssetLinksPriority(this, priority, true, refParams);

            progressData.OnRetrieveCatalog(progressTracker, allGuids);

            //now we just run the precaching function as normal
            progressData = await CacheFunc_PrecacheAssetReferences(progressData, progressTracker, refParams);

            PreCachingStatus.OnComplete();
            return progressData.state == AssetCacheProgress.State.Completed;
        }

        //public wrappers for the internal Load functions
        public sealed override AssetRefLink LoadAsset(AssetReference assetRef, IAssetRequestCallback requester) { return LoadAsset(assetRef, requester, CachePriority_Default); }
        public AssetRefLink LoadAsset(AssetReference assetRef, IAssetRequestCallback requester, int priority) { UpdateAssetLinkPriority(assetRef, requester, priority, true); return LoadAssetInternal(assetRef, requester); }
        public async UniTask<Object> LoadAssetAsync(AssetReference assetRef, IAssetRequestCallback requester, int priority = CachePriority_Default)
        {
            UpdateAssetLinkPriority(assetRef, requester, priority, true);
            var loadHandle = await LoadAssetInternalAsync(assetRef, requester);

            if (loadHandle.IsAssetLoaded)
                return loadHandle.LoadedObject;
            else
                return null;
        }

        public void UpdateAssetLinkPriority(AssetReference assetRef, IAssetRequest requester, int priority, bool createIfNull = true)
        {
            SimpleAbstractAssetRefLink refLink = ValidateTemplatedAssetLink(assetRef, requester, createIfNull);
            if(refLink != null)
                refLink.SetPriority(requester, priority);
        }

        public void UpdateAssetLinksPriority(IAssetRequest requester, int priority, bool createIfNull, params AssetReference[] assetRefs)
        {
            foreach (AssetReference assetRef in assetRefs)
                UpdateAssetLinkPriority(assetRef, requester, priority, createIfNull);
        }

        //Autocaching logic implementation
        protected override bool PostLoadCheck_AssetLoadStillRequired(AssetReference assetRef) { return DoAutoCacheBundlesInMemory || base.PostLoadCheck_AssetLoadStillRequired(assetRef); }
        protected override bool ShouldReleaseAsset(AssetRefLink refLink) { return !DoAutoCacheBundlesInMemory && base.ShouldReleaseAsset(refLink); }

        //clear away unneccesary caches, and disable autocaching as a default reaction to Memory warnings, would like to expand in the future but this is fine for now
        protected override void HandleLowMemoryWarning()
        {
            DoAutoCacheBundlesInMemory = false;
            List<string> allKeys = new List<string>(LoadedAssets.Keys);
            foreach (string key in allKeys)
            {
                if (LoadedAssets[key].Priority < CachePriority_Required)
                {
                    LoadedAssets[key].DelinkRequestID(this);
                    if (!LoadedAssets[key].HasRequests && LoadedAssets[key].IsAssetLoaded)
                        ReleaseAsset(LoadedAssets[key]);
                }
            }
        }

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
