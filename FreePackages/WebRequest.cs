using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace FreePackages {
	internal static class WebRequest {
		private static SemaphoreSlim AppDetailsSemaphore = new SemaphoreSlim(1, 1);
		private const int AppDetailsDelaySeconds = 2;

		internal static async Task<UserData?> GetUserData(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<UserData>? userDataResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<UserData>(request).ConfigureAwait(false);
			
			return userDataResponse?.Content;
		}

		internal static async Task<AppDetails?> GetAppDetails(Bot bot, uint appID) {
			// Reportedly, this API has a possibly reachable rate limit of 200 requests per 5 minutes
			await AppDetailsSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/api/appdetails/?appids={0}", appID));
				ObjectResponse<JObject>? appDetailsResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<JObject>(request).ConfigureAwait(false);
				
				return appDetailsResponse?.Content?.Properties().First().Value.ToObject<AppDetails>();
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(AppDetailsDelaySeconds)).ConfigureAwait(false);
						AppDetailsSemaphore.Release();
					}
				);
			}
		}

		internal static async Task<Dictionary<uint, string>?> GetAppList(Bot bot) {
			WebAPI.AsyncInterface steamAppsService = bot.SteamConfiguration.GetAsyncWebAPIInterface("ISteamApps");
			KeyValue? response = await steamAppsService.CallAsync(HttpMethod.Get, "GetAppList", 2).ConfigureAwait(false);
			if (response == null) {
				return null;
			}

			Dictionary<uint, string> appList = new();
			foreach (var app in response["apps"].Children) {
				appList.TryAdd(app["appid"].AsUnsignedInteger(), app["name"].AsString()!);
			}

			return appList;
		}
	}
}