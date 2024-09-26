using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;

namespace Some.Utility.AddressableManager
{
    #region Enums, Interfaces, and data Structs & Classes
    //Returned by the HandleOnAssetLoaded function to help the Load functions determine the result and how to proceed
    public enum LoadResult
    {
        Cancelled,
        Retry,
        Failed,
        Succeeded
    }

    //Required interface for any class needing to load and cache Addressables as the RequestID is used to track who's using what
    public interface IAssetRequest
    {
        string name { get; }
        int RequestID { get; set; } //Default to 0 or less, should only be set by the manager
    }

    //For classes that can just wait for callback functions instead of awaiting async functions
    public interface IAssetRequestCallback : IAssetRequest
    {
        void OnAssetLoaded(AssetReference assetRef);
        void OnAssetFailToLoad(AssetReference assetRef);
    }

    //This interface along with the struct defined below can be used by External objects to handle progress updates during a "Download" sequence
    public interface ITrackAssetDownloads
    {
        string name { get; }
        //in case we don't want to update every frame
        int UpdateDownloadPercentAfterTimeFrames { get; }

        void UpdateAssetDownloadProgress(AssetDownloadProgress progress);
        //True: Continue Download, False: Cancel Download
        bool OnAssetDownloadError(AssetDownloadProgress progress);
        void OnAssetDownloadFail(AssetDownloadProgress progress);
        //Cycle is for if we want to seperate the downloads, also to allow the Complete to be delayed until after the AsyncHandle is released
        void OnAssetDownloadCycle(AssetDownloadProgress progress);
        void OnAssetDownloadComplete(AssetDownloadProgress progress);
    }

    //Not sure how much I want to expand on how much data is sent, so a struct seems safer for any future scaling
    public struct AssetDownloadProgress
    {
        public enum State
        {
            Inactive,
            GetDownloadSize,
            DownloadingAssets,
            Completed
        }

        public State state;
        public float percentComplete;   //0.0-1.0
        public long currentDownloadSizeBytes;
        public long totalPackageSizeBytes;

        public int consecutiveDownloadFaults;
        public System.Exception lastException;

        public string DownloadedAmountLabel => currentDownloadSizeBytes.BytesToSizeLabel();
        public string TotalToDownloadLabel => totalPackageSizeBytes.BytesToSizeLabel();

        public static AssetDownloadProgress Create(params string[] bundleLabels)
        {
            return new AssetDownloadProgress()
            {
                state = State.GetDownloadSize,
                percentComplete = 0,
                currentDownloadSizeBytes = 0,
                totalPackageSizeBytes = 0,

                consecutiveDownloadFaults = 0
            };
        }
        public void OnGetDownloadSize(long totalSizeBytes, ITrackAssetDownloads listener) { totalPackageSizeBytes = totalSizeBytes; state = State.DownloadingAssets; if (listener != null) listener.UpdateAssetDownloadProgress(this); }
        public void UpdatePercentDownloaded(float percent, long downloadedBytes, ITrackAssetDownloads listener) { percentComplete = percent; currentDownloadSizeBytes = downloadedBytes; if (listener != null) listener.UpdateAssetDownloadProgress(this); }

        public bool OnAssetDownloadError(ITrackAssetDownloads listener, System.Exception exception) { consecutiveDownloadFaults++; lastException = exception; return listener?.OnAssetDownloadError(this) ?? consecutiveDownloadFaults < 3; }
        public void OnAssetDownloadFail(ITrackAssetDownloads listener) { if (listener != null) listener.OnAssetDownloadFail(this); }
        public void OnAssetDownloadCycle(ITrackAssetDownloads listener, bool isFinal) { consecutiveDownloadFaults = 0; if (isFinal) { state = State.Completed; percentComplete = 1; } }
        public void OnAssetDownloadComplete(ITrackAssetDownloads listener) { state = State.Completed; percentComplete = 1; consecutiveDownloadFaults = 0; if (listener != null) listener.OnAssetDownloadComplete(this); }
    }

    //similar to the above interface/struct pair but for caching instead
    public interface ITrackAssetCaching
    {
        string name { get; }
        int UpdateCachePercentAfterTimeFrames { get; }
        bool AutoSkipFailedCaches { get; }

        void UpdateAssetCacheProgress(AssetCacheProgress progress);
        //True: Continue Caching, False: Cancel Caching
        bool OnAssetCacheError(AssetCacheProgress progress);
        void OnAssetCacheFail(AssetCacheProgress progress);
        
        void OnAssetCached(AssetRefLink assetLink, bool isFinal);
        void OnAssetCachingComplete(AssetCacheProgress progress);
    }

    //to be called by settings classes or anything else that wants to sequence a bunch of different precache calls in a row, see SimpleAddressableManagerSettings for an example of when the calls are made and SimpleLoadingScreen for handling those callbacks
    public interface ITrackSequencedAssetCaching : ITrackAssetCaching
    {
        void OnAssetCachingSequenceStart(int totalCycles);
        void OnStartAssetCachingCycle(params string[] bundleLabels);
        void OnStartAssetCachingCycle(params AssetReference[] assetReferences);
        void OnCompleteAssetCachingCyle(int cycle);
        void OnAssetCachingSequenceBreak();
        void OnAssetCachingSequenceComplete();
    }

    public struct AssetCacheProgress
    {
        public enum State
        {
            Inactive,
            RetrievingCatalog,
            CachingAssets,
            Completed
        }

        public State state;
        private int m_loadsCurrent;
        private int m_loadsTotal;

        public int consecutiveLoadFaults;
        public System.Exception lastException;

        public float PercentTotal => m_loadsTotal > 0 ?
            ((float)m_loadsCurrent / (float)m_loadsTotal)
            : 0;

