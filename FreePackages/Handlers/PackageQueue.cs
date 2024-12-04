using System;
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
		private const int DelayBetweenActivationsSeconds = 5;
		private readonly uint ActivationsPerPeriod = 25;
		private const uint MaxActivationsPerPeriod = 30; // Steam's imposed limit
		internal const uint ActivationPeriodMinutes = 90; // Steam's imposed limit
		private bool PauseWhilePlaying = false;

		internal PackageQueue(Bot bot, BotCache botCache, uint? packageLimit, bool pauseWhilePlaying) {
			Bot = bot;
			BotCache = botCache;
			PauseWhilePlaying = pauseWhilePlaying;

			if (packageLimit != null) {
				ActivationsPerPeriod = Math.Min(packageLimit.Value, MaxActivationsPerPeriod);
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
				// Used to remove duplicates.  
				// Whenever we're trying to activate an app and also an package for that app, get rid of the app.
				// I only really like to do this because the error messages for packages are more descriptive and useful.
				BotCache.RemoveAppPackages(appIDsToRemove);
			}
		}

		internal void AddPackages(IEnumerable<Package> packages) {
			if (!BotCache.AddPackages(packages)) {
				return;
			}
		}

		internal void Start() {
			UpdateTimer(DateTime.Now.AddMinutes(1));
		}

		private async Task ProcessQueue() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			Package? package = BotCache.GetNextPackage();
			if (package == null) {
				// No packages to activate
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			if (BotCache.NumActivationsPastPeriod() >= ActivationsPerPeriod) {
				// Rate limit reached
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddMinutes(ActivationPeriodMinutes + 1);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));
				UpdateTimer(resumeTime);
				
				return;
			}

			if (PauseWhilePlaying && !Bot.IsPlayingPossible) {
				// Don't activate anything while the user is playing a game (does not apply to ASF card farming)
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			EResult result = await ClaimPackage(package).ConfigureAwait(false);

			if (result == EResult.RateLimitExceeded) {
				BotCache.AddActivation(DateTime.Now, MaxActivationsPerPeriod); // However many activations we thought were made, we were wrong.  Correct for this by adding a bunch of fake times to our cache
				DateTime resumeTime = DateTime.Now.AddMinutes(ActivationPeriodMinutes + 1);
				Bot.ArchiLogger.LogGenericInfo(Strings.RateLimitExceeded);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));
				UpdateTimer(resumeTime);

				return;
			}

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
			// Sometimes it'll return EResult.RateLimitExceeded, but this is unrelated to the package limit
			if (response.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), response.Result));

				return EResult.Fail;
			}

			if (response.GrantedApps.Count > 0 || response.GrantedPackages.Count > 0) {
				// When only GrantedPackages is empty we probably tried to activate an app we already own.  I don't think it's possible for only GrantedApps to be empty
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicenseWithItems, String.Format("app/{0}", appID), response.Result, String.Join(", ", response.GrantedApps.Select(x => $"app/{x}").Union(response.GrantedPackages.Select(x => $"sub/{x}")))));

				return EResult.OK;
			}

			// Either app isn't available, or we're rate limited.  Impossible to tell the difference
			// Assume invalid as to not attempt to activate invalid apps endlessly
			Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), Strings.Unknown));

			return EResult.Invalid;
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

			if (result == EResult.OK) {
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("sub/{0}", subID), String.Format("{0}/{1}", result, purchaseResult)));
			} else {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("sub/{0}", subID), String.Format("{0}/{1}", result, purchaseResult)));
			}

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
			Steam.PlaytestAccessResponse? response = await WebRequest.RequestPlaytestAccess(Bot, appID).ConfigureAwait(false);

			if (response == null) {
				// Playtest does not exist currently
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Invalid));

				return EResult.Invalid;
			}

			if (response.Success != 1) {
				// Not sure if/when this happens
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Failed));

				return EResult.Invalid;
			}

			if (response.Granted == null) {
				// Playtest has limited slots, account was added to the waitlist
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), Strings.Waitlisted));

				// This won't show up in our owned apps until we're accepted, save it so we don't attempt to join the playtest again
				BotCache.AddWaitlistedPlaytest(appID);

				return EResult.OK;
			}

			// Access to unlimited playtest granted
			Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("playtest/{0}", appID), EResult.OK));

			return EResult.OK;
		}

		internal string GetStatus() {
			HashSet<string> responses = new HashSet<string>();

			int activationsPastPeriod = Math.Min(BotCache.NumActivationsPastPeriod(), (int) MaxActivationsPerPeriod);
			responses.Add(String.Format(Strings.QueueStatus, BotCache.Packages.Count, activationsPastPeriod, ActivationsPerPeriod));

			if (PauseWhilePlaying && !Bot.IsPlayingPossible) {
				responses.Add(Strings.QueuePausedWhileIngame);
			}

			if (activationsPastPeriod >= ActivationsPerPeriod) {
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddMinutes(ActivationPeriodMinutes + 1);
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
