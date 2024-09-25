# SomeUnityAddressableManager
It's just some Addressable Manager for the Unity Game Engine. This is essentially a rewrite of a similar Addressable Manager I created for a former project to act as a code sample of my work. The goal of this project specifically is to create an extendable manager to help simplify the process of loading/unloading Addressable Assets. If anyone ends up using this please feel free to leave comments if you run into any issues or if you think it can be optimized in any way. I'll try and address any bugs when I get the chance.

## Basic Installation
It is recommended to install via the Github URL (https://docs.unity3d.com/Manual/upm-ui-giturl.html) but this project also exists as it's own package folder, so you could just clone it directly into your project's Packages folder. Regardless the order of installation is:
* Install CySharp's UniTask package: https://github.com/Cysharp/UniTask
  * SomeUnityAddressableManager's async functions are all built as UniTask functions as it's more Unity Garbage Collection-friendly compared to normal C# aysnc Tasks
* Install this package: (install via Github URL: https://github.com/Tendus/SomeUnityAddressableManager.git)
It is recommended to also at least take a look at the provided Sample: "Simple Loading Sequence" (you can install via PackageManager) as it provides a very basic example of how the Full Init load sequence can be implemented

## Design Philosophy
### Former Setup
The former "Addressable Manager" was created for an idle crafting-focused mobile game with a strict APK size limit we couldn't go over. Because of this I was asked to handle setting up the Addressables not only to get our build size down but also to try and get our memory overhead as low as possible. For a quick general breakdown of the development needs for this Manager:
* Needs to Load/Unload high quality character portraits on specific menus.
* Needs to Load/Unload multiple visual novel style thumbnails for past dialogue events.
* Needs to Load/Unload vehicle models and sprites dependent on vehicle level and player customization options.
* Needs to Load/Unload active Event and Promotion data.
* Needs to account for the active player's Device performance/specification.
  * Autocaching and Preloading was setup specifically for this, so if the game is running on a semi-decent phone it would help remove the load times for certain menus.
  * The crowning achievement for this whole setup was learning that someone was able to play our game on a phone with only 2GB of RAM.
* The ability to wholly seperate the Characters and Events from the built Game so we can push updates for them not through a new build of the game but by simply updating the Asset Bundles on our CDN (Content Delivery Network).

To achieve all this I created a few types of scripts to help handle the Addressables:
* The "AssetBundleBridge" script to act as the primary manager. It would handle the Loading/Unloading of the assets and Cache the Loaded AssetReferences into a dictionary lookup if applicable
  * Preloading would be done on all Settings object, and would be extended to the character portraits if the device had enough memory support this
  * Autocaching was a toggle that would allow Loaded assets to stay in the Cache even after the Unload. This toggle would also only be allowed for devices with enough memory
* ObjectReference component scripts that would call the AssetBundleBridge's Load/Unload functions automatically and handle the returned loaded asset as each was designed
  * As an example the CharacterReference would Instantiate a GameObject model from the loaded asset, parent it to itself, and set the visual elements needed. When it was time to Unload the asset it would destroy the Instantiated GameObject. The Load/Unload calls would be made as the CharacterReference's GameObject was enabled/disabled
* Catalog lookup scripts for the vehicles that have references to seperate ScriptableObjects with the actual AssetReferences to the sprite/model data
* Event/Promotion manager classes that would only Load/Unload what should be the active Events/Promotions.
  * These especially benefit from the Addressable setup as we can "push" new Events/Promotions just by adding them to the Asset Bundles and letting the game iterate through what's downloaded to see what's active
---
### Some Addressable Manager Setup
The pitfall of setting up anything for a specific project, especially one with a tiny development team and a harsh deadline, is that the resulting code is usually fairly rigid. It exists for that project only and it can be unwieldy to try and extend to other projects. While some of the underlying structure is still viable (The way loading, unloading, and caching is handled is solid IMO), most of the surrounding code needs to be torn down and rebuilt with scalability in mind. To that end I split the duties of the AddressableManager into the following system dynamic:
* Manager: Exists in the Game on it's own GameObject. Exposes the Load and Unload functionality as well as handles the Caching. Most External calls/needs from code and the Game at large should go through the Manager.
* Settings: Exposes what should be customizable of the Manager to the Developer. Should be where developers define the parameters and threholds for caching to function. Should only expose Init sequence calls to External functions
* AssetLink: The "Block" of cached data that actually holds to the loaded asset. Handles the actual Load and Unload calls. Is expected to keep track of what has requested it's Asset

The most straightforward way to explain how the Dynamic works is:
* On Init, The Manager asks the Settings "What are the Caching rules?"
* Something in the Game Asks the Manager to load an asset (via AssetReference and a direct link to itself)
* The Manager retrieves an AssetLink (either from the cache or by creating a new one, which then gets added to the cache)
* The Manager tells the AssetLink that Something in the Game needs it's asset
* The AssetLink loads the Asset
* The Manager returns the Asset loaded by the AssetLink
* Something in the Game does uses the loaded Asset as needed
* Something in the Game tells the Manager it can now Unload the Asset
* The Manager tells the AssetLink that Something in the Game no long needs it's Asset
* IF nothing else is requesting the AssetLink's Asset, the Manager will check "What are the Caching Rules?" set by the Settings and either:
  * Tell the AssetLink to Release the Asset, and delete the AssetLink
  * or leave the AssetLink in the Cache

The following sections go more into how each individual portion of this dynamic is setup, but the gist is Settings sets the Rules, Manager uses the Rules to manage the AssetLinks, AssetLinks handle the actual data.

#### The Manager
* The Base class: "AddressableManager": Implements the core functionality either itself or by defining what child classes need to implement through abstract functions.
  * Handles the Internal Loading/Unloading functionality.
  * Handles the Init, Update Catalogs, Download Bundles, and PreCache Bundles services/sequences as well offers callback functionality on each's completion.
    * On a side note, not the biggest fan of the word "services" but I'm not really sure what else to call them as "sequences" also doesn't seem right.
* The Template class: "AddressableManager<M,S,A>": Implements the Caching functionality and implements most of the abstract functionality defined in the Base class.
  * M: Manager, S: Settings, A: AssetLink.
  * Along Caching overrides the following abstract functionality from the Base class:
    * Applies the Settings Object (can be referenced by the adding your Implementation manager as a component to a GameObject in the scene, will also handle marking it as "DontDestroyOnLoad").
    * Creates the actual AssetLink used.
    * Implements callbacks for Load/Unload.
  * This class is sort of optional, you can ignore this and just inherit from the Base class if you really want to but the way this class is defined is to help reinforce the way the whole system is designed with the emphasis on the Manager, Settings, AssetLink dynamic.
* The Implementation class: "YourManager": Implements the actual Caching rules the Template class uses to handle AssetLinks
  * The design intention is for developers to write Wrapper functions to call the internal Load functions so they can set any additional necessary data on the AssetLinks as part of those wrappers
  * Only real required ovverride is for the abstract "CreateNewAssetLink" function defined by the Template class
  * "SimpleAddressableManager" exists as a very basic implementation of this that can be extended or used as-is

#### The Settings
* The Base class: "AddressableManagerSettings": Primarily exists as the inherit base for the Implementation class
  * Includes a "FindSettings" function which can help find the Settings if we're initing a null Manager. Ideally never used
  * Includes a "CreateManagerSingleton" function which can be used to create the Manager from the Settings, instead of using an existing Manager with a reference to it's Settings
    * This is fine as the Manager should have little to no variables/fields on it, the Settings should always contain the settable data
* The Template class: "AddressableManagerSettings<M>": Primarily exists to inform the Base class on the Manager's Type.
  * That's essentialy it. Also gives a static "GetManagerInstance" that can be used with the Implementation class to try and get the singleton Manager
* The Implementation class: "YourSettings": Defines the Caching rules along with any other settable data for the Manager to use
  * Unlike the Manager Implementation class, the dev is expected to fill out the Settings' Implementation class for the most part instead of relying on parent classes to handle most everything.
  * The reason for this is the Settings Implementation exists almost exclusively to pass data to the Manager Implementation class, and provide devs a way to expose the options that actually modify that specific Manager Implementation
    * Fields that would normally be part of the Manager Implementation are instead defined and set here, so it's easy slot in different Settings objects if needed
    * I considered filling out the Template class with some super basic options but ultimately decided against it since it'd 1) make the Inspector a little less organized, 2) would've introduced a point of rigidity (Can't change the option without changing the object, when we may want the option to change based on some other variable). Everytime I considered it, I thought of the Implementation class being strongarmed into behaving a certain way when the Settings Implementation class should be the least limited in how it has to be setup
  * "SimpleAddressableManagerSettingsBase" and it's child classes "SimpleAddressableManagerSettings" and "SimpleAddressableManagerSettings_Dynamic" exist as a basic implementation of this that can extended or used as-is

#### The AssetLink
* The Base class: "AssetRefLink": Handles the Load and Release functionality of the Asset itself
  * Uses an "AssetReference" as the direct link to Loading/Releasing/Accessing the Asset. Documentation can be found: https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/AssetReferences.html
    * I'm considering setting up a new class for this specifically as an AssetReference might have an issue with being made from an already Loaded Object. I need to do a bit more research first
  * Defines abstract functions for handling Requester callbacks as I wanted to leave how those Requesters are stored up to the Implementation class
  * I wanted to make this class as small as possible so the Implementation class is primarily responsible for how much space it takes up on the data stack
* The Implementation class: "YourAssetRefLink": Handles Request tracking by oerriding abstract functions defined in Base class, along with defining any additional wanted data
  * Template class not really needed for this as the AssetLink doesn't directly reference the Manager or Settings classes at all. It wholly responsible for itself. The original draft of the dynamic didn't include it as it was just considered part of the Manager. I wanted to allow extending the Base though to whoever would want to override the Requester tracking
  * "SimpleAssetRefLink" exists as a very basic implementation of this that can extended or used as-is
