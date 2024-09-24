using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Some.Utility.AddressableManager.Simple
{
    using SettingsClasses;
    using UnityEngine.AddressableAssets;

    [CreateAssetMenu(fileName = "AddressableManagerSettings", menuName = "Some/AddressableManager/DynamicSettings")]
    public class SimpleAddressableManagerSettings_DynamicConfig : SimpleAddressableManagerSettingsBase
    {
        [Tooltip("For setting up simple config setups. You might want these for console builds since the architecture is more \"set\"")]
        [SerializeField] private BuildConfigSet[] m_setConfigs;
        [Tooltip("For setting up configs that are chosen based on the current System Memory")]
        [SerializeField] private BuildConfigDynamic[] m_dynamicConfigs;
        [Tooltip("Simple check if we even need to bother with the Download sequence")]
        [SerializeField] private bool m_hasRemoteAddressables = true;
        [Tooltip("For if you want to define the URL as part of these Settings instead of the Profile Settings. It it referenced thru \"{Some.Utility.AddressableManager.Paths.BaseLoadURL}\"")]
        [SerializeField] private string m_remoteAssetLoadPath = "Insert.Your.CDN.URL.Here";
        [Tooltip("Define which Addressables should be downloaded as part of the \"Download\" sequence, you can define these Labels in the Addressable Groups menu")]
        [SerializeField] private string[] m_downloadAssetLabels = new string[0];
        [Tooltip("For defining which Assets should be precached. A Priority score is taken from the BuildConfig used for the user's system and anything with a higher priority than that will be preloaded during the \"Precache\" sequence")]
        [SerializeField] private AssetLoadConfig[] m_precacheAssetGroups = new AssetLoadConfig[0];

        [Header("DEBUG ONLY")]
        [SerializeField] private bool m_printManagerLogs = false;
        [SerializeField] private bool m_debug = false;
        [SerializeField] private BuildFlag m_debugConfig = BuildFlag.None;
        [SerializeField] private int m_debugAsIfMemoryMB = 4 * 1024;

        private BuildFlag m_cachedFlag = BuildFlag.None;
        private BuildFlag BuildConfigPlatform
        {
            get
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if (m_debug && m_debugConfig != BuildFlag.None)
                    return m_debugConfig;
                else
                    return SettingsClasses.Tools.GetBuildFlagForPlatform();
#else
                if (m_cachedFlag == BuildFlag.None)
                    m_cachedFlag = SettingsClasses.Tools.GetBuildFlagForPlatform();
                return m_cachedFlag;
#endif
            }
        }

        protected override BuildConfigBase GetConfig()
        {
            BuildFlag buildFlag = BuildConfigPlatform;
            for (int i = 0; i < m_setConfigs.Length; i++) if (m_setConfigs[i].HasBuildFlag(buildFlag)) return m_setConfigs[i];
            for (int i = 0; i < m_dynamicConfigs.Length; i++) if (m_dynamicConfigs[i].HasBuildFlag(buildFlag)) return m_dynamicConfigs[i];

            Debug.LogError($"{name}: Attempted To Find a BuildConfig compatible with BuildPlatform[{buildFlag}] but failed to do so, returning an empty config");

            return new BuildConfigSet();
        }

        protected override bool HasRemoteAddressables() { return m_hasRemoteAddressables; }
        protected override string RemoteLoadBasePath() { return m_remoteAssetLoadPath; }
        protected override string[] GetDownloadAssetLabels() { return m_downloadAssetLabels; }

        protected override List<AssetReference> GetPrecacheAssetReferences()
        {
            List<AssetReference> assetRefs = new List<AssetReference>();

            int memory = UnityEngine.Device.SystemInfo.systemMemorySize;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_debug && m_debugAsIfMemoryMB > 0)
                memory = m_debugAsIfMemoryMB;
#endif

            int priority = GetConfig().PreCacheBundlesInMemoryOfPriority(memory);
            foreach (AssetLoadConfig config in m_precacheAssetGroups)
                if (config.IsValid(priority))
                {
                    foreach (AssetReference s in config.AssetReferencesToLoad)
                    {
                        bool isDupe = false;
                        foreach(AssetReference e in assetRefs)
                            if(e.AssetGUID == s.AssetGUID)
                            {
                                isDupe = true;
                                break;
                            }

                        if (!isDupe)
                            assetRefs.Add(s);
                    }
                }

            return assetRefs;
        }

        protected override List<string> GetPrecacheAssetLabels()
        {
            List<string> assetLabels = new List<string>();

            int memory = UnityEngine.Device.SystemInfo.systemMemorySize;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (m_debug && m_debugAsIfMemoryMB > 0)
                memory = m_debugAsIfMemoryMB;
#endif

            int priority = GetConfig().PreCacheBundlesInMemoryOfPriority(memory);
            foreach (AssetLoadConfig config in m_precacheAssetGroups)
                if (config.IsValid(priority))
                {
                    foreach (string s in config.AssetBundlesToLoad)
                        if (!assetLabels.Contains(s))
                            assetLabels.Add(s);
                }

            return assetLabels;
        }

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
}

