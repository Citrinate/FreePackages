using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageQueue : IDisposable {
		private readonly Bot Bot;
		private readonly BotCache BotCache;
		private Timer Timer;
		private readonly ConcurrentQueue<Package> Packages = new();
		private const int DelayBetweenActivationsSeconds = 5;
		private readonly uint ActivationsPerHour = 25;
		private const uint MaxActivationsPerHour = 30; // Steam's imposed limit
		private bool PauseWhilePlaying = false;

		internal PackageQueue(Bot bot, BotCache botCache, uint? packageLimit, bool pauseWhilePlaying) {
			Bot = bot;
			BotCache = botCache;
			PauseWhilePlaying = pauseWhilePlaying;

			if (packageLimit != null) {
				ActivationsPerHour = Math.Min(packageLimit.Value, MaxActivationsPerHour);
			}

			Timer = new Timer(async e => await ProcessQueue().ConfigureAwait(false), null, 0, Timeout.Infinite);
		}

		public void Dispose() {
			Timer.Dispose();
		}

		internal void AddPackage(Package package, HashSet<uint>? appIDsToRemove = null) {
			if (!BotCache.AddPackage(package)) {
				return;
			}

			if (package.Type == EPackageType.Sub && appIDsToRemove != null) {
				// Remove duplicates.  Whenever we're trying to activate and app and also an package for that app, get rid of the app.  Because error messages for packages are more descriptive and useful.
				BotCache.RemoveAppPackages(appIDsToRemove);
			}
		}

		internal void AddPackages(IEnumerable<Package> packages) {
			if (!BotCache.AddPackages(packages)) {
				return;
			}
		}

		private async Task ProcessQueue() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			if (BotCache.Packages.Count == 0) {
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			if (BotCache.NumActivationsPastHour() >= ActivationsPerHour) {
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddHours(1).AddMinutes(1);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));
				UpdateTimer(resumeTime);
				
				return;
			}

			if (PauseWhilePlaying && !Bot.IsPlayingPossible) {
				// Don't activate anything while the user is playing a game (does not apply to ASF card farming)
				UpdateTimer(DateTime.Now.AddMinutes(1));

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
				Bot.ArchiLogger.LogGenericInfo(Strings.RateLimitExceeded);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));
				UpdateTimer(resumeTime);

				return;
			}
			
			// Note: Not everything counts against the activation limit, ex: All playtests?, Some sub errors (dunno which), Maybe some app errors
			// Might be worth revisiting later, but for now I feel comfortable just assuming everything counts
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

			UpdateTimer(DateTime.Now.AddMinutes(1));
		}

		private async Task<EResult> ClaimPackage(Package package) {
			if (package.Type == EPackageType.App) {
				return await ClaimFreeApp(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.Sub) {
				return await ClaimFreeSub(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.Playtest) {
				return await ClaimPlaytest(package.ID).ConfigureAwait(false);
			}

			return EResult.Invalid;
		}

		private async Task<EResult> ClaimFreeApp(uint appID) {
			SteamApps.FreeLicenseCallback response;
			try {
				response = await Bot.SteamApps.RequestFreeLicense(appID).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return EResult.Timeout;
			}
			

			// The Result returned by RequestFreeLicense is useless and I've only ever seen it return EResult.OK
			if (response.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), response.Result));

				return EResult.Fail;
			}

			if (response.GrantedApps.Count > 0 || response.GrantedPackages.Count > 0) {
				// When only GrantedPackages is empty we probably tried to activate an app we already own.  I don't think it's possible for only GrantedApps to be empty
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicenseWithItems, String.Format("app/{0}", appID), response.Result, String.Join(", ", response.GrantedApps.Select(x => $"app/{x}").Union(response.GrantedPackages.Select(x => $"sub/{x}")))));

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
					Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), EResult.Invalid));

					return EResult.Invalid;
				}

				// App is available, but we couldn't activate it.  We might be rate limited
				
				if (hasPackages) {
					// Replace the app with the appropriate package and when we try to activate that we'll find out for sure if we're rate limited or not
					// Note: This is mostly wishful thinking. /api/appdetails rarely shows the free packages for free apps (one example where it does: https://steamdb.info/app/2119270/)
					Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), String.Format(Strings.ReplacedWith, String.Join(", ", appDetails!.Data!.Packages.Select(x => $"sub/{x}")))));
					BotCache.AddChanges(packageIDs: appDetails.Data.Packages);

					return EResult.OK;
				}

				// We could be rate limited, but the app could also be invalid beacause it has no available licenses.  It's necessary to assume invalid so we don't get into an infinite loop.
				// Examples: https://steamdb.info/app/2401570/ on Oct 2, 2023, Attempting to download demo through Steam client gives error "no licenses"
				// Free games that still have store pages but display "At the request of the publisher, ___ is unlisted on the Steam store and will not appear in search.": https://store.steampowered.com/app/376570/WildStar/
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), Strings.Unknown));

				return EResult.Invalid;
			}

			return EResult.OK;
		}

		private async Task<EResult> ClaimFreeSub(uint subID) {
			EResult result;
			EPurchaseResultDetail purchaseResult;
			try {
				(result, purchaseResult) = await Bot.Actions.AddFreeLicensePackage(subID).ConfigureAwait(false);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return EResult.Invalid;
			}

			Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("sub/{0}", subID), String.Format("{0}/{1}", result, purchaseResult)));

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

		private async Task<EResult> ClaimPlaytest(uint appID) {
			PlaytestAccessResponse? response = await WebRequest.RequestPlaytestAccess(Bot, appID).ConfigureAwait(false);

			if (response == null) {
				// Playtest does not exist currently
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Invalid));

				return EResult.Invalid;
			}

			if (response.Success != 1) {
				// Not sure if/when this happens
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Failed));

				return EResult.Invalid;
			}

			if (response.Granted == null) {
				// Playtest has limited slots, account was added to the waitlist
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Waitlisted));
				// This won't show up in our owned apps until we're accepted, save it so we don't retry
				BotCache.AddWaitlistedPlaytest(appID);

				return EResult.OK;
			}

			// Access to playtest granted
			Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), EResult.OK));

			return EResult.OK;
		}

		internal string GetStatus() {
			HashSet<string> responses = new HashSet<string>();

			int activationsPastHour = Math.Min(BotCache.NumActivationsPastHour(), (int) MaxActivationsPerHour);
			responses.Add(String.Format(Strings.QueueStatus, BotCache.Packages.Count, activationsPastHour, ActivationsPerHour));

			if (PauseWhilePlaying && !Bot.IsPlayingPossible) {
				responses.Add(Strings.QueuePausedWhileIngame);
			}

			if (activationsPastHour >= ActivationsPerHour) {
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddHours(1).AddMinutes(1);
				responses.Add(String.Format(Strings.QueueLimitedUntil, String.Format("{0:T}", resumeTime)));
			}

			if (BotCache.ChangedApps.Count > 0 || BotCache.ChangedPackages.Count > 0) {
				responses.Add(String.Format(Strings.QueueDiscoveryStatus, BotCache.ChangedApps.Count, BotCache.ChangedPackages.Count));
			}

			return String.Join(" ", responses);;
		}

		private static int GetMillisecondsFromNow(DateTime then) => Math.Max(0, (int) (then - DateTime.Now).TotalMilliseconds);
		private void UpdateTimer(DateTime then) => Timer?.Change(GetMillisecondsFromNow(then), Timeout.Infinite);
	}
}