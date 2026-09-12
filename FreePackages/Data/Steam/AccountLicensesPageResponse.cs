using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using ArchiSteamFarm.Steam;

namespace FreePackages {
	internal sealed class AccountLicensesResponse {
		private static readonly Regex RemovablePackageIDsRegex = new Regex("RemoveFreeLicense\\(\\s*(?<subID>[0-9]+),\\s*'(?<encodedName>[A-Za-z0-9+/=]*)'", RegexOptions.CultureInvariant); // matches the parameters of: RemoveFreeLicense( 45946, 'UmV2ZXJzaW9uOiBUaGUgRXNjYXBl' );
		private static readonly Regex NextPageQueryParametersRegex = new Regex("\"\\?continuationToken=(?<continuationToken>[0-9:]+)&(?:amp;)?offset=(?<offset>[0-9]+)\"", RegexOptions.CultureInvariant);
		internal const uint PackagesPerPage = 100;

		internal Dictionary<uint, string> RemoveablePackages = new();
		internal string? ContinuationToken;
		internal uint? ContinuationOffset;

		internal bool HasNextPage => ContinuationToken != null && ContinuationOffset != null;
		internal bool IsSamePage(AccountLicensesResponse? page) => page != null && page.ContinuationOffset == ContinuationOffset && page.ContinuationToken == ContinuationToken;

		internal AccountLicensesResponse(Bot bot, IDocument? accountLicensesPage) {
			ArgumentNullException.ThrowIfNull(accountLicensesPage);

			// Parse removable packages
			{
				MatchCollection removablePackageMatches = RemovablePackageIDsRegex.Matches(accountLicensesPage.Source.Text);
				foreach (Match match in removablePackageMatches) {
					string name;
					try {
						name = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["encodedName"].Value));
					} catch (Exception e) {
						bot.ArchiLogger.LogGenericException(e);

						throw new FormatException(String.Format(ArchiSteamFarm.Localization.Strings.ErrorParsingObject, "encodedName"));
					}

					string subIDString = match.Groups["subID"].Value;
					if (!uint.TryParse(subIDString, out uint subID)) {
						throw new FormatException(String.Format(ArchiSteamFarm.Localization.Strings.ErrorParsingObject, "subID"));
					}

					RemoveablePackages[subID] = name;
				}
			}

			// Get query parameters for the next page
			{
				Match nextPageQueryParameters = NextPageQueryParametersRegex.Match(accountLicensesPage.Source.Text);
				if (nextPageQueryParameters.Success) {
					ContinuationToken = nextPageQueryParameters.Groups["continuationToken"].Value;

					string continuationOffsetString = nextPageQueryParameters.Groups["offset"].Value;
					if (!uint.TryParse(continuationOffsetString, out uint continuationOffset)) {
						throw new FormatException(String.Format(ArchiSteamFarm.Localization.Strings.ErrorParsingObject, "continuationOffset"));
					}

					ContinuationOffset = continuationOffset;
				}
			}
		}
	}
}
