using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Cysharp.Threading.Tasks;

namespace Some.Utility.AddressableManager.Simple
{
    using SettingsClasses;
    
    [CreateAssetMenu(fileName = "AddressableManagerSettings", menuName = "Some/AddressableManager/SimpleSettings")]
    public class SimpleAddressableManagerSettings : SimpleAddressableManagerSettingsBase
    {
        [SerializeField] private BuildConfigSingle m_config = new BuildConfigSingle();
        //TODO: pull this from the AddressableSettings automatically. I want to think about it a bit more on the ideal way to do this
        [Tooltip("Simple check if we even need to bother with the Download sequence")]
        [SerializeField] private bool m_hasRemoteAddressables = true;
        [Tooltip("For if you want to define the URL as part of these Settings instead of the Profile Settings. It it referenced thru \"{Some.Utility.AddressableManager.Paths.BaseLoadURL}\"")]
        //You can find more info on Loading through custom path variables: https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/AddressableAssetsProfiles.html#profile-variable-syntax
        [SerializeField] private string m_remoteAssetLoadPath = "Insert.Your.CDN.URL.Here";
        [Tooltip("What is the Priority Level at which an asset is required to remain in the cache?")]
        [SerializeField] private int m_priorityThresholdRequiredAssets = 100;
        [Tooltip("Define which Addressables should be downloaded as part of the \"Download\" sequence, you can define these Labels in the Addressable Groups menu")]
        [SerializeField] private string[] m_downloadAssetLabels = new string[] { "Download" };
        //TODO: revisit and add settable priority per assetreference/label
        [Tooltip("For specific AssetReferences, if you have Settings Objects that need to be loaded first here is where to reference them. These direct references will be marked as \"Required\" and will not be unloaded")]
        [SerializeField] private AssetReference[] m_precacheAssets = new AssetReference[0];
        [Tooltip("For more general Labels, you can define these in the Addressable Groups menu to simplify preloading sets of assets")]
        [SerializeField] private string[] m_precacheAssetLabels = new string[] { };

        [Header("DEBUG ONLY")]
        [SerializeField] private bool m_printManagerLogs = false;
        [SerializeField] private bool m_debug = false;
        [SerializeField] private int m_debugAsIfMemoryMB = 4 * 1024;

        protected override BuildConfigBase GetConfig() { return m_config; }
        protected override bool HasRemoteAddressables() { return m_hasRemoteAddressables; }
        protected override string RemoteLoadBasePath() { return m_remoteAssetLoadPath; }
        protected override string[] GetDownloadAssetLabels() { return m_downloadAssetLabels; }

        protected override int PriorityThreshold_RequiredAssets => m_priorityThresholdRequiredAssets;
        protected override List<AssetReference> GetPrecacheAssetReferences() { return new List<AssetReference>(m_precacheAssets); }
        protected override List<string> GetPrecacheAssetLabels() { return new List<string>(m_precacheAssetLabels); }

        protected override bool ShouldEnableAutoCaching()
        {
            int memory = UnityEngine.Device.SystemInfo.systemMemorySize;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_debug && m_debugAsIfMemoryMB > 0)
                memory = m_debugAsIfMemoryMB;
#endif

            return GetConfig().DoAutoCacheBundlesInMemory(memory);
        }

