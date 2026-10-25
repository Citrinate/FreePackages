using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;

namespace FreePackages {
	internal static class FullScan {
		private const string FlagFileName = "FreePackages.fullscan";
		private static readonly SemaphoreSlim RunSemaphore = new(1, 1);
		private static readonly HttpClient AppListClient = new() { Timeout = TimeSpan.FromMinutes(10) };

		internal static bool Requested { get; private set; }
		internal static bool InProgress { get; private set; }
		internal static bool ShouldWait => Requested || InProgress;

		internal static void OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties) {
			bool asfFlag = false;
			if (additionalConfigProperties != null) {
				foreach (KeyValuePair<string, JsonElement> property in additionalConfigProperties) {
					if ((property.Key == "FreePackagesFullScan") && ((property.Value.ValueKind == JsonValueKind.True) || (property.Value.ValueKind == JsonValueKind.False))) {
						asfFlag = property.Value.GetBoolean();
					}
				}
			}

			FullScanConfig config = ReadConfig();
			bool fileFlag = config.FullScan;

			if (fileFlag) {
				Requested = true;
				WriteConfig(new FullScanConfig { FullScan = false, ASFFlagConsumed = asfFlag });
				ASF.ArchiLogger.LogGenericInfo("FreePackages full scan requested from FreePackages.fullscan; flag cleared. Activations wait until library and catalog seed finish.");

				return;
			}

			if (asfFlag && !config.ASFFlagConsumed) {
				Requested = true;
				WriteConfig(new FullScanConfig { FullScan = false, ASFFlagConsumed = true });
				ASF.ArchiLogger.LogGenericInfo("FreePackages full scan requested from ASF.json FreePackagesFullScan; consumed without rewriting ASF.json. Activations wait until library and catalog seed finish.");

				return;
			}

			if (!asfFlag && config.ASFFlagConsumed) {
				WriteConfig(new FullScanConfig { FullScan = false, ASFFlagConsumed = false });
			}
		}

		internal static string RequestNow() {
			if (InProgress) {
				return "Full scan is already running.";
			}

			Requested = true;
			FullScanConfig config = ReadConfig();
			WriteConfig(new FullScanConfig { FullScan = false, ASFFlagConsumed = config.ASFFlagConsumed });
			Utilities.InBackground(async () => await TryStart().ConfigureAwait(false));

			return "Full scan requested. Activations pause until the library and catalog seed finishes; the Steam app list continues in the background.";
		}

		internal static async Task TryStart() {
			if (!Requested) {
				return;
			}

			if (!await RunSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				if (!Requested || InProgress) {
					return;
				}

				InProgress = true;
				Requested = false;

				await Run().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			} finally {
				InProgress = false;
				RunSemaphore.Release();
			}
		}

		private static async Task Run() {
			ASF.ArchiLogger.LogGenericInfo("FreePackages full scan started.");

			if (!await PackageHandler.WaitForLoggedOnBots().ConfigureAwait(false)) {
				ASF.ArchiLogger.LogGenericWarning("FreePackages full scan aborted: no ready bots.");

				return;
			}

			SeedLibrary();
			await ASFInfo.RefreshCatalog(processChanges: false).ConfigureAwait(false);
			await PackageHandler.HandleChangesBlocking().ConfigureAwait(false);

			ASF.ArchiLogger.LogGenericInfo("FreePackages library and catalog seed finished; activations may start. Steam app list continues in the background.");

			Utilities.InBackground(static async () => await ScanSteamAppList().ConfigureAwait(false));
		}

		private static void SeedLibrary() {
			foreach (PackageHandler handler in PackageHandler.Handlers.Values) {
				if (!handler.Bot.IsConnectedAndLoggedOn || !handler.PackageFilter.Ready) {
					continue;
				}

				HashSet<uint> ownedPackages = handler.Bot.OwnedPackages.Keys.ToHashSet();
				ownedPackages.UnionWith(handler.PackageFilter.GetOwnedPackageIDs());
				if (ownedPackages.Count == 0) {
					continue;
				}

				handler.BotCache.AddChanges(newOwnedPackageIDs: ownedPackages);
				ASF.ArchiLogger.LogGenericInfo($"FreePackages library seed for {handler.Bot.BotName}: {ownedPackages.Count} packages.");
			}
		}

