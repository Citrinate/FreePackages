using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;

namespace FreePackages {
	internal static class WebRequest {
		internal const uint AccountLicensesRequestSpacingMilliseconds = 3000;
		private static readonly SemaphoreSlim AccountLicensesRequestSpacingSemaphore = new SemaphoreSlim(1, 1);

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

		internal static async Task<AccountLicensesResponse?> GetAccountLicenses(Bot bot, AccountLicensesResponse? previousPage = null) {
			await AccountLicensesRequestSpacingSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				List<string> parameters = new List<string>();
				if (previousPage != null && previousPage.ContinuationToken != null && previousPage.ContinuationOffset != null) {
					parameters.Add($"continuationToken={previousPage.ContinuationToken}");
					parameters.Add($"offset={previousPage.ContinuationOffset}");
				}

				Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/account/licenses/?{0}", String.Join("&", parameters)));
				HtmlDocumentResponse? accountLicensesPage = await bot.ArchiWebHandler.UrlGetToHtmlDocumentWithSession(request).ConfigureAwait(false);

				try {
					AccountLicensesResponse accountLicensesResponse = new AccountLicensesResponse(bot, accountLicensesPage?.Content);

					return accountLicensesResponse;
				} catch (Exception e) {
					bot.ArchiLogger.LogGenericException(e);

					return null;
				}
			} finally {
				await Task.Delay(TimeSpan.FromMilliseconds(AccountLicensesRequestSpacingMilliseconds)).ConfigureAwait(false);
				AccountLicensesRequestSpacingSemaphore.Release();
			}
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