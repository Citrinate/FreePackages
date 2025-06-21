using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal static class ProductInfo {
		private static SemaphoreSlim ProductInfoSemaphore = new SemaphoreSlim(1, 1);
		internal const int ProductInfoLimitingDelaySeconds = 10;
		internal const int ItemsPerProductInfoRequest = 255;

		internal async static Task<List<SteamApps.PICSProductInfoCallback>?> GetProductInfo(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null, CancellationToken? cancellationToken = null) {
			List<SteamApps.PICSProductInfoCallback> productInfo = new();

			foreach ((HashSet<uint>? batchedAppIDs, HashSet<uint>? batchedPackageIDs) in GetProductIDBatches(appIDs, packageIDs)) {
				cancellationToken?.ThrowIfCancellationRequested();

				List<SteamApps.PICSProductInfoCallback>? partialProductInfo = await FetchProductInfo(batchedAppIDs, batchedPackageIDs).ConfigureAwait(false);
				if (partialProductInfo == null) {
					return null;
				}

				productInfo = productInfo.Concat(partialProductInfo).ToList();
			}

			return productInfo;
		}

		internal static IEnumerable<(HashSet<uint>?, HashSet<uint>?)> GetProductIDBatches(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null) {
			if ((appIDs?.Count ?? 0) + (packageIDs?.Count ?? 0) <= ItemsPerProductInfoRequest) {
				 yield return (appIDs, packageIDs);
			} else {
				if (appIDs != null) {
					for (int i = 0; i < Math.Ceiling((decimal) appIDs.Count / ItemsPerProductInfoRequest); i++) {
						HashSet<uint> batchedAppIDs = appIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();

						yield return (batchedAppIDs, null);
					}
				}

				if (packageIDs != null) {
					for (int i = 0; i < Math.Ceiling((decimal) packageIDs.Count / ItemsPerProductInfoRequest); i++) {
						HashSet<uint> batchedPackageIDs = packageIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();

						yield return (null, batchedPackageIDs);
					}
				}
			}
		}

		private async static Task<List<SteamApps.PICSProductInfoCallback>?> FetchProductInfo(IEnumerable<uint>? appIDs = null, IEnumerable<uint>? packageIDs = null) {
			await ProductInfoSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? bot = Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
				if (bot == null) {
					return null;
				}

				var apps = appIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : appIDs.Select(x => new SteamApps.PICSRequest(x));
				var packages = packageIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : packageIDs.Select(x => new SteamApps.PICSRequest(x, ASF.GlobalDatabase?.PackageAccessTokensReadOnly.GetValueOrDefault(x, (ulong) 0) ?? 0));
				var response = await bot.SteamApps.PICSGetProductInfo(apps, packages).ToLongRunningTask().ConfigureAwait(false);

				return response.Results?.ToList();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(ProductInfoLimitingDelaySeconds)).ConfigureAwait(false);
						ProductInfoSemaphore.Release();
					}
				);
			}
		}
	}
}