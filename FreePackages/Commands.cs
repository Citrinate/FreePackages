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

						case "CANCELREMOVE" or "CANCELREMOVAL":
							return ResponseCancelRemove(bot, access);

						case "CONFIRMREMOVE" or "CONFIRMREMOVAL":
							return ResponseConfirmRemove(bot, access);

						case "CLEARFREEPACKAGESQUEUE":
							return ResponseClearQueue(bot, access);

						case "QSA":
							return ResponseQueueStatus(access, steamID, "ASF");
						case "QSTATUS" or "QUEUESTATUS":
							return ResponseQueueStatus(bot, access);

						case "REMOVEFREEPACKAGES":
							return await ResponseRemoveFreePackages(bot, access, new StatusReporter(bot, steamID)).ConfigureAwait(false);

						case "REMOVEFREEPACKAGES^":
							return await ResponseRemoveFreePackages(bot, access, new StatusReporter(bot, steamID), excludePlayed : true).ConfigureAwait(false);

						default:
							return null;
					};
				default:
					switch (args[0].ToUpperInvariant()) {
						case "CANCELREMOVE" or "CANCELREMOVAL":
							return ResponseCancelRemove(access, steamID, args[1]);

						case "CONFIRMREMOVE" or "CONFIRMREMOVAL":
							return ResponseConfirmRemove(access, steamID, args[1]);

						case "CLEARFREEPACKAGESQUEUE":
							return ResponseClearQueue(access, steamID, args[1]);

						case "DONTREMOVE" when args.Length > 2:
							return ResponseDontRemove(access, steamID, args[1], Utilities.GetArgsAsText(args, 2, ","));
						case "DONTREMOVE":
							return ResponseDontRemove(bot, access, args[1]);

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

						case "REMOVEFREEPACKAGES":
							return await ResponseRemoveFreePackages(access, steamID, args[1], new StatusReporter(bot, steamID)).ConfigureAwait(false);

						case "REMOVEFREEPACKAGES^":
							return await ResponseRemoveFreePackages(access, steamID, args[1], new StatusReporter(bot, steamID), excludePlayed: true).ConfigureAwait(false);

						default:
							return null;
					}
			}
		}

		private static string? ResponseCancelRemove(Bot bot, EAccess access) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
			}

			return FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].CancelRemoval());
		}

		private static string? ResponseCancelRemove(EAccess access, ulong steamID, string botNames) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseCancelRemove(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID)));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
		}

		private static string? ResponseConfirmRemove(Bot bot, EAccess access) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
			}

			return FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].ConfirmRemoval());
		}

		private static string? ResponseConfirmRemove(EAccess access, ulong steamID, string botNames) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseConfirmRemove(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID)));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			return responses.Count > 0 ? String.Join(Environment.NewLine, responses) : null;
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

		private static string? ResponseDontRemove(Bot bot, EAccess access, string licenses) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
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
					packageType = EPackageType.RemoveApp;
				} else {
					packageType = EPackageType.RemoveSub;
				}

				response.AppendLine(FormatBotResponse(bot, PackageHandler.Handlers[bot.BotName].ModifyRemovables(packageType, gameID)));
			}

			return response.Length > 0 ? response.ToString() : null;
		}

		private static string? ResponseDontRemove(EAccess access, ulong steamID, string botNames, string licenses) {
			if (String.IsNullOrEmpty(botNames)) {
				throw new ArgumentNullException(nameof(botNames));
			}

			HashSet<Bot>? bots = Bot.GetBots(botNames);

			if ((bots == null) || (bots.Count == 0)) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}

			IEnumerable<string?> results = bots.Select(bot => ResponseDontRemove(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), licenses));

			List<string?> responses = new(results.Where(result => !String.IsNullOrEmpty(result)));

			Utilities.InBackground(async() => await PackageHandler.HandleChanges().ConfigureAwait(false));

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
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
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

		private static async Task<string?> ResponseRemoveFreePackages(Bot bot, EAccess access, StatusReporter statusReporter, bool excludePlayed = false) {
			if (access < EAccess.Master) {
				return null;
			}

			if (!bot.IsConnectedAndLoggedOn) {
				return FormatBotResponse(bot, ArchiSteamFarm.Localization.Strings.BotNotConnected);
			}

			if (!PackageHandler.Handlers.Keys.Contains(bot.BotName)) {
				return FormatBotResponse(bot, Strings.PluginNotEnabled);
			}

			IDocument? accountLicensesPage = await WebRequest.GetAccountLicenses(bot);
			if (accountLicensesPage == null) {
				return FormatBotResponse(bot, Strings.LicensePageFetchFail);
			}

			Regex removablePackageIDsRegex = new Regex("RemoveFreeLicense\\(\\s*(?<subID>[0-9]+),\\s*'(?<encodedName>[A-Za-z0-9+/=]*)'", RegexOptions.CultureInvariant); // matches the parameters of: RemoveFreeLicense( 45946, 'UmV2ZXJzaW9uOiBUaGUgRXNjYXBl' );
			MatchCollection removablePackageMatches = removablePackageIDsRegex.Matches(accountLicensesPage.Source.Text);
			if (removablePackageMatches.Count == 0) {
				return FormatBotResponse(bot, Strings.LicensePageEmpty);
			}

			Dictionary<uint, string> removeablePackages = new();
			foreach (Match match in removablePackageMatches) {
				string name;
				try {
					name = Encoding.UTF8.GetString(Convert.FromBase64String(match.Groups["encodedName"].Value));
				} catch (Exception e) {
					bot.ArchiLogger.LogGenericException(e);

					return FormatBotResponse(bot, String.Format(ArchiSteamFarm.Localization.Strings.ErrorParsingObject, "encodedName"));
				}

				string subIDString = match.Groups["subID"].Value;
				if (!uint.TryParse(subIDString, out uint subID)) {
					return FormatBotResponse(bot, String.Format(ArchiSteamFarm.Localization.Strings.ErrorParsingObject, "subID"));
				}

				removeablePackages[subID] = name;				
			}

			Utilities.InBackground(
				async() => {
					await PackageHandler.Handlers[bot.BotName].ScanRemovables(removeablePackages, excludePlayed, statusReporter).ConfigureAwait(false);
				}
			);

			int removableScanTimeEstimateMinutes = (int) Math.Round(2.5 * ((double) removeablePackages.Count / ProductInfo.ItemsPerProductInfoRequest) * ((double) ProductInfo.ProductInfoLimitingDelaySeconds / 60));

			return FormatBotResponse(bot, String.Format(Strings.RemovalWaitMessage, removableScanTimeEstimateMinutes, String.Format("!cancelremove {0}", bot.BotName)));
		}

		private static async Task<string?> ResponseRemoveFreePackages(EAccess access, ulong steamID, string botName, StatusReporter statusReporter, bool excludePlayed = false) {
			if (String.IsNullOrEmpty(botName)) {
				throw new ArgumentNullException(nameof(botName));
			}

			Bot? bot = Bot.GetBot(botName);

			if (bot == null) {
				return access >= EAccess.Owner ? FormatStaticResponse(String.Format(ArchiSteamFarm.Localization.Strings.BotNotFound, botName)) : null;
			}

			return await ResponseRemoveFreePackages(bot, ArchiSteamFarm.Steam.Interaction.Commands.GetProxyAccess(bot, access, steamID), statusReporter, excludePlayed).ConfigureAwait(false);
		}

		internal static string FormatStaticResponse(string response) => ArchiSteamFarm.Steam.Interaction.Commands.FormatStaticResponse(response);
		internal static string FormatBotResponse(Bot bot, string response) => bot.Commands.FormatBotResponse(response);
	}
}