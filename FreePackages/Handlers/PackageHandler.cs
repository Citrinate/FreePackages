using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageHandler : IDisposable {
		internal readonly Bot Bot;
		internal readonly BotCache BotCache;
		internal readonly PackageFilter PackageFilter;
		private readonly PackageQueue PackageQueue;
		internal static ConcurrentDictionary<string, PackageHandler> Handlers = new();

		private static SemaphoreSlim AddHandlerSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim ProcessChangesSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim ProductInfoSemaphore = new SemaphoreSlim(1, 1);
		private const int ProductInfoLimitingDelaySeconds = 5;
		private const int ItemsPerProductInfoRequest = 255;

		private PackageHandler(Bot bot, BotCache botCache, FilterConfig filterConfig, uint? packageLimit) {
			Bot = bot;
			BotCache = botCache;
			PackageFilter = new PackageFilter(bot, botCache, filterConfig);
			PackageQueue = new PackageQueue(bot, botCache, packageLimit);
		}

		public void Dispose() {
			PackageQueue.Dispose();
		}

		internal static async Task AddHandler(Bot bot, FilterConfig? filterConfig, uint? packageLimit) {
			if (Handlers.ContainsKey(bot.BotName)) {
				Handlers[bot.BotName].Dispose();
				Handlers.TryRemove(bot.BotName, out PackageHandler? _);
			}

			await AddHandlerSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				filterConfig ??= new();

				if (filterConfig.PlaytestMode != EPlaytestMode.None) {
					// Only allow 1 bot to request playtests
					int numBotsThatIncludePlaytests = Handlers.Values.Where(x => x.PackageFilter.FilterConfig.PlaytestMode != EPlaytestMode.None).Count();
					if (numBotsThatIncludePlaytests > 0) {
						filterConfig.PlaytestMode = EPlaytestMode.None;
						bot.ArchiLogger.LogGenericInfo("Changed PlaytestMode to 0 (None), only 1 bot is allowed to use this filter");
					}
				}

				string cacheFilePath = Bot.GetFilePath(String.Format("{0}_{1}", bot.BotName, nameof(FreePackages)), Bot.EFileType.Database);
				BotCache? botCache = await BotCache.CreateOrLoad(cacheFilePath).ConfigureAwait(false);
				if (botCache == null) {
					bot.ArchiLogger.LogGenericError(String.Format(Strings.ErrorDatabaseInvalid, cacheFilePath));
					botCache = new(cacheFilePath);
				}

				Handlers.TryAdd(bot.BotName, new PackageHandler(bot, botCache, filterConfig, packageLimit));
			} finally {
				AddHandlerSemaphore.Release();
			}
		}

		internal static void OnAccountInfo(Bot bot, SteamUser.AccountInfoCallback callback) {
			if (!Handlers.ContainsKey(bot.BotName)) {
				return;
			}

			Handlers[bot.BotName].PackageFilter.UpdateAccountInfo(callback);
		}

		internal static async Task OnBotLoggedOn(Bot bot) {
			if (!Handlers.ContainsKey(bot.BotName)) {
				return;
			}
			
			await Handlers[bot.BotName].PackageFilter.UpdateUserData().ConfigureAwait(false);
		}

		internal async static Task OnPICSChanges(IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (Handlers.Count == 0) {
				return;	
			}

			// It's possible for a PICS change to effect thousands of apps and packages, Ex: https://steamdb.info/changelist/20445399/ (47,074 apps total, 31,529 packages total)
			// Store a list of changed apps/packages so that we can guarantee they'll all be processed eventually
			// Each bot has its own list, so that if any bots are offline, they'll be able to get caught up
			HashSet<uint> appIDs = appChanges.Select(x => x.Key).ToHashSet<uint>();
			HashSet<uint> packageIDs = packageChanges.Select(x => x.Key).ToHashSet<uint>();
			Handlers.Values.ToList().ForEach(x => x.BotCache.AddChanges(appIDs, packageIDs));

			await HandleChanges().ConfigureAwait(false);
		}

		internal async static Task OnPICSRestart(uint oldChangeNumber) {
			if (Handlers.Count == 0) {
				return;	
			}

			// ASF restarts PICS if either apps or packages needs a full update.  Check the old change number, as one of them might still be good.
			// TODO: search for the smallest valid change number
			SteamApps.PICSChangesCallback picsChanges;
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return;
				}

				picsChanges = await refreshBot.SteamApps.PICSGetChangesSince(oldChangeNumber, true, true).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return;
			}

			if (picsChanges.RequiresFullAppUpdate) {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free apps due to PICS restart");
			}
			if (picsChanges.RequiresFullPackageUpdate) {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free packages due to PICS restart");
			}

			await OnPICSChanges(picsChanges.AppChanges, picsChanges.PackageChanges).ConfigureAwait(false);
		}

		private async static Task<bool> IsReady(uint maxWaitTimeSeconds = 120) {
			DateTime timeoutTime = DateTime.Now.AddSeconds(maxWaitTimeSeconds);
			do {
				bool ready = Handlers.Values.Where(x => x.Bot.BotConfig.Enabled && !x.PackageFilter.Ready).Count() == 0;
				if (ready) {
					return true;
				}

				await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
			} while (maxWaitTimeSeconds == 0 || DateTime.Now < timeoutTime);

			return false;
		}

		internal async static Task HandleChanges() {
			if (!await ProcessChangesSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				await IsReady().ConfigureAwait(false);

				HashSet<uint> appIDs = Handlers.Values.Where(x => x.Bot.IsConnectedAndLoggedOn).SelectMany(x => x.BotCache.ChangedApps).ToHashSet<uint>();
				HashSet<uint> packageIDs = Handlers.Values.Where(x => x.Bot.IsConnectedAndLoggedOn).SelectMany(x => x.BotCache.ChangedPackages).ToHashSet<uint>();
				if (appIDs.Count == 0 && packageIDs.Count == 0) {
					return;
				}

				// Process the changes in batches of 255 items
				await GetProductInfo(appIDs, packageIDs, HandleProductInfo).ConfigureAwait(false);
			} finally {
				ProcessChangesSemaphore.Release();
			}
		}

		private async static Task HandleProductInfo(List<SteamApps.PICSProductInfoCallback> productInfo) {
			// Figure out which apps are free and add any wanted apps to the queue
			var apps = productInfo.SelectMany(static result => result.Apps.Values);
			if (apps.Count() != 0) {
				HashSet<uint> freeAppIDs = new();
				HashSet<uint> playtestAppIDs = new();
				HashSet<uint> playtestParentAppIDs = new();
				// Need to get the product info of the parent apps of playtests in order to apply filters
				// This first loop gets a list of these apps and also filters out any non-free packages
				foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in apps) {
					if (!PackageFilter.IsFreeApp(app) || !PackageFilter.IsAvailableApp(app)) {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: app.ID));

						continue;
					}

					KeyValue kv = app.KeyValues;
					EAppType type = kv["common"]["type"].AsEnum<EAppType>();
					if (type == EAppType.Beta) {
						playtestAppIDs.Add(app.ID);
						// There's another field: ["extended"]["betaforappid"], but it's less reliable
						// Ex: https://steamdb.info/app/2420490/ on Oct 17 2023 has "parent" and is redeemable, but doesn't have "betaforappid"
						uint parentAppID = kv["common"]["parent"].AsUnsignedInteger();
						if (parentAppID > 0) {
							playtestParentAppIDs.Add(parentAppID);
						}
					}

					freeAppIDs.Add(app.ID);
				}

				var playtestParentAppProductInfo = await GetProductInfo(appIDs: playtestParentAppIDs).ConfigureAwait(false);
				if (playtestParentAppProductInfo != null) {
					var playtestParentApps = playtestParentAppProductInfo.SelectMany(static result => result.Apps.Values);

					foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in apps.Where(x => freeAppIDs.Contains(x.ID))) {
						if (playtestAppIDs.Contains(app.ID)) {
							KeyValue kv = app.KeyValues;
							uint parentAppID = kv["common"]["parent"].AsUnsignedInteger();
							var parentApp = playtestParentApps.FirstOrDefault(x => x.ID == parentAppID);

							Handlers.Values.ToList().ForEach(x => x.HandlePlaytest(app, parentApp));
						} else {
							Handlers.Values.ToList().ForEach(x => x.HandleFreeApp(app));
						}
					}
				}
			}

			// Figure out which packages are free and add any wanted packages to the queue
			var packages = productInfo.SelectMany(static result => result.Packages.Values);
			if (packages.Count() != 0) {
				HashSet<uint> freePackageIDs = new();
				HashSet<uint> relatedAppIDs = new();
				// Need to get the product info of the apps that are contained in each free package in order to apply filters
				// This first loop gets a list of these apps and also filters out any non-free packages
				foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo package in packages) {
					if (!PackageFilter.IsFreePackage(package) || !PackageFilter.IsAvailablePackage(package)) {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: package.ID));

						continue;
					}

					KeyValue kv = package.KeyValues;
					var childAppIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger());
					
					relatedAppIDs.UnionWith(childAppIDs);
					freePackageIDs.Add(package.ID);
				}

				var relatedAppProductInfo = await GetProductInfo(appIDs: relatedAppIDs).ConfigureAwait(false);
				if (relatedAppProductInfo != null) {
					var relatedApps = relatedAppProductInfo.SelectMany(static result => result.Apps.Values);

					foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo package in packages.Where(x => freePackageIDs.Contains(x.ID))) {
						KeyValue kv = package.KeyValues;
						var childAppIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger()).ToHashSet<uint>();
						var childApps = relatedApps.Where(x => childAppIDs.Contains(x.ID));
						
						if (!PackageFilter.IsAvailablePackageContents(package, childApps)) {
							Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: package.ID));

							continue;
						}

						Handlers.Values.ToList().ForEach(x => x.HandleFreePackage(package, childApps));
					}
				}
			}

			// Remove invalid apps from the app change list
			foreach (uint unknownAppID in productInfo.SelectMany(static result => result.UnknownApps)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: unknownAppID));
			}

			// Remove invalid packages from the package change list
			foreach (uint unknownPackageID in productInfo.SelectMany(static result => result.UnknownPackages)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: unknownPackageID));
			}

			// Save changes to the app/package change lists
			Handlers.Values.ToList().ForEach(x => x.BotCache.SaveChanges());
		}

		private async static Task<List<SteamApps.PICSProductInfoCallback>?> GetProductInfo(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null, Func<List<SteamApps.PICSProductInfoCallback>, Task>? onFetchProductInfoCallback = null) {
			List<SteamApps.PICSProductInfoCallback> productInfo = new();

			if (appIDs != null) {
				for (int i = 0; i < Math.Ceiling((decimal) appIDs.Count / ItemsPerProductInfoRequest); i++) {
					HashSet<uint> batchAppIDs = appIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();
					
					List<SteamApps.PICSProductInfoCallback>? partialProductInfo = await FetchProductInfo(appIDs: batchAppIDs).ConfigureAwait(false);
					if (partialProductInfo == null) {
						return null;
					}

					// If I'm processing package info in batches using the callback, then I don't care what this function returns
					if (onFetchProductInfoCallback != null) {
						await onFetchProductInfoCallback(partialProductInfo).ConfigureAwait(false);
					} else {
						productInfo = productInfo.Concat(partialProductInfo).ToList();
					}
				}
			}

			if (packageIDs != null) {
				for (int i = 0; i < Math.Ceiling((decimal) packageIDs.Count / ItemsPerProductInfoRequest); i++) {
					HashSet<uint> batchPackageIDs = packageIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();

					List<SteamApps.PICSProductInfoCallback>? partialProductInfo = await FetchProductInfo(packageIDs: batchPackageIDs).ConfigureAwait(false);
					if (partialProductInfo == null) {
						return null;
					}

					// If I'm processing package info in batches using the callback, then I don't care what this function returns
					if (onFetchProductInfoCallback != null) {
						await onFetchProductInfoCallback(partialProductInfo).ConfigureAwait(false);
					} else {
						productInfo.Concat(partialProductInfo);
					}
				}
			}

			return productInfo;
		}

		private async static Task<List<SteamApps.PICSProductInfoCallback>?> FetchProductInfo(IEnumerable<uint>? appIDs = null, IEnumerable<uint>? packageIDs = null) {
			await ProductInfoSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return null;
				}

				var apps = appIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : appIDs.Select(x => new SteamApps.PICSRequest(x));
				var packages = packageIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : packageIDs.Select(x => new SteamApps.PICSRequest(x));
				var response = await refreshBot.SteamApps.PICSGetProductInfo(apps, packages).ToLongRunningTask().ConfigureAwait(false);

				return response.Results?.ToList();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(ProductInfoLimitingDelaySeconds)).ConfigureAwait(false);
						ProductInfoSemaphore.Release();
					}
				);
			}
		}

		private void HandleFreeApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			if (!BotCache.ChangedApps.Contains(app.ID)) {
				return;
			}

			if (!PackageFilter.Ready) {
				return;
			}

			try {
				if (!PackageFilter.IsRedeemableApp(app)) {
					return;
				}

				if (!PackageFilter.IsWantedApp(app)) {
					return;
				}

				if (PackageFilter.IsIgnoredApp(app)) {
					return;
				}
			
				PackageQueue.AddPackage(new Package(EPackageType.App, app.ID));				
			} finally {
				BotCache.RemoveChange(appID: app.ID);
			}
		}

		private void HandleFreePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package, IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> apps) {
			if (!BotCache.ChangedPackages.Contains(package.ID)) {
				return;
			}

			if (!PackageFilter.Ready) {
				return;
			}

			try {
				if (!PackageFilter.IsRedeemablePackage(package, apps)) {
					return;
				}

				if (!PackageFilter.IsWantedPackage(package, apps)) {
					return;
				}

				if (PackageFilter.IsIgnoredPackage(package, apps)) {
					return;
				}

				KeyValue kv = package.KeyValues;
				ulong startTime = kv["extended"]["starttime"].AsUnsignedLong();
				HashSet<uint> appIDs = apps.Select(x => x.ID).ToHashSet<uint>();
				PackageQueue.AddPackage(new Package(EPackageType.Sub, package.ID, startTime), appIDs);
			} finally {
				BotCache.RemoveChange(packageID: package.ID);
			}
		}

		private void HandlePlaytest(SteamApps.PICSProductInfoCallback.PICSProductInfo app, SteamApps.PICSProductInfoCallback.PICSProductInfo? parentApp) {
			if (!BotCache.ChangedApps.Contains(app.ID)) {
				return;
			}

			if (!PackageFilter.Ready) {
				return;
			}

			try {
				if (parentApp == null) {
					return;
				}

				if (!PackageFilter.IsRedeemableApp(app)) {
					return;
				}

				if (!PackageFilter.IsRedeemablePlaytest(app, parentApp)) {
					return;
				}

				if (!PackageFilter.IsWantedPlaytest(app, parentApp)) {
					return;
				}

				if (PackageFilter.IsIgnoredPlaytest(app, parentApp)) {
					return;
				}

				PackageQueue.AddPackage(new Package(EPackageType.Playtest, parentApp.ID));
			} finally {
				BotCache.RemoveChange(appID: app.ID);
			}
		}

		internal string GetStatus() {
			return PackageQueue.GetStatus();
		}

		internal string ClearQueue() {
			int numPackages = BotCache.Packages.Count;
			int numChangedApps = BotCache.ChangedApps.Count;
			int numChangedPackages = BotCache.ChangedPackages.Count;

			if (numPackages == 0 && numChangedApps == 0 && numChangedPackages == 0) {
				return "Queue is empty";
			}

			BotCache.Clear();

			HashSet<string> responses = new HashSet<string>();
			
			if (numPackages > 0) {
				responses.Add(String.Format("{0} free packages removed.", numPackages));
			}
			if (numChangedApps > 0) {
				responses.Add(String.Format("{0} discovered apps removed.", numChangedApps));
			}
			if (numChangedPackages > 0) {
				responses.Add(String.Format("{0} discovered packages removed.", numChangedPackages));
			}

			return String.Join(" ", responses);
		}

		internal string AddPackage(EPackageType type, uint id, bool useFilter) {
			if (useFilter) {
				if (type == EPackageType.App) {
					BotCache.AddChanges(appIDs: new HashSet<uint> {id});

					return String.Format("Added app/{0} to discovered apps queue", id);
				} else {
					BotCache.AddChanges(packageIDs: new HashSet<uint> {id});

					return String.Format("Added sub/{0} to discovered packages queue", id);
				}
			}

			PackageQueue.AddPackage(new Package(type, id));

			if (type == EPackageType.App) {
				return String.Format("Added app/{0} to free packages queue", id);
			} else {
				return String.Format("Added sub/{0} to free packages queue", id);
			}
		}

		internal void AddPackages(HashSet<uint>? appIDs, HashSet<uint>? packageIDs, bool useFilter) {
			if (useFilter) {
				BotCache.AddChanges(appIDs, packageIDs);

				return;
			}

			HashSet<Package> packages = new();
			if (appIDs != null) {
				packages.UnionWith(appIDs.Select(static id => new Package(EPackageType.App, id)));
			}
			if (packageIDs != null) {
				packages.UnionWith(packageIDs.Select(static id => new Package(EPackageType.Sub, id)));
			}

			PackageQueue.AddPackages(packages);
		}

		private static Bot? GetRefreshBot() => Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
	}
}