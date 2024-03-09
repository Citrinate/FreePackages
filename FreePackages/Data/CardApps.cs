using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;

namespace FreePackages {
	internal static class CardApps {
		internal static HashSet<uint> AppIDs = new();
		
		private static Timer UpdateTimer = new(async e => await DoUpdate().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
		private static TimeSpan UpdateFrequency = TimeSpan.FromHours(1);

		internal static void Update() {
			UpdateTimer.Change(TimeSpan.Zero, UpdateFrequency);
		}

		private static async Task DoUpdate() {
			ArgumentNullException.ThrowIfNull(ASF.WebBrowser);

			Uri request = new("https://raw.githubusercontent.com/nolddor/steam-badges-db/main/data/badges.min.json");
			ObjectResponse<Badges>? response = await ASF.WebBrowser.UrlGetToJsonObject<Badges>(request).ConfigureAwait(false);

			if (response == null) {
				ASF.ArchiLogger.LogGenericDebug("Failed to fetch badge data for free packages");
				UpdateTimer.Change(TimeSpan.FromMinutes(1), UpdateFrequency);

				return;
			}

			try {
				ArgumentNullException.ThrowIfNull(response.Content);
				
				AppIDs = response.Content.Data.Keys.Select(uint.Parse).ToHashSet();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				ASF.ArchiLogger.LogGenericError("Failed to parse badge data for free packages");

				return;
			}
		}

		private sealed class Badges {
			[JsonExtensionData]
			[JsonInclude]
			internal Dictionary<string, JsonElement> Data { get; private init; } = new();
		}
	}
}