using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace FreePackages {
	internal static class WebRequest {
		private static SemaphoreSlim AppDetailsSemaphore = new SemaphoreSlim(1, 1);
		private static SemaphoreSlim StorePageSemaphore = new SemaphoreSlim(1, 1);
		private const int AppDetailsDelaySeconds = 2;
		private const int StorePageDelaySeconds = 2;

		internal static async Task<UserData?> GetUserData(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<UserData>? userDataResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<UserData>(request).ConfigureAwait(false);
			
			return userDataResponse?.Content;
		}

		internal static async Task<AppDetails?> GetAppDetails(uint appID) {
			ArgumentNullException.ThrowIfNull(ASF.WebBrowser);

			// Reportedly, this API has a possibly reachable rate limit of 200 requests per 5 minutes
			await AppDetailsSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/api/appdetails/?appids={0}", appID));
				ObjectResponse<Dictionary<uint, AppDetails>>? appDetailsResponse = await ASF.WebBrowser.UrlGetToJsonObject<Dictionary<uint, AppDetails>>(request, maxTries: 1).ConfigureAwait(false);
				
				return appDetailsResponse?.Content?[appID];
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

		internal static async Task<PlaytestAccessResponse?> RequestPlaytestAccess(Bot bot, uint appID) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/ajaxrequestplaytestaccess/{0}", appID));
			Dictionary<string, string> data = new(1); // Extra entry for sessionID
			// Returns 401 error error with body "false" if playtest doesn't exist for appID
			ObjectResponse<PlaytestAccessResponse>? playtestAccessResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<PlaytestAccessResponse>(request, data: data, maxTries: 1).ConfigureAwait(false);

			return playtestAccessResponse?.Content;
		}

		internal static async Task<HtmlDocumentResponse?> GetStorePage(uint? appID) {
			ArgumentNullException.ThrowIfNull(appID);
			ArgumentNullException.ThrowIfNull(ASF.WebBrowser);

			await StorePageSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/app/{0}", appID));

				return await ASF.WebBrowser.UrlGetToHtmlDocument(request, maxTries: 1, requestOptions: WebBrowser.ERequestOptions.ReturnRedirections);
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(StorePageDelaySeconds)).ConfigureAwait(false);
						StorePageSemaphore.Release();
					}
				);
			}
		}
	}
}