        public static AssetCacheProgress Create(State startingState, params AssetReference[] assetReferences)
        {
            return new AssetCacheProgress()
            {
                state = startingState,
                m_loadsCurrent = 0,
                m_loadsTotal = assetReferences.Length
            };
        }

        public static AssetCacheProgress Create(State startingState, params string[] bundleIds)
        {
            return new AssetCacheProgress()
            {
                state = startingState,
                m_loadsCurrent = 0,
                m_loadsTotal = bundleIds.Length
            };
        }

        public void OnRetrieveCatalog(ITrackAssetCaching listener, List<string> guids) { m_loadsTotal = guids.Count; state = State.CachingAssets; if (listener != null) listener.UpdateAssetCacheProgress(this); }

        public bool OnAssetCacheError(ITrackAssetCaching listener, System.Exception exception) { consecutiveLoadFaults++; lastException = exception; return listener?.OnAssetCacheError(this) ?? consecutiveLoadFaults < 3; }
        public void OnAssetCacheFail(ITrackAssetCaching listener) { if (listener != null) listener.OnAssetCacheFail(this); }

        public void OnAssetCached(ITrackAssetCaching listener, AssetRefLink assetLink, bool isFinal) { if (listener != null) listener.OnAssetCached(assetLink, isFinal); consecutiveLoadFaults = 0; }
        public void OnAssetCachingComplete(ITrackAssetCaching listener) { state = State.Completed; consecutiveLoadFaults = 0; if (listener != null) listener.OnAssetCachingComplete(this); }
    }
    
    /*
     * On Manager Services: the idea behind these classes are providing a simple way to check that anything that could be part of the AddressableManagers' full initialization sequence is initialized
     * It should be noted that this seperation/specification mainly exists for games that use Remote distribution (online) for AssetBundles, and if you aren't you only really need the Init and Precache services
     * For a quick rundown of each individual sequence along with how you as a dev should probably approach them:
     *  Init - The most basic Manager functionality, all needed references should be set on complete. This isn't neccessarily the "Full Initialization" sequence, just when InitAsync finishes
     *  UpdateCatalogs - Have we checked that our AddressableCatalogs are up to date? This can usually be wrapped into Init without any problem but it will require an internet connection (miniscule)
     *  DownloadAssets - Force download of required AssetBundles. Keep in mind for Remote distribution if an asset isn't already in Unity's AssetBundle Storage it has to download the asset as part of the load call. This sequence exists to avoid that (as well as let the user decide when to take that download)
     *  PreCacheAssets - Force caching of Addressables/AssetBundles before they're needed. Allows you to preload needed assets and skip the load wait later
     */
    [System.Flags]
    public enum ManagerService
    {
        Init            = 1 << 0,
        UpdateCatalogs  = 1 << 1,
        DownloadAssets  = 1 << 2,
        PreCacheAssets  = 1 << 3,
        FullInit        = Init | UpdateCatalogs | DownloadAssets | PreCacheAssets
    }

    public class ManagerServiceData
    {
        public enum State { Inactive, Loading, Failed, Complete }

        public State ActiveState { get; protected set; }
        public System.Action AwaitingService;

        public static ManagerServiceData Create() { return new ManagerServiceData() { ActiveState = State.Inactive }; }
        public virtual void OnStart() { ActiveState = State.Loading; }
        public virtual void OnFault() { ActiveState = State.Failed; }
        public virtual void OnComplete() { ActiveState = State.Complete; if (AwaitingService != null) AwaitingService.Invoke(); AwaitingService = null; }
        //May want to expand on this later, mainly needed for the Remote services that some devs may not even bother with
        public virtual void OnSkip() { ActiveState = State.Complete; if (AwaitingService != null) AwaitingService.Invoke(); AwaitingService = null; }

        public virtual bool Inactive => ActiveState == State.Inactive;
        public virtual bool Loading => ActiveState == State.Loading;
        public virtual bool Failed => ActiveState == State.Failed;
        public virtual bool Complete => ActiveState == State.Complete;

        public static implicit operator bool(ManagerServiceData data) { return data.HasInit; }
        public virtual bool HasInit => Complete;

        //purpose for this function is to let us wait for active async functions handling this class to finish if neccessary
        public async UniTask<bool> ReturnEarly(bool returnIfLoaded = true)
        {
            if (Complete)
                return returnIfLoaded;
            else if(Loading)
            {
                while (Loading)
                    await UniTask.DelayFrame(1);
                return returnIfLoaded;
            }

            return false;
        }
    }

    public class ManagerLoadServiceData : ManagerServiceData
    {
        private int m_loadCalls = 0;
        private int m_completedLoads = 0;

        public static new ManagerLoadServiceData Create() { return new ManagerLoadServiceData() { ActiveState = State.Inactive }; }

        void UpdateActiveState(bool fromFailState = false)
        {
            if (m_loadCalls == 0) ActiveState = State.Inactive;
            else if (m_loadCalls > m_completedLoads) ActiveState = State.Loading;
            else ActiveState = fromFailState ? State.Failed : State.Complete;
        }

        public override void OnStart() { m_loadCalls++; UpdateActiveState(); }
        public override void OnFault() { m_completedLoads = Mathf.Min(m_completedLoads + 1, m_loadCalls); UpdateActiveState(true); }
        public override void OnComplete() { m_completedLoads = Mathf.Min(m_completedLoads + 1, m_loadCalls); UpdateActiveState(false); if (Complete) base.OnComplete(); }
        public override void OnSkip() { if(m_loadCalls <= 0) m_loadCalls++; m_completedLoads = m_loadCalls; UpdateActiveState(false); base.OnSkip(); }

        public override bool HasInit => m_completedLoads > 0;
    }

    //The most basic link to the Loaded asset. Abstract in case we want to override how the caching of IAssetRequest is handled.
    //TODO: look into including asset memory size as part of base class (would be helpful for autocaching/priority functionality)
    public abstract class AssetRefLink
    {
        public bool IsAssetLoaded => m_ref != null && m_ref.IsDone && m_ref.Asset != null;
        public bool IsLoadOpValid => m_ref != null && m_ref.IsValid();

