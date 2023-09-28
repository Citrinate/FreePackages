using System;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Integration;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json.Linq;

namespace FreePackages {
	internal static class WebRequest {
		internal static async Task<UserData?> GetUserData(Bot bot) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, "/dynamicstore/userdata/");
			ObjectResponse<UserData>? userDataResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<UserData>(request).ConfigureAwait(false);
			
			return userDataResponse?.Content;
		}

		internal static async Task<AppDetails?> GetAppDetails(Bot bot, uint appID) {
			Uri request = new(ArchiWebHandler.SteamStoreURL, String.Format("/api/appdetails/?appids={0}", appID));
			ObjectResponse<JObject>? appDetailsResponse = await bot.ArchiWebHandler.UrlGetToJsonObjectWithSession<JObject>(request).ConfigureAwait(false);
			
			return appDetailsResponse?.Content?.Properties().First().Value.ToObject<AppDetails>();
		}
	}
}