		private static async Task ScanSteamAppList() {
			try {
				HashSet<uint>? appIDs = await FetchSteamAppIDs().ConfigureAwait(false);
				if ((appIDs == null) || (appIDs.Count == 0)) {
					ASF.ArchiLogger.LogGenericWarning("FreePackages Steam app list fetch failed or empty.");

					return;
				}

				ASF.ArchiLogger.LogGenericInfo($"FreePackages Steam app list: {appIDs.Count} apps. Queueing in chunks...");

				const int chunkSize = 2000;
				List<uint> ordered = appIDs.ToList();
				for (int i = 0; i < ordered.Count; i += chunkSize) {
					HashSet<uint> chunk = ordered.Skip(i).Take(chunkSize).ToHashSet();
					foreach (PackageHandler handler in PackageHandler.Handlers.Values) {
						if (handler.Bot.IsConnectedAndLoggedOn) {
							handler.BotCache.AddChanges(appIDs: chunk);
						}
					}

					await PackageHandler.HandleChangesBlocking().ConfigureAwait(false);
					ASF.ArchiLogger.LogGenericInfo($"FreePackages Steam app list progress: {Math.Min(i + chunkSize, ordered.Count)}/{ordered.Count}");
				}

				ASF.ArchiLogger.LogGenericInfo("FreePackages Steam app list scan finished.");
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		private static async Task<HashSet<uint>?> FetchSteamAppIDs() {
			try {
				Uri requestUri = new("https://api.steampowered.com/ISteamApps/GetAppList/v2/");
				if (ASF.WebBrowser != null) {
					var webResponse = await ASF.WebBrowser.UrlGetToJsonObject<SteamAppListResponse>(requestUri).ConfigureAwait(false);
					if (webResponse?.Content?.Applist?.Apps != null) {
						return webResponse.Content.Applist.Apps.Where(static app => app.AppID > 0).Select(static app => app.AppID).ToHashSet();
					}
				}

				await using Stream stream = await AppListClient.GetStreamAsync(requestUri).ConfigureAwait(false);
				SteamAppListResponse? parsed = await JsonSerializer.DeserializeAsync<SteamAppListResponse>(stream).ConfigureAwait(false);
				if (parsed?.Applist?.Apps == null) {
					return null;
				}

				return parsed.Applist.Apps.Where(static app => app.AppID > 0).Select(static app => app.AppID).ToHashSet();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			}
		}

		private static string FlagPath => Path.Combine(SharedInfo.ConfigDirectory, FlagFileName);

		private static FullScanConfig ReadConfig() {
			try {
				if (!File.Exists(FlagPath)) {
					return new FullScanConfig();
				}

				string json = File.ReadAllText(FlagPath);
				return json.ToJsonObject<FullScanConfig>() ?? new FullScanConfig();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return new FullScanConfig();
			}
		}

		private static void WriteConfig(FullScanConfig config) {
			try {
				File.WriteAllText(FlagPath, config.ToJsonText(writeIndented: true));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);
			}
		}

		private sealed class FullScanConfig {
			[JsonPropertyName("FullScan")]
			[JsonInclude]
			internal bool FullScan { get; set; }

			[JsonPropertyName("ASFFlagConsumed")]
			[JsonInclude]
			internal bool ASFFlagConsumed { get; set; }
		}

		private sealed class SteamAppListResponse {
			[JsonPropertyName("applist")]
			public SteamAppList? Applist { get; set; }
		}

		private sealed class SteamAppList {
			[JsonPropertyName("apps")]
			public List<SteamAppEntry>? Apps { get; set; }
		}

		private sealed class SteamAppEntry {
			[JsonPropertyName("appid")]
			public uint AppID { get; set; }
		}
	}
}
