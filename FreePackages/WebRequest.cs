using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web;
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
			// Steam returns 401 with body "false" when the playtest does not exist for this appID.
			// Treat that as a normal negative result instead of a failed HTTP request (which spams WARN).
			ObjectResponse<Steam.PlaytestAccessResponse>? playtestAccessResponse = await bot.ArchiWebHandler.UrlPostToJsonObjectWithSession<Steam.PlaytestAccessResponse>(
				request,
				data: data,
				requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors,
				maxTries: 1
			).ConfigureAwait(false);

			if (playtestAccessResponse == null || playtestAccessResponse.StatusCode != HttpStatusCode.OK) {
				return null;
			}

			return playtestAccessResponse.Content;
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

		internal static Steam.UserInfo? GetUserInfo(Bot bot) {
			string? countryCode = GetStoreCountryCode(bot);

			if (string.IsNullOrEmpty(countryCode)) {
				return null;
			}

			return new Steam.UserInfo {
				LoggedIn = true,
				SteamID = bot.SteamID.ToString(),
				CountryCode = countryCode
			};
		}

		internal static string? GetStoreCountryCode(Bot bot) {
			string? cookieCountry = ParseSteamCountryCookie(bot.ArchiWebHandler.WebBrowser.CookieContainer.GetCookieValue(ArchiWebHandler.SteamStoreURL, "steamCountry"))
				?? ParseSteamCountryCookie(bot.ArchiWebHandler.WebBrowser.CookieContainer.GetCookieValue(ArchiWebHandler.SteamCommunityURL, "steamCountry"));

			if (!string.IsNullOrEmpty(cookieCountry)) {
				return cookieCountry;
			}

			return typeof(Bot).GetField("IPCountryCode", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(bot) as string;
		}

		private static string? ParseSteamCountryCookie(string? value) {
			if (string.IsNullOrEmpty(value)) {
				return null;
			}

			string decoded = Uri.UnescapeDataString(value);
			int separator = decoded.IndexOfAny(['|', '%']);
			string country = separator >= 0 ? decoded[..separator] : decoded;
			country = country.Trim();

			return (country.Length == 2) ? country.ToUpperInvariant() : null;
		}
	}
}