namespace Some.Utility.AddressableManager.Simple.SettingsClasses
{
    public abstract class BuildConfigFlagged : BuildConfigBase
    {
        [Tooltip("Flags enum for which platforms this Config should be valid for")]
        [SerializeField] private BuildFlag m_buildFlags;
        [Tooltip("Should the AddressableManager check for updated catalogs during the \"Init\" sequence? Internet may be required to perform but it's a miniscule amount. Updating Catalogs will mark downloaded assets that need to be updated as dirty and download the updated version \"later\" (either during the \"Download\" sequence or on 1st request of the asset)")]
        [SerializeField] private bool m_autoUpdateCatalogsOnInit = true;
        [Tooltip("Should the AddressableManager run the \"Download\" sequence as part of the \"Init\" sequence?")]
        [SerializeField] private bool m_autoDownloadAssetBundlesOnInit = true;
        [Tooltip("Should the AddressableManager run the \"Precache\" sequence as part of the \"Init\" sequence? Ideally you wouldn't run the \"Precache\" sequence if you haven't run the \"Download\" sequence as precaching will force a download of any asset not already downloaded")]
        [SerializeField] private bool m_autoPrecacheAssetBundlesOnInit = true;

        public bool HasBuildFlag(BuildFlag flag) { return ((int)flag & (int)m_buildFlags) != 0; }
        public override bool AutoUpdateCatalogsOnInit => m_autoUpdateCatalogsOnInit;
        public override bool AutoDownloadAssetBundlesOnInit => m_autoDownloadAssetBundlesOnInit;
        public override bool AutoPrecacheAssetBundlesOnInit => m_autoPrecacheAssetBundlesOnInit;

        public bool ValidConfig(BuildFlag activeBuildFlag) { return HasBuildFlag(activeBuildFlag); }
    }

    [System.Serializable]
    public class BuildConfigSet : BuildConfigFlagged
    {
        [Tooltip("Should the AddressableManager enable \"Autocaching\"? (Leaving assets in the cache even after all active references are destroyed)")]
        [SerializeField] private bool m_autoCacheAssets = true;
        [Tooltip("What is the Priority score threshold used to determine which Assets we precache? ")]
        [SerializeField] private int m_precacheAssetsOfPriority = 0;

        public override bool DoAutoCacheBundlesInMemory(int memory) { return m_autoCacheAssets; }
        public override int PreCacheBundlesInMemoryOfPriority(int memory) { return m_precacheAssetsOfPriority; }
    }

    [System.Serializable]
    public class BuildConfigDynamic : BuildConfigFlagged
    {
        [Tooltip("What is the required System Memory to enable \"Autocaching\"? (Leaving assets in the cache even after all active references are destroyed)")]
        [SerializeField] private int m_minMemoryForAutoCachingMB = (int)(4.5 * 1024);
        [Tooltip("Potential Priority scores, returning the lowest possible defined Priority score possible based on the System Memory")]
        [SerializeField] private MemoryPriorityLimit[] m_memoryPriorityLimits;

        public override bool DoAutoCacheBundlesInMemory(int memory) { return memory >= m_minMemoryForAutoCachingMB; }
        public override int PreCacheBundlesInMemoryOfPriority(int memory)
        {
            int minPriority = int.MaxValue;
            if (m_memoryPriorityLimits != null)
                for (int i = 0; i < m_memoryPriorityLimits.Length; i++)
                    if (memory >= m_memoryPriorityLimits[i].m_minMemoryNeededMB)
                        minPriority = Mathf.Min(minPriority, m_memoryPriorityLimits[i].m_preloadAssetsOfPriority);

            return minPriority;
        }

        [System.Serializable]
        public struct MemoryPriorityLimit
        {
            public int m_minMemoryNeededMB;
            public int m_preloadAssetsOfPriority;
        }
    }

    [System.Serializable]
    public class AssetLoadConfig
    {
        [SerializeField] private int m_precacheAssetsPriority;
        [Tooltip("For specific AssetReferences, if you have Settings Objects that need to be loaded first here is where to reference them")]
        [SerializeField] private UnityEngine.AddressableAssets.AssetReference[] m_assetReferencesToLoad = new UnityEngine.AddressableAssets.AssetReference[0];
        [Tooltip("For more general Labels, you can define these in the Addressable Groups menu to simplify preloading sets of assets")]
        [SerializeField] private string[] m_assetBundlesToLoad = new string[] { "Preload" };

        public bool IsValid(int priority) { return m_precacheAssetsPriority <= priority; }
        public UnityEngine.AddressableAssets.AssetReference[] AssetReferencesToLoad => m_assetReferencesToLoad;
        public string[] AssetBundlesToLoad => m_assetBundlesToLoad;
    }
}
