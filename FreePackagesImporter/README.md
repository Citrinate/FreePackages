# Free Packages Importer

This userscript lets you transfer packages from SteamDB's [free packages tool](https://steamdb.info/freepackages/) to the [Free Packages ASF plugin](https://github.com/Citrinate/FreePackages).

## Installation

1. Install a userscript manager like [Tampermonkey](https://www.tampermonkey.net/), [Greasemonkey](https://addons.mozilla.org/en-US/firefox/addon/greasemonkey/), or [Violentmonkey](https://violentmonkey.github.io/)
2. Go [here](https://raw.githubusercontent.com/Citrinate/FreePackages/main/FreePackagesImporter/code.user.js) and click "Install"
3. Make sure that you have:
    - ArchiSteamFarm with [IPC](https://github.com/JustArchiNET/ArchiSteamFarm/wiki/IPC) enabled (which is the default)
    - The [Free Packages plugin](https://github.com/Citrinate/FreePackages) (v1.1.0 or newer)
    - At least one bot with the plugin [enabled](https://github.com/Citrinate/FreePackages#enabling-the-plugin)

## Usage

The userscript will add an additional element to SteamDB's [free packages tool](https://steamdb.info/freepackages/) where you can choose to send all of the packages shown on SteamDB to one or all of your ASF bots.

![Interface](https://raw.githubusercontent.com/Citrinate/FreePackages/main/FreePackagesImporter/Screenshots/interface.png)

If you use non-default IPC settings, you can click on "Settings" to change how the userscript connects to ASF.

Here you can also control whether or not the packages sent to the plugin will be [filtered](https://github.com/Citrinate/FreePackages#enabling-package-filters).  If not filtered, the plugin will attempt to activate all packages sent to it.

![Settings](https://raw.githubusercontent.com/Citrinate/FreePackages/main/FreePackagesImporter/Screenshots/settings.png)
