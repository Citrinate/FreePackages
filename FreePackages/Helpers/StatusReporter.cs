using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;

// For when long-running commands are issued through Steam chat, this is used to send status reports from the bot the command was sent to, to the user who issued the command
// If the commands weren't issued through Steam chat, this just logs the status reports

namespace FreePackages {
	internal sealed class StatusReporter {
		[JsonInclude]
		[JsonRequired]
		private ulong SenderSteamID; // When we send status reports, they'll come from this SteamID

		[JsonInclude]
		[JsonRequired]
		private ulong RecipientSteamID; // When we send status reports, they'll go to this SteamID

		private ConcurrentDictionary<Bot, List<string>> Reports = new();
		private ConcurrentDictionary<Bot, List<string>> PreviousReports = new();
		private uint ReportDelaySeconds;
		private uint ReportMaxDelaySeconds;
		private const uint DefaultReportDelaySeconds = 5;

		private Timer? ReportTimer;
		private DateTime? ReportMaxDelayTime = null;
		private SemaphoreSlim ReportSemaphore = new SemaphoreSlim(1, 1);

		internal StatusReporter(Bot? sender = null, ulong recipientSteamID = 0, uint reportDelaySeconds = DefaultReportDelaySeconds, uint? reportMaxDelaySeconds = null) {
			SenderSteamID = sender?.SteamID ?? 0;
			RecipientSteamID = recipientSteamID;
			ReportDelaySeconds = reportDelaySeconds;
			ReportMaxDelaySeconds = reportMaxDelaySeconds ?? reportDelaySeconds * 5;
		}

		[JsonConstructor]
		internal StatusReporter(ulong senderSteamID = 0, ulong recipientSteamID = 0) {
			SenderSteamID = senderSteamID;
			RecipientSteamID = recipientSteamID;
		}

		internal static StatusReporter StatusLogger() {
			// Create a status reporter that doesn't send messages through chat, it just logs everything
			return new StatusReporter(0, 0);
		}

		internal void Report(Bot reportingBot, string report, bool suppressDuplicateMessages = false, bool log = false) {
			if (log || SenderSteamID == 0 || RecipientSteamID == 0) {
				reportingBot.ArchiLogger.LogGenericInfo(report);
					
				return;
			}

			ReportSemaphore.Wait();
			try {
				if (suppressDuplicateMessages) {
					bool existsInReports = Reports.TryGetValue(reportingBot, out var reports) && reports.Contains(report);
					bool existsInPreviousReports = PreviousReports.TryGetValue(reportingBot, out var previousReports) && previousReports.Contains(report);

					if (existsInReports || existsInPreviousReports) {
						return;
					}
				}

				Reports.TryAdd(reportingBot, new List<string>());
				Reports[reportingBot].Add(report);

				// I prefer to send all reports in as few messages as possible
				// As long as reports continue to come in, we wait (until some limit, to avoid possibly waiting forever)

				double delayCorrectionSeconds = 0;
				if (ReportMaxDelayTime != null) {
					if (ReportMaxDelayTime <= DateTime.Now) {
						return;
					}

					delayCorrectionSeconds = Math.Max(0, (DateTime.Now.AddSeconds(ReportDelaySeconds) - ReportMaxDelayTime.Value).TotalSeconds);
				}

				if (ReportTimer != null) {
					ReportTimer.Change(Timeout.Infinite, Timeout.Infinite);
					ReportTimer.Dispose();
				}

				ReportTimer = new Timer(async _ => await Send().ConfigureAwait(false), null, TimeSpan.FromSeconds(ReportDelaySeconds - delayCorrectionSeconds), Timeout.InfiniteTimeSpan);

				if (ReportMaxDelayTime == null) {
					ReportMaxDelayTime = DateTime.Now.AddSeconds(ReportMaxDelaySeconds);
				}
			} finally {
				ReportSemaphore.Release();
			}
		}

		internal void ForceSend() {
			Utilities.InBackground(async() => await Send().ConfigureAwait(false));
		}

		private async Task Send() {
			await ReportSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				ReportTimer?.Dispose();
				ReportMaxDelayTime = null;

				List<string> messages = new List<string>();
				List<Bot> bots = Reports.Keys.OrderBy(bot => bot.BotName).ToList();

				foreach (Bot bot in bots) {
					messages.Add(Commands.FormatBotResponse(bot, String.Join(Environment.NewLine, Reports[bot])));
					if (Reports[bot].Count > 1) {
						// Add an extra line if there's more than 1 message from a bot
						messages.Add("");
					}

					if (Reports.TryRemove(bot, out List<string>? previousReports)) {
						if (previousReports != null) {
							PreviousReports[bot] = previousReports;
						}
					}
				}

				if (messages.Count == 0) {
					return;
				}

				Bot? sender = SenderSteamID == 0 ? null : Bot.BotsReadOnly?.Values.FirstOrDefault(bot => bot.SteamID == SenderSteamID);
				if (sender == null 
					|| RecipientSteamID == 0 
					|| !new SteamID(RecipientSteamID).IsIndividualAccount 
					|| sender.SteamFriends.GetFriendRelationship(RecipientSteamID) != EFriendRelationship.Friend
				) {
					// Can't send a chat message through Steam, just log the report
					ASF.ArchiLogger.LogGenericInfo(String.Join(Environment.NewLine, messages));

					return;
				}
				
				try {
					if (!await sender.SendMessage(RecipientSteamID, String.Join(Environment.NewLine, messages)).ConfigureAwait(false)) {
						ASF.ArchiLogger.LogGenericInfo(String.Join(Environment.NewLine, messages));
					}
				} catch (Exception) {
					ASF.ArchiLogger.LogGenericInfo(String.Join(Environment.NewLine, messages));
				}
			} finally {
				ReportSemaphore.Release();
			}
		}
	}
}
