using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;

namespace FreePackages {
	internal static class WebRequest {
		internal static async Task<Steam.UserData?> GetUserData(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<Steam.UserData>? userDataResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<Steam.UserData>(request).ConfigureAwait(false);
			
			return userDataResponse?.Content;
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

		internal static async Task<Steam.UserInfo?> GetUserInfo(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "");
			HtmlDocumentResponse? storeResponse = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

			if (storeResponse == null || storeResponse.Content == null) {
				return null;
			}

			try {
				Regex pageObjRegex = new Regex("data-userinfo=\"({[\\s\\S]*?})\"", RegexOptions.CultureInvariant);
				Match match = pageObjRegex.Match(storeResponse.Content.Source.Text);

				if (!match.Success) {
					throw new Exception(String.Format(ArchiSteamFarm.Localization.Strings.ErrorIsEmpty, nameof(match)));
				}

				return match.Groups[1].Value.Replace("&quot;", "\"").ToJsonObject<Steam.UserInfo>();
			} catch (Exception e) {
				bot.ArchiLogger.LogGenericException(e);

				return null;
			}
		}
	}
}