        public override bool PrintManagerLogs => m_printManagerLogs;
    }

    //Template class defines functions, Base simplifies inheritance and accessing into a single funnel class, idea being we can swap out Settings for different config setups and possibly avoid having to touch the Manager code
    public abstract class SimpleAddressableManagerSettingsBase : SimpleAddressableManagerSettingsTemplate<SimpleAddressableManager, SimpleAddressableManagerSettingsBase, SimpleAssetRefLink> { }
    public abstract class SimpleAddressableManagerSettingsTemplate<M,S,A> : AddressableManagerSettings<M>
        where M : SimpleAddressableManagerTemplate<M,S,A> 
        where S : SimpleAddressableManagerSettingsTemplate<M,S,A>
        where A : SimpleAbstractAssetRefLink
    {
#region Fields and Variables
        protected abstract BuildConfigBase GetConfig();
        protected abstract bool HasRemoteAddressables();
        protected abstract string RemoteLoadBasePath();
        protected abstract string[] GetDownloadAssetLabels();

        protected abstract int PriorityThreshold_RequiredAssets { get; }
        protected virtual List<AssetReference> GetPrecacheAssetReferences() { return new List<AssetReference>(); }
        protected virtual List<string> GetPrecacheAssetLabels() { return new List<string>(); }
        protected abstract bool ShouldEnableAutoCaching();

        public virtual bool PrintManagerLogs { get => false; }
#endregion

#region Functionality

        public async UniTask<bool> FinalizeInit<T>(T manager) where T : SimpleAddressableManagerTemplate<M, S, A>
        {
            bool hasInit = true;
            BuildConfigBase buildConfig = GetConfig();

            SimpleAddressableManager.CachePriority_Required = PriorityThreshold_RequiredAssets;
            Paths.BaseLoadURL = RemoteLoadBasePath();
            if (HasRemoteAddressables())
            {
                if (buildConfig.AutoUpdateCatalogsOnInit)
                {
                    hasInit = await manager.UpdateCatalogsAsync();
                    if (!hasInit)
                        return false;
                }

                if (buildConfig.AutoDownloadAssetBundlesOnInit)
                {
                    hasInit = await DownloadBundlesAsync(buildConfig);
                    if (!hasInit)
                        return false;
                }
            }
            else
            {
                AddressableManager.CatalogsStatus.OnSkip();
                AddressableManager.DownloadStatus.OnSkip();
            }

            if (buildConfig.AutoPrecacheAssetBundlesOnInit)
            {
                hasInit = await CacheBundlesAsync(buildConfig);
                if (!hasInit)
                    return false;
            }

            manager.DoAutoCacheBundlesInMemory = ShouldEnableAutoCaching();

            return hasInit;
        }

        //All of these are public functions to be called as part of the Game's Init Sequence
        public async UniTask<long> GetNeededDownloadSizeAsync() { return await GetNeededDownloadSizeAsync(GetConfig()); }
        public async UniTask<long> GetNeededDownloadSizeAsync(BuildConfigBase buildConfig)
        {
            return await GetManagerInstance().GetDownloadSizeAsync(GetDownloadAssetLabels());
        }

        public async UniTask<bool> DownloadBundlesAsync(ITrackAssetDownloads tracker = null) { return await DownloadBundlesAsync(GetConfig(), tracker); }
        public async UniTask<bool> DownloadBundlesAsync(BuildConfigBase buildConfig, ITrackAssetDownloads tracker = null)
        {
            return await GetManagerInstance().DownloadBundlesAsync(tracker, GetDownloadAssetLabels());
        }

        public async UniTask<bool> CacheBundlesAsync(ITrackAssetCaching tracker = null) { return await CacheBundlesAsync(GetConfig(), tracker); }
        public async UniTask<bool> CacheBundlesAsync(BuildConfigBase buildConfig, ITrackAssetCaching tracker = null)
        {
            ITrackSequencedAssetCaching seqTracker = tracker != null ? tracker as ITrackSequencedAssetCaching : null;
            bool cachingComplete = true;

            AssetReference[] cacheRefs = GetPrecacheAssetReferences().ToArray();
            string[] cacheBundleLabels = GetPrecacheAssetLabels().ToArray();

            if (seqTracker != null)
                seqTracker.OnAssetCachingSequenceStart((cacheRefs.Length > 0 ? 1 : 0) + (cacheBundleLabels.Length > 0 ? 1 : 0));

            int totalSequences = 0;
            if (cacheRefs.Length > 0)
            {
                if (seqTracker != null)
                    seqTracker.OnStartAssetCachingCycle(cacheRefs);

                cachingComplete = await GetManagerInstance().PrecacheBundlesAsync(tracker, PriorityThreshold_RequiredAssets, cacheRefs);

                if (seqTracker != null)
                    seqTracker.OnCompleteAssetCachingCyle(totalSequences);
                totalSequences++;
            }

            if (!cachingComplete)
            {
                if (seqTracker != null)
                    seqTracker.OnAssetCachingSequenceBreak();
                return false;
            }

            if (cacheBundleLabels.Length > 0)
            {
                if (seqTracker != null)
                    seqTracker.OnStartAssetCachingCycle(cacheBundleLabels);

                cachingComplete = await GetManagerInstance().PrecacheBundlesAsync(tracker, SimpleAddressableManager.CachePriority_Default, cacheBundleLabels);

                if (seqTracker != null)
                    seqTracker.OnCompleteAssetCachingCyle(totalSequences);
                totalSequences++;
            }

            if (!cachingComplete)
            {
                if (seqTracker != null)
                    seqTracker.OnAssetCachingSequenceBreak();

                return false;
            }
            else
            {
                if (seqTracker != null)
                    seqTracker.OnAssetCachingSequenceComplete();

                return true;
            }
        }

#endregion
    }
}

namespace Some.Utility.AddressableManager.Simple.SettingsClasses
{
    #region Config Classes
    [System.Flags]
    public enum BuildFlag
    {
        None = 0,

        Standalone_PC = 1 << 0,
        Standalone_Mac = 1 << 1,
        Standalone_Linux = 1 << 2,

        Mobile_Android = 1 << 3,
        Mobile_iOS = 1 << 4,

        Other_WebGL = 1 << 5,
        Other_WSA = 1 << 6,   //Windows Store App
        Other_tvOS = 1 << 7,
        Other_Stadia = 1 << 8,

        Console_Switch = 1 << 9,
        Console_PS4 = 1 << 10,
        Console_PS5 = 1 << 11,
        Console_XboxOne = 1 << 12,
        Console_XboxSeriesS = 1 << 13,
        Console_XboxSeriesX = 1 << 14,

