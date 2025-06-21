using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;
using SteamKit2;

namespace FreePackages {
	internal sealed class RemovalQueue(Bot bot, BotCache botCache) : PackageQueue(bot, botCache) {
		private const int DelayBetweenRemovalsSeconds = 1;
		private const int RateLimitedCooldownMinutes = 10;
		internal static readonly HashSet<EPackageType> RemovalTypes = [EPackageType.RemoveSub, EPackageType.RemoveApp];
		internal int RemovalsRemaining => BotCache.Packages.Where(x => RemovalTypes.Contains(x.Type)).Count();

		protected override Package? GetNextPackage() => BotCache.GetNextPackage([EPackageType.RemoveApp]) ?? BotCache.GetNextPackage([EPackageType.RemoveSub]);

		protected override DateTime? BeforeProcessing() => null;

		protected override DateTime? HandleResult(Package package, EResult result) {
			if (result == EResult.RateLimitExceeded) {
				DateTime resumeTime = DateTime.Now.AddMinutes(RateLimitedCooldownMinutes);
				Bot.ArchiLogger.LogGenericInfo(String.Format(Strings.RemovalsPaused, String.Format("{0:T}", resumeTime)));

				return DateTime.Now.AddMinutes(RateLimitedCooldownMinutes);
			}

			if (result == EResult.Timeout) {
				return DateTime.Now.AddMinutes(5);
			}

			BotCache.RemovePackage(package);

			if (RemovalsRemaining > 0) {
				return DateTime.Now.AddSeconds(DelayBetweenRemovalsSeconds);
			}

			return null;
		}
	}
}
