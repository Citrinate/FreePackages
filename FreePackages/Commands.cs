using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using FreePackages.Localization;

namespace FreePackages {
	internal static class Commands {
		internal static async Task<string?> Response(Bot bot, EAccess access, ulong steamID, string message, string[] args) {
			if (!Enum.IsDefined(access)) {
				throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
			}

			if (string.IsNullOrEmpty(message)) {
				return null;
			}

			switch (args.Length) {
				case 1:
					switch (args[0].ToUpperInvariant()) {
						case "FREEPACKAGES" when access >= EAccess.Master:
							return String.Format("{0} {1}", nameof(FreePackages), (typeof(FreePackages).Assembly.GetName().Version ?? new Version("0")).ToString());

						case "CLEARFREEPACKAGESQUEUE":
							return ResponseClearQueue(bot, access);

						case "QSA":
							return ResponseQueueStatus(access, steamID, "ASF");
						case "QSTATUS" or "QUEUESTATUS":
							return ResponseQueueStatus(bot, access);

						case "REMOVEALLFREEPACKAGES":
							return await ResponseRemoveAllFreePackages(bot, access).ConfigureAwait(false);

						default:
							return null;
					};
				default:
					switch (args[0].ToUpperInvariant()) {
						case "CLEARFREEPACKAGESQUEUE":
							return ResponseClearQueue(access, steamID, args[1]);

						case "QSTATUS" or "QUEUESTATUS":
							return ResponseQueueStatus(access, steamID, args[1]);

						case "QLICENSE" or "QUEUELICENSE" or "QLICENCE" or "QUEUELICENCE" when args.Length > 2:
							return ResponseQueueLicense(access, steamID, args[1], Utilities.GetArgsAsText(args, 2, ","));
						case "QLICENSE" or "QUEUELICENSE" or "QLICENCE" or "QUEUELICENCE" :
							return ResponseQueueLicense(bot, access, args[1]);

						case "QLICENSE^" or "QUEUELICENSE^" or "QLICENCE^" or "QUEUELICENCE^" when args.Length > 2:
							return ResponseQueueLicense(access, steamID, args[1], Utilities.GetArgsAsText(args, 2, ","), useFilter: true);
						case "QLICENSE^" or "QUEUELICENSE^" or "QLICENCE^" or "QUEUELICENCE^" :
							return ResponseQueueLicense(bot, access, args[1], useFilter: true);

						case "REMOVEALLFREEPACKAGES" :
							return await ResponseRemoveAllFreePackages(access, steamID, args[1]).ConfigureAwait(false);

						default:
							return null;
					}
			}
		}

		private static string? ResponseClearQueue(Bot bot, EAccess access) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
			}

			return FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].ClearQueue());
		}

		private static string? ResponseClearQueue(EAccess access, ulong steamID, string botNames) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseClearQueue(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID)));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
		}

		private static string? ResponseQueueStatus(Bot bot, EAccess access) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
			}

			return FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].GetStatus());
		}

		private static string? ResponseQueueStatus(EAccess access, ulong steamID, string botNames) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseQueueStatus(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID)));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
		}

		private static string? ResponseQueueLicense(Bot bot, EAccess access, string licenses, bool useFilter = false, [CallerMemberName] string? previousMethodName = null) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, "Free Packages plugin not enabled");
			}

			// https://github.com/JustArchiNET/ArchiSteamFarm/blob/d972c93072dd8d2bf0f2cecda3561dc3ba77a9ed/ArchiSteamFarm/Steam/Interaction/Commands.cs#L626C3-L626C34
			StringBuilder response = new();

			string[] entries = licenses.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

			foreach (string entry in entries) {
				uint gameID;
				string type;

				int index = entry.IndexOf('/', StringComparison.Ordinal);

				if ((index > 0) && (entry.Length > index + 1)) {
					if (!uint.TryParse(entry[(index + 1)..], out gameID) || (gameID == 0)) {
						response.AppendLine(FormatBotResponse(bot, string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.ErrorIsInvalid, nameof(gameID))));

						continue;
					}

					type = entry[..index];
				} else if (uint.TryParse(entry, out gameID) && (gameID > 0)) {
					type = "SUB";
				} else {
					response.AppendLine(FormatBotResponse(bot, string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.ErrorIsInvalid, nameof(gameID))));

					continue;
				}

				EPackageType packageType;
				type = type.ToUpperInvariant();
				if (type == "A" || type == "APP") {
					packageType = EPackageType.App;
				} else {
					packageType = EPackageType.Sub;
				}

				response.AppendLine(FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].AddPackage(packageType, gameID, useFilter)));
			}

			if (previousMethodName == nameof(Response)) {
				Utilities.InBackground(async() => await PackageHandler.HandleChanges().ConfigureAwait(false));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static string? ResponseQueueLicense(EAccess access, ulong steamID, string botNames, string licenses, bool useFilter = false) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseQueueLicense(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), licenses, useFilter));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			Utilities.InBackground(async() => await PackageHandler.HandleChanges().ConfigureAwait(false));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
		}

		private static async Task<string?> ResponseRemoveAllFreePackages(Bot bot, EAccess access) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, "Free Packages plugin not enabled");
			}

			IDocument? accountLicensesPage = await WebRequest.GetAccountLicenses(bot);
			if (accountLicensesPage == null) {
				return FormatBotResponse(bot, "Failed to fetch licenses page");
			}

			Regex removablePackageIDsRegex = new Regex("(?<=javascript:RemoveFreeLicense\\( )[0-9]+", RegexOptions.CultureInvariant);
			MatchCollection removablePackageIDMatches = removablePackageIDsRegex.Matches(accountLicensesPage.Source.Text);
			if (removablePackageIDMatches.Count == 0) {
				return FormatBotResponse(bot, "Failed to find any removable package ids");
			}

			HashSet<uint> removablePackgeIDs = new();
			foreach (Match match in removablePackageIDMatches) {
				if (uint.TryParse(match.Value, out uint packageID)) {
					removablePackgeIDs.Add(packageID);
				} else {
					return FormatBotResponse(bot, String.Format("Failed to parse package ids match: {0}", match.Value));
				}
			}

			foreach (uint packageID in removablePackgeIDs) {
				PackageHandler.Handlers[bot.BotName].AddPackage(EPackageType.RemoveSub, packageID, false);
			}

			return String.Format("Removing {0} packages", removablePackgeIDs.Count);
		}

		private static async Task<string?> ResponseRemoveAllFreePackages(EAccess access, ulong steamID, string botNames) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = await Utilities.InParallel(bots.Select(bot => ResponseRemoveAllFreePackages(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID)))).ConfigureAwait(false);

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
		}

		private static string FormatStaticResponse(string response) => ArchiSteamFarm.Steam.Interaction.Commands.FormatStaticResponse(response);
		private static string FormatBotResponse(Bot bot, string response) => bot.Commands.FormatBotResponse(response);
	}
}