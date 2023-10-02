# Free Package Plugin for ArchiSteamFarm

## Introduction

This plugin discovers free packages on Steam and activates them on your account.

This plugin works by listening for changes to Steam's [PICS](https://steamdb.info/faq/#pics).  Since PICs doesn't show changes that happened before a certain time, the plugin may not discover all previously released free packages.  It's also possible to miss newly released free packages if your bot is logged out for extended periods of time.

## Installation

- Download the .zip file from the [latest release](https://github.com/Citrinate/FreePackages/releases/latest)
- Unpack the downloaded .zip file to the `plugins` folder inside your ASF folder.
- (Re)start ASF, you should get a message indicating that the plugin loaded successfully.

> Please note, this plugin is only tested to work with ASF-generic.  It may or may not work with other ASF variants.

## Usage

### Enabling the plugin

You can enable the plugin per individual bot by adding `EnableFreePackages` to that bot's config file.

Example: `"EnableFreePackages": true,`

> `bool` type with default value of `false`.

---

### Changing the hourly package limit

A maximum of 50 free packages can be activated per hour.  By default, this plugin will use at most 40 of those hourly activations and will resume where it left off if it's ever interrupted.  You can control this limit by adding `FreePackagesPerHour` to your individual bot's config files.

Example: `"FreePackagesPerHour": 42,`

> Note: 40 is used as the default to give you the ability to redeem free packages manually without having to fight against the plugin.  It's also not always possible to the plugin to identify when it's being rate limited, and so it's better to just never get rate limited.  For these reasons, I don't recommend setting this to 50.

> `uint` type with default value of 40.

---

### Enabling package filters

By default, the plugin will attempt to activate all free packages.  You can control what kinds of packages are activated by adding `FreePackagesFilter` to your individual bot's config files with the following structure:

```
"FreePackagesFilter": {
    "ImportStoreFilters": false,
    "Categories": [],
    "Tags": [],
    "IgnoredTypes": [],
    "IgnoredTags": [],
    "IgnoredContentDescriptors": [],
    "IgnoredAppIDs": [],
    "IgnoreFreeWeekends": false,
},
```

All filter options are explained below:

---

#### ImportStoreFilters

`"ImportStoreFilters": <true/false>`

Example: `"ImportStoreFilters": true,`

If set to `true`, the plugin will use the ignored games, ignored tags, and ignored content descriptors settings you use on the Steam storefront, in addition to any other package filters you define.

> `bool` type with default value of `false`.

---

#### Categories

`"Categories": [<CategoryIDs>]`

Example: `"Categories": [1, 22],`

Packages must contain an app with at least one of these `CategoryIDs` or they will not be added to your account.  You can leave this empty to allow for all categories.

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

> `ImmutableHashSet<uint>` type with default value of being empty.

---

#### Tags

`"Tags": [<TagIDs>]`

Example: `"Tags": [492, 1664, 5432],`

Packages must contain an app with at least one of these `TagIDs` or they will not be added to your account.  You can leave this empty to allow for all tags.

A list of tags can be found [here](https://steamdb.info/tags/).  The tag's ID will be at the end of the URL.  For example, the ID for the [Indie](https://steamdb.info/tag/492/) tag is 492.

> Note: The "Profile Features Limited" tag presented by SteamDB is not a real tag that Steam uses.  There is no way for this plugin to detect whether or not an app has limited profile features.

> `ImmutableHashSet<uint>` type with default value of being empty.

---

#### IgnoredTypes

`"IgnoredTypes": [<TypeNames>]`

Example: `"IgnoredTypes": ["Demo", "Application"],`

Packages containing apps with any of the `TypeNames` specified here will not be added to your account.

The available types for filtering are: `Game`, `Application`, `Tool`, `Demo`, `DLC`, `Music`

> `ImmutableHashSet<string>` type with default value of being empty.

---

#### IgnoredTags

`"IgnoredTags": [<TagIDs>]`

Example: `"IgnoredTags": [4085],`

Packages containing apps with any of these `TagIDs` will not be added to your account.

Refer to [Tags](#tags) for more information about `TagIDs`.

> `HashSet<uint>` type with default value of being empty.

---

#### IgnoredContentDescriptors

`"IgnoredTags": [<ContentDescriptionIDs>]`

Example: `"IgnoredContentDescriptors": [3, 4],`

Packages containing apps with any of the `ContentDescriptionIDs` specified here will not be added to your account.

A list of content descriptors is provided below.  More detailed information about content descriptors can be found [here](https://store.steampowered.com/account/preferences/) under "Mature Content Filtering".

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

> `HashSet<uint>` type with default value of being empty.

---

#### IgnoredAppIDs

`"IgnoredAppIDs": [<AppIDs>]`

Example: `"IgnoredAppIDs": [440, 730],`

Packages containing apps with any of these IDs will not be added to your account.

> `HashSet<uint>` type with default value of being empty.

---

#### IgnoreFreeWeekends

`"IgnoreFreeWeekends": <true/false>`

Example: `"IgnoreFreeWeekends": true,`

Free weekend packages will be ignored if set to `true`.

> `bool` type with default value of `false`.

---

## IPC Interface

API | Method | Parameters | Description
--- | --- | --- | ---
`/Api/FreePackages/{botName}/GetChangesSince`|`GET`|`changeNumber`|Request changes for apps and packages since a given change number
`/Api/FreePackages/{botName}/GetOwnedPackages`|`GET`| |Retrieves all packages owned by the given bot
`/Api/FreePackages/{botName}/GetProductInfo`|`GET`|`appIDs`, `packageIDs`|Request product information for a list of apps or packages
`/Api/FreePackages/{botName}/RequestFreeAppLicense`|`GET`|`appIDs`|Request a free license for given appids
`/Api/FreePackages/{botName}/RequestFreeSubLicense`|`GET`|`subID`|Request a free license for given subid