        public AssetReference AssetReference => m_ref;
        public string AssetGUID => m_ref != null ? m_ref.AssetGUID : System.Guid.Empty.ToString();
        public Object LoadedObject => m_ref != null ? m_ref.Asset : null;
        public bool Locked => m_locked;
        public abstract bool HasRequests { get; }

        //Not sure how I feel about keeping the direct AssetReference, since it also manages the Load op it can cause an issue with making an AssetRefLink after the asset has loaded. might override with custom reference code later
        protected AssetReference m_ref = null;
        protected bool m_locked = false;

        public virtual void OnCreate(AssetReference assetRef) { m_ref = assetRef; }
        public abstract void LinkRequestID(IAssetRequest requester);
        public abstract void DelinkRequestID(IAssetRequest requester);
        public abstract void DelinkAllRequestIDs();
        public abstract bool HasRequestID(int requestID);

        public virtual AsyncOperationHandle LoadAssetAsync(System.Action<AsyncOperationHandle> OnComplete = null)
        {
            if (m_locked)
            {
                if (OnComplete != null)
                    m_ref.OperationHandle.Completed += OnComplete;

                return m_ref.OperationHandle;
            }

            m_locked = true;
            var loadHandle = m_ref.LoadAssetAsync<Object>();
            loadHandle.Completed += (o) => { m_locked = false; };
            if (OnComplete != null)
                loadHandle.Completed += (o) => { if (OnComplete != null) OnComplete.Invoke(o); };

            return loadHandle;
        }

        public virtual void ReleaseAsset() { DelinkAllRequestIDs(); m_ref.ReleaseAsset(); }
    }
    #endregion

    #region Base Manager Class: handles basic loading functionality and defines all the functions needed to be overridden by child classes
    public abstract class AddressableManager : MonoBehaviour, IAssetRequest
    {
        private static AddressableManager m_baseInstance;
        public static AddressableManager BaseInstance
        {
            get
            {
                if (m_baseInstance == null)
                    m_baseInstance = AddressableManagerSettings.FindSettingsAndCreateSingleton();

                return m_baseInstance;
            }
            protected set { m_baseInstance = value; }
        }

        public static bool HasInstance => m_baseInstance != null;
        public static bool HasInit { get => HasInstance && InitStatus; }
        public static System.Action AwaitingInit = null;

        protected abstract AddressableManagerSettings BaseSettings { get; }
        public abstract bool ApplySettings(AddressableManagerSettings settings);

        //Might want to revisit how to handle "public" access to make it more "internal" or even "protected" at least in changing what can be done to them
        public static ManagerServiceData InitStatus { get; protected set; } = ManagerServiceData.Create();
        public static ManagerServiceData CatalogsStatus { get; protected set; } = ManagerServiceData.Create();
        public static ManagerLoadServiceData DownloadStatus { get; protected set; } = ManagerLoadServiceData.Create();
        public static ManagerLoadServiceData PreCachingStatus { get; protected set; } = ManagerLoadServiceData.Create();

        public static ManagerServiceData GetServiceStatus(ManagerService service)
        {
            switch(service)
            {
                case ManagerService.Init: return InitStatus;
                case ManagerService.UpdateCatalogs: return CatalogsStatus;
                case ManagerService.DownloadAssets: return DownloadStatus;
                case ManagerService.PreCacheAssets: return PreCachingStatus;
            }
            return ManagerServiceData.Create();
        }

        public static bool ServicesComplete(ManagerService serviceFlags)
        {
            if (serviceFlags.HasFlag(ManagerService.Init) && !InitStatus) return false;
            if (serviceFlags.HasFlag(ManagerService.UpdateCatalogs) && !CatalogsStatus) return false;
            if (serviceFlags.HasFlag(ManagerService.DownloadAssets) && !DownloadStatus) return false;
            if (serviceFlags.HasFlag(ManagerService.PreCacheAssets) && !PreCachingStatus) return false;

            return true;
        }

        public static void AwaitServices(ManagerService serviceFlags, System.Action action)
        {
            IgnoreServices(serviceFlags, action);

            if (serviceFlags.HasFlag(ManagerService.Init)) InitStatus.AwaitingService += action;
            if (serviceFlags.HasFlag(ManagerService.UpdateCatalogs)) CatalogsStatus.AwaitingService += action;
            if (serviceFlags.HasFlag(ManagerService.DownloadAssets)) DownloadStatus.AwaitingService += action;
            if (serviceFlags.HasFlag(ManagerService.PreCacheAssets)) PreCachingStatus.AwaitingService += action;
        }

        public static void IgnoreServices(ManagerService serviceFlags, System.Action action)
        {
            if (serviceFlags.HasFlag(ManagerService.Init)) InitStatus.AwaitingService -= action;
            if (serviceFlags.HasFlag(ManagerService.UpdateCatalogs)) CatalogsStatus.AwaitingService -= action;
            if (serviceFlags.HasFlag(ManagerService.DownloadAssets)) DownloadStatus.AwaitingService -= action;
            if (serviceFlags.HasFlag(ManagerService.PreCacheAssets)) PreCachingStatus.AwaitingService -= action;
        }

        protected virtual int RetryLoadAttempts => 3;

        //IAssetRequest
        public int RequestID { get; set; } = 0;

        private int m_nextRequestID = 0;
        public int GetNextRequestID { get { return ++m_nextRequestID; } }

        protected bool PrintLogs { get; set; } = false;

