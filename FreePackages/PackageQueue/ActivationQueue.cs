using System;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal sealed class ActivationQueue : PackageQueue {
		private const int DelayBetweenActivationsSeconds = 5;
		internal readonly uint ActivationsPerPeriod = 25;
		internal const uint MaxActivationsPerPeriod = 30; // Steam's imposed limit
		internal const uint ActivationPeriodMinutes = 90; // Steam's imposed limit
		internal bool PauseWhilePlaying = false;

		internal ActivationQueue(Bot bot, BotCache botCache, uint? packageLimit, bool pauseWhilePlaying) : base(bot, botCache) {
			PauseWhilePlaying = pauseWhilePlaying;

			if (packageLimit != null) {
				ActivationsPerPeriod = Math.Min(packageLimit.Value, MaxActivationsPerPeriod);
			}
		}

		protected override Package? GetNextPackage() {
			return BotCache.GetNextPackage();
		}

		protected override DateTime? BeforeProcessing() {
			if (BotCache.NumActivationsPastPeriod() >= ActivationsPerPeriod) {
				// Rate limit reached
				DateTime resumeTime = BotCache.GetLastActivation()!.Value.AddMinutes(ActivationPeriodMinutes + 1);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.ActivationPaused, String.Format("{0:T}", resumeTime)));

				return resumeTime;
			}

			if (PauseWhilePlaying && !Bot.IsPlayingPossible) {
				// Don't activate anything while the user is playing a game (does not apply to ASF card farming)
				return DateTime.Now.AddMinutes(1);
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

			if (result == EResult.OK || result == EResult.Invalid || result == EResult.AlreadyOwned) {
				BotCache.RemovePackage(package);
			} else if (result == EResult.Timeout) {
				return DateTime.Now.AddMinutes(5);
			}

			if (BotCache.Packages.Count > 0) {
				return DateTime.Now.AddSeconds(DelayBetweenActivationsSeconds);
			}

			return null;
		}
	}
}
