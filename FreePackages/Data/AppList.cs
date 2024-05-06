using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace FreePackages {
	internal static class AppList {
		internal static async Task<HashSet<uint>?> GetAllApps() {
			try {
				return (await GetCachedAPIApps().ConfigureAwait(false))
					.Union(await GetAPIApps().ConfigureAwait(false))
					.Union(await GetStoreAPIApps().ConfigureAwait(false))
					.ToHashSet<uint>();

			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}
		}

		private static async Task<HashSet<uint>> GetCachedAPIApps() {
			ArgumentNullException.ThrowIfNull(ASF.WebBrowser);
			
			try {
				Uri source = new("https://raw.githubusercontent.com/Citrinate/Steam-MarketableApps/main/data/marketable_apps.min.json");
				ObjectResponse<HashSet<uint>>? response = await ASF.WebBrowser.UrlGetToJsonObject<HashSet<uint>>(source).ConfigureAwait(false);

				ArgumentNullException.ThrowIfNull(response);
				ArgumentNullException.ThrowIfNull(response.Content);

				return response.Content;
			} catch (Exception) {
				throw;
			}
		}

		private static async Task<HashSet<uint>> GetAPIApps() {
			Bot? bot = Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
		
			ArgumentNullException.ThrowIfNull(bot);

			MethodInfo? GetMarketableAppIDs = typeof(Bot).GetMethods(BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance).FirstOrDefault(x => x.Name == "GetMarketableAppIDs");

			ArgumentNullException.ThrowIfNull(GetMarketableAppIDs);

			try {
				var res = (Task<HashSet<uint>?>?) GetMarketableAppIDs.Invoke(bot, new object[]{});
				
				ArgumentNullException.ThrowIfNull(res);

				await res.ConfigureAwait(false);

				ArgumentNullException.ThrowIfNull(res.Result);

				return res.Result;
			} catch (Exception) {
				throw;
			}
		}

		private static async Task<HashSet<uint>> GetStoreAPIApps() {
			Bot? bot = Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);

			ArgumentNullException.ThrowIfNull(bot);

			try {
				using WebAPI.AsyncInterface storeService = bot.SteamConfiguration.GetAsyncWebAPIInterface("IStoreService");

				List<uint> apps = new();
				uint lastAppId = 0;
				KeyValue response;
				do {
					response = await storeService.CallAsync(HttpMethod.Get, "GetAppList", 1, new Dictionary<string, object?> {
						{ "access_token", bot.AccessToken },
						{ "last_appid", lastAppId },
						{ "max_results", 50000 },
					}).ConfigureAwait(false);

					apps.AddRange(response["apps"].Children.Select(app => app["appid"].AsUnsignedInteger()).ToList());
					lastAppId = response["last_appid"].AsUnsignedInteger();
				} while (response["have_more_results"].AsBoolean());

				return apps.Distinct().ToHashSet<uint>();
			} catch (Exception) {
				throw;
			}
		}
	}
}