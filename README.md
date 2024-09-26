# SomeUnityAddressableManager
It's just some Addressable Manager for the Unity Game Engine. This is essentially a rewrite of a similar Addressable Manager I created for a former project to act as a code sample of my work. The goal of this project specifically is to create an extendable manager to help simplify the process of loading/unloading Addressable Assets. If anyone ends up using this please feel free to leave comments if you run into any issues or if you think it can be optimized in any way. I'll try and address any bugs when I get the chance.

## Basic Installation
It is recommended to install via the Github URL (https://docs.unity3d.com/Manual/upm-ui-giturl.html) but this project also exists as it's own package folder, so you could just clone it directly into your project's Packages folder. Regardless the order of installation is:
* Install CySharp's UniTask package: https://github.com/Cysharp/UniTask
  * SomeUnityAddressableManager's async functions are all built as UniTask functions as it's more Unity Garbage Collection-friendly compared to normal C# aysnc Tasks
* Install this package: (install via Github URL: https://github.com/Tendus/SomeUnityAddressableManager.git)

### Next Steps
* New to Addressables? Check out the ["What Are Addressables"](#what-are-addressables) section for brief explanation.
* Go over the ["Design Philosophy"](#design-philosophy) section for an extensive overview on why "Some Unity Addressable Manager" is setup the way it is.
* If that's too daunting or you just need something to handle Addressables check out the ["Simple Addressable Manager"](#simpleaddressablemanager) section for how to set one of those up.
* I'd also recommend at least taking a look at the provided Sample: ["Simple Loading Sequence"](#simple-loading-sequence) (you can install via PackageManager) as it provides a very basic example of how the Full Init load sequence can be implemented.

## What Are Addressables
Build sizes too large? Excessive Memory usage got ya down? Stuck wondering "Why is my Resources folder so darn bloated!" There's got to be another way! Hi, I'm Tyler of TylerIsProgramming of Tendus of "This Bit Has Run Its Course" to show you the wonderful world of Addressables and Asset Bundles, the Unity-approved way to get that Memory allocation under control. Instead of just letting the "Resources" functions control your loading, let's instead sieze our memory-management destiny and package the heavy stuff into AssetBundles and load them with the Addressables system! You may be asking yourself, "Ok, but what are Addressables?", to which I might reply, "Read through every page of [this documentation](https://docs.unity3d.com/Packages/com.unity.addressables@1.22/manual/index.html)" (like I once had to). Lucky for you I'm not so cruel and For those just looking for a quick explanation, we'll get to that in the only way I know how: Lists!
* The "Resources.Load" Method: Where every Unity dev first learns to load assets from. For small projects it is absolutely fine to just rely on the Resources folder but for those wanting/needing more control over memory-management it doesn't really cut it. This is partly because you don't really get any control over the "Unload" functionality, but also it's easier to neglect how assets reference each other when it's as "loose" as relying on Resources allows. For example, if you have a MonsterData ScriptableObject, which includes direct references to all the different models it has ("public GameObject model" *cough cough*), you will load in each of those models whenever you load in that MonsterData, even if all you need are some battle states. One of the first lessons you'll learn in memory-management is just how easy it is to increase the memory overhead of the simplest assets from those sneaky direct references, and I mean you could just make every reference a "string lookupID", I mean, surely that doesn't already exist right?
* Why it's called the "Addressables" system: Ok so, good news, that very thing does exist. Enter "Addressable" assets. With Unity's "AddressableAssets" package installed, you can mark almost any asset you want as an "AddressableAsset", and you can even define what the "Address" is, no longer bound to constraints of the folder structure affecting your Load calls! There is a catch, you can't mark an Asset in a "Resources" folder as "Addressable". This is because the how the "Addressable" system actually loads the asset is handled very differently from how the "Resources" system loads assets, and this is because the "Addressable" system is built on the "AssetBundles" asset provider system.
  * A note on the "AssetReference" class (and AssetReference<T>): This is the Unity-sanctioned way to Reference your assets indirectly, including a nifty generic version that'll even let you define the Type of asset you need it to be! There's not really anywhere else to mention it, but it is worth looking into as an extension of the Addressables system
* And Finally "AssetBundles": These are the packaged assets. The way these are setup is that they don't have to be built with the game itself (like "Resources are) and can instead be loaded in later. If you've ever played any mobile game that asked you to download updates after opening the game, it was downloading AssetBundles. When building these, there are some nice options such as how you want them packed (all together or segmented) and they are very cool, but there's not much else to really say about them on a general "building understanding" level. Except, well, there is one tiny little thing you should probably know about our friends "AssetBundles" and "Addressables", and even ol' "Resources". "Resource" loads are synchronous, "Addressables" loads are not.
* Async and what "More Control" actually entails: For those without a CompSci background, Synchronous functions are essentially "completed" on the same call that requested them, you get what you went right when you ask. Async functions however, can't make that some promise and we just kind of have wait until their done and deal with the results then. With the "Resources" system we could rely on it to index out everything that may need to be loaded, and Unity built it in such a way that those Load calls could be made without the wait. Now the training wheels are off and we have to handle all that ourselves! Dealing with Async functions AND having to keep track of whether and asset should be loaded or not? Oh No! Fret not front-end dev, for dealing with that very problem is why you're here!
* The purpose of the "Some Addressable Manager": This project exists to bridge that gap between "Asset Requester" and "Asset Provider" for when that provider is the "Addressables" system, and act as a middle-man between the 2, providing both extendable classes and functions for the devs that want it and a solid simple base framework if you just need that bridge. You will still have to wait on those async functions, but you do get Callbacks!

## Design Philosophy
### Former Setup
The former "Addressable Manager" was created for an idle crafting-focused mobile game with a strict APK size limit we couldn't go over. Because of this I was asked to handle setting up the Addressables not only to get our build size down but also to try and get our memory overhead as low as possible. For a quick general breakdown of the development needs for this Manager:
* Needs to Preload and Cache all Item and Character data 
* Needs to Load/Unload high quality character portraits on specific menus.
* Needs to Load/Unload multiple visual novel style thumbnails for past dialogue events.
* Needs to Load/Unload vehicle models and sprites dependent on vehicle level and player customization options.
* Needs to Load/Unload active Event and Promotion data.
* Needs to account for the active player's Device performance/specification.
  * Autocaching and Preloading was setup specifically for this, so if the game is running on a semi-decent phone it would help remove the load times for certain menus.
  * The crowning achievement for this whole setup was learning that someone was able to play our game on a phone with only 2GB of RAM.
* The ability to wholly seperate the Items, Characters, and Events from the built Game so we can push updates for them not through a new build of the game but by simply updating the Asset Bundles on our CDN (Content Delivery Network).

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
* Settings: Exposes what should be customizable of the Manager to the Developer. Should be where developers define the parameters and thresholds for caching to function. Should only expose Init and other service sequence calls to External functions
* AssetLink: The "Block" of cached data that actually holds the link to the loaded asset. Handles the actual Load and Unload calls. Is expected to keep track of what has requested it's Asset

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

The following sections go more into how each individual portion of this dynamic is setup, but the gist is Settings sets the Rules, Manager uses the Rules to manage the AssetLinks, AssetLinks handle the actual data. Something you'll notice reading through those sections is that each role is usually split among three different levels of class inheritence:
* A Base class: Will usually handle the most basic functionality along with defining abstract functions to be overridden by child classes. I try to set these up with an almost interface-based approach, where the Base class defines the jobs that need to be done and it's up the Template and Implementation classes to override and deliver on what those jobs do so that the act of swapping between different Implementations should't fully break the existing setups. There are exceptions of course, some intended to keep the Base class from getting too bloated, but that's the general intention of the Base class.
* A Template class: A class dependent on all needed generic references. Will usually override as much of the Base class's abstract definitions as it can to help keep the Implemenatation class focused on more project-specific code.
* An Implementation class: a class that fills out the generic types of the Template class and defines the code unique to the project the AddressableManager is being used in. Depending on the Role we're trying to fill with this specific Base/Template/Implementation setup, I usually try to set these up in a way to where a dev only has to define the Implemenation as child of the Template and pass the classes that fill the generic types out with minimal additional code needed.

With that explained, an overview of the full "Some Unity Addressable Manager" system:
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
  * "SimpleAddressableManagerSettingsBase" and it's child classes "SimpleAddressableManagerSettings" and "SimpleAddressableManagerSettings_Dynamic" exist as a basic implementation of this that can extended or used as-is.

#### The AssetLink
* The Base class: "AssetRefLink": Handles the Load and Release functionality of the Asset itself
  * Uses an "AssetReference" as the direct link to Loading/Releasing/Accessing the Asset. Documentation can be found: https://docs.unity3d.com/Packages/com.unity.addressables@1.20/manual/AssetReferences.html
    * I'm considering setting up a new class for this specifically as an AssetReference might have an issue with being made from an already Loaded Object. I need to do a bit more research first
  * Defines abstract functions for handling Requester callbacks as I wanted to leave how those Requesters are stored up to the Implementation class
  * I wanted to make this class as small as possible so the Implementation class is primarily responsible for how much space it takes up on the data stack
* The Implementation class: "YourAssetRefLink": Handles Request tracking by oerriding abstract functions defined in Base class, along with defining any additional wanted data
  * Template class not really needed for this as the AssetLink doesn't directly reference the Manager or Settings classes at all. It wholly responsible for itself. The original draft of the dynamic didn't include it as it was just considered part of the Manager. I wanted to allow extending the Base though to whoever would want to override the Requester tracking
  * "SimpleAssetRefLink" exists as a very basic implementation of this that can extended or used as-is

## The Simple Addressable Manager
While all the Base/Template/Implementation stuff is fun "middle-level" management code that I'm certain everyone reading this wants to extend and play with, you can just use the SimpleImplementation provided with the package to handle all the Addressable Management stuff without the hassle of setting up any additional (implementation) code yourself. This section is a quick overview of the SimpleImplementation of the AddressableManager. For each system section, the 1st bullet point will be on how to set it up to run the project, with the following points explaining import functions and points of interest:

### Implementation Design overview
* AssetLink stores Requesters' RequestID, and acts as empty if none are stored. It also stores a numeric Priority score to be used in a future build.
* Settings Enables Autocaching if the Device's SystemMemory is above a certain threshold, otherwise it leaves it disabled. I also allows tying what can be PreCached to SystemMemory as well through a numeric Priority score.

#### SimpleAddressableManager
* Add the component "SimpleAddressableManager" to a GameObject in your scene. It will handle turning itself into a singleton, and all that comes with it.
  * Consider setting up a "Boot" prefab or something like it that you can throw into every scene and will destroy itself if it's not the 1st of it's kind (Look into AddressableManager<M,S,A>.Awake() for an example of how to do this).
    * You can also spawn a singleton from the Settings, but we'll get there when we get to the Settings.
 * Has DoAutoCache bool that determines whether or not the AssetLinks should remain in the cache after all their Requesters have finished using them.
 * Has LoadAsset wrapper functions that allowing passing a Priority score value, to be implemented in a future build.
 * Includes UpdateAssetRefPriority function to update that value if it needs to be updated per Requester.
 * Most of the code exists in a Template class: "SimpleAddressableManagerSettingsTemplate<M,S,A>" for specific reason being that if you want to extend this code at all you can just inherit from that class.

#### SimpleAddressableManagerSettings
* Somewhere in your Project's folder heirarchy, RightClick -> Create -> Some/AddressableManager/SimpleSettings. Fill it out as desired and reference via the SimpleAddressableManager component you created in the former section.
* If you want the Settings to create the Manager rather than the other around, you can call the "TryCreateManagerFromSettings" function from your Settings Object. It returns true if a Manager singleton doesn't already exist and it successfully creates a Manager singleton of the correct type
* There is a also a "Dynamic" Settings object you can create as well (DynamicSettings) that will select the BuildConfig to use based on the current Device and its SystemInfo that is running the Game.
* For the most part I've tried to add Tooltip for every variable that isn't immediately self-explanatory, so I'm skipping the nitty-gritty for just q uick high-level explanation of the important bits:
  * BuildConfig: Is used to determine if we want to enable AutoCaching for basic Settings, For the Dynamic settings this returns a Priority score that is used to determine which Labels/Addressables to PreCache.
  * Remote Options: "HasRemoteAddressables", "RemoteAssetLoadPath", and "DownloadAssetLabels": Ignore these if you aren't distributing the AssetBundles over a CDN.
  * PreCache Assets/Labels: Define what we should precache into our AddressableManager. any Settings objects we want to treat as Addressables should be included here. As mentioned above using a Dynamic Settings object we can limit what gets PreCached.
* Included GetNeededDownloadSizeAsync function that can be called Externally if you want to display the size of the Download needed (assuming one is needed, a return of 0, says "none")
* Included DownloadBundlesAsync function that can be called Externally if you want to seperate the Download sequence from the Init sequence (you should if you are doing Remote assets)
* Included CacheBundlesAsync function that can be called Externally if you want to seperate the PreCache sequence from the Init sequence (it should come after Download but you aren't using Remote assets it doen't really matter)
* Similar to the SimpleAddressableManager, most of the code exists in a Template class: "SimpleAddressableManagerSettingsTemplate<M,S,A>". Unlike the Manager there is an abstract class right below to help simplify the Simple and Dynamic implementations into 1 reference type: "SimpleAddressableManagerSettingsBase". You can override that with your own Implementation if you like and it'll supply all the data the Manager needs so long as you override the abstract function.

#### SimpleAssetRefLink:
* No additional work is needed, it is implemented as part of the SimpleAddressableManager's inheritence of the Template class
* Includes Priority score value to be implemented in the future, currently returns the max score between all Requesters and it's default Priority score
* Stores Requesters as a Dictionary lookup of RequestID:Priority score, empty dictionary = no Requesters

### Additional Helper Classes
#### SimpleObjectReference
Acts as a very basic in-Scene instance of an Object to be loaded at runtime. Includes settable UnityEvent for in-scene simple callbacks and overridable "Set" and "Clear" functions for extended versions of this base class. A quick rundown of extendend versions already included as part of the SimpleImplementation:
* SimpleGameObjectReference: Can set Transform parent for the Object to be spawned onto. Will Instantiate a copy of the LoadedAsset as part of the "Set" call, and delete said copy on "Clear"
* TODO: Add more Reference classes

## Setting up Samples
A quick overview of how to go about setting up and implementing samples as well what's worth looking at just to look at it. At the moment there is just the one, but if anyone needs an example of a spefic feature I can look adding it to the list. (Side note: I also do a bit more research into including addressable settings as part of the package, you'll need to modify the "Groups" but it's really simple and easy and included below)

### Simple Loading Sequence
TODO: expand upon sample explanation
On how to get it to work:
* Set "DownloadAssets" folder as addressables (Select the folder from the Project view, enable the tickbox "Addressable"
* Go to AddressableGroups and add a "Download" Label (Window -> AssetManagement -> AddressableGroups. Then in that window: Tools -> Windows -> Labels. Add the Label)
* In the Groups window, find the folder we marked "Addressable" and under Labels, enable the newly created "Download" label
* That's all that is required to get it to run! Keep reading for how to enable proper reading from built AssetBundles
* Build the AssetBundles (in the Groups window, Build -> New Build -> Default Build Script. Ignore the other options for now those are for way more advanced functionality than what is needed to run this sample)
* Set the "Play Mode Script" to "Use Existing Build" (we are still in the Groups window)
* Congratulations! Remote loading is bit tricker, and you are responsible for finding your own CDN
* TODO: Add instructions for Remote loading

## On My TODO list:
A List of changes I want to make and features I want to add in the future
* Setup SimpleAddressableManager's Priority system, allowing Autocaching above a certain threshold
* Setup a Catalog system to load in Assets based on defined lookup parameters (less by players, more by code and settings)
* Look into feasibility of hosting a Remote Distribution Sample, DDOS scares my poor empty wallet
* Add more ObjectReferences in general
* All the TODO lines in the project's code itself
