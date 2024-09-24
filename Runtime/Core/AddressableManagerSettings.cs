using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace Some.Utility.AddressableManager
{
    public abstract class AddressableManagerSettings : ScriptableObject
    {
#region Lookup Functions
        public static S FindSettings<S>() where S : AddressableManagerSettings
        {
            S settings = Resources.Load<S>("AddressableManagerSettings");
            if (settings != null)
                return settings;

            settings = Resources.Load<S>("Config/AddressableManagerSettings");
            if (settings != null)
                return settings;

            S[] allSettings = Resources.FindObjectsOfTypeAll<S>();
            if (allSettings.Length > 0)
            {
                Debug.LogWarning("Unable to find \"AddressableManagerSettings\" in either a base \"Resources\" folder or a \"Config\" subfolder. Consider moving your Settings into one of these folders or making a GameObject with the appropriate AddressableManager on it as a component as the \"FindAll\" search is extremely slow");
                return allSettings[0];
            }

            Debug.LogError("Unable to find \"AddressableManagerSettings\" at all. Please create one with that name for dynamic settings lookup, otherwise create a GameObject with the appropriate AddressableManager component in the startup scene of your project with a reference to the appropriate Settings object");
            return null;
        }

        public static AddressableManager FindSettingsAndCreateSingleton()
        {
            AddressableManagerSettings settings = FindSettings<AddressableManagerSettings>();
            if (settings != null)
                return settings.CreateManagerSingleton();
            return null;
        }

        protected virtual AddressableManager CreateManagerSingleton()
        {
            GameObject newManager = new GameObject("AddressableManager");
            DontDestroyOnLoad(newManager);

            AddressableManager managerComp = newManager.AddComponent(GetManagerType()) as AddressableManager;
            managerComp.ApplySettings(this);
            return managerComp;
        }

        protected abstract System.Type GetManagerType();
#endregion
    }

    public abstract class AddressableManagerSettings<M> : AddressableManagerSettings where M : AddressableManager
    {
        protected override System.Type GetManagerType() { return typeof(M); }
        public static M GetManagerInstance() { return AddressableManager.BaseInstance as M; }
    }
}