        #region Base AssetManager Services functionality, ideally all 4 services have been "accounted for" before using the AssetManager externally
        //Init - Initialize Unity's Addressable framework, allow Child classes to extend their own initialization logic though the InitSelfAsync function
        public void Init() { if (InitStatus.Inactive) { InitAsync().Forget(); } }
        public async UniTask<bool> InitAsync()
        {
            bool earlyRelease = await InitStatus.ReturnEarly(true);
            if (earlyRelease)
                return InitStatus;
            
            InitStatus.OnStart();

            var handle = Addressables.InitializeAsync(false);
            await handle.Task;
            Addressables.Release(handle);

            if (PrintLogs)
                Debug.Log("Addressable Manager: Base Class Has Init", this);

            bool didInitAll = await InitSelfAsync();

            if (PrintLogs)
                Debug.Log("Addressable Manager: Child Class Has Init: " + didInitAll, this);

            if (!didInitAll)
                InitStatus.OnFault();
            else
                InitStatus.OnComplete();

            return didInitAll;
        }

        protected virtual async UniTask<bool> InitSelfAsync() { return true; }

        //Update Catalogs - gets the most updated catalog from the defined CDN (if you're using a Content Delivery Network, or other online asset storage)
        public virtual async UniTask<bool> UpdateCatalogsAsync()
        {
            bool earlyRelease = await CatalogsStatus.ReturnEarly(true);
            if (earlyRelease)
                return CatalogsStatus;

            CatalogsStatus.OnStart();

            var checkHandle = Addressables.CheckForCatalogUpdates(false);
            await checkHandle.Task;

            List<string> catalogs = new List<string>(checkHandle.Result);
            Addressables.Release(checkHandle);

            if(catalogs.Count > 0)
            {
                var updateHandle = Addressables.UpdateCatalogs(true, catalogs, true);
                await updateHandle.Task;
            }

            if(PrintLogs)
            {
                string log = "AddressableManager: Update Catalogs";
                if (catalogs.Count == 0)
                    log += "\n\tNo Updates Needed";
                else
                    foreach (string s in catalogs)
                        log += $"\n\t{s}";

                Debug.Log(log, this);
            }

            CatalogsStatus.OnComplete();

            return true;
        }

        //Download - GetDownloadSize funtions can be used to check if a download is needed or display just how much needs to be downloaded for the given Labels
        public virtual async UniTask<long> GetDownloadSizeAsync(params string[] bundleLabels)
        {
            var sizeHandle = Addressables.GetDownloadSizeAsync(bundleLabels as IEnumerable);
            await sizeHandle.Task;

            long sizeBytes = sizeHandle.Result;
            Addressables.Release(sizeHandle);

            if(PrintLogs)
            {
                string log = $"AddressableManager: Retrieved Download Size of {sizeBytes.BytesToSizeLabel()}";
                if(bundleLabels.Length > 0)
                    log += "\n\tFor Bundle Labels: " + bundleLabels.ArrayToLabel();

                Debug.Log(log, this);
            }

            return sizeBytes;
        }

        //Download - will download all Assets with the given Labels (Labels can be added/set in the Addressables' "Groups" window)
        public async UniTask<bool> DownloadBundlesAsync(params string[] bundlesLabels) { return await DownloadBundlesAsync(null, bundlesLabels); }
        public virtual async UniTask<bool> DownloadBundlesAsync(ITrackAssetDownloads listener, params string[] bundleLabels)
        {
            DownloadStatus.OnStart();

            AssetDownloadProgress progressData = AssetDownloadProgress.Create(bundleLabels);
            string logLabel = "";
            if(PrintLogs)
            {
                logLabel = $"AddressableManager DownloadOp: {bundleLabels.ArrayToLabel()}{(listener != null ? $" for {listener.name}" : "")}";
                Debug.Log($"{logLabel}: Start Get Download Size", this);
            }

            long downloadSize = await GetDownloadSizeAsync(bundleLabels);
            progressData.OnGetDownloadSize(downloadSize, listener);

            if (PrintLogs)
                Debug.Log($"{logLabel}: Download Size: {progressData.TotalToDownloadLabel}; {(downloadSize <= 0 ? "Download Not Needed" : "Starting Download")}", this);

            if (downloadSize <= 0)
                return downloadSize == 0;
            
            bool continueDownload = true;
            do
            {
                var downloadHandle = Addressables.DownloadDependenciesAsync(bundleLabels as IEnumerable, Addressables.MergeMode.Union);

                int updateDelayFrames = listener?.UpdateDownloadPercentAfterTimeFrames ?? 15;
                int framesTilUpdate = updateDelayFrames; 
                while(!downloadHandle.IsDone)
                {
                    await UniTask.DelayFrame(1);
                    framesTilUpdate--;

                    if (framesTilUpdate <= 0)
                    {
                        var downloadStatus = downloadHandle.GetDownloadStatus();
                        progressData.UpdatePercentDownloaded(downloadStatus.Percent, downloadStatus.DownloadedBytes, listener);

                        framesTilUpdate = updateDelayFrames;
                        if (PrintLogs)
                            Debug.Log($"{logLabel}: Downloaded : {progressData.currentDownloadSizeBytes.BytesToSizeLabel()} [{progressData.percentComplete * 100}%]", this);
                    }
                }

                switch(downloadHandle.Status)
                {
                    case AsyncOperationStatus.Succeeded:
                        progressData.OnAssetDownloadCycle(listener, true);
                        continueDownload = progressData.state != AssetDownloadProgress.State.Completed;
                        if (PrintLogs)
                            Debug.Log($"{logLabel}: Finish Download Cycle", this);
                        break;
                    case AsyncOperationStatus.Failed:
                        continueDownload = progressData.OnAssetDownloadError(listener, downloadHandle.OperationException);
                        if (PrintLogs)
                            Debug.LogError($"{logLabel}: Download Error : {downloadHandle.OperationException.Message}", this);
                        break;
                }

                Addressables.Release(downloadHandle);
            }
            while (continueDownload);

            if (progressData.state != AssetDownloadProgress.State.Completed)
            {
                progressData.OnAssetDownloadFail(listener);
                if (PrintLogs)
                    Debug.LogError($"{logLabel}: Failed to Download Bundles, Check previous logs for exception", this);

                DownloadStatus.OnFault();
                return false;
            }
            else
            {
                progressData.OnAssetDownloadComplete(listener);
                if (PrintLogs)
                    Debug.Log($"{logLabel}: Download Complete!", this);

                DownloadStatus.OnComplete();
                return true;
            }
        }

