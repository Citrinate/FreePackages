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

		private async static Task<SteamApps.PICSChangesCallback?> FetchPICSChanges(uint changeNumber, bool sendAppChangeList = true, bool sendPackageChangeList = true) {
			await PICSChangesSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				Bot? refreshBot = GetRefreshBot();
				if (refreshBot == null) {
					return null;
				}

				return await refreshBot.SteamApps.PICSGetChangesSince(changeNumber, sendAppChangeList, sendPackageChangeList).ToLongRunningTask().ConfigureAwait(false);
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

		private static Bot? GetRefreshBot() => Bot.BotsReadOnly?.Values.FirstOrDefault(static bot => bot.IsConnectedAndLoggedOn);
	}
}