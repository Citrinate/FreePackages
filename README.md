# Free Packages Plugin for ArchiSteamFarm

[![Check out my other ArchiSteamFarm plugins](https://img.shields.io/badge/Check%20out%20my%20other%20ArchiSteamFarm%20plugins-blue?logo=github)](https://github.com/stars/Citrinate/lists/archisteamfarm-plugins) [![Help with translations](https://img.shields.io/badge/Help%20with%20translations-purple?logo=crowdin)](https://github.com/Citrinate/FreePackages/tree/main/FreePackages/Localization) ![GitHub all releases](https://img.shields.io/github/downloads/Citrinate/FreePackages/total?logo=github&label=Downloads)

## Introduction

This plugin finds free packages on Steam and adds them to your account.

This plugin works by listening for [changes](https://steamdb.info/faq/#changenumber) to Steam's [PICS](https://steamdb.info/faq/#pics).  The plugin can discover new packages as they're released, but is limited due to PICS not showing all old changes.  As a result, the plugin can only discover packages that have changed recently, usually in the last ~12 hours.

## Installation

- Download the .zip file from the [latest release](https://github.com/Citrinate/FreePackages/releases/latest)
- Locate the `plugins` folder inside your ASF folder.  Create a new folder here and unpack the downloaded .zip file to that folder.
- (Re)start ASF, you should get a message indicating that the plugin loaded successfully.

> [!NOTE]
> This plugin is only tested to work with ASF-generic.  It may or may not work with other ASF variants, but feel free to report any issues you may encounter.

## Usage

### Enabling the plugin

You can enable the plugin per individual bot by adding `EnableFreePackages` to that bot's config file:

```json
"EnableFreePackages": true,
```

---

### Pausing package activations while playing a game

Under certain conditions, activating a free package while playing a game on Steam can cause the game to temporarily freeze.  You can prevent the plugin from activating packages while you're in-game by adding `PauseFreePackagesWhilePlaying` to your individual bot's config file.  It's recommended you use this for any account you play games on:

```json
"PauseFreePackagesWhilePlaying": true,
```

> [!NOTE]
> This applies when your account is playing a game outside of ASF, and does not apply when ASF is idling a game.  Your library being locked through Family Sharing will also prevent package activation.  You likely don't want to enable this if you run idle games 24/7, or your library is otherwise almost always in use, or you only run ASF rarely.

---

### Changing the package limit

A maximum of 30 packages can be activated per 1.5 hours.  By default, this plugin will use at most 25 of those activations and will resume where it left off if it's ever interrupted.  You can control this limit by adding `FreePackagesLimit` to your individual bot's config files of `uint` type:

```json
"FreePackagesLimit": 25,
```

> [!NOTE]
> The default is intentionally made lower than the actual limit to allow for you the ability to manually redeem packages without having to fight with the plugin.

---

### Enabling package filters

By default, the plugin will attempt to activate all free non-demo and non-playtest packages.  You can control what kinds of packages are activated by adding `FreePackagesFilters` to your individual bot's config files with the following structure:

```json
"FreePackagesFilters": [{
  "Types": [],
  "Tags": [],
  "Categories": [],
  "Languages": [],
  "Systems": [],
  "MinReviewScore": 0,
  "MinDaysOld": 0,
  "IgnoredContentDescriptors": [],
  "IgnoredTypes": ["Demo"],
  "IgnoredTags": [],
  "IgnoredCategories": [],
  "IgnoredAppIDs": [],
  "RequireAllTags": false,
  "RequireAllCategories": false,
  "ImportStoreFilters": false,
  "WishlistOnly": false,
  "IgnoreFreeWeekends": false,
  "NoCostOnly": false,
  "PlaytestMode": 0,
}],
```

All filter options are explained below:

---

#### Types

`HashSet<string>` type with default value of `[]`.  Packages must contain an app with one of the `TypeNames` specified here or they will not be added to your account.  You can leave this empty to allow for all types.  The available `TypeNames` for filtering are: `"Game"`, `"Application"`, `"Tool"`, `"Demo"`, `"DLC"`, `"Music"`, `"Video"`

---

#### Tags

`HashSet<uint>` type with default value of `[]`.  Packages must contain an app with at least one of these `TagIDs` or they will not be added to your account.  You can leave this empty to allow for all tags.  A list of tags can be found [here](https://steamdb.info/tags/).  The `TagID` will be at the end of the URL.  For example, the `TagID` for the [Indie](https://steamdb.info/tag/492/) tag is 492.

> [!NOTE]
> The "Profile Features Limited" tag presented by SteamDB is not a real tag that Steam uses.  This plugin does not detect whether or not an app has limited profile features.

---

#### Categories

`HashSet<uint>` type with default value of `[]`.  Packages must contain an app with at least one of these `CategoryIDs` or they will not be added to your account.  You can leave this empty to allow for all categories.

<details>
  <summary>List of Category IDs</summary>

  Category ID | Description
  --- | ---
  1  | Multi-player
  2  | Single-player
  6  | Mods (require HL2)
  7  | Mods (require HL1)
  8  | Valve Anti-Cheat enabled
  9  | Co-op
  10 | Game demo
  12 | HDR available
  13 | Captions available
  14 | Commentary available
  15 | Stats
  16 | Includes Source SDK
  17 | Includes level editor
  18 | Partial Controller Support
  19 | Mods
  20 | MMO
  21 | Downloadable Content
  22 | Steam Achievements
  23 | Steam Cloud
  24 | Shared/Split Screen
  25 | Steam Leaderboards
  27 | Cross-Platform Multiplayer
  28 | Full controller support
  29 | Steam Trading Cards
  30 | Steam Workshop
  32 | Steam Turn Notifications
  33 | Native Steam Controller
  35 | In-App Purchases
  36 | Online PvP
  37 | Shared/Split Screen PvP
  38 | Online Co-op
  39 | Shared/Split Screen Co-op
  40 | SteamVR Collectibles
  41 | Remote Play on Phone
  42 | Remote Play on Tablet
  43 | Remote Play on TV
  44 | Remote Play Together
  45 | Cloud Gaming
  46 | Cloud Gaming (NVIDIA)
  47 | LAN PvP
  48 | LAN Co-op
  49 | PvP
  50 | Additional High-Quality Audio
  51 | Steam China Workshop
  52 | Tracked Controller Support
  53 | VR Supported
  54 | VR Only
  55 | PS4 Controller Support
  56 | PS4 Controller BT Support
  57 | PS5 Controller BT Support
  58 | PS5 Controller BT Support
  59 | Steam Input API Supported
  60 | Controller Preferred
</details>

---

#### Languages

`HashSet<string>` type with default value of `[]`.  Packages must contain an app with support for at least one of these `LanguageIDs` or they will not be added to your account.  You can leave this empty to allow for all languages.

<details>
  <summary>List of Language IDs</summary>

  Language ID | Language
  --- | ---
  `"afrikaans"` | Afrikaans
  `"albanian"` | Albanian
  `"amharic"` | Amharic
  `"arabic"` | Arabic
  `"armenian"` | Armenian
  `"assamese"` | Assamese
  `"azerbaijani"` | Azerbaijani
  `"bangla"` | Bangla
  `"basque"` | Basque
  `"belarusian"` | Belarusian
  `"bosnian"` | Bosnian
  `"bulgarian"` | Bulgarian
  `"catalan"` | Catalan
  `"cherokee"` | Cherokee
  `"croatian"` | Croatian
  `"czech"` | Czech
  `"danish"` | Danish
  `"dari"` | Dari
  `"dutch"` | Dutch
  `"english"` | English
  `"estonian"` | Estonian
  `"filipino"` | Filipino
  `"finnish"` | Finnish
  `"french"` | French
  `"galician"` | Galician
  `"georgian"` | Georgian
  `"german"` | German
  `"greek"` | Greek
  `"gujarati"` | Gujarati
  `"hausa"` | Hausa
  `"hebrew"` | Hebrew
  `"hindi"` | Hindi
  `"hungarian"` | Hungarian
  `"icelandic"` | Icelandic
  `"igbo"` | Igbo
  `"irish"` | Irish
  `"italian"` | Italian
  `"japanese"` | Japanese
  `"kannada"` | Kannada
  `"kazakh"` | Kazakh
  `"khmer"` | Khmer
  `"kinyarwanda"` | Kinyarwanda
  `"konkani"` | Konkani
  `"koreana"` | Korean
  `"kyrgyz"` | Kyrgyz
  `"kiche"` | K'iche'
  `"latvian"` | Latvian
  `"lithuanian"` | Lithuanian
  `"luxembourgish"` | Luxembourgish
  `"macedonian"` | Macedonian
  `"malay"` | Malay
  `"malayalam"` | Malayalam
  `"maltese"` | Maltese
  `"maori"` | Maori
  `"marathi"` | Marathi
  `"mongolian"` | Mongolian
  `"nepali"` | Nepali
  `"norwegian"` | Norwegian
  `"odia"` | Odia
  `"persian"` | Persian
  `"polish"` | Polish
  `"portuguese"` | Portuguese - Portugal
  `"gurmukhi"` | Punjabi (Gurmukhi)
  `"shahmukhi"` | Punjabi (Shahmukhi)
  `"quechua"` | Quechua
  `"romanian"` | Romanian
  `"russian"` | Russian
  `"scots"` | Scots
  `"serbian"` | Serbian
  `"schinese"` | Simplified Chinese
  `"sindhi"` | Sindhi
  `"sinhala"` | Sinhala
  `"slovak"` | Slovak
  `"slovenian"` | Slovenian
  `"sorani"` | Sorani
  `"sotho"` | Sotho
  `"latam"` | Spanish - Latin America
  `"spanish"` | Spanish - Spain
  `"swahili"` | Swahili
  `"swedish"` | Swedish
  `"tajik"` | Tajik
  `"tamil"` | Tamil
  `"tatar"` | Tatar
  `"telugu"` | Telugu
  `"thai"` | Thai
  `"tigrinya"` | Tigrinya
  `"tchinese"` | Traditional Chinese
  `"tswana"` | Tswana
  `"turkish"` | Turkish
  `"turkmen"` | Turkmen
  `"ukrainian"` | Ukrainian
  `"urdu"` | Urdu
  `"uyghur"` | Uyghur
  `"uzbek"` | Uzbek
  `"valencian"` | Valencian
  `"vietnamese"` | Vietnamese
  `"welsh"` | Welsh
  `"wolof"` | Wolof
  `"xhosa"` | Xhosa
  `"yoruba"` | Yoruba
  `"zulu"` | Zulu
</details>

---

#### Systems

`HashSet<string>` type with default value of `[]`.  Packages must contain an app with support for one of the `SystemNames` specified here or they will not be added to your account.  You can leave this empty to allow for all systems.  The available `SystemNames` for filtering are: `"Windows"`, `"MacOS"`, `"Linux"`, `"DeckVerified"`, `"DeckPlayable"`, `"DeckUnsupported"`, `"DeckUnknown"`

---

#### MinReviewScore

`uint` type with default value of `0`.  Packages must contain an app with a `ReviewScore` greater than or equal to this or they will not be added to your account.  You can leave this at `0` to allow for all values.  A `ReviewScore` may range from 1 to 9 and is not the same as the percentage of positive reviews.  Refer to the list below for more information.  This filter is not applied to demos or playtests as they can't normally be reviewed.

<details>
  <summary>List of Review Scores</summary>

  Review Score | Description | # of Reviews | % of Positive Reviews 
  --- | --- | --- | ---
  1 | Overwhelmingly Negative | 500+ | 0%-19%
  2 | Very Negative | 50-499 | 0%-19%
  3 | Negative | 1-49 | 0%-19%
  4 | Mostly Negative | - | 20%-39%
  5 | Mixed | - | 40%-69%
  6 | Mostly Positive | - | 70%-79%
  7 | Positive | 1-49 | 80%-100%
  8 | Very Positive | 50-499 | 80%-100%
  8 | Very Positive | 500+ | 80%-94%
  9 | Overwhelmingly Positive | 500+ | 95%-100%
</details>

---

#### MinDaysOld

`uint` type with default value of `0`.  Packages must contain an app which was released on Steam within the last `MinDaysOld` days or they will not be added to your account.  You can leave this at `0` to not filter by release date.

---

#### IgnoredContentDescriptors

`HashSet<uint>` type with default value of `[]`.  Packages containing apps with any of the `ContentDescriptorIDs` specified here will not be added to your account.  Detailed information about content descriptors can be found [here](https://store.steampowered.com/account/preferences/) under "Mature Content Filtering".

<details>
  <summary>List of Content Descriptor IDs</summary>

  Descriptor ID | Description
  --- | ---
  1 | Some Nudity or Sexual Content
  2 | Frequent Violence or Gore
  3 | Adult Only Sexual Content
  4 | Frequent Nudity or Sexual Content
  5 | General Mature Content
</details>

---

#### IgnoredTypes

`HashSet<string>` type with default value of `["Demo"]`.  Packages containing apps with any of the `TypeNames` specified here will not be added to your account.  Refer to [Types](#types) for more information about `TypeNames`.

> [!NOTE]
> Demos are filtered out by default.  This is because Steam has at times removed all uninstalled demos from accounts.  If you'd like the plugin to activate demos, you can do so by setting `IgnoredTypes` to `[]`, or some other value that doesn't include `"Demo"`.

---

#### IgnoredTags

`HashSet<uint>` type with default value of `[]`.  Packages containing apps with any of these `TagIDs` will not be added to your account.  Refer to [Tags](#tags) for more information about `TagIDs`.

---

#### IgnoredCategories

`HashSet<uint>` type with default value of `[]`.  Packages containing apps with any of these `CategoryIDs` will not be added to your account.  Refer to [Categories](#categories) for more information about `CategoryIDs`.

---

#### IgnoredAppIDs

`HashSet<uint>` type with default value of `[]`.  Packages containing apps with any of these `AppIDs` will not be added to your account.

---

#### RequireAllTags

`bool` type with default value of `false`.  If set to `true`, packages must contain an app with **all** of the `TagIDs` specified in the [Tags](#tags) filter or they will not be added to your account.

---

#### RequireAllCategories

`bool` type with default value of `false`.  If set to `true`, packages must contain an app with **all** of the `CategoryIDs` specified in the [Categories](#categories) filter or they will not be added to your account.

---

#### ImportStoreFilters

`bool` type with default value of `false`.  If set to `true`, the filter will also use the ignored games, ignored tags, and ignored content descriptor settings you use on the Steam storefront.

---

#### WishlistOnly

`bool` type with default value of `false`.  If set to `true`, packages must contain an app your account has wishlisted or followed on the Steam storefront or they will not be added to your account.

---

#### IgnoreFreeWeekends

`bool` type with default value of `false`.  Free weekend packages will be ignored if set to `true`.

---

#### NoCostOnly

`bool` type with default value of `false`.  If set to `true`, only "No Cost" packages will be added to your account.  "No Cost" packages tend to be those which are free for only a limited time, and can also sometimes give a +1 to your owned games count.

---

#### PlaytestMode

`uint` type with default value of `0`.  Some or all playtests will be ignored based on the provided value.

<details>
  <summary>List of Playtest Modes</summary>

  Playtest Modes | Description
  --- | ---
  0 | Ignore all playtests
  1 | Include only unlimited playtests 
  2 | Include only limited playtests
  3 | Include all playtests
</details>

> [!NOTE]
> Only one of your bots may use the `PlaytestMode` filter option.  As some playtests have a limited number of slots, this is an artificial restriction I've put in place to limit how many slots a single person can occupy.

> [!NOTE]
> If you use `PauseFreePackagesWhilePlaying`, be aware that when it comes to limited playtests, the plugin cannot control when the playtest package is added to your account.  When or if this happens is decided by the game's developer, and so it's possible that a package will be added to your account while you're playing a game.

---

### Using multiple package filters

You can define as many filters as you'd like, and packages that pass any one of your filters will be added to your account.  For example, with the three filters below we can allow for any of:

  - Free games with Steam Trading Cards, but without nudity
  - Free games or playtests which have English or French language support, and Puzzle or Programming tags
  - Free DLC for games you own

```json
"FreePackagesFilters": [{
  "Types": ["Game"],
  "Categories": [29],
  "IgnoredContentDescriptors": [3, 4],
},{
  "Types": ["Game"],
  "Tags": [1664, 5432],
  "Languages": ["english", "french"],
  "PlaytestMode": 3,
},{
  "Types": ["DLC"],
  "IgnoredTypes": ["Game", "Application"],
}],
```

---

### Importing packages

While the plugin can be used passively, you can also manually import free packages from [SteamDB](https://steamdb.info/freepackages/) using [the importer userscript](https://github.com/Citrinate/FreePackages/tree/main/FreePackagesImporter), or through the commands and [IPC interface](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/IPC) endpoints below.

### Commands

Command | Access | Description
--- | --- | ---
`freepackages`|`FamilySharing`|Prints version of plugin.
`queuestatus [Bots]`|`Master`|Prints the status of the given bot's packages queue
`queuelicense [Bots] <Licenses>`|`Master`|Adds given `licenses`, explained [here](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Commands#addlicense-licenses), to the given bot's packages queue.  Playtests cannot be added to the package queue using this command
`queuelicense^ [Bots] <Licenses>`|`Master`|Adds given `licenses`, explained [here](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/Commands#addlicense-licenses), to the given bot's packages queue using that bot's package filters
`clearfreepackagesqueue [Bots]`|`Master`|Removes everything from the given bot's packages queue

#### Command Aliases

Command | Alias |
--- | --- |
`queuestatus`|`qstatus`
`queuestatus asf`|`qsa`
`queuelicense`|`queuelicence`, `qlicense`, `qlicence`
`queuelicense^`|`queuelicence^`, `qlicense^`, `qlicence^`

### IPC Interface

API | Method | Parameters | Description
--- | --- | --- | ---
`/Api/FreePackages/{botNames}/GetChangesSince/{changeNumber}`|`GET`| |Request changes for apps and packages since a given change number [^1]
`/Api/FreePackages/{botName}/GetOwnedApps`|`GET`|`showNames`|Retrieves all apps owned by the given bot
`/Api/FreePackages/{botName}/GetOwnedPackages`|`GET`| |Retrieves all packages owned by the given bot
`/Api/FreePackages/{botNames}/GetProductInfo`|`GET`|`appIDs`, `packageIDs`|Request product information for a list of apps or packages [^1]
`/Api/FreePackages/{botNames}/QueueLicenses`|`POST`|`appIDs`, `packageIDs`, `useFilter`|Adds the given appIDs and packageIDs to the given bot's package queue
`/Api/FreePackages/{botName}/RequestFreeAppLicense`|`GET`|`appIDs`|Request a free license for given appIDs
`/Api/FreePackages/{botName}/RequestFreeSubLicense`|`GET`|`subID`|Request a free license for given subID

[^1]: Responses are not dependent on the account used to make these requests.  You may provide multiple `botNames`, and the first available bot will be used to make the request.