        //TODO: maybe seperate into Async Load and Callback Load Caching Functions? Might just leave that to Children classes to handle for now. Download functions might want callback functions as well
        //Precaching - Cache bundles into memory before any external load calls to cut out the load wait when the assets are needed
        public async UniTask<bool> PrecacheBundlesAsync(params AssetReference[] assetReferences) { return await PrecacheBundlesAsync(null, assetReferences); }
        public virtual async UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, params AssetReference[] assetReferences)
        {
            PreCachingStatus.OnStart();

            AssetCacheProgress progressData = AssetCacheProgress.Create(AssetCacheProgress.State.CachingAssets, assetReferences);
            progressData = await CacheFunc_PrecacheAssetReferences(progressData, progressTracker, assetReferences);

            PreCachingStatus.OnComplete();
            return progressData.state == AssetCacheProgress.State.Completed;
        }
        
        public async UniTask<bool> PrecacheBundlesAsync(params string[] bundleIds) { return await PrecacheBundlesAsync(null, bundleIds); }
        public virtual async UniTask<bool> PrecacheBundlesAsync(ITrackAssetCaching progressTracker, params string[] bundleIds)
        {
            PreCachingStatus.OnStart();

            AssetCacheProgress progressData = AssetCacheProgress.Create(AssetCacheProgress.State.RetrievingCatalog, bundleIds);

            //Before precaching we need to get a list of all the assetguids to create the asset refs for the AssetRefLinks
            List<string> allGuids = await CacheFunc_RetrieveGUIDsFromBundleIdsAsync(progressData, progressTracker, bundleIds);
            AssetReference[] refParams = new AssetReference[allGuids.Count];
            for (int i = 0; i < allGuids.Count; i++)
                refParams[i] = allGuids[i].GUIDtoAssetReference();

            progressData.OnRetrieveCatalog(progressTracker, allGuids);

            //now we just run the precaching function as normal
            progressData = await CacheFunc_PrecacheAssetReferences(progressData, progressTracker, refParams);

            PreCachingStatus.OnComplete();
            return progressData.state == AssetCacheProgress.State.Completed;
        }
        
        protected virtual async UniTask<List<string>> CacheFunc_RetrieveGUIDsFromBundleIdsAsync(AssetCacheProgress progressData, ITrackAssetCaching progressTracker, params string[] bundleIds)
        {
            string logLabel = "";
            if (PrintLogs)
            {
                logLabel = $"AddressableManager RetrieveGUIDsOp: {bundleIds.ArrayToLabel()}{(progressTracker != null ? $" for {progressTracker.name}" : "")}";
                Debug.Log($"{logLabel}: Load Resource Locations", this);
            }

            List<string> allGuids = new List<string>();

            //Get LocationData for the BundleIDs, will give us the name of the asset for later
            var getHandle = Addressables.LoadResourceLocationsAsync(bundleIds as IEnumerable, Addressables.MergeMode.Union);
            await getHandle.Task;
            
            List<string> allInternalIDs = new List<string>();
            foreach (var location in getHandle.Result)
                allInternalIDs.Add(location.InternalId);

            if (PrintLogs)
            {
                string listLog = "";
                for (int i = 0; i < getHandle.Result.Count; i++)
                    listLog += $"\n\t{getHandle.Result[i].InternalId} => {getHandle.Result[i].PrimaryKey}";
                Debug.Log($"{logLabel}: Found {getHandle.Result.Count} ResourceLocations for {bundleIds.ArrayToLabel()}:{listLog}", this);
            }

#if UNITY_EDITOR
            //If we're in Editor and doing the AssetDatabase loading for addressables we aren't going to get anything from the LocationData search below
            if(UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.SettingsExists 
                && UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings.ActivePlayModeDataBuilderIndex == 0)
            {
                for (int i = 0; i < allInternalIDs.Count; i++)
                {
                    var guid = UnityEditor.AssetDatabase.GUIDFromAssetPath(allInternalIDs[i]);
                    if(!guid.Empty())
                        allGuids.Add(guid.ToString());
                }

                if (PrintLogs)
                    Debug.Log($"{logLabel}: Converted {getHandle.Result.Count} ResourceLocations to {allGuids.Count} GUIDs: {allGuids.ToArray().ArrayToLabel()};\n\tWarning: this was an AssetDatabase load, consider using the \"Groups\" or \"Existing Build\" Play Mode Script options", this);

                return allGuids;
            }
#endif

            //iterate through all LocationDatas to find the ones that 1) point to the same assets we want and 2) are GUIDs
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                ResourceLocationMap map = locator as ResourceLocationMap;
                if (map == null)
                    continue;
                foreach (KeyValuePair<object, IList<IResourceLocation>> keyToLocs in map.Locations)
                {
                    foreach (IResourceLocation loc in keyToLocs.Value)
                    {
                        if (PrintLogs)
                            Debug.Log($"Locator: {loc.InternalId} => {loc.PrimaryKey}");
                        if (!allInternalIDs.Contains(loc.InternalId))
                            continue;

                        string key = keyToLocs.Key.ToString();
                        if (System.Guid.TryParse(key, out System.Guid result) && !allGuids.Contains(loc.PrimaryKey))
                        {
                            allGuids.Add(key);
                            break;
                        }
                    }
                }
            }

