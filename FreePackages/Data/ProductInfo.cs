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
		private const int ProductInfoLimitingDelaySeconds = 5;
		private const int ItemsPerProductInfoRequest = 255;

		internal async static Task<List<SteamApps.PICSProductInfoCallback>?> GetProductInfo(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null, Func<List<SteamApps.PICSProductInfoCallback>, Task>? onFetchProductInfoCallback = null) {
			List<SteamApps.PICSProductInfoCallback> productInfo = new();

			if (appIDs != null) {
				for (int i = 0; i < Math.Ceiling((decimal) appIDs.Count / ItemsPerProductInfoRequest); i++) {
					HashSet<uint> batchAppIDs = appIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();
					
					List<SteamApps.PICSProductInfoCallback>? partialProductInfo = await FetchProductInfo(appIDs: batchAppIDs).ConfigureAwait(false);
					if (partialProductInfo == null) {
						return null;
					}

					// Process the data as it comes in using callback
					if (onFetchProductInfoCallback != null) {
						await onFetchProductInfoCallback(partialProductInfo).ConfigureAwait(false);
					}

					productInfo = productInfo.Concat(partialProductInfo).ToList();
				}
			}

			if (packageIDs != null) {
				for (int i = 0; i < Math.Ceiling((decimal) packageIDs.Count / ItemsPerProductInfoRequest); i++) {
					HashSet<uint> batchPackageIDs = packageIDs.Skip(i * ItemsPerProductInfoRequest).Take(ItemsPerProductInfoRequest).ToHashSet<uint>();

					List<SteamApps.PICSProductInfoCallback>? partialProductInfo = await FetchProductInfo(packageIDs: batchPackageIDs).ConfigureAwait(false);
					if (partialProductInfo == null) {
						return null;
					}

					// Process the data as it comes in using callback
					if (onFetchProductInfoCallback != null) {
						await onFetchProductInfoCallback(partialProductInfo).ConfigureAwait(false);
					}

					productInfo.Concat(partialProductInfo);
				}
			}

			return productInfo;
		}

		private async static Task<List<SteamApps.PICSProductInfoCallback>?> FetchProductInfo(IEnumerable<uint>? appIDs = null, IEnumerable<uint>? packageIDs = null) {
			await ProductInfoSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return null;
				}

				var apps = appIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : appIDs.Select(x => new SteamApps.PICSRequest(x));
				var packages = packageIDs == null ? Enumerable.Empty<SteamApps.PICSRequest>() : packageIDs.Select(x => new SteamApps.PICSRequest(x, ASF.GlobalDatabase?.PackageAccessTokensReadOnly.GetValueOrDefault(x, (ulong) 0) ?? 0));
				var response = await refreshBot.SteamApps.PICSGetProductInfo(apps, packages).ToLongRunningTask().ConfigureAwait(false);

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
		
		private static Bot? GetRefreshBot() => Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
	}
}