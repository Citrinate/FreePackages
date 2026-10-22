using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using SteamKit2;

namespace FreePackages {
	internal sealed class BotCache : SerializableFile {
		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<Package> Packages { get; private set; } = new(new PackageComparer());

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<DateTime> Activations { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> ChangedApps { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> ChangedPackages { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> NewOwnedPackages { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> SeenPackages { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> WaitlistedPlaytests { get; private set; } = new();

		// BASE appIDs of playtests we've already POSTed /ajaxrequestplaytestaccess for in
		// the current catalog-membership epoch. Suppresses re-requests while the playtest
		// stays live; pruned (along with WaitlistedPlaytests) when a playtest leaves the
		// catalog so a re-opening is caught. See PlaytestCatalog.
		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> RequestedPlaytests { get; private set; } = new();

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> IgnoredApps { get; private set; } = new();

		[JsonInclude]
		internal uint LastASFInfoItemCount { get; private set; }

		private HashSet<uint> SeenPackageIDActivations = new();
		private readonly object LockObject = new();

		// Debounced save: a burst of mutations collapses into a single deferred write.
		// SerializableFile already coalesces concurrent saves (SavingScheduled flag), but
		// replacing Utilities.InBackground(Save) — which allocates a threadpool task per
		// mutation — with a one-shot timer re-arm avoids tens of thousands of task
		// allocations under a large PICS changelist. ASF exposes no shutdown hook for
		// IBotModules, so a final flush on process exit is not possible from the plugin;
		// worst case is losing mutations made in the last SaveDebounceDelay before an
		// abrupt exit.
		private Timer? SaveTimer;
		private static readonly TimeSpan SaveDebounceDelay = TimeSpan.FromSeconds(5);

		[JsonConstructor]
		internal BotCache() {
			SaveTimer = new Timer(
				async _ => {
					try {
						await Save().ConfigureAwait(false);
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);
					}
				},
				null,
				Timeout.Infinite,
				Timeout.Infinite
			);
		}

		internal BotCache(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		protected override Task Save() => Save(this);

		private void ScheduleSave() => SaveTimer?.Change(SaveDebounceDelay, Timeout.InfiniteTimeSpan);

		protected override void Dispose(bool disposing) {
			if (disposing) {
				SaveTimer?.Dispose();
				SaveTimer = null;
			}

			base.Dispose(disposing);
		}

		internal static async Task<BotCache?> CreateOrLoad(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (!File.Exists(filePath)) {
				return new BotCache(filePath);
			}

			BotCache? botCache;
			try {
				string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(ArchiSteamFarm.Localization.Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botCache = json.ToJsonObject<BotCache>();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			if (botCache == null) {
				ASF.ArchiLogger.LogNullError(botCache);

				return null;
			}

			botCache.Packages = new(botCache.Packages.GroupBy(package => package, new PackageComparer()).Select(group => group.First()), new PackageComparer());
			botCache.FilePath = filePath;
			
			return botCache;
		}

		internal bool AddPackage(Package package) {
			if (Packages.Contains(package)) {
				return false;
			}

			Packages.Add(package);
			ScheduleSave();

			return true;
		}

		internal bool AddPackages(IEnumerable<Package> packages) {
			if (!packages.Except(Packages).Any()) {
				// There are no new packages to add
				return false;
			}

			Packages.UnionWith(packages);
			ScheduleSave();

			return true;
		}

		internal bool RemovePackage(Package package) {
			Packages.Remove(package);
			ScheduleSave();

			return true;
		}

		internal bool RemoveAppPackages(HashSet<uint> appIDsToRemove) {
			Packages.Where(x => x.Type == EPackageType.App && appIDsToRemove.Contains(x.ID)).ToList().ForEach(x => Packages.Remove(x));
			ScheduleSave();

			return true;
		}

		internal Package? GetNextPackage(HashSet<EPackageType> types) {
			// Return the package which should be activated first, prioritizing first packages which have a start and end date
			ulong now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);
			Package? package = Packages.FirstOrDefault(x => x.StartTime != null && now > x.StartTime && types.Contains(x.Type));
			if (package != null) {
				return package;
			}

			return Packages.FirstOrDefault(x => x.StartTime == null && types.Contains(x.Type));
		}

		internal void AddActivation(DateTime activation, uint count = 1, IReadOnlyCollection<uint>? packageIDs = null) {
			var activationsToPrune = Activations.Where(x => x < DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)).ToList();
			if (activationsToPrune.Count > 0) {
				activationsToPrune.ForEach(x => Activations.Remove(x));
			}

			lock(LockObject) {
				int numUnseenPackageActivations = packageIDs?.Where(packageID => !SeenPackageIDActivations.Contains(packageID)).Count() ?? 0;
				if (packageIDs == null || numUnseenPackageActivations > 0) {
					if (packageIDs != null) {
						SeenPackageIDActivations.UnionWith(packageIDs);
					}

					for (int i = 0; i < Math.Max(count, numUnseenPackageActivations); i++) {
						Activations.Add(activation.AddSeconds(-1 * i));
					}
				}
			}

			ScheduleSave();
		}

		internal int NumActivationsPastPeriod() {
			return Activations.Where(activation => activation > DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)).Count();
		}

		internal DateTime? GetLastActivation() {
			// Can't use Activations.Max() because it's missing on non-generic ASF
			DateTime? lastActivation = null;
			foreach (DateTime activation in Activations) {
				if (lastActivation == null || activation > lastActivation) {
					lastActivation = activation;
				}
			}

			return lastActivation;
		}

		internal void AddChanges(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null, HashSet<uint>? newOwnedPackageIDs = null, bool ignoreFailedApps = false) {
			if (appIDs != null) {
				ChangedApps.UnionWith(appIDs);

				if (ignoreFailedApps) {
					ChangedApps.ExceptWith(IgnoredApps);
				}
			}

			if (packageIDs != null) {
				ChangedPackages.UnionWith(packageIDs);
			}

			if (newOwnedPackageIDs != null) {
				NewOwnedPackages.UnionWith(newOwnedPackageIDs);
			}

			ScheduleSave();
		}

		internal void RemoveChange(uint? appID = null, uint? packageID = null, uint? newOwnedPackageID = null) {
			if (appID != null) {
				ChangedApps.Remove(appID.Value);
			}

			if (packageID != null) {
				ChangedPackages.Remove(packageID.Value);
			}

			if (newOwnedPackageID != null) {
				NewOwnedPackages.Remove(newOwnedPackageID.Value);
			}
		}

		internal void SaveChanges() {
			ScheduleSave();
		}

		internal void ClearQueue() {
			Packages.RemoveWhere(package => ActivationQueue.ActivationTypes.Contains(package.Type));
			ChangedApps.Clear();
			ChangedPackages.Clear();
			ScheduleSave();
		}

		internal void CancelRemoval() {
			Packages.RemoveWhere(package => RemovalQueue.RemovalTypes.Contains(package.Type));
			ScheduleSave();
		}

		internal void AddWaitlistedPlaytest(uint appID) {
			WaitlistedPlaytests.Add(appID);

			ScheduleSave();
		}

		internal void AddRequestedPlaytest(uint appID) {
			RequestedPlaytests.Add(appID);

			ScheduleSave();
		}

		// Drop playtests that are no longer in the live catalog. Called only on a
		// successful, complete catalog fetch (see PlaytestCatalog.DoUpdate) — never on a
		// failed or partial one, so a network blip or markup change can't wipe the
		// suppression sets and re-request the whole catalog. A playtest that re-opens
		// later re-enters the catalog, gets pruned out of these sets on the cycle where it
		// was absent, and is requested again on the cycle where it returns.
		internal void PrunePlaytests(HashSet<uint> liveSet) {
			ArgumentNullException.ThrowIfNull(liveSet);

			if (RequestedPlaytests.Count > 0) {
				RequestedPlaytests.RemoveWhere(id => !liveSet.Contains(id));
			}

			if (WaitlistedPlaytests.Count > 0) {
				WaitlistedPlaytests.RemoveWhere(id => !liveSet.Contains(id));
			}

			ScheduleSave();
		}

		// Remove playtest packages from the activation queue that are no longer in the live
		// catalog. Without this, a playtest that was enqueued (pre-gate, or via the PICS path)
		// and then closed would sit in the persisted queue and produce a 401 Invalid when it
		// is finally claimed — one per stale entry, draining slowly under the activation
		// rate limit. Pruning them here — only on a successful complete fetch, like
		// PrunePlaytests — makes that cleanup instant and silent. Live playtests stay in the
		// queue and are claimed normally (OK/Waitlisted). A playtest that re-opens later
		// re-enters the catalog, is pruned out of RequestedPlaytests on the cycle it was
		// absent, and is re-enqueued by OnPlaytestCatalogUpdated on the cycle it returns.
		internal void PrunePlaytestPackages(HashSet<uint> liveSet) {
			ArgumentNullException.ThrowIfNull(liveSet);

			List<Package> stale = Packages.Where(x => x.Type == EPackageType.Playtest && !liveSet.Contains(x.ID)).ToList();
			if (stale.Count == 0) {
				return;
			}

			foreach (Package package in stale) {
				Packages.Remove(package);
			}

			ScheduleSave();
		}

		internal void UpdateSeenPackages(List<SteamApps.LicenseListCallback.License> newLicenses) {
			SeenPackages.UnionWith(newLicenses.Select(license => license.PackageID));

			// Keep track of how many free licenses we activated to enforce the free packages limit
			// This is to catch packages that were activated, but didn't return a success status, or were activated outside of the plugin
			/* NOTE: The below code will not capture all recent activations.  If Steam removes a demo from your account, but you add it back,
			 then the package will re-appear with the original TimeCreated value. Activations like these are instead logged when steam reports a
			 successful activation.
			 */
			// Count clearly-free license types against the free package limit:
			//   - Complimentary (1024): gifted/free grant from Steam
			//   - AutoGrant (64): automatically granted (e.g. free-on-demand activations)
			// Deliberately excluded:
			//   - GuestPass (8): temporary, does not consume a permanent activation slot
			//   - ActivationCode (1): redeemed via key, tracked separately
			//   - Promotional (131): uncertain semantics; left out to avoid over-counting
			foreach(SteamApps.LicenseListCallback.License license in newLicenses) {
				if (CountsAsFreeActivation(license.PaymentMethod) &&
					license.TimeCreated.ToLocalTime() > DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)
				) {
					AddActivation(license.TimeCreated.ToLocalTime(), packageIDs: [ license.PackageID ]);
				}
			}

			ScheduleSave();
		}

		internal void IgnoreApp(uint appID) {
			IgnoredApps.Add(appID);

			ScheduleSave();
		}

		internal void UpdateASFInfoItemCount(uint currentASFInfoItemCount) {
			LastASFInfoItemCount = currentASFInfoItemCount;

			ScheduleSave();
		}

		// Whether a license's PaymentMethod counts as a free activation against the
		// free-package limit. See UpdateSeenPackages for the rationale of what's included.
		private static bool CountsAsFreeActivation(EPaymentMethod paymentMethod) =>
			paymentMethod == EPaymentMethod.Complimentary || paymentMethod == EPaymentMethod.AutoGrant;
	}
}