using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal sealed class RemovalQueue(Bot bot, BotCache botCache) : PackageQueue(bot, botCache) {
		private const int DelayBetweenRemovalsSeconds = 1;
		private const int RateLimitedCooldownMinutes = 10;
		private readonly HashSet<EPackageType> RemovalTypes = [EPackageType.RemoveSub];
		internal int RemovalsRemaining => BotCache.Packages.Where(x => RemovalTypes.Contains(x.Type)).Count();

		protected override Package? GetNextPackage() => BotCache.GetNextPackage(RemovalTypes);

		protected override DateTime? BeforeProcessing() => null;

		protected override DateTime? HandleResult(Package package, EResult result) {
			if (result == EResult.RateLimitExceeded) {
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
