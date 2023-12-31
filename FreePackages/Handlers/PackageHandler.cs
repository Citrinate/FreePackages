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

		private readonly Timer UserDataRefreshTimer;
		private static SemaphoreSlim AddHandlerSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim ProcessChangesSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim ProductInfoSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim PICSChangesSemaphore = new SemaphoreSlim(1, 1);
		private const int ProductInfoLimitingDelaySeconds = 5;
		private const int PICSChangesLimitingDelaySeconds = 5;
		private const int ItemsPerProductInfoRequest = 255;

		private PackageHandler(Bot bot, BotCache botCache, List<FilterConfig> filterConfigs, uint? packageLimit) {
			Bot = bot;
			BotCache = botCache;
			PackageFilter = new PackageFilter(botCache, filterConfigs);
			PackageQueue = new PackageQueue(bot, botCache, packageLimit);
			UserDataRefreshTimer = new Timer(async e => await FetchUserData().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
		}

		public void Dispose() {
			PackageQueue.Dispose();
			UserDataRefreshTimer.Dispose();
		}

		internal static async Task AddHandler(Bot bot, List<FilterConfig> filterConfigs, uint? packageLimit) {
			if (Handlers.ContainsKey(bot.BotName)) {
				Handlers[bot.BotName].Dispose();
				Handlers.TryRemove(bot.BotName, out PackageHandler? _);
			}

			await AddHandlerSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				if (filterConfigs.Any(filterConfig => filterConfig.PlaytestMode != EPlaytestMode.None)) {
					// Only allow 1 bot to request playtests
					// int numBotsThatIncludePlaytests = Handlers.Values.Where(x => x.PackageFilter.FilterConfig.PlaytestMode != EPlaytestMode.None).Count();
					int numBotsThatIncludePlaytests = Handlers.Values.Where(x => x.PackageFilter.FilterConfigs.Any(filterConfig => filterConfig.PlaytestMode != EPlaytestMode.None)).Count();
					if (numBotsThatIncludePlaytests > 0) {
						filterConfigs.ForEach(filterConfig => filterConfig.PlaytestMode = EPlaytestMode.None);
						bot.ArchiLogger.LogGenericInfo("Changed PlaytestMode to 0 (None), only 1 bot is allowed to use this filter");
					}
				}

				string cacheFilePath = Bot.GetFilePath(String.Format("{0}_{1}", bot.BotName, nameof(FreePackages)), Bot.EFileType.Database);
				BotCache? botCache = await BotCache.CreateOrLoad(cacheFilePath).ConfigureAwait(false);
				if (botCache == null) {
					bot.ArchiLogger.LogGenericError(String.Format(Strings.ErrorDatabaseInvalid, cacheFilePath));
					botCache = new(cacheFilePath);
				}

				Handlers.TryAdd(bot.BotName, new PackageHandler(bot, botCache, filterConfigs, packageLimit));
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

		internal static void OnLicenseList(Bot bot, SteamApps.LicenseListCallback callback) {
			if (!Handlers.ContainsKey(bot.BotName)) {
				return;
			}

			Handlers[bot.BotName].HandleLicenseList(callback);
		}

		internal static async Task OnBotLoggedOn(Bot bot) {
			if (!Handlers.ContainsKey(bot.BotName)) {
				return;
			}
			
			await Handlers[bot.BotName].FetchUserData().ConfigureAwait(false);
		}

		internal static void OnPICSChanges(IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (Handlers.Count == 0) {
				return;	
			}

			// It's possible for a PICS change to effect thousands of apps and packages, Ex: https://steamdb.info/changelist/20445399/ (47,074 apps total, 31,529 packages total)
			// Store a list of changed apps/packages so that we can guarantee they'll all be processed eventually
			// Each bot has its own list, so that if any bots are offline, they'll be able to get caught up
			HashSet<uint> appIDs = appChanges.Select(x => x.Key).ToHashSet<uint>();
			HashSet<uint> packageIDs = packageChanges.Select(x => x.Key).ToHashSet<uint>();
			Handlers.Values.ToList().ForEach(x => x.BotCache.AddChanges(appIDs, packageIDs));

			Utilities.InBackground(async() => await HandleChanges().ConfigureAwait(false));
		}

		internal async static Task OnPICSRestart(uint oldChangeNumber) {
			if (Handlers.Count == 0) {
				return;	
			}

			// ASF restarts PICS if either apps or packages needs a full update.  Check the old change number, as one of them might still be good.
			SteamApps.PICSChangesCallback? picsChanges = await FetchPICSChanges(oldChangeNumber, sendAppChangeList: false, sendPackageChangeList: true).ConfigureAwait(false);
			if (picsChanges == null) {
				return;
			}

			if (!picsChanges.RequiresFullAppUpdate) {
				OnPICSChanges(picsChanges.AppChanges, new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>());
			} else {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free apps due to PICS restart");
				
				// Search for the oldest change number which is still valid for apps
				var appChanges = await FindOldestPICSChanges(oldChangeNumber + 1, picsChanges.CurrentChangeNumber, findApps: true);
				if (appChanges != null) {
					ASF.ArchiLogger.LogGenericDebug(String.Format("Recovered {0} app changes at change number {1}", appChanges.AppChanges.Count, appChanges.LastChangeNumber + 1));

					OnPICSChanges(appChanges.AppChanges, new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>());
				}
			}

			if (!picsChanges.RequiresFullPackageUpdate) {
				OnPICSChanges(new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>(), picsChanges.PackageChanges);
			} else {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free packages due to PICS restart");

				// Search for the oldest change number which is still valid for packages
				var packageChanges = await FindOldestPICSChanges(oldChangeNumber + 1, picsChanges.CurrentChangeNumber, findApps: false);
				if (packageChanges != null) {
					ASF.ArchiLogger.LogGenericDebug(String.Format("Recovered {0} package changes at change number {1}", packageChanges.PackageChanges.Count, packageChanges.LastChangeNumber + 1));

					OnPICSChanges(new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>(), packageChanges.PackageChanges);
				}
			}
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
				HashSet<uint> newOwnedPackageIDs = Handlers.Values.Where(x => x.Bot.IsConnectedAndLoggedOn).SelectMany(x => x.BotCache.NewOwnedPackages).ToHashSet<uint>();
				packageIDs.UnionWith(newOwnedPackageIDs);

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
			var appProductInfos = productInfo.SelectMany(static result => result.Apps.Values);
			if (appProductInfos.Count() > 0) {
				List<FilterableApp> apps = appProductInfos.Select(x => new FilterableApp(x)).ToList();

				// Filter out non-free apps
				apps.RemoveAll(app => {
					if (!app.IsFree() || !app.IsAvailable()) {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: app.ID));

						return true;
					}

					return false;
				});

				// Get the parents of the free apps
				HashSet<uint> parentIDs = apps.Where(app => app.ParentID != null).Select(app => app.ParentID!.Value).ToHashSet();
				var parentProductInfos = (await GetProductInfo(appIDs: parentIDs).ConfigureAwait(false))?.SelectMany(static result => result.Apps.Values);				
				if (parentProductInfos == null) {
					ASF.ArchiLogger.LogNullError(parentProductInfos);

					return;
				}

				if (parentProductInfos.Count() > 0) {
					apps.ForEach(app => {
						if (app.ParentID != null) {
							app.AddParent(parentProductInfos.FirstOrDefault(parent => parent.ID == app.ParentID));
						}
					});
				}

				// Add wanted apps to the queue
				apps.ForEach(app => {
					if (app.Type == EAppType.Beta) {
						Handlers.Values.ToList().ForEach(x => x.HandlePlaytest(app));
					} else {
						Handlers.Values.ToList().ForEach(x => x.HandleFreeApp(app));
					}
				});
			}

			// Figure out which packages are free and add any wanted packages to the queue
			var packageProductInfos = productInfo.SelectMany(static result => result.Packages.Values);
			if (packageProductInfos.Count() > 0) {
				HashSet<uint> newOwnedPackageIDs = Handlers.Values.Where(x => x.Bot.IsConnectedAndLoggedOn).SelectMany(x => x.BotCache.NewOwnedPackages).ToHashSet<uint>();
				List<FilterablePackage> packages = packageProductInfos.Select(x => new FilterablePackage(x, newOwnedPackageIDs.Contains(x.ID))).ToList();

				// Filter out non-free, non-new packages
				packages.RemoveAll(package => {
					if (!package.IsFree() || !package.IsAvailable()) {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: package.ID));

						if (!package.IsNew) {
							return true;
						}
					}

					return false;
				});

				// Get the apps contained in each package
				HashSet<uint> packageContentsIDs = packages.SelectMany(package => package.PackageContentIDs).ToHashSet();
				var packageContentProductInfos = (await GetProductInfo(appIDs: packageContentsIDs).ConfigureAwait(false))?.SelectMany(static result => result.Apps.Values);
				if (packageContentProductInfos == null) {
					ASF.ArchiLogger.LogNullError(packageContentProductInfos);

					return;
				}

				packages.ForEach(package => package.AddPackageContents(packageContentProductInfos.Where(x => package.PackageContentIDs.Contains(x.ID))));

				// Filter out any packages which contain unavailable apps
				packages.RemoveAll(package => {
					if (!package.IsAvailablePackageContents()) {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: package.ID));

						if (!package.IsNew) {
							return true;
						}
					}

					return false;
				});

				// Get the parents for the apps in each package
				HashSet<uint> parentIDs = packages.SelectMany(package => package.PackageContentParentIDs).ToHashSet();
				var parentProductInfos = (await GetProductInfo(appIDs: parentIDs).ConfigureAwait(false))?.SelectMany(static result => result.Apps.Values);				
				if (parentProductInfos == null) {
					ASF.ArchiLogger.LogNullError(parentProductInfos);

					return;
				}

				if (parentProductInfos.Count() > 0) {
					packages.ForEach(package => {
						if (package.PackageContentParentIDs.Count != 0) {
							package.AddPackageContentParents(parentProductInfos.Where(parent => package.PackageContentParentIDs.Contains(parent.ID)));
						}
					});
				}

				// Add wanted packages to the queue or check new packages for free DLC
				packages.ForEach(package => {
					if (package.IsNew) {
						Handlers.Values.ToList().ForEach(x => x.HandleNewPackage(package));
					} else {
						Handlers.Values.ToList().ForEach(x => x.HandleFreePackage(package));
					}
				});
			}

			// Remove invalid apps from the app change list
			foreach (uint unknownAppID in productInfo.SelectMany(static result => result.UnknownApps)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: unknownAppID));
			}

			// Remove invalid packages from the package change list
			foreach (uint unknownPackageID in productInfo.SelectMany(static result => result.UnknownPackages)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: unknownPackageID));
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(newOwnedPackageID: unknownPackageID));
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
				var packages = packageIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : packageIDs.Select(x => new SteamApps.PICSRequest(x, ASF.GlobalDatabase?.PackageAccessTokensReadOnly.GetValueOrDefault(x, (ulong) 0) ?? 0));
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

		private async static Task<SteamApps.PICSChangesCallback?> FindOldestPICSChanges(uint minValidChangeNumber, uint maxValidChangeNumber, bool findApps) {
			if (minValidChangeNumber >= maxValidChangeNumber) {
				return null;
			}

			bool sendAppChangeList = findApps;
			bool sendPackageChangeList = !findApps;
			uint changeNumber = maxValidChangeNumber - ((uint) Math.Floor((maxValidChangeNumber - minValidChangeNumber) / 2.0));
			SteamApps.PICSChangesCallback? oldestPicsChanges = null;
			
			do {
				SteamApps.PICSChangesCallback? picsChanges = await FetchPICSChanges(changeNumber, sendAppChangeList, sendPackageChangeList).ConfigureAwait(false);
				if (picsChanges == null) {
					break;
				}

				bool isValid = (findApps && !picsChanges.RequiresFullAppUpdate) || (!findApps && !picsChanges.RequiresFullPackageUpdate);
				if (isValid) {
					oldestPicsChanges = picsChanges;
					maxValidChangeNumber = changeNumber;
				} else {
					minValidChangeNumber = changeNumber;
				}

				changeNumber = maxValidChangeNumber - Math.Max(1, ((uint) Math.Floor((maxValidChangeNumber - minValidChangeNumber) / 2.0)));
			} while (changeNumber > minValidChangeNumber);

			return oldestPicsChanges;
		}

		private async static Task<SteamApps.PICSChangesCallback?> FetchPICSChanges(uint changeNumber, bool sendAppChangeList = true, bool sendPackageChangeList = true) {
			await PICSChangesSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return null;
				}

				return await refreshBot.SteamApps.PICSGetChangesSince(changeNumber, sendAppChangeList, sendPackageChangeList).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(PICSChangesLimitingDelaySeconds)).ConfigureAwait(false);
						PICSChangesSemaphore.Release();
					}
				);
			}
		}

		private async Task FetchUserData() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

				return;
			}

			UserData? userData = await WebRequest.GetUserData(Bot).ConfigureAwait(false);
			if (userData == null) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
				Bot.ArchiLogger.LogGenericError(String.Format(Strings.ErrorObjectIsNull, userData));

				return;
			}

			PackageFilter.UpdateUserData(userData);

			UserDataRefreshTimer.Change(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
		}

		private void HandleFreeApp(FilterableApp app) {
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

				PackageQueue.AddPackage(new Package(EPackageType.App, app.ID));				
			} finally {
				BotCache.RemoveChange(appID: app.ID);
			}
		}

		private void HandleFreePackage(FilterablePackage package) {
			if (!BotCache.ChangedPackages.Contains(package.ID)) {
				return;
			}

			if (!PackageFilter.Ready) {
				return;
			}

			try {
				if (!PackageFilter.IsRedeemablePackage(package)) {
					return;
				}

				if (!PackageFilter.IsWantedPackage(package)) {
					return;
				}

				PackageQueue.AddPackage(new Package(EPackageType.Sub, package.ID, package.StartTime), package.PackageContentIDs);
			} finally {
				BotCache.RemoveChange(packageID: package.ID);
			}
		}

		private void HandlePlaytest(FilterableApp app) {
			if (!BotCache.ChangedApps.Contains(app.ID)) {
				return;
			}

			if (!PackageFilter.Ready) {
				return;
			}

			try {
				if (app.Parent == null) {
					return;
				}

				if (!PackageFilter.IsRedeemablePlaytest(app)) {
					return;
				}

				if (!PackageFilter.IsWantedPlaytest(app)) {
					return;
				}

				PackageQueue.AddPackage(new Package(EPackageType.Playtest, app.Parent.ID));
			} finally {
				BotCache.RemoveChange(appID: app.ID);
			}
		}

		private void HandleNewPackage(FilterablePackage package) {
			if (!BotCache.NewOwnedPackages.Contains(package.ID)) {
				return;
			}

			try {
				if (package.PackageContents.Count == 0) {
					return;
				}

				// Check for free DLC on newly added packages
				HashSet<uint> dlcAppIDs = new();

				foreach (FilterableApp app in package.PackageContents) {
					if (String.IsNullOrEmpty(app.ListOfDLC)) {
						continue;
					}

					foreach (string dlcAppIDString in app.ListOfDLC.Split(",", StringSplitOptions.RemoveEmptyEntries)) {
						if (!uint.TryParse(dlcAppIDString, out uint dlcAppID) || (dlcAppID == 0)) {
							continue;
						}

						dlcAppIDs.Add(dlcAppID);
					}
				}

				if (dlcAppIDs.Count != 0) {
					BotCache.AddChanges(appIDs: dlcAppIDs);
				}
			} finally {
				BotCache.RemoveChange(newOwnedPackageID: package.ID);
			}
		}

		internal void HandleLicenseList(SteamApps.LicenseListCallback callback) {
			HashSet<uint> ownedPackageIDs = callback.LicenseList.Select(license => license.PackageID).ToHashSet();
			HashSet<uint> newOwnedPackageIDs = ownedPackageIDs.Except(BotCache.SeenPackages).ToHashSet();

			if (BotCache.SeenPackages.Count > 0) {
				BotCache.AddChanges(newOwnedPackageIDs: newOwnedPackageIDs);
			}

			BotCache.UpdateSeenPackages(newOwnedPackageIDs);
			UserDataRefreshTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(15));
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