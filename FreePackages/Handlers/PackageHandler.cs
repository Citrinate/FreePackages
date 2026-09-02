using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageHandler : IDisposable {
		internal readonly Bot Bot;
		internal readonly BotCache BotCache;
		internal readonly PackageFilter PackageFilter;
		private readonly ActivationQueue ActivationQueue;
		private readonly RemovalQueue RemovalQueue;
		private CancellationTokenSource? RemovalCancellation;
		ConcurrentHashSet<Package> PackagesToRemove = new(new PackageComparer());
		internal static ConcurrentDictionary<string, PackageHandler> Handlers = new();

	// Set by PlaytestCatalog.DoUpdate for the duration of a fetch so every bot's
	// ActivationQueue skips PLAYTEST claims only (apps/subs keep going) — the persisted
	// playtest queue is stale until the live set is refreshed, and claiming from it
	// produces the 401 Invalid storm we're fixing. Volatile: written on the catalog
	// timer thread, read on each queue's timer thread.
	internal static volatile bool PlaytestsPausedGlobally;

		private readonly Timer UserDataRefreshTimer;
		private static SemaphoreSlim AddHandlerSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim ProcessChangesSemaphore = new SemaphoreSlim(1, 1);

		private PackageHandler(Bot bot, BotCache botCache, List<FilterConfig> filterConfigs, uint? packageLimit, bool pauseWhilePlaying, bool pauseWhileFarming) {
			Bot = bot;
			BotCache = botCache;
			PackageFilter = new PackageFilter(botCache, filterConfigs);
			ActivationQueue = new ActivationQueue(bot, botCache, pauseWhilePlaying, pauseWhileFarming, packageLimit, PackageFilter);
			RemovalQueue = new RemovalQueue(bot, botCache, pauseWhilePlaying, pauseWhileFarming);
			UserDataRefreshTimer = new Timer(async e => await FetchUserData().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
		}

		public void Dispose() {
			ActivationQueue.Dispose();
			UserDataRefreshTimer.Dispose();
			BotCache.Dispose();
		}

		internal static async Task AddHandler(Bot bot, List<FilterConfig> filterConfigs, uint? packageLimit, bool pauseWhilePlaying, bool pauseWhileFarming) {
			if (Handlers.ContainsKey(bot.BotName)) {
				Handlers[bot.BotName].Dispose();
				Handlers.TryRemove(bot.BotName, out PackageHandler? _);
			}

			await AddHandlerSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				if (filterConfigs.Any(filterConfig => filterConfig.PlaytestMode != EPlaytestMode.None)) {
					// Only allow 1 bot to request playtests
					int numBotsThatIncludePlaytests = Handlers.Values.Where(x => x.PackageFilter.FilterConfigs.Any(filterConfig => filterConfig.PlaytestMode != EPlaytestMode.None)).Count();
					if (numBotsThatIncludePlaytests > 0) {
						filterConfigs.ForEach(filterConfig => filterConfig.PlaytestMode = EPlaytestMode.None);
						bot.ArchiLogger.LogGenericInfo(Strings.PlaytestConfigLimitTriggered);
					}
				}

				string cacheFilePath = Bot.GetFilePath(String.Format("{0}_{1}", bot.BotName, nameof(FreePackages)), Bot.EFileType.Database);
				BotCache? botCache = await BotCache.CreateOrLoad(cacheFilePath).ConfigureAwait(false);
				if (botCache == null) {
					bot.ArchiLogger.LogGenericError(String.Format(ArchiSteamFarm.Localization.Strings.ErrorDatabaseInvalid, cacheFilePath));
					botCache = new(cacheFilePath);
				}

				Handlers.TryAdd(bot.BotName, new PackageHandler(bot, botCache, filterConfigs, packageLimit, pauseWhilePlaying, pauseWhileFarming));
			} finally {
				AddHandlerSemaphore.Release();
			}
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
			Handlers[bot.BotName].ActivationQueue.Start();
			Handlers[bot.BotName].RemovalQueue.Start();
		}

		internal static void OnBotDisconnected(Bot bot) {
			if (!Handlers.TryGetValue(bot.BotName, out PackageHandler? handler)) {
				return;
			}

			// Cancel any in-flight ScanRemovables for this bot so a disconnect doesn't
			// leave it blocked on ProcessChangesSemaphore / product info fetches. The
			// activation/removal queues self-pause via Bot.IsConnectedAndLoggedOn and
			// resume on their own timers on reconnect — no need to touch them here.
			handler.RemovalCancellation?.Cancel();
		}

		// Bracket a PlaytestCatalog fetch: while the flag is set, every ActivationQueue skips
		// PLAYTEST packages only (see ActivationQueue.GetNextPackage) so the stale persisted
		// playtest queue doesn't fire 401s before the live set is refreshed — apps/subs keep
		// activating normally. Resume nudges each queue so it re-checks immediately.
		// Removals are never paused — they're unrelated to the playtest storm.
		internal static void PausePlaytestActivations() {
			PlaytestsPausedGlobally = true;
		}

		internal static void ResumePlaytestActivations() {
			PlaytestsPausedGlobally = false;

			foreach (PackageHandler handler in Handlers.Values) {
				handler.ActivationQueue.Nudge();
			}
		}

		// Called by PlaytestCatalog after every successful, complete fetch. Prunes the
		// suppression sets and stale playtest packages of every connected, ready bot
		// against the new live set, then proactively enqueues live playtests for bots
		// whose filter accepts every playtest. This catches playtests PICS never
		// surfaced (the original discovery gap) without re-requesting ones already
		// handled this epoch, and without bypassing the user's filters.
		//
		// The proactive path runs only for a bot that has an "unconstrained all
		// playtests" filter (PackageFilter.IsUnconstrainedAllPlaytestsFilter: PlaytestMode
		// is All and no other app-level constraint is set). The catalog carries only BASE
		// appIDs — no playtest_type, tags, review score, languages, etc. — so the
		// proactive path can't honor limited/unlimited or any other filter; it is only
		// safe to enqueue every live playtest when the filter would accept any playtest
		// regardless. Bots with any finer filter (limited-only, a tag, an ignored app, a
		// review floor, wishlist-only, an age cap, ...) are left to the PICS path
		// (HandlePlaytest), which resolves the full app metadata and applies the filter
		// before enqueueing. Pruning still runs for every bot so a playtest that re-opens
		// later is re-requestable.
		internal static void OnPlaytestCatalogUpdated(HashSet<uint> liveSet) {
			ArgumentNullException.ThrowIfNull(liveSet);

			if (liveSet.Count == 0 || Handlers.Count == 0) {
				return;
			}

			foreach (PackageHandler handler in Handlers.Values) {
				if (!handler.Bot.IsConnectedAndLoggedOn || !handler.PackageFilter.Ready) {
					// Offline / not-yet-ready bots are skipped; they'll be caught on the next
					// cycle once they're connected and their filter is populated.
					continue;
				}

				handler.BotCache.PrunePlaytests(liveSet);

				// Drop now-closed playtests from the activation queue too, so the stale
				// persisted entries (from before the gate existed, or from the PICS path) don't
				// drain one-by-one as 401 Invalid. Live playtests stay queued and are claimed.
				handler.BotCache.PrunePlaytestPackages(liveSet);

				// Only a bot whose filter accepts every playtest can be driven proactively
				// from the catalog; the catalog carries no metadata to honor any finer filter,
				// so a bot with a constrained filter is left to the PICS path instead.
				bool allowProactive = handler.PackageFilter.FilterConfigs.Any(PackageFilter.IsUnconstrainedAllPlaytestsFilter);
				if (!allowProactive) {
					continue;
				}

				int filterHash = handler.PackageFilter.Hash;
				foreach (uint baseAppID in liveSet) {
					if (handler.BotCache.RequestedPlaytests.Contains(baseAppID) || handler.BotCache.WaitlistedPlaytests.Contains(baseAppID)) {
						continue;
					}

					handler.BotCache.AddPackage(new Package(EPackageType.Playtest, baseAppID, filterHash: filterHash));
				}
			}
		}

		private void UpdateUserData() {
			UserDataRefreshTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(15));
		}

		private async Task FetchUserData() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

				return;
			}

			Steam.UserData? userData = await WebRequest.GetUserData(Bot).ConfigureAwait(false);
			if (userData == null) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
				Bot.ArchiLogger.LogGenericError(String.Format(ArchiSteamFarm.Localization.Strings.ErrorObjectIsNull, nameof(userData)));

				return;
			}

			Steam.UserInfo? userInfo = await WebRequest.GetUserInfo(Bot).ConfigureAwait(false);
			if (userInfo == null) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
				Bot.ArchiLogger.LogGenericError(String.Format(ArchiSteamFarm.Localization.Strings.ErrorObjectIsNull, nameof(userInfo)));

				return;
			}

			PackageFilter.UpdateUserDetails(userData, userInfo);

			UserDataRefreshTimer.Change(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
		}

		internal static void AddChanges(IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (Handlers.Count == 0) {
				return;
			}

			// It's possible for a PICS change to effect thousands of apps and packages, Ex: https://steamdb.info/changelist/20445399/ (47,074 apps total, 31,529 packages total)
			// Store a list of changed apps/packages so that we can guarantee they'll all be processed eventually
			// Each bot has its own list, so that if any bots are offline, they'll be able to get caught up
			HashSet<uint> appIDs = appChanges.Select(x => x.Key).ToHashSet<uint>();
			HashSet<uint> packageIDs = packageChanges.Select(x => x.Key).ToHashSet<uint>();
			Handlers.Values.ToList().ForEach(x => x.BotCache.AddChanges(appIDs, packageIDs, ignoreFailedApps: true));

			Utilities.InBackground(async() => await HandleChanges().ConfigureAwait(false));
		}

		// Waits for every enabled bot's PackageFilter to be ready before processing a
		// changelist. The return value is intentionally ignored by HandleChanges: a
		// non-ready bot early-returns inside HandleFreeApp/HandleFreePackage (before its
		// RemoveChange finally block), so its changes stay in ChangedApps/ChangedPackages
		// and are retried on the next cycle. We therefore cap the wait at a short bound
		// (default 15 s) rather than blocking the whole batch for one stuck bot, and log
		// when we give up so the operator can see which bot is lagging.
		private async static Task<bool> IsReady(uint maxWaitTimeSeconds = 15) {
			DateTime timeoutTime = DateTime.Now.AddSeconds(maxWaitTimeSeconds);
			do {
				IReadOnlyCollection<PackageHandler> notReady = Handlers.Values.Where(x => x.Bot.BotConfig.Enabled && !x.PackageFilter.Ready).ToList();
				if (notReady.Count == 0) {
					return true;
				}

				if (maxWaitTimeSeconds > 0 && DateTime.Now >= timeoutTime) {
					ASF.ArchiLogger.LogGenericWarning($"IsReady timed out after {maxWaitTimeSeconds}s; {notReady.Count}/{Handlers.Values.Count(x => x.Bot.BotConfig.Enabled)} bot(s) not ready: {String.Join(", ", notReady.Select(x => x.Bot.BotName))}. Proceeding anyway — their changes will be retried.");

					return false;
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

				foreach ((HashSet<uint>? batchedAppIDs, HashSet<uint>? batchedPackageIDs) in ProductInfo.GetProductIDBatches(appIDs, packageIDs)) {
					var productInfo = await ProductInfo.GetProductInfo(batchedAppIDs, batchedPackageIDs).ConfigureAwait(false);
					if (productInfo == null) {
						continue;
					}

					await HandleProductInfo(productInfo).ConfigureAwait(false);
				}
			} finally {
				ProcessChangesSemaphore.Release();
			}
		}

		private async static Task HandleProductInfo(List<SteamApps.PICSProductInfoCallback> productInfos) {
			{ // Add wanted apps to the queue
				List<FilterableApp>? apps = await FilterableApp.GetFilterables(
					productInfos,
					app => {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: app.ID));

						return true;
					}
				).ConfigureAwait(false);

				if (apps == null) {
					ASF.ArchiLogger.LogGenericError($"{Strings.ProductInfoFetchFailed} (while extracting apps from {productInfos.Count} product info callback(s))");

					return;
				}

				if (apps.Count > 0) {
					apps.ForEach(app => {
						if (app.Type == EAppType.Beta) {
							Handlers.Values.ToList().ForEach(x => x.HandlePlaytest(app));
							// A Beta-app change may be the first sign a playtest just opened; wake the
							// catalog (debounced) so the live set reflects it within a minute.
							PlaytestCatalog.RequestRefresh();
						} else {
							Handlers.Values.ToList().ForEach(x => x.HandleFreeApp(app));
						}
					});
				}
			}

			{ // Add wanted packages to the queue or check new packages for free DLC
				HashSet<uint> newOwnedPackageIDs = Handlers.Values.Where(x => x.Bot.IsConnectedAndLoggedOn).SelectMany(x => x.BotCache.NewOwnedPackages).ToHashSet<uint>();
				List<FilterablePackage>? packages = await FilterablePackage.GetFilterables(
					productInfos,
					package => {
						Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: package.ID));

						return !newOwnedPackageIDs.Contains(package.ID);
					}
				).ConfigureAwait(false);

				if (packages == null) {
					ASF.ArchiLogger.LogGenericError($"{Strings.ProductInfoFetchFailed} (while extracting packages from {productInfos.Count} product info callback(s))");

					return;
				}

				if (packages.Count > 0) {
					packages.ForEach(package => {
						if (newOwnedPackageIDs.Contains(package.ID)) {
							Handlers.Values.ToList().ForEach(x => x.HandleNewPackage(package));
						} else {
							Handlers.Values.ToList().ForEach(x => x.HandleFreePackage(package));
						}
					});
				}
			}

			// Remove invalid apps from the app change list
			foreach (uint unknownAppID in productInfos.SelectMany(static result => result.UnknownApps)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(appID: unknownAppID));
			}

			// Remove invalid packages from the package change list
			foreach (uint unknownPackageID in productInfos.SelectMany(static result => result.UnknownPackages)) {
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(packageID: unknownPackageID));
				Handlers.Values.ToList().ForEach(x => x.BotCache.RemoveChange(newOwnedPackageID: unknownPackageID));
			}

			// Save changes to the app/package change lists
			Handlers.Values.ToList().ForEach(x => x.BotCache.SaveChanges());
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

				BotCache.AddPackage(new Package(EPackageType.App, app.ID, filterHash: PackageFilter.Hash));
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

				if (BotCache.AddPackage(new Package(EPackageType.Sub, package.ID, package.StartTime, filterHash: PackageFilter.Hash))) {
					// Remove duplicates.  
					// Whenever we're trying to activate an app and also an package for that app, get rid of the app.
					// This is because the error messages for activating packages are more descriptive and useful.
					BotCache.RemoveAppPackages(package.PackageContentIDs);
				}
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

				// Once the catalog has fetched at least once, gate strictly on the live
				// set: a Beta-app PICS change for a playtest whose signup isn't currently
				// open must not produce a POST. Before the first successful fetch the live
				// set is empty, so we fall back to the original PICS-only behaviour rather
				// than killing every playtest on a fresh start.
				if (PlaytestCatalog.HasFetched && !PlaytestCatalog.IsLive(app.Parent.ID)) {
					return;
				}

				if (!PackageFilter.IsRedeemablePlaytest(app)) {
					return;
				}

				if (!PackageFilter.IsWantedPlaytest(app)) {
					return;
				}

				BotCache.AddPackage(new Package(EPackageType.Playtest, app.Parent.ID, filterHash: PackageFilter.Hash));
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
					Utilities.InBackground(async() => await HandleChanges().ConfigureAwait(false));
				}
			} finally {
				BotCache.RemoveChange(newOwnedPackageID: package.ID);
			}
		}

		internal void HandleLicenseList(SteamApps.LicenseListCallback callback) {
			List<SteamApps.LicenseListCallback.License> newLicenses = callback.LicenseList.Where(license => !BotCache.SeenPackages.Contains(license.PackageID)).ToList();

			if (newLicenses.Count == 0) {
				return;
			}

			UpdateUserData();

			// Initialize SeenPackages
			if (BotCache.SeenPackages.Count == 0) {
				BotCache.UpdateSeenPackages(newLicenses);

				return;
			}

			BotCache.AddChanges(newOwnedPackageIDs: newLicenses.Select(license => license.PackageID).ToHashSet());
			BotCache.UpdateSeenPackages(newLicenses);
			Utilities.InBackground(async() => await HandleChanges().ConfigureAwait(false));
		}

		internal string GetStatus() {
			HashSet<string> responses = new HashSet<string>();

			// x packages queued. y activations used
			int activationsPastPeriod = Math.Min(BotCache.NumActivationsPastPeriod(), (int)ActivationQueue.MaxActivationsPerPeriod);
			responses.Add(String.Format(Strings.QueueStatus, ActivationQueue.ActivationsRemaining, activationsPastPeriod, ActivationQueue.ActivationsPerPeriod));

			// activations are paused due to account in use
			if (ActivationQueue.PauseWhilePlaying && !Bot.IsPlayingPossible) {
				responses.Add(Strings.QueuePausedWhileIngame);
			}

			// activations are paused due to farming
			if (ActivationQueue.PauseWhileFarming && Bot.CardsFarmer.NowFarming) {
				responses.Add(Strings.QueuePausedWhileFarming);
			}

			// activations will resume when
			if (activationsPastPeriod >= ActivationQueue.ActivationsPerPeriod) {
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddMinutes(ActivationQueue.ActivationPeriodMinutes + 1);
				responses.Add(String.Format(Strings.QueueLimitedUntil, String.Format("{0:T}", resumeTime)));
			}

			// x apps and y packages discovered but not processed
			if (BotCache.ChangedApps.Count > 0 || BotCache.ChangedPackages.Count > 0) {
				responses.Add(String.Format(Strings.QueueDiscoveryStatus, BotCache.ChangedApps.Count, BotCache.ChangedPackages.Count));
			}

			// removing x packages
			if (RemovalQueue.RemovalsRemaining > 0) {
				responses.Add(String.Format(Strings.RemovingPackages, RemovalQueue.RemovalsRemaining));
			}

			return String.Join(" ", responses);
		}

		internal string ClearQueue() {
			int numPackages = BotCache.Packages.Where(package => ActivationQueue.ActivationTypes.Contains(package.Type)).Count();
			int numChangedApps = BotCache.ChangedApps.Count;
			int numChangedPackages = BotCache.ChangedPackages.Count;

			if (numPackages == 0 && numChangedApps == 0 && numChangedPackages == 0) {
				return Strings.QueueEmpty;
			}

			BotCache.ClearQueue();

			List<string> responses = new List<string>();

			if (numPackages > 0) {
				responses.Add(String.Format(Strings.PackagesRemoved, numPackages));
			}
			if (numChangedApps > 0) {
				responses.Add(String.Format(Strings.DiscoveredAppsRemoved, numChangedApps));
			}
			if (numChangedPackages > 0) {
				responses.Add(String.Format(Strings.DiscoveredPackagesRemoved, numChangedPackages));
			}

			return String.Join(" ", responses);
		}

		internal string AddPackage(EPackageType type, uint id, bool useFilter) {
			if (useFilter) {
				if (type == EPackageType.App) {
					BotCache.AddChanges(appIDs: new HashSet<uint> { id });

					return String.Format(Strings.DiscoveredAppsAdded, String.Format("app/{0}", id));
				} else {
					BotCache.AddChanges(packageIDs: new HashSet<uint> { id });

					return String.Format(Strings.DiscoveredPackagesAdded, String.Format("sub/{0}", id));
				}
			}

			BotCache.AddPackage(new Package(type, id));

			if (type == EPackageType.App) {
				return String.Format(Strings.AppsQueued, String.Format("app/{0}", id));
			} else {
				return String.Format(Strings.PackagesQueued, String.Format("sub/{0}", id));
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

			BotCache.AddPackages(packages);
		}

		internal async Task ScanRemovables(Dictionary<uint, string> removeablePackages, bool excludePlayed, bool removeAll, StatusReporter statusReporter) {
			if (RemovalCancellation != null) {
				statusReporter.Report(Bot, Strings.RemovalScanAlreadyRunning);

				return;
			}
			
			RemovalCancellation = new CancellationTokenSource();
			try {
				await ProcessChangesSemaphore.WaitAsync(RemovalCancellation.Token).ConfigureAwait(false);
				try {
					await IsReady().ConfigureAwait(false);

					Dictionary<uint, SteamKit2.Internal.CPlayer_GetOwnedGames_Response.Game>? ownedGameDetails = null;
					if (excludePlayed) {
						ownedGameDetails = await SteamHandler.Handlers[Bot.BotName].GetOwnedGames(Bot.SteamID).ConfigureAwait(false);
						if (ownedGameDetails == null) {
							statusReporter.Report(Bot, Strings.PlaytimeFetchFailed);

							return;
						}
					}

					var productInfos = await ProductInfo.GetProductInfo(packageIDs: removeablePackages.Keys.ToHashSet(), cancellationToken: RemovalCancellation.Token).ConfigureAwait(false);
					if (productInfos == null) {
						statusReporter.Report(Bot, Strings.ProductInfoFetchFailed);

						return;
					}

					List<FilterablePackage>? packages = await FilterablePackage.GetFilterables(productInfos, cancellationToken: RemovalCancellation.Token, onNonFreePackage: x => !removeAll).ConfigureAwait(false);
					if (packages == null) {
						statusReporter.Report(Bot, Strings.ProductInfoFetchFailed);

						return;
					}

					if (packages.Count == 0) {
						statusReporter.Report(Bot, Strings.RemovingNoPackages);

						return;
					}

					RemovalCancellation.Token.ThrowIfCancellationRequested();

					PackagesToRemove.Clear();
					List<string> previewResponses = [];
					var ownedPackageIDs = Bot.OwnedPackages.Keys.ToHashSet();			
					foreach (FilterablePackage package in packages) {
						if (!removeAll) {
							if (!PackageFilter.IsRedeemablePackage(package, ignoreAlreadyOwned: true)) {
								continue;
							}

							if (PackageFilter.IsWantedPackage(package, ignoreAgeFilters: true)) {
								continue;
							}
						}

						if (excludePlayed) {
							if (package.PackageContents.Any(app => {
								return (ownedGameDetails!.ContainsKey(app.ID) && ownedGameDetails![app.ID].playtime_forever > 0)
									|| (app.Type != EAppType.Demo && app.ParentID != null && ownedGameDetails!.ContainsKey(app.ParentID.Value) && ownedGameDetails![app.ParentID.Value].playtime_forever > 0);
							})) {
								continue;
							}
						}

						// Attempt to remove the app directly, which uses an API with a more generous rate limit
						if (package.PackageContents.Count == 1) {
							FilterableApp app = package.PackageContents.First();

							// Apparently the API for removing apps doesn't care which package is removed? (Haven't tested this) https://github.com/JustArchiNET/ArchiSteamFarm/issues/3434#issuecomment-2954303590
							// Only remove by app if this is the only package that can be removed
							int ownedPackagesWithApp = ASF.GlobalDatabase!.PackagesDataReadOnly.Where(x => ownedPackageIDs.Contains(x.Key) && x.Value.AppIDs != null && x.Value.AppIDs.Contains(app.ID)).Count();
							if (ownedPackagesWithApp <= 1) {
								PackagesToRemove.Add(new Package(EPackageType.RemoveApp, app.ID));
								previewResponses.Add(String.Format("app/{0} ({1})", app.ID, removeablePackages[package.ID]));

								continue;
							}
						}
						
						PackagesToRemove.Add(new Package(EPackageType.RemoveSub, package.ID));
						previewResponses.Add(String.Format("sub/{0} ({1})", package.ID, removeablePackages[package.ID]));
					}

					if (PackagesToRemove.Count == 0) {
						statusReporter.Report(Bot, Strings.RemovingNoUnwatedPackages);

						return;
					}

					statusReporter.Report(Bot, String.Format(Strings.RemovablePackagesFound, new object?[] {
						PackagesToRemove.Count, 
						String.Join(PackagesToRemove.Count > 100 ? ", " : Environment.NewLine, previewResponses),
						String.Format("!cancelremove {0}", Bot.BotName),
						String.Format("!confirmremove {0}", Bot.BotName),
						String.Format("!dontremove {0} <Licenses>", Bot.BotName)
					}));
				} finally {
					ProcessChangesSemaphore.Release();
				}
			} catch (OperationCanceledException) {
				statusReporter.Report(Bot, Strings.RemovalScanCancelled);
			} finally {
				RemovalCancellation?.Dispose();
				RemovalCancellation = null;
			}
		}

		internal string ConfirmRemoval() {
			if (PackagesToRemove.Count == 0) {
				return String.Format(Strings.RemovalScanNeeded, String.Format("!removefreepackages {0}", Bot.BotName));
			}

			int numRemovalsDiscovered = PackagesToRemove.Count;
			BotCache.AddPackages(PackagesToRemove);
			PackagesToRemove.Clear();

			return String.Format(Strings.RemovingPackages, numRemovalsDiscovered);
		}

		internal string ModifyRemovables(EPackageType type, uint id) {
			if (PackagesToRemove.Count == 0) {
				return String.Format(Strings.RemovalScanNeeded, String.Format("!removefreepackages {0}", Bot.BotName));
			}

			Package? package = PackagesToRemove.FirstOrDefault(package => package.Type == type && package.ID == id);
			if (package == null) {
				if (type == EPackageType.RemoveApp) {
					return String.Format(Strings.RemovalPackageNotFound, String.Format("app/{0}", id)) + " :steamthumbsdown:";
				} else {
					return String.Format(Strings.RemovalPackageNotFound, String.Format("sub/{0}", id)) + " :steamthumbsdown:";
				}
			}

			PackagesToRemove.Remove(package);

			if (type == EPackageType.RemoveApp) {
				return String.Format(Strings.RemovalPackageCancelled, String.Format("app/{0}", id));
			} else {
				return String.Format(Strings.RemovalPackageCancelled, String.Format("sub/{0}", id));
			}
		}

		internal string CancelRemoval() {
			bool stoppingScan = RemovalCancellation != null;
			int numRemovalsDiscovered = PackagesToRemove.Count;
			int numRemovals = BotCache.Packages.Where(package => RemovalQueue.RemovalTypes.Contains(package.Type)).Count();

			if (!stoppingScan && numRemovalsDiscovered == 0 && numRemovals == 0) {
				return Strings.RemovalQueueEmpty;
			}

			RemovalCancellation?.Cancel();
			PackagesToRemove.Clear();
			BotCache.CancelRemoval();

			List<string> responses = new List<string>();
			if (numRemovals > 0) {
				responses.Add(String.Format(Strings.RemovalsCancelled, numRemovals));
			}
			if (numRemovalsDiscovered > 0) {
				responses.Add(String.Format(Strings.RemovalScanCancelled));
			}
			if (stoppingScan) {
				responses.Add(String.Format(Strings.RemovalScanCanceling));
			}

			return String.Join(" ", responses);
		}
	}
}
