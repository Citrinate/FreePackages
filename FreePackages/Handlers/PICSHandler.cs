using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal static class PICSHandler {
		private static SemaphoreSlim PICSChangesSemaphore = new SemaphoreSlim(1, 1);
		private const int PICSChangesLimitingDelaySeconds = 10;
		
		private static TimeSpan BigPICSStart = new TimeSpan(9, 59, 0); // From 9:59 AM PST
		private static TimeSpan BigPICSDuration = TimeSpan.FromMinutes(6); // To 10:05 AM PST
		private static DateTime? BigPICSStartTime = null;
		private static Timer BigPICSTimer = new Timer(async e => await BigPICSLookout().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);

		internal static void OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (FreePackages.GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			if (currentChangeNumber <= FreePackages.GlobalCache.LastChangeNumber) {
				return;
			}

			PackageHandler.AddChanges(appChanges, packageChanges);
			FreePackages.GlobalCache.UpdateChangeNumber(currentChangeNumber);
			
			return;
		}

		internal async static Task OnPICSRestart(uint currentChangeNumber) {
			if (PackageHandler.Handlers.Count == 0) {
				return;	
			}

			if (FreePackages.GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			

			uint oldChangeNumber = FreePackages.GlobalCache.LastChangeNumber;
			if (currentChangeNumber <= oldChangeNumber) {
				return;
			}

			ASF.ArchiLogger.LogGenericDebug(String.Format("PICS restarted, skipping from change number {0} to {1}", oldChangeNumber, currentChangeNumber));

			// ASF restarts PICS if either apps or packages needs a full update.  Check the old change number, as one of them might still be good.
			SteamApps.PICSChangesCallback? picsChanges = await FetchPICSChanges(oldChangeNumber, sendAppChangeList: false, sendPackageChangeList: true).ConfigureAwait(false);
			if (picsChanges == null) {
				return;
			}

			if (!picsChanges.RequiresFullAppUpdate) {
				PackageHandler.AddChanges(picsChanges.AppChanges, new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>());
			} else {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free apps due to PICS restart");
				
				// Search for the oldest change number which is still valid for apps
				var appChanges = await FindOldestPICSChanges(oldChangeNumber + 1, picsChanges.CurrentChangeNumber, findApps: true);
				if (appChanges != null) {
					ASF.ArchiLogger.LogGenericDebug(String.Format("Recovered {0} app changes at change number {1}", appChanges.AppChanges.Count, appChanges.LastChangeNumber + 1));

					PackageHandler.AddChanges(appChanges.AppChanges, new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>());
				}
			}

			if (!picsChanges.RequiresFullPackageUpdate) {
				PackageHandler.AddChanges(new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>(), picsChanges.PackageChanges);
			} else {
				ASF.ArchiLogger.LogGenericDebug("Possibly missed some free packages due to PICS restart");

				// Search for the oldest change number which is still valid for packages
				var packageChanges = await FindOldestPICSChanges(oldChangeNumber + 1, picsChanges.CurrentChangeNumber, findApps: false);
				if (packageChanges != null) {
					ASF.ArchiLogger.LogGenericDebug(String.Format("Recovered {0} package changes at change number {1}", packageChanges.PackageChanges.Count, packageChanges.LastChangeNumber + 1));

					PackageHandler.AddChanges(new Dictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData>(), packageChanges.PackageChanges);
				}
			}

			FreePackages.GlobalCache.UpdateChangeNumber(currentChangeNumber);
		}

		private async static Task<SteamApps.PICSChangesCallback?> FindOldestPICSChanges(uint minValidChangeNumber, uint maxValidChangeNumber, bool findApps) {
			if (minValidChangeNumber >= maxValidChangeNumber) {
				return null;
			}

			bool sendAppChangeList = findApps;
			bool sendPackageChangeList = !findApps;
			uint changeNumber = maxValidChangeNumber - ((uint) Math.Floor((maxValidChangeNumber - minValidChangeNumber) / 2.0));
			SteamApps.PICSChangesCallback? oldestPicsChanges = null;
			
			do {
				SteamApps.PICSChangesCallback? picsChanges = await FetchPICSChanges(changeNumber, sendAppChangeList, sendPackageChangeList).ConfigureAwait(false);
				if (picsChanges == null) {
					break;
				}

				bool isValid = (findApps && !picsChanges.RequiresFullAppUpdate) || (!findApps && !picsChanges.RequiresFullPackageUpdate);
				if (isValid) {
					oldestPicsChanges = picsChanges;
					maxValidChangeNumber = changeNumber;
				} else {
					minValidChangeNumber = changeNumber;
				}

				changeNumber = maxValidChangeNumber - Math.Max(1, ((uint) Math.Floor((maxValidChangeNumber - minValidChangeNumber) / 2.0)));
			} while (changeNumber > minValidChangeNumber);

			return oldestPicsChanges;
		}

		private async static Task<SteamApps.PICSChangesCallback?> FetchPICSChanges(uint lastChangeNumber, bool sendAppChangeList = true, bool sendPackageChangeList = true, bool returnChanges = true) {
			await PICSChangesSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return null;
				}

				if (!returnChanges) {
					await refreshBot.SteamApps.PICSGetChangesSince(lastChangeNumber, sendAppChangeList, sendPackageChangeList);

					return null;
				}

				return await refreshBot.SteamApps.PICSGetChangesSince(lastChangeNumber, sendAppChangeList, sendPackageChangeList).ToLongRunningTask().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarningException(e);

				return null;
			} finally {
				Utilities.InBackground(
					async() => {
						await Task.Delay(TimeSpan.FromSeconds(PICSChangesLimitingDelaySeconds)).ConfigureAwait(false);
						PICSChangesSemaphore.Release();
					}
				);
			}
		}

		internal static void StartBigPICSLookout() {
			// From 9:59 AM PST to 10:05 AM PST, check for PICS changes as often as PICSChangesLimitingDelaySeconds allows

			// ASF will check for PICS changes every 15 minutes which is usually fine
			// The only time this is an issue is when there's a PICS change which contains thousands of items
			// PICS will reset soon after these large changes, and so we have very little time to see them.
			// Some examples:
			// https://steamdb.info/changelist/21634577/   Dec 21 2023 10:02:17 AM PST, previous change time: 9:59:59 AM PST
			// https://steamdb.info/changelist/20333525/   Sep 18 2023 10:00:21 AM PST, previous change time: 10:00:00 AM PST
			// https://steamdb.info/changelist/20445399/   Sep 26 2023 3:49:33 PM PST, previous change time: 3:13:04 PM PST (we'll miss stuff like this, but I'm assuming these are extra rare)

			string valveTimeZone = "Pacific Standard Time";
			DateTime valveTime = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.FindSystemTimeZoneById(valveTimeZone));
			DateTime todayStartTime = valveTime.Date.Add(BigPICSStart);
			DateTime tomorrowStartTime = todayStartTime.AddDays(1);

			if (valveTime > todayStartTime.Add(BigPICSDuration)) {
				BigPICSStartTime = TimeZoneInfo.ConvertTime(tomorrowStartTime, TimeZoneInfo.FindSystemTimeZoneById(valveTimeZone), TimeZoneInfo.Local);
			} else {
				BigPICSStartTime = TimeZoneInfo.ConvertTime(todayStartTime, TimeZoneInfo.FindSystemTimeZoneById(valveTimeZone), TimeZoneInfo.Local);
			}

			UpdateTimer(BigPICSStartTime.Value);
		}

		private static async Task BigPICSLookout() {
			if (FreePackages.GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			if (BigPICSStartTime == null) {
				throw new InvalidOperationException(nameof(BigPICSStartTime));
			}

			SteamApps.PICSChangesCallback? picsChanges = await FetchPICSChanges(FreePackages.GlobalCache.LastChangeNumber, returnChanges: false).ConfigureAwait(false);
			// if (picsChanges != null) {
			// 	if (picsChanges.CurrentChangeNumber == FreePackages.GlobalCache.LastChangeNumber) {
			// 		ASF.ArchiLogger.LogGenericDebug(String.Format("PICS No changes"));
			// 	} else {
			// 		ASF.ArchiLogger.LogGenericDebug(String.Format("PICS Change # {0} since # {1}, Apps: {2}, Packages: {3}", picsChanges.CurrentChangeNumber, FreePackages.GlobalCache.LastChangeNumber, picsChanges.AppChanges.Count, picsChanges.PackageChanges.Count));
			// 	}
			// 	if (picsChanges.RequiresFullAppUpdate || picsChanges.RequiresFullPackageUpdate || picsChanges.RequiresFullUpdate) {
			// 		ASF.ArchiLogger.LogGenericDebug(String.Format("PICS RESET on Change # {0}, APP: {1} PACKAGE: {2} FULL: {3}", picsChanges.CurrentChangeNumber, picsChanges.RequiresFullAppUpdate, picsChanges.RequiresFullPackageUpdate, picsChanges.RequiresFullUpdate));
			// 	}
			// 	OnPICSChanges(picsChanges.CurrentChangeNumber, picsChanges.AppChanges, picsChanges.PackageChanges);
			// }

			if (DateTime.Now > BigPICSStartTime.Value.Add(BigPICSDuration)) {
				// Finished for today
				StartBigPICSLookout();
			} else {
				UpdateTimer(DateTime.Now);
			}
		}

		private static Bot? GetRefreshBot() => Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
		private static int GetMillisecondsFromNow(DateTime then) => Math.Max(0, (int) (then - DateTime.Now).TotalMilliseconds);
		private static void UpdateTimer(DateTime then) {
			ASF.ArchiLogger.LogGenericDebug(String.Format("Will look for PICS on {0:d} at {0:T}", then));			
			BigPICSTimer.Change(GetMillisecondsFromNow(then), Timeout.Infinite);
		}
	}
}