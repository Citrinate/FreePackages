using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;
using FreePackages.Localization;

// There are limitations to using PICS for discovery such that an account that's online 24/7 can still miss certain free games
// To fill in some of these gaps, we periodically check the free apps/subs list provided by https://github.com/C4illin/ASFinfo
// For more information see here: https://github.com/Citrinate/FreePackages/commit/7541807f10e8dde53b1352a2c103b867e5446fa1#commitcomment-137669223

namespace FreePackages {
	internal static class ASFInfo {
		private static Uri Source = new("https://gist.githubusercontent.com/C4illin/e8c5cf365d816f2640242bf01d8d3675/raw/Steam%2520Codes");
		private static readonly Regex SourceLine = new Regex("(?<type>[as])/(?<id>[0-9]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase); // Match examples: a/12345 or s/12345
		private static TimeSpan UpdateFrequency = TimeSpan.FromHours(1);

		private static Timer UpdateTimer = new(async e => await DoUpdate().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
		
		internal static void Update() {
			UpdateTimer.Change(TimeSpan.FromMinutes(15), UpdateFrequency);
		}

		private static async Task DoUpdate() {
			ArgumentNullException.ThrowIfNull(ASF.WebBrowser);
			ArgumentNullException.ThrowIfNull(FreePackages.GlobalCache);

			StreamResponse? response = await ASF.WebBrowser.UrlGetToStream(Source).ConfigureAwait(false);

			if (response == null) {
				ASF.ArchiLogger.LogNullError(response);

				return;
			}

			if (response.Content == null) {
				ASF.ArchiLogger.LogNullError(response.Content);

				return;
			}

			HashSet<uint> appIDs = new();
			HashSet<uint> packageIDs = new();
			uint itemCount = 0;

			using (StreamReader sr = new StreamReader(response.Content)) {
				while (sr.Peek() >= 0) {
					itemCount++;
					string? line = sr.ReadLine();

					if (line == null) {
						ASF.ArchiLogger.LogNullError(line);

						return;
					}

					if (itemCount <= FreePackages.GlobalCache.LastASFInfoItemCount) {
						continue;
					}

					Match item = SourceLine.Match(line);

					if (!item.Success) {
						ASF.ArchiLogger.LogGenericError(String.Format("{0}: {1}", Strings.ASFInfoParseFailed, line));

						return;
					}

					if (!uint.TryParse(item.Groups["id"].Value, out uint id)) {
						ASF.ArchiLogger.LogGenericError(String.Format("{0}: {1}", Strings.ASFInfoParseFailed, line));

						return;
					}

					if (item.Groups["type"].Value == "a") {
						// App
						appIDs.Add(id);
					} else if (item.Groups["type"].Value == "s") {
						// Sub
						packageIDs.Add(id);
					}
				}
			}
			
			if (appIDs.Count == 0 && packageIDs.Count == 0) {
				return;
			}

			PackageHandler.Handlers.Values.ToList().ForEach(x => x.BotCache.AddChanges(appIDs, packageIDs));
			FreePackages.GlobalCache.UpdateASFInfoItemCount(itemCount);
			Utilities.InBackground(async() => await PackageHandler.HandleChanges().ConfigureAwait(false));
		}
	}
}