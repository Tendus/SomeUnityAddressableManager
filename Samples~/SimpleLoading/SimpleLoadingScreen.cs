using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace Some.Utility.AddressableManager.Sample
{
    /*
     * I set this Loading sequence up in a way to where there's 
     *  1) A main thread we're waiting on to show the Ideal FullInit sequence as simplified as possible, but
     *  2) Defining the AwaitServices as their own AsyncFunctions to show the Ideal sequence for each as implementing the "SimpleAddressableManager" specifically, and
     *  3) Using callbacks for when the Loading Services complete to help show the key points in the sequence we want to look out for and react to
     */ 
    public class SimpleLoadingScreen : MonoBehaviour, ITrackAssetDownloads, ITrackSequencedAssetCaching
    {
        public CanvasGroup m_selfGroup;
        public float m_fadeTimeSeconds = .5f;
        public float m_fadeOpaquePercent = 1f;

        public Image m_loadingBarFill;

        public Text m_loadingMessage;
        public Text m_loadingSubmessage;

        public GameObject m_downloadPrompt;
        public Text m_downloadPromptMessage;

        public GameObject m_cachingPrompt;

        public float m_downloadEndDelaySeconds = 5;
        public float m_cachingEndDelaySeconds = 5;

        public bool m_callOnCompleteBeforeFade = true;
        public UnityEngine.Events.UnityEvent OnFadeInComplete = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent OnLoadingComplete = new UnityEngine.Events.UnityEvent();

        private bool m_isLoading = false;

        private bool m_inDownloadLock = false;
        private bool m_skipDownload = false;
        
        private bool m_inCachingLock = false;
        private bool m_skipCaching = false;

        private bool m_encounteredError = false;

        public void StartLoadingSequence(MonoBehaviour coroutinePump, bool fadeImmediate = true)
        {
            gameObject.SetActive(true);
            coroutinePump.StartCoroutine(LoadingSequence());

            if (fadeImmediate || m_fadeTimeSeconds <= 0)
                SetFadePercent(1);
            else
            {
                SetFadePercent(0);
                coroutinePump.StartCoroutine(FadeSelf(true));
            }
        }

        public IEnumerator LoadingSequence()
        {
            m_isLoading = true;

            Debug.Log("Start Loading Process, wait for Init");
            var InitTask = InitAddressablesAsync();
            while (InitTask.Status < UniTaskStatus.Succeeded)
                yield return null;

            Debug.Log("Continue Loading Process, wait for Downloads");
            var DownloadTask = WaitForDownload();
            while (DownloadTask.Status < UniTaskStatus.Succeeded)
                yield return null;

            if (m_encounteredError)
                yield break;

            Debug.Log("Continue Loading Process, wait for Caching");
            var CacheTask = WaitForCaching();
            while (CacheTask.Status < UniTaskStatus.Succeeded)
                yield return null;

            if (m_encounteredError)
                yield break;

            Debug.Log("Finish Loading Process, fade out");
            if (m_callOnCompleteBeforeFade)
                OnLoadingComplete.Invoke();

            if (m_fadeTimeSeconds <= 0)
                SetFadePercent(0);
            else
            {
                var fadeEnum = FadeSelf(false);
                while (fadeEnum.MoveNext())
                    yield return fadeEnum.Current;
            }

            m_isLoading = false;

            if(!m_callOnCompleteBeforeFade)
                OnLoadingComplete.Invoke();
        }

        IEnumerator FadeSelf(bool toOpaque)
        {
            if(m_fadeTimeSeconds <= 0)
            {
                SetFadePercent(toOpaque ? 1 : 0);
                yield break;
            }

            float time = 0;
            while(time < m_fadeTimeSeconds)
            {
                SetFadePercent(toOpaque ? time / m_fadeTimeSeconds : 1f - time / m_fadeTimeSeconds);
                time += Time.deltaTime;
                yield return null;
            }

            SetFadePercent(toOpaque ? 1 : 0);
            if (toOpaque && OnFadeInComplete != null)
                OnFadeInComplete.Invoke();
        }

        void SetFadePercent(float percent) { m_selfGroup.alpha = Mathf.Lerp(0, m_fadeOpaquePercent, percent); }

        #region Manager Services' Init Sequences
        //Base level Init Sequence, the way I set up the default settings, we only Init the Manager itself and the UpdateCatalogs service, we want the Download portion to be a prompt given to the User so they can decide when they handle that
        private async UniTask<bool> InitAddressablesAsync()
        {
            var initTask = AddressableManager.BaseInstance.InitAsync();
            bool hasInit = await initTask;

            if (!hasInit)
            {
                m_loadingMessage.text = "Failed to Init";
                m_loadingSubmessage.text = "Check your settings or the logs";
                return false;
            }

            return true;
        }

        //Download Sequence, 1st we check if there's anything to Download, and if so Prompt the User if they want to download the assets. This iteration allows skipping for the purpose of demoing but any professional release should not
        private async UniTask WaitForDownload()
        {
            m_skipDownload = false;
            m_encounteredError = false;
            if (AddressableManager.BaseInstance is Simple.SimpleAddressableManager simpleManager)
            {
                long neededDownload = await simpleManager.Settings.GetNeededDownloadSizeAsync();
                if (neededDownload > 0)
                {
                    PromptForDownload(neededDownload);
                    while (m_inDownloadLock && !m_encounteredError)
                        await UniTask.DelayFrame(1);

                    if (m_encounteredError || m_skipDownload)
                        return;

                    await UniTask.Delay(System.TimeSpan.FromSeconds(m_downloadEndDelaySeconds));
                }
                else
                    SkipDownload();
            }
        }

        //Caching Sequence, Prompt exists mainly for demoing, but any professional release would just force the Precache sequence
        private async UniTask WaitForCaching()
        {
            m_skipCaching = false;
            m_encounteredError = false;
            if (AddressableManager.BaseInstance is Simple.SimpleAddressableManager simpleManager)
            {
                PromptForCaching();
                while (m_inCachingLock && !m_encounteredError)
                    await UniTask.DelayFrame(1);

                if (m_encounteredError || m_skipCaching)
                    return;

                await UniTask.Delay(System.TimeSpan.FromSeconds(m_cachingEndDelaySeconds));
            }
        }
        #endregion

        #region Download Prompt
        public void PromptForDownload(long downloadSizeBytes)
        {
            m_inDownloadLock = true;
            m_downloadPrompt.gameObject.SetActive(true);
            m_downloadPromptMessage.text = $"Download of {downloadSizeBytes.BytesToSizeLabel()} required. Proceed?";
        }

        public void StartDownload()
        {
            m_downloadPrompt.SetActive(false);

            m_loadingBarFill.fillAmount = 0;
            m_loadingMessage.text = "Now Downloading!";
            m_loadingSubmessage.text = "";

            if (AddressableManager.BaseInstance is Simple.SimpleAddressableManager simpleManager)
                simpleManager.Settings.DownloadBundlesAsync(this).Forget();
        }

        public void SkipDownload()
        {
            AddressableManager.DownloadStatus.OnSkip();
            m_skipDownload = true;
            m_downloadPrompt.SetActive(false);
            m_inDownloadLock = false;
        }
        #endregion

        #region Caching Prompt
        public void PromptForCaching()
        {
            m_inCachingLock = true;
            m_cachingPrompt.SetActive(true);
        }

        public void StartCaching()
        {
            m_cachingPrompt.gameObject.SetActive(false);

            m_loadingBarFill.fillAmount = 0;
            m_loadingMessage.text = "Now Caching!";
            m_loadingSubmessage.text = "";

            if (AddressableManager.BaseInstance is Simple.SimpleAddressableManager simpleManager)
                simpleManager.Settings.CacheBundlesAsync(this).Forget();
        }

        public void SkipCaching()
        {
            AddressableManager.PreCachingStatus.OnSkip();
            m_skipCaching = true;
            m_cachingPrompt.SetActive(false);
            m_inCachingLock = false;
        }
        #endregion

        #region Download Service Callback functions
        //ITrackAssetDownloads
        public int UpdateDownloadPercentAfterTimeFrames => 1;

        public void UpdateAssetDownloadProgress(AssetDownloadProgress progress)
        {
            m_loadingBarFill.fillAmount = progress.percentComplete;
            m_loadingMessage.text = "Now Downloading!";
            m_loadingSubmessage.text = $"Downloaded {progress.DownloadedAmountLabel} / {progress.TotalToDownloadLabel}"; 
        }

        public bool OnAssetDownloadError(AssetDownloadProgress progress) { return progress.consecutiveDownloadFaults < 3; }
        public void OnAssetDownloadFail(AssetDownloadProgress progress) { m_loadingMessage.text = "Download Failed!"; m_loadingSubmessage.text = "Check your Addressable lookup settings and try resetting"; m_encounteredError = true; }

        public void OnAssetDownloadCycle(AssetDownloadProgress progress) { m_inDownloadLock = false; }
        public void OnAssetDownloadComplete(AssetDownloadProgress progress)
        {
            m_loadingBarFill.fillAmount = 1;
            m_loadingMessage.text = "Download Complete!";
            m_loadingSubmessage.text = "";
            m_inDownloadLock = false;
        }
        #endregion

        #region Caching Service Callback functions
        //ITrackAssetCaching
        public int UpdateCachePercentAfterTimeFrames => 1;
        public bool AutoSkipFailedCaches => false;

        public void UpdateAssetCacheProgress(AssetCacheProgress progress)
        {
            m_loadingBarFill.fillAmount = progress.PercentTotal * CachingCycleStep + CachingCycleOffset;
            m_loadingMessage.text = "Now Caching!";
            m_loadingSubmessage.text = "";
        }

        public bool OnAssetCacheError(AssetCacheProgress progress) { return progress.consecutiveLoadFaults < 3; }
        public void OnAssetCacheFail(AssetCacheProgress progress) { m_loadingMessage.text = "Caching Failed!"; m_loadingSubmessage.text = "Check your logs and Precaching setup"; m_encounteredError = true; }

        public void OnAssetCached(AssetRefLink assetLink, bool isFinal) { }
        public void OnAssetCachingComplete(AssetCacheProgress progress)
        {
            m_loadingBarFill.fillAmount = CachingCycleStep + CachingCycleOffset;
            m_loadingSubmessage.text = "";
        }

        //ITrackSequencedAssetCaching
        private int m_cachingCycles = 0;
        private int m_activeCachingCycle = 0;
        private float CachingCycleStep => m_cachingCycles > 0 ? 1 / m_cachingCycles : 1;
        private float CachingCycleOffset => m_cachingCycles > 0 ? (float)m_activeCachingCycle / m_cachingCycles : 0;

        public void OnAssetCachingSequenceStart(int totalCycles) { m_cachingCycles = totalCycles; m_loadingBarFill.fillAmount = 0; }

        public void OnStartAssetCachingCycle(params string[] bundleLabels) { }
        public void OnStartAssetCachingCycle(params UnityEngine.AddressableAssets.AssetReference[] assetReferences) { }

        public void OnCompleteAssetCachingCyle(int cycle)
        {
            m_activeCachingCycle++;
            m_loadingBarFill.fillAmount = CachingCycleOffset;
        }

        public void OnAssetCachingSequenceBreak()
        {
            m_loadingMessage.text = "Caching Failed!";
            m_loadingSubmessage.text = "Check your logs and Precaching setup";
            m_encounteredError = true;
        }

        public void OnAssetCachingSequenceComplete()
        {
            m_loadingBarFill.fillAmount = 1;
            m_loadingMessage.text = "Caching Complete!";
            m_inCachingLock = false;
        }
        #endregion
    }
}
