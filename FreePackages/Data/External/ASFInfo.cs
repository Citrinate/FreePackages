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

			StreamResponse? response = await ASF.WebBrowser.UrlGetToStream(Source).ConfigureAwait(false);

			if (response == null) {
				ASF.ArchiLogger.LogNullError(nameof(response));

				return;
			}

			if (response.Content == null) {
				ASF.ArchiLogger.LogNullError(nameof(response.Content));

				return;
			}

			List<(EPackageType Type, uint Id)> items = new();

			try {
				using (StreamReader sr = new(response.Content)) {
					while (sr.Peek() >= 0) {
						string? line = sr.ReadLine();
						
						if (line == null) {
							ASF.ArchiLogger.LogNullError(nameof(line));

							continue;
						}

						Match match = SourceLine.Match(line);

						if (!match.Success) {
							ASF.ArchiLogger.LogGenericError(string.Format("{0}: {1}", Strings.ASFInfoParseFailed, line));
							
							return;
						}

						if (!uint.TryParse(match.Groups["id"].Value, out uint id)) {
							ASF.ArchiLogger.LogGenericError(string.Format("{0}: {1}", Strings.ASFInfoParseFailed, line));
							
							return;
						}

						if (match.Groups["type"].Value == "a") {
							items.Add((EPackageType.App, id));
						} else if (match.Groups["type"].Value == "s") {
							items.Add((EPackageType.Sub, id));
						}
					}
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return;
			}
			
			if (items.Count == 0) {
				return;
			}

			bool anyChangesAdded = false;

			foreach (PackageHandler handler in PackageHandler.Handlers.Values) {
				uint lastCount = handler.BotCache.LastASFInfoItemCount;
				if (lastCount >= items.Count) {
					continue;
				}

				HashSet<uint> appIDs = items.Skip((int) lastCount).Where(x => x.Type == EPackageType.App).Select(x => x.Id).ToHashSet();
				HashSet<uint> packageIDs = items.Skip((int) lastCount).Where(x => x.Type == EPackageType.Sub).Select(x => x.Id).ToHashSet();
				if (appIDs.Count == 0 && packageIDs.Count == 0) {
					continue;
				}

				handler.BotCache.AddChanges(appIDs, packageIDs);
				handler.BotCache.UpdateASFInfoItemCount((uint) items.Count);
				anyChangesAdded = true;
			}

			if (anyChangesAdded) {
				Utilities.InBackground(async () => await PackageHandler.HandleChanges().ConfigureAwait(false));
			}
		}
	}
}