            if (PrintLogs)
                Debug.Log($"{logLabel}: Converted {getHandle.Result.Count} ResourceLocations to {allGuids.Count} GUIDs: {allGuids.ToArray().ArrayToLabel()}", this);

            return allGuids;
        }

        //Essentially a copy of the LoadAssets function, made into it's own function so we can bypass any autocaching if needed and/or replace the default LoadFault handling with a passed ITrackAssetCaching.
        protected virtual async UniTask<AssetCacheProgress> CacheFunc_PrecacheAssetReferences(AssetCacheProgress progressData, ITrackAssetCaching progressTracker, params AssetReference[] assetReferences)
        {
            string logLabel = "";
            if (PrintLogs)
            {
                logLabel = $"AddressableManager PreCacheOp: {assetReferences.Length}AssetRefs{(progressTracker != null ? $" for {progressTracker.name}" : "")}";
                Debug.Log($"{logLabel}: Start Caching", this);
            }

            if (assetReferences != null)
            {
                bool continueCachingAll = true;
                for (int i = 0; i < assetReferences.Length; i++)
                {
                    AssetReference assetReference = assetReferences[i];
                    AssetRefLink link = ValidateAssetLink(assetReference, this);
                    while (link.Locked)
                        await UniTask.DelayFrame(1);

                    if (!link.IsAssetLoaded && !link.IsLoadOpValid)
                    {
                        bool continueCachingSingle = true;
                        do
                        {
                            AsyncOperationHandle loadHandle = link.LoadAssetAsync();
                            await loadHandle.Task;
                            LoadResult result = HandleOnAssetLoaded(loadHandle, assetReferences[i], progressData.consecutiveLoadFaults, true);

                            if (PrintLogs)
                                Debug.Log($"{logLabel}: Load: {assetReference.SubObjectName}[{assetReference.AssetGUID}]; Result: {result}");

                            switch (result)
                            {
                                case LoadResult.Cancelled:
                                case LoadResult.Failed:
                                    continueCachingSingle = progressData.OnAssetCacheError(progressTracker, loadHandle.OperationException);
                                    break;

                                case LoadResult.Succeeded:
                                    continueCachingSingle = false;
                                    break;
                            }
                        } while (continueCachingSingle);
                    }

                    if (link.IsAssetLoaded)
                        progressData.OnAssetCached(progressTracker, link, i >= assetReferences.Length - 1);
                    else if (!(progressTracker?.AutoSkipFailedCaches ?? true))
                        continueCachingAll = false;

                    if (!continueCachingAll)
                    {
                        PreCachingStatus.OnFault();
                        progressData.OnAssetCacheFail(progressTracker);
                        return progressData;
                    }
                }
            }

            if (PrintLogs)
                Debug.Log($"{logLabel}: Caching Complete!");

            progressData.OnAssetCachingComplete(progressTracker);
            return progressData;
        }
        #endregion

        #region Internal Loading functions - Child classes can use wrappers to handle any caching, pre/post load, priority-setting functionality
        public virtual AssetRefLink LoadAsset(AssetReference assetRef, IAssetRequestCallback requester) { return LoadAssetInternal(assetRef, requester); }
        protected virtual AssetRefLink LoadAssetInternal(AssetReference assetRef, IAssetRequestCallback requester)
        {
            AssetRefLink link = ValidateAssetLink(assetRef, requester);
            if(link.IsAssetLoaded)
            {
                requester.OnAssetLoaded(link.AssetReference);
                return link;
            }

            RegisterRequesterForCallback(assetRef, requester);
            if(!link.Locked && !link.IsLoadOpValid)
                link.LoadAssetAsync((h) => HandleOnAssetLoaded(h, assetRef, 0));

            return link;
        }

        public virtual UniTask<AssetRefLink> LoadAssetAsync(AssetReference assetRef, IAssetRequest requester, bool forceCache = false) { return LoadAssetInternalAsync(assetRef, requester, forceCache); }
        protected async UniTask<AssetRefLink> LoadAssetInternalAsync(AssetReference assetRef, IAssetRequest requester, bool forceCache = false)
        {
            AssetRefLink link = ValidateAssetLink(assetRef, requester);
            while (link.Locked)
                await UniTask.DelayFrame(1);

            if (link.IsAssetLoaded)
                return link;

            if(!link.IsLoadOpValid)
            {
                int loadAttempts = 0;
                do
                {
                    AsyncOperationHandle loadHandle = link.LoadAssetAsync();
                    await loadHandle.Task;
                    LoadResult result = HandleOnAssetLoaded(loadHandle, assetRef, loadAttempts, forceCache);
                    switch (result)
                    {
                        case LoadResult.Cancelled:
                        case LoadResult.Failed:
                            return null;

                        case LoadResult.Succeeded:
                            return link;
                    }
                } while (loadAttempts < RetryLoadAttempts);
            }

            return null;
        }

        protected virtual void Callback_OnAssetRequestLoad(AsyncOperationHandle handle, AssetReference assetRef, int attempts)
        {
            LoadResult result = HandleOnAssetLoaded(handle, assetRef, attempts);
            if(result == LoadResult.Retry)
            {
                AssetRefLink link = ValidateAssetLink(assetRef, null);
                link.LoadAssetAsync((h) => Callback_OnAssetRequestLoad(h, assetRef, attempts++));
            }
        }

        protected virtual LoadResult HandleOnAssetLoaded(AsyncOperationHandle loadHandle, AssetReference assetRef, int attempts, bool forceCache = false)
        {
            //fail states
            if(loadHandle.Status != AsyncOperationStatus.Succeeded || assetRef.Asset == null)
            {
                if (!PostLoadCheck_AssetLoadStillRequired(assetRef))
                {
                    if (PrintLogs)
                        Debug.Log($"Cancelled Load of AssetRef {assetRef.SubObjectName}[{assetRef.AssetGUID}]");

                    RunCallbacks_AssetFailedToLoad(assetRef);
                    ReleaseAsset(assetRef);

                    return LoadResult.Cancelled;
                }
                else if (attempts < RetryLoadAttempts)
                    return LoadResult.Retry;
                else
                {
                    if (PrintLogs)
                        Debug.Log($"Failed to Load AssetRef {assetRef.SubObjectName}[{assetRef.AssetGUID}]");

                    RunCallbacks_AssetFailedToLoad(assetRef);

                    return LoadResult.Failed;
                }
            }

            RunCallbacks_AssetLoaded(assetRef);

            if(!forceCache && !PostLoadCheck_AssetLoadStillRequired(assetRef))
            {
                ReleaseAsset(assetRef);
                return LoadResult.Cancelled;
            }

            return LoadResult.Succeeded;
        }

        public virtual void UnloadAsset(AssetReference assetRef, IAssetRequest forRequester)
        {
            if (string.IsNullOrEmpty(assetRef.AssetGUID))
                return;

            AssetRefLink link = ValidateAssetLink(assetRef, forRequester, false);
            if (link != null)
                UnloadAsset(link, forRequester);
            else
                ReleaseAsset(assetRef);
        }

        public virtual void UnloadAsset(AssetRefLink assetRefLink, IAssetRequest forRequester)
        {
            assetRefLink.DelinkRequestID(forRequester);
            if (ShouldReleaseAsset(assetRefLink))
                assetRefLink.ReleaseAsset();
        }


        //Functions to be overridden and have their functionality filled in by Child classes below, keep in mind many of the Public functions exist as simple wrappers for External calls

        //Should Try and pull an AssetRefLink object from our active cache, and if one doesn't exist in that cache create a new one if "createIfNull" is true
        public abstract AssetRefLink ValidateAssetLink(AssetReference assetRef, IAssetRequest forRequester, bool createIfNull = true);

        //Handle the Registration/Deregistration for Requesters needing to be called back
        public abstract void RegisterRequesterForCallback(AssetReference assetRef, IAssetRequestCallback requester);
        public abstract void DeregisterRequesterForCallback(AssetReference assetRef, IAssetRequestCallback requester);
        public abstract void DeregisterAllRequestsForCallback(AssetReference assetRef);

        //Called by the HandleOnAssetLoaded function above to let everything that registered via the RegisterForCallback function handle the Load/FailToLoad
        protected abstract void RunCallbacks_AssetLoaded(AssetReference assetRef);
        protected abstract void RunCallbacks_AssetFailedToLoad(AssetReference assetRef);

        /*
         * Used by the HandleOnAssetLoaded function to check if the Load should be canceled/discarded. 
         * Important override in Autocaching. To explain:
         *  Template child class (below): returns "true" only if 1) the AssetRefLink is valid AND 2) the AssetRefLink has requesters. This setup DISABLES auto caching, so:
         *  Simple child class: overrides that and returns "true" if 1) Autocaching is enabled (via a static bool) OR 2) the base (the Template child in this case) returns "true"
         */
        protected abstract bool PostLoadCheck_AssetLoadStillRequired(AssetReference assetRef);

        //Should not act as a forced Unload, but rather remove the Requester from everything it requested a Load of. "ShouldReleaseAsset" is used to determine if the given asset should be Unloaded from the Cache itself
        protected abstract void UnloadAllAssets(IAssetRequest forRequester);

        //Separate from "Unload", "Release" functions should be overridden to remove from caching. To some degree these are the "True" unload functions
        //Similar to PostLoadCheck_AssetStillRequired, this is a very important override for Autocaching and it has a very similar setup in the Template and Simple class implementations
        protected virtual bool ShouldReleaseAsset(AssetRefLink refLink) { return !refLink.HasRequests; }
        protected abstract void ReleaseAsset(AssetReference assetRef);
        protected abstract void ReleaseAllAssets();
        #endregion
    }
    #endregion

    #region Template Manager Class: handles basic caching functionality, child classes are responsible for maintaining the logic of what gets cached and when to stop caching
    public abstract class AddressableManager<M, S, A> : AddressableManager
        where M : AddressableManager
        where S : AddressableManagerSettings 
        where A : AssetRefLink
    {
        protected static Dictionary<string, A> LoadedAssets = new Dictionary<string, A>();
        protected static Dictionary<string, List<IAssetRequestCallback>> AwaitingCallback = new Dictionary<string, List<IAssetRequestCallback>>();

        public static M Instance => BaseInstance as M;

        [SerializeField] private S m_settings;
        public S Settings { get; protected set; }
        protected override AddressableManagerSettings BaseSettings => Settings;
        public override bool ApplySettings(AddressableManagerSettings settings)
        {
            if(!(settings is S))
            {
                if(PrintLogs)
                    Debug.LogErrorFormat("AddressableManagerSettings of Type({0}) doesn't match the Manager's given Settings Type({1})", settings.GetType(), typeof(S));
                return false;
            }

            Settings = settings as S;
            return true;
        }

        //Probably shouldn't override this, the Init function should handle actual initialization logic, this is just the most basic/necessary setup logic/references for the Init to run smoothly
        protected virtual void Awake()
        {
            if(HasInstance)
            {
                Destroy(gameObject);
                return;
            }

            BaseInstance = this;
            DontDestroyOnLoad(gameObject);
            RequestID = GetNextRequestID;

            if (m_settings == null)
                m_settings = AddressableManagerSettings.FindSettings<S>();

            if (m_settings != null)
                ApplySettings(m_settings);

            Application.lowMemory += HandleLowMemoryWarning;
        }

        protected virtual void OnDestroy() { if (HasInstance && BaseInstance == this) { ReleaseAllAssets(); Application.lowMemory -= HandleLowMemoryWarning; } }

        //sealed because child classes should only be overriding the templated version, this base function is just a glorified wrapper to make it easier on the parent class
        public sealed override AssetRefLink ValidateAssetLink(AssetReference assetRef, IAssetRequest requester, bool createIfNull = true) { return ValidateTemplatedAssetLink(assetRef, requester, createIfNull); }
        public virtual A ValidateTemplatedAssetLink(AssetReference assetRef, IAssetRequest requester, bool createIfNull = true)
        {
            A link = null;

            if (LoadedAssets.ContainsKey(assetRef.AssetGUID)) link = LoadedAssets[assetRef.AssetGUID];
            else if (createIfNull)
            {
                link = CreateNewAssetLink(assetRef);
                link.OnCreate(assetRef);
                LoadedAssets.Add(assetRef.AssetGUID, link);
            }

            if (requester != null)
            {
                if(requester.RequestID <= 0)
                    requester.RequestID = GetNextRequestID;

                if (PrintLogs)
                    Debug.Log($"{requester.name} Validating AssetRefLink to Load: {assetRef.AssetGUID}; RequestID: {requester.RequestID}", this);

                link.LinkRequestID(requester);
            }

            return link;
        }

        protected abstract A CreateNewAssetLink(AssetReference assetRef);

        public override void RegisterRequesterForCallback(AssetReference assetRef, IAssetRequestCallback requester)
        {
            if (!AwaitingCallback.ContainsKey(assetRef.AssetGUID))
                AwaitingCallback.Add(assetRef.AssetGUID, new List<IAssetRequestCallback>() { requester });
            else if (!AwaitingCallback[assetRef.AssetGUID].Contains(requester))
                AwaitingCallback[assetRef.AssetGUID].Add(requester);
        }

        public override void DeregisterRequesterForCallback(AssetReference assetRef, IAssetRequestCallback requester)
        {
            if (AwaitingCallback.ContainsKey(assetRef.AssetGUID))
                AwaitingCallback[assetRef.AssetGUID].Remove(requester);
        }

        public override void DeregisterAllRequestsForCallback(AssetReference assetRef) { AwaitingCallback.Remove(assetRef.AssetGUID); }

        protected override void RunCallbacks_AssetLoaded(AssetReference assetRef)
        {
            if(AwaitingCallback.ContainsKey(assetRef.AssetGUID))
                for (int i = 0; i < AwaitingCallback[assetRef.AssetGUID].Count; i++)
                    AwaitingCallback[assetRef.AssetGUID][i].OnAssetLoaded(assetRef);

            DeregisterAllRequestsForCallback(assetRef);
        }

        protected override void RunCallbacks_AssetFailedToLoad(AssetReference assetRef)
        {
            if (AwaitingCallback.ContainsKey(assetRef.AssetGUID))
                for (int i = 0; i < AwaitingCallback[assetRef.AssetGUID].Count; i++)
                    AwaitingCallback[assetRef.AssetGUID][i].OnAssetFailToLoad(assetRef);

            DeregisterAllRequestsForCallback(assetRef);
        }

        protected override bool PostLoadCheck_AssetLoadStillRequired(AssetReference assetRef)
        {
            A link = ValidateTemplatedAssetLink(assetRef, null, false);
            return link != null && link.HasRequests;
        }

        protected override void UnloadAllAssets(IAssetRequest forRequester)
        {
            List<string> allKeys = new List<string>(LoadedAssets.Keys);
            foreach (string key in allKeys)
                if (LoadedAssets[key].HasRequestID(RequestID))
                    UnloadAsset(LoadedAssets[key], forRequester);
        }

        protected override void ReleaseAllAssets()
        {
            List<string> allKeys = new List<string>(LoadedAssets.Keys);
            foreach (string key in allKeys)
                LoadedAssets[key].ReleaseAsset();

            LoadedAssets.Clear();
        }

        //sealed to put all functionality into the 1 function
        protected sealed override void ReleaseAsset(AssetReference assetRef) { ReleaseAsset(assetRef.AssetGUID, ValidateTemplatedAssetLink(assetRef, null, false)); }
        protected void ReleaseAsset(A assetLink) { ReleaseAsset(assetLink.AssetGUID, assetLink); }
        protected virtual void ReleaseAsset(string guid, A assetLink)
        {
            if (assetLink != null)
                assetLink.ReleaseAsset();

            LoadedAssets.Remove(guid);
        }

        protected abstract void HandleLowMemoryWarning();
    }
    #endregion

    public static class Paths
    {
        public static string BaseLoadURL { get; set; } = "";
    }

    public static class Tools
    {
        public static AssetReference GUIDtoAssetReference(this string guid) { return new AssetReference(guid); }

        public static string BytesToSizeLabel(this long sizeBytes, bool includeDecimal = true)
        {
            if (sizeBytes < 1024)
                return $"{sizeBytes}{(includeDecimal ? ".0" : "")}b";
            else if (sizeBytes < 1024 * 1024)
                return $"{(includeDecimal ? ((double)sizeBytes / 1024).ToString(".0") : (sizeBytes / 1024).ToString())}kb";
            else if (sizeBytes < 1024 * 1024 * 1024)
                return $"{(includeDecimal ? ((double)sizeBytes / (1024 * 1024)).ToString(".0") : (sizeBytes / (1024 * 1024)).ToString())}mb";
            else
                return $"{(includeDecimal ? ((double)sizeBytes / (1024 * 1024 * 1024)).ToString(".0") : (sizeBytes / (1024 * 1024 * 1024)).ToString())}gb";
        }

        public static string ArrayToLabel(this string[] stringArray)
        {
            if (stringArray == null)
                return "";
            else if (stringArray.Length == 0)
                return "EMPTY";
            else
            {
                string label = "[";
                for (int i = 0; i < stringArray.Length; i++)
                    label += i > 0 ? $", {stringArray[i]}" : stringArray[i];
                label += "]";

                return label;
            }
        }
    }
}
