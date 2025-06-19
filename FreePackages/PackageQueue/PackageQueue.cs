using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal abstract class PackageQueue : IDisposable {
		protected readonly Bot Bot;
		protected readonly BotCache BotCache;
		private PackageFilter PackageFilter => PackageHandler.Handlers[Bot.BotName].PackageFilter;
		private Timer Timer;

		internal PackageQueue(Bot bot, BotCache botCache) {
			Bot = bot;
			BotCache = botCache;
			Timer = new Timer(async e => await ProcessQueue().ConfigureAwait(false), null, 0, Timeout.Infinite);
		}

		public void Dispose() {
			Timer.Dispose();
		}

		internal void Start() {
			UpdateTimer(DateTime.Now.AddMinutes(1));
		}

		private async Task ProcessQueue() {
			if (!Bot.IsConnectedAndLoggedOn || !PackageFilter.Ready) {
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			Package? package = GetNextPackage();
			if (package == null) {
				// No packages to activate
				UpdateTimer(DateTime.Now.AddMinutes(1));

				return;
			}

			{
				DateTime? waitUntil = BeforeProcessing();
				if (waitUntil != null) {
					UpdateTimer(waitUntil.Value);

					return;
				}
			}

			EResult result = await ProcessPackage(package).ConfigureAwait(false);

			{
				DateTime? waitUntil = HandleResult(package, result);
				if (waitUntil != null) {
					UpdateTimer(waitUntil.Value);

					return;
				}
			}

			UpdateTimer(DateTime.Now.AddMinutes(1));
		}

		protected abstract Package? GetNextPackage();

		protected abstract DateTime? BeforeProcessing();

		protected abstract DateTime? HandleResult(Package package, EResult result);

		private async Task<EResult> ProcessPackage(Package package) {
			if (package.Type == EPackageType.App) {
				return await ClaimFreeApp(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.Sub) {
				return await ClaimFreeSub(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.Playtest) {
				return await ClaimPlaytest(package.ID).ConfigureAwait(false);
			}

			if (package.Type == EPackageType.RemoveSub) {
				return await RemoveSub(package.ID).ConfigureAwait(false);
			}

			return EResult.Invalid;
		}

		private async Task<EResult> ClaimFreeApp(uint appID) {
			// One final check before claiming to make sure we still don't own this app
			if (PackageFilter.OwnsApp(appID)) {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), EResult.AlreadyOwned));

				return EResult.AlreadyOwned;
			}

			SteamApps.FreeLicenseCallback response;
			try {
				response = await Bot.SteamApps.RequestFreeLicense(appID).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return EResult.Timeout;
			}
			
			if (response.Result != EResult.OK) {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), response.Result));

				if (response.Result == EResult.RateLimitExceeded) {
					// Note: this is the rate limit for this api, and is unrelated to the package limit
					// I still treat this like a package rate limit however, as it seems to behave similarly, and doing this will avoid a lot of errors
					return EResult.RateLimitExceeded;
				}
				
				return EResult.Fail;
			}

			if (response.GrantedApps.Count > 0 || response.GrantedPackages.Count > 0) {
				// When only GrantedPackages is empty we probably tried to activate an app we already own.  I don't think it's possible for only GrantedApps to be empty
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicenseWithItems, String.Format("app/{0}", appID), response.Result, String.Join(", ", response.GrantedApps.Select(x => $"app/{x}").Union(response.GrantedPackages.Select(x => $"sub/{x}")))));

				return EResult.OK;
			}

			// App isn't available (usually not available in this region, which we can't determine ahead of time)
			// Ignore this AppID if we see it again in a PICS update (will not prevent us from activating it if it's discovered through a SubID or by some other method)
			BotCache.IgnoreApp(appID);
			Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("app/{0}", appID), Strings.Unknown));

			return EResult.Invalid;
		}

		private async Task<EResult> ClaimFreeSub(uint subID) {
			// One final check before claiming to make sure we still don't own this package
			if (PackageFilter.OwnsSub(subID)) {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("sub/{0}", subID), EResult.AlreadyOwned));

				return EResult.AlreadyOwned;
			}

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

		private async Task<EResult> RemoveSub(uint subID) {
			EResult result;
			try {
				result = await Bot.Actions.RemoveLicensePackage(subID).ConfigureAwait(false);
			} catch (Exception e) {
				Bot.ArchiLogger.LogGenericException(e);

				return EResult.Invalid;
			}

			if (result == EResult.OK) {
				Bot.ArchiLogger.LogGenericInfo(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("removeSub/{0}", subID), result));
			} else {
				Bot.ArchiLogger.LogGenericDebug(String.Format(ArchiSteamFarm.Localization.Strings.BotAddLicense, String.Format("removeSub/{0}", subID), result));
			}

			if (result == EResult.RateLimitExceeded) {
				return EResult.RateLimitExceeded;
			}

			if (result == EResult.Timeout) {
				return EResult.Timeout;
			}

			if (result != EResult.OK) {
				return EResult.Invalid;
			}

			return EResult.OK;
		}

		private static int GetMillisecondsFromNow(DateTime then) => Math.Max(0, (int)(then - DateTime.Now).TotalMilliseconds);
		private void UpdateTimer(DateTime then) => Timer?.Change(GetMillisecondsFromNow(then), Timeout.Infinite);
	}
}