        Standalone_Any = Standalone_PC | Standalone_Mac | Standalone_Linux,
        Mobile_Any = Mobile_Android | Mobile_iOS,
        Console_LowEnd = Console_Switch | Console_PS4 | Console_XboxOne | Console_XboxSeriesS,
        Console_HighEnd = Console_PS5 | Console_XboxSeriesX,
        Console_Any = Console_LowEnd | Console_HighEnd,

        WindowsOS_Any = Standalone_PC | Other_WSA,
        iOS_Any = Standalone_Mac | Mobile_iOS | Other_tvOS,
        Linux_Any = Standalone_Linux | Mobile_Android | Other_Stadia
    }

    public abstract class BuildConfigBase
    {
        public abstract bool AutoUpdateCatalogsOnInit { get; }
        public abstract bool AutoDownloadAssetBundlesOnInit { get; }
        public abstract bool AutoPrecacheAssetBundlesOnInit { get; }

        public abstract bool DoAutoCacheBundlesInMemory(int memory);
        public abstract int PreCacheBundlesInMemoryOfPriority(int memory);
    }

    [System.Serializable]
    public class BuildConfigSingle : BuildConfigBase
    {
        [Tooltip("Should the AddressableManager check for updated catalogs during the \"Init\" sequence? Internet may be required to perform but it's a miniscule amount. Updating Catalogs will mark downloaded assets that need to be updated as dirty and download the updated version \"later\" (either during the \"Download\" sequence or on 1st request of the asset)")]
        [SerializeField] private bool m_autoUpdateCatalogsOnInit = true;
        [Tooltip("Should the AddressableManager run the \"Download\" sequence as part of the \"Init\" sequence?")]
        [SerializeField] private bool m_autoDownloadAssetBundlesOnInit = true;
        [Tooltip("Should the AddressableManager run the \"Precache\" sequence as part of the \"Init\" sequence? Ideally you wouldn't run the \"Precache\" sequence if you haven't run the \"Download\" sequence as precaching will force a download of any asset not already downloaded")]
        [SerializeField] private bool m_autoPrecacheAssetBundlesOnInit = true;
        [Tooltip("What is the required System Memory to enable \"Autocaching\"? (Leaving assets in the cache even after all active references are destroyed)")]
        [SerializeField] private int m_autoCacheAssetsAtMemoryMB = (int)(5 * 1024);

        public override bool AutoUpdateCatalogsOnInit => m_autoUpdateCatalogsOnInit;
        public override bool AutoDownloadAssetBundlesOnInit => m_autoDownloadAssetBundlesOnInit;
        public override bool AutoPrecacheAssetBundlesOnInit => m_autoPrecacheAssetBundlesOnInit;

        public override bool DoAutoCacheBundlesInMemory(int memory) { return memory >= m_autoCacheAssetsAtMemoryMB; }
        public override int PreCacheBundlesInMemoryOfPriority(int memory) { return -1; }
    }

    public static class Tools
    {
        public static BuildFlag GetBuildFlagForPlatform()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                case RuntimePlatform.WindowsServer:
                    return BuildFlag.Standalone_PC;

                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                case RuntimePlatform.OSXServer:
                    return BuildFlag.Standalone_Mac;

                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                case RuntimePlatform.LinuxServer:
                    return BuildFlag.Standalone_Linux;

                case RuntimePlatform.Android: return BuildFlag.Mobile_Android;
                case RuntimePlatform.IPhonePlayer: return BuildFlag.Mobile_iOS;

                case RuntimePlatform.WebGLPlayer: return BuildFlag.Other_WebGL;
                case RuntimePlatform.tvOS: return BuildFlag.Other_tvOS;
                case RuntimePlatform.Stadia: return BuildFlag.Other_Stadia;

                case RuntimePlatform.WSAPlayerARM:
                case RuntimePlatform.WSAPlayerX64:
                case RuntimePlatform.WSAPlayerX86:
                    return BuildFlag.Other_WSA;

                case RuntimePlatform.Switch: return BuildFlag.Console_Switch;
                case RuntimePlatform.PS4: return BuildFlag.Console_PS4;
                case RuntimePlatform.PS5: return BuildFlag.Console_PS5;
                case RuntimePlatform.XboxOne:
                case RuntimePlatform.GameCoreXboxOne:
                    return BuildFlag.Console_XboxOne;
                case RuntimePlatform.GameCoreXboxSeries:
                    //Dont currently have access to xbox documentation for more direct differentiator, so just going off system specs
                    return UnityEngine.Device.SystemInfo.systemMemorySize < 11 * 1024
                        ? BuildFlag.Console_XboxSeriesS
                        : BuildFlag.Console_XboxSeriesX;

                //Standalone_PC should be an ok fallback if something doesn't hit
                default: return BuildFlag.Standalone_PC;
            }
        }
    }
    #endregion
}