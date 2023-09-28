using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageHandler {
		internal readonly Bot Bot;
		private readonly PackageFilter PackageFilter;
		private readonly PackageQueue PackageQueue;
		internal static ConcurrentDictionary<string, PackageHandler> Handlers = new();

		private const int AppInfosPerSingleRequest = 255;

		private PackageHandler(Bot bot, BotCache botCache, FilterConfig filterConfig, uint? packageLimit) {
			Bot = bot;
			PackageFilter = new PackageFilter(bot, botCache, filterConfig);
			PackageQueue = new PackageQueue(bot, botCache, packageLimit);
		}

		internal static async Task AddHandler(Bot bot, FilterConfig? filterConfig, uint? packageLimit) {
			if (Handlers.ContainsKey(bot.BotName)) {
				Handlers.TryRemove(bot.BotName, out PackageHandler? _);
			}

			filterConfig ??= new();

			string cacheFilePath = Bot.GetFilePath(String.Format("{0}_{1}", bot.BotName, nameof(FreePackages)), Bot.EFileType.Database);
			BotCache? botCache = await BotCache.CreateOrLoad(cacheFilePath).ConfigureAwait(false);
			if (botCache == null) {
				bot.ArchiLogger.LogGenericError(String.Format(Strings.ErrorDatabaseInvalid, cacheFilePath));
				botCache = new(cacheFilePath);
			}

			Handlers.TryAdd(bot.BotName, new PackageHandler(bot, botCache, filterConfig, packageLimit));
		}

		internal static void OnAccountInfo(Bot bot, SteamUser.AccountInfoCallback callback) {
			if (!PackageHandler.Handlers.ContainsKey(bot.BotName)) {
				return;
			}

			Handlers[bot.BotName].PackageFilter.UpdateAccountInfo(callback);
		}

		internal static async Task OnBotLoggedOn(Bot bot) {
			if (!PackageHandler.Handlers.ContainsKey(bot.BotName)) {
				return;
			}
			
			await Handlers[bot.BotName].PackageFilter.UpdateUserData().ConfigureAwait(false);
		}

		internal async static Task<bool> IsReady(uint maxWaitTimeSeconds = 120) {
			DateTime timeoutTime = DateTime.Now.AddSeconds(maxWaitTimeSeconds);
			do {
				bool ready = Handlers.Values.Where(x => !x.PackageFilter.Ready).Count() == 0;
				if (ready) {
					return true;
				}

				await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
			} while (maxWaitTimeSeconds == 0 || DateTime.Now < timeoutTime);

			return false;
		}

		internal async static Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (PackageHandler.Handlers.Count == 0) {
				return;	
			}

			await IsReady().ConfigureAwait(false);
			await HandleAppUpdates(appChanges).ConfigureAwait(false);
			await HandlePackageUpdates(packageChanges).ConfigureAwait(false);
		}

		private async static Task HandleAppUpdates(IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges) {
			var appIDsToCheck = appChanges.Select(x => x.Key);			
			for (int i = 0; i < Math.Ceiling((decimal) appIDsToCheck.Count() / AppInfosPerSingleRequest); i++) {
				var appIDs = appIDsToCheck.Skip(i * AppInfosPerSingleRequest).Take(AppInfosPerSingleRequest);

				AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet response;
				try {
					Bot? refreshBot = GetRefreshBot();
					if (refreshBot == null) {
						return;
					}

					response = await refreshBot.SteamApps.PICSGetProductInfo(appIDs.Select(x => new SteamApps.PICSRequest(x)), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				if (response.Results == null) {
					continue;
				}

				foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in response.Results.SelectMany(static result => result.Apps.Values)) {
					if (!PackageFilter.IsFreeApp(app)) {
						continue;
					}

					Handlers.Values.ToList().ForEach(x => x.HandleFreeApp(app));
				}
			}
		}

		private void HandleFreeApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
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
		}

		internal async static Task HandlePackageUpdates(IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>? packageChanges = null, HashSet<uint>? packageIDsToCheck = null) {
			packageIDsToCheck ??= new();
			if (packageChanges != null) {
				packageIDsToCheck.UnionWith(packageChanges.Select(x => x.Key));
			}

			for (int i = 0; i < Math.Ceiling((decimal) packageIDsToCheck.Count() / AppInfosPerSingleRequest); i++) {
				var packageIDs = packageIDsToCheck.Skip(i * AppInfosPerSingleRequest).Take(AppInfosPerSingleRequest);

				AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet response;
				try {
					Bot? refreshBot = GetRefreshBot();
					if (refreshBot == null) {
						return;
					}

					response = await refreshBot.SteamApps.PICSGetProductInfo(Enumerable.Empty<SteamApps.PICSRequest>(), packageIDs.Select(x => new SteamApps.PICSRequest(x))).ToLongRunningTask().ConfigureAwait(false);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				if (response.Results == null) {
					continue;
				}

				foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo package in response.Results.SelectMany(static result => result.Packages.Values)) {
					if (!PackageFilter.IsFreePackage(package)) {
						continue;
					}

					KeyValue kv = package.KeyValues;
					var appIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger());

					AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet app_response;
					try {
						Bot? refreshBot = GetRefreshBot();
						if (refreshBot == null) {
							return;
						}

						app_response = await refreshBot.SteamApps.PICSGetProductInfo(appIDs.Select(x => new SteamApps.PICSRequest(x)), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericWarningException(e);

						continue;
					}

					if (app_response.Results == null) {
						continue;
					}

					var apps = app_response.Results.SelectMany(static result => result.Apps.Values).Where(x => !x.MissingToken);

					if (apps.Count() != appIDs.Count()) {
						continue;
					}

					Handlers.Values.ToList().ForEach(x => x.HandleFreePackage(package, apps));
				}
			}
		}

		private void HandleFreePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package, IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> apps) {
			if (!PackageFilter.IsRedeemablePackage(package)) {
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
		}

		private static Bot? GetRefreshBot() => Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
	}
}