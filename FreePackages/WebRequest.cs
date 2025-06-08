using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using SteamKit2;

namespace FreePackages {
	internal static class WebRequest {
		internal static async Task<Steam.UserData?> GetUserData(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<Steam.UserData>? userDataResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<Steam.UserData>(request).ConfigureAwait(false);
			
			return userDataResponse?.Content;
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

		internal static async Task<Steam.PlaytestAccessResponse?> RequestPlaytestAccess(Bot bot, uint appID) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/ajaxrequestplaytestaccess/{0}", appID));
			Dictionary<string, string> data = new(1); // Extra entry for sessionID
			// Returns 401 error error with body "false" if playtest doesn't exist for appID
			ObjectResponse<Steam.PlaytestAccessResponse>? playtestAccessResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<Steam.PlaytestAccessResponse>(request, data: data, maxTries: 1).ConfigureAwait(false);

			return playtestAccessResponse?.Content;
		}

		internal static async Task<IDocument?> GetAccountLicenses(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/account/licenses/");
			HtmlDocumentResponse? accountLicensesResponse = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			return accountLicensesResponse?.Content;
		}

		internal static async Task<Steam.RemoveLicenseResponse?> RemoveLicense(Bot bot, uint packageID) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/account/removelicense/");
			Dictionary<string, string> data = new(2) { // Extra entry for sessionID
				{ "packageid", packageID.ToString() }
			};

			ObjectResponse<Steam.RemoveLicenseResponse>? removeLicenseResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<Steam.RemoveLicenseResponse>(request, data: data).ConfigureAwait(false);

			return removeLicenseResponse?.Content;
		}
	}
}