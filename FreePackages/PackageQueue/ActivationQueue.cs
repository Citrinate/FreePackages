using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal sealed class ActivationQueue : PackageQueue {
		private const int DelayBetweenActivationsSeconds = 5;
		internal readonly uint ActivationsPerPeriod = 25;
		internal const uint MaxActivationsPerPeriod = 30; // Steam's imposed limit
		internal const uint ActivationPeriodMinutes = 90; // Steam's imposed limit
		internal readonly PackageFilter PackageFilter;
		internal static readonly HashSet<EPackageType> ActivationTypes = [EPackageType.App, EPackageType.Sub, EPackageType.Playtest];
		internal int ActivationsRemaining => BotCache.Packages.Where(x => ActivationTypes.Contains(x.Type)).Count();

		internal ActivationQueue(Bot bot, BotCache botCache, bool pauseWhilePlaying, uint? packageLimit, PackageFilter packageFilter) : base(bot, botCache, pauseWhilePlaying) {
			PackageFilter = packageFilter;

			if (packageLimit != null) {
				ActivationsPerPeriod = Math.Min(packageLimit.Value, MaxActivationsPerPeriod);
			}
		}

		protected override Package? GetNextPackage() => BotCache.GetNextPackage(ActivationTypes);

		protected override async Task<DateTime?> BeforeProcessing(Package package) {
			// Rate limit reached
			if (BotCache.NumActivationsPastPeriod() >= ActivationsPerPeriod) {
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddMinutes(ActivationPeriodMinutes + 1);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));

				return resumeTime;
			}

			// User has changed their filters, re-scan packages queued under the old filter to see if they're still wanted
			if (package.FilterHash != null && package.FilterHash != PackageFilter.Hash) {
				List<Package> appsToRescan = BotCache.Packages.Where(x => x.Type is EPackageType.App or EPackageType.Playtest && x.FilterHash != null && x.FilterHash != PackageFilter.Hash).ToList();
				List<Package> subsToRescan = BotCache.Packages.Where(x => x.Type is EPackageType.Sub && x.FilterHash != null && x.FilterHash != PackageFilter.Hash).ToList();

				List<SteamApps.PICSProductInfoCallback>? productInfos = await ProductInfo.GetProductInfo(appIDs: appsToRescan.Select(x => x.ID).ToHashSet(), packageIDs: subsToRescan.Select(x => x.ID).ToHashSet()).ConfigureAwait(false);
				if (productInfos == null) {
					Bot.ArchiLogger.LogGenericError(Strings.ProductInfoFetchFailed);

					return DateTime.Now.AddMinutes(1);
				}

				// Recheck all apps not queued with the current filter
				if (appsToRescan.Count > 0) {
					List<FilterableApp>? filterableApps = await FilterableApp.GetFilterables(productInfos, onNonFreeApp: x => false).ConfigureAwait(false);
					if (filterableApps == null) {
						Bot.ArchiLogger.LogGenericError(Strings.ProductInfoFetchFailed);
						return DateTime.Now.AddMinutes(1);
					}

					foreach (Package app in appsToRescan) {
						FilterableApp? filterableApp = filterableApps.FirstOrDefault(x => x.ID == app.ID);
						if (filterableApp != null) {
							if (app.Type == EPackageType.App) {
								if (!PackageFilter.IsWantedApp(filterableApp)) {
									Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", app.ID), Strings.Unwanted));
									BotCache.RemovePackage(app);

									continue;
								}
							} else if (app.Type == EPackageType.Playtest) {
								if (!PackageFilter.IsWantedPlaytest(filterableApp)) {
									Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", app.ID), Strings.Unwanted));
									BotCache.RemovePackage(app);

									continue;
								}
							}
						}

						// App either passes the current filter, or its info is missing for some reason
						app.FilterHash = PackageFilter.Hash;
					}

					BotCache.SaveChanges();
				}

				// Recheck all subs not queued with the current filter
				if (subsToRescan.Count > 0) {
					List<FilterablePackage>? filterablePackages = await FilterablePackage.GetFilterables(productInfos, onNonFreePackage: x => false).ConfigureAwait(false);
					if (filterablePackages == null) {
						Bot.ArchiLogger.LogGenericError(Strings.ProductInfoFetchFailed);

						return DateTime.Now.AddMinutes(1);
					}

					foreach (Package sub in subsToRescan) {
						FilterablePackage? filterablePackage = filterablePackages.FirstOrDefault(x => x.ID == sub.ID);
						if (filterablePackage != null) {
							if (!PackageFilter.IsWantedPackage(filterablePackage)) {
								Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("sub/{0}", sub.ID), Strings.Unwanted));
								BotCache.RemovePackage(sub);

								continue;
							}
						}

						// Sub either passes the current filter, or its info is missing for some reason
						sub.FilterHash = PackageFilter.Hash;
					}

					BotCache.SaveChanges();
				}

				return DateTime.Now.AddSeconds(1);
			}

			return null;
		}

		protected override DateTime? HandleResult(Package package, EResult result) {
			if (result == EResult.RateLimitExceeded) {
				BotCache.AddActivation(DateTime.Now, MaxActivationsPerPeriod); // However many activations we thought were made, we were wrong.  Correct for this by adding a bunch of fake times to our cache
				DateTime resumeTime = DateTime.Now.AddMinutes(ActivationPeriodMinutes + 1);
				Bot.ArchiLogger.LogGenericInfo(Strings.RateLimitExceeded);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));

				return resumeTime;
			}
			
			if (result == EResult.Timeout) {
				return DateTime.Now.AddMinutes(5);
			}

			if (result == EResult.OK || result == EResult.Invalid || result == EResult.AlreadyOwned) {
				BotCache.RemovePackage(package);
			}

			if (ActivationsRemaining > 0) {
				return DateTime.Now.AddSeconds(DelayBetweenActivationsSeconds);
			}

			return null;
		}
	}
}
