using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageQueue {
		private readonly Bot Bot;
		private readonly BotCache BotCache;
		private Timer? Timer;
		private readonly ConcurrentQueue<Package> Packages = new();
		private const int DelayBetweenActivationsSeconds = 5;
		private readonly uint ActivationsPerHour = 40;
		private const uint MaxActivationsPerHour = 50; // Steam's imposed limit
		internal static MethodInfo? AddFreeLicense;

		static PackageQueue() {
			AddFreeLicense = typeof(ArchiWebHandler).GetMethods(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(x => x.Name == "AddFreeLicense");
			if (AddFreeLicense == null) {
				ASF.ArchiLogger.LogGenericError("Couldn't find ArchiWebHandler.AddFreeLicense method");
			}
		}

		internal PackageQueue(Bot bot, BotCache botCache, uint? packageLimit) {
			Bot = bot;
			BotCache = botCache;

			if (packageLimit != null) {
				ActivationsPerHour = Math.Min(packageLimit.Value, MaxActivationsPerHour);
			}

			if (BotCache.Packages.Count > 0) {
				Timer = new Timer(async e => await ProcessQueue().ConfigureAwait(false), null, 0, Timeout.Infinite);
			}
		}

		internal void AddPackage(Package package, HashSet<uint>? appIDsToRemove = null) {
			if (!BotCache.AddPackage(package)) {
				return;
			}

			if (package.Type == EPackageType.Sub && appIDsToRemove != null) {
				// Remove duplicates.  Whenever we're trying to activate and app and also an package for that app, get rid of the app.  Because error messages for packages are more descriptive and useful.
				BotCache.RemoveAppPackages(appIDsToRemove);
			}

			if (Timer == null) {
				Timer = new Timer(async e => await ProcessQueue().ConfigureAwait(false), null, 0, Timeout.Infinite);
			}
		}

		internal void AddPackages(IEnumerable<Package> packages) {
			if (!BotCache.AddPackages(packages)) {
				return;
			}

			if (Timer == null) {
				Timer = new Timer(async e => await ProcessQueue().ConfigureAwait(false), null, 0, Timeout.Infinite);
			}
		}

		private async Task ProcessQueue() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			if (BotCache.Packages.Count == 0) {
				return;
			}

			if (BotCache.NumActivationsPastHour() >= ActivationsPerHour) {
				DateTime resumeTime = BotCache.Activations.Max().AddHours(1).AddMinutes(1);
				Bot.ArchiLogger.LogGenericInfo(String.Format("Pausing free package activations until {0:T}", resumeTime));
				UpdateTimer(resumeTime);
				
				return;
			}

			Package? package = BotCache.GetNextPackage();
			if (package == null) {
				// There are packages to redeem, but they aren't active yet
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			EResult result = await ClaimPackage(package).ConfigureAwait(false);

			if (result == EResult.RateLimitExceeded) {
				BotCache.AddActivation(DateTime.Now, MaxActivationsPerHour); // However many activations we thought were made, we were wrong.  Correct for this by adding a bunch of fake times to our cache
				DateTime resumeTime = DateTime.Now.AddHours(1).AddMinutes(1);
				Bot.ArchiLogger.LogGenericInfo("Free Package rate limit exceeded");
				Bot.ArchiLogger.LogGenericInfo(String.Format("Pausing free package activations until {0:T}", resumeTime));
				UpdateTimer(resumeTime);

				return;
			}
			
			BotCache.AddActivation(DateTime.Now);

			if (result == EResult.OK || result == EResult.Invalid) {
				BotCache.RemovePackage(package);
			} else if (result == EResult.Timeout) {
				UpdateTimer(DateTime.Now.AddMinutes(5));

				return;
			}

			if (BotCache.Packages.Count > 0) {
				UpdateTimer(DateTime.Now.AddSeconds(DelayBetweenActivationsSeconds));

				return;
			}

			Timer?.Dispose();
			Timer = null;
		}

		private async Task<EResult> ClaimPackage(Package package) {
			if (package.Type == EPackageType.App) {
				return await ClaimFreeApp(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.Sub) {
				return await ClaimFreeSub(package.ID).ConfigureAwait(false);
			}

			// TODO
			// if (package.Type == EPackageType.Demo) {
			// 	return await ClaimFreeDemo(package.ID).ConfigureAwait(false);
			// }

			return EResult.Invalid;
		}

		private async Task<EResult> ClaimFreeApp(uint appID) {
			SteamApps.FreeLicenseCallback response = await Bot.SteamApps.RequestFreeLicense(appID).ToLongRunningTask().ConfigureAwait(false);

			// The Result returned by RequestFreeLicense is useless and I've only ever seen it return EResult.OK
			if (response.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericInfo(string.Format("ID: app/{0} | Status: {1}", appID, response.Result));

				return EResult.Fail;
			}

			if (response.GrantedApps.Count > 0 || response.GrantedPackages.Count > 0) {
				// When only GrantedPackages is empty we probably tried to activate an app we already own.  I don't think it's possible for only GrantedApps to be empty
				Bot.ArchiLogger.LogGenericInfo(string.Format("ID: app/{0} | Status: {1} | Items: {2}", appID, response.Result, String.Join(", ", response.GrantedApps.Select(x => $"app/{x}").Union(response.GrantedPackages.Select(x => $"sub/{x}")))));

				return EResult.OK;
			}

			// When both GrantedApps and GrantedPackages are empty something went wrong, It could be we're rate limited or it could mean we just can't activate this app
			if (response.GrantedApps.Count == 0 && response.GrantedPackages.Count == 0) {
				// Only way to really get an idea of what might have went wrong is to check the store page
				AppDetails? appDetails = await WebRequest.GetAppDetails(Bot, appID).ConfigureAwait(false);
				bool success = appDetails?.Success ?? false;
				bool hasPackages = (appDetails?.Data?.Packages.Count ?? 0) != 0;
				bool isFree = appDetails?.Data?.IsFree ?? false;
				bool isComingSoon = appDetails?.Data?.ReleaseDate?.ComingSoon ?? true;

				if (!success || !isFree || isComingSoon) {
					Bot.ArchiLogger.LogGenericInfo(string.Format("ID: app/{0} | Status: {1}", appID, EResult.Invalid));

					return EResult.Invalid;
				}

				// App is available, but we couldn't activate it.  We might be rate limited
				
				if (hasPackages) {
					// Replace the app with the appropriate package and when we try to activate that we'll find out for sure if we're rate limited or not
					// Note: This is mostly wishful thinking. /api/appdetails rarely shows the free packages for free apps
					Bot.ArchiLogger.LogGenericInfo(string.Format("ID: app/{0} | Status: Replaced with {1}", appID, String.Join(", ", appDetails!.Data!.Packages.Select(x => $"sub/{x}"))));
					BotCache.AddChanges(packageIDs: appDetails.Data.Packages);

					return EResult.OK;
				}

				// We could be rate limited, but the app could also be invalid beacause it has no available licenses.  It's necessary to assume invalid so we don't get into an infinite loop.
				// Examples: https://steamdb.info/app/2401570/ on Oct 2, 2023, Attempting to download demo through Steam client gives error "no licenses"
				// Free games that still have store pages but display "At the request of the publisher, ___ is unlisted on the Steam store and will not appear in search.": https://store.steampowered.com/app/376570/WildStar/
				Bot.ArchiLogger.LogGenericInfo(string.Format("ID: app/{0} | Status: {1}", appID, EResult.Invalid));

				return EResult.Invalid;
			}

			return EResult.OK;
		}

		private async Task<EResult> ClaimFreeSub(uint subID) {
			if (AddFreeLicense == null) {
				return EResult.Invalid;
			}

			EResult result;
			EPurchaseResultDetail purchaseResult;
			try {
				var res = (Task<(EResult, EPurchaseResultDetail)>) AddFreeLicense.Invoke(Bot.ArchiWebHandler, new object[]{subID})!;
				await res;
				(result, purchaseResult) = res.Result;
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return EResult.Invalid;
			}

			Bot.ArchiLogger.LogGenericInfo(string.Format("ID: sub/{0} | Status: {1}/{2}", subID, result, purchaseResult));

			if (purchaseResult == EPurchaseResultDetail.RateLimited) {
				return EResult.RateLimitExceeded;
			}

			if (purchaseResult == EPurchaseResultDetail.Timeout) {
				return EResult.Timeout;
			}

			if (result != EResult.OK) {
				return EResult.Invalid;
			}

			return EResult.OK;
		}

		internal string GetStatus() {
			HashSet<string> responses = new HashSet<string>();

			int activationsPastHour = Math.Min(BotCache.NumActivationsPastHour(), (int) MaxActivationsPerHour);
			responses.Add(String.Format("{0} free packages queued.  {1}/{2} hourly activations used.", BotCache.Packages.Count, activationsPastHour, ActivationsPerHour));

			if (activationsPastHour >= ActivationsPerHour) {
				DateTime resumeTime = BotCache.Activations.Max().AddHours(1).AddMinutes(1);
				responses.Add(String.Format("Activations will resume at {0:T}.", resumeTime));
			}

			if (BotCache.ChangedApps.Count > 0 || BotCache.ChangedPackages.Count > 0) {
				responses.Add(String.Format("{0} apps and {1} packages discovered but not processed yet.", BotCache.ChangedApps.Count, BotCache.ChangedPackages.Count));
			}

			return String.Join(" ", responses);;
		}

		private static int GetMillisecondsFromNow(DateTime then) => Math.Max(0, (int) (then - DateTime.Now).TotalMilliseconds);
		private void UpdateTimer(DateTime then) => Timer?.Change(GetMillisecondsFromNow(then), Timeout.Infinite);
	}
}