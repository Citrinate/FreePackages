using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
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

		[JsonInclude]
		[JsonDisallowNull]
		internal ConcurrentHashSet<uint> IgnoredApps { get; private set; } = new();

		[JsonInclude]
		internal uint LastASFInfoItemCount { get; private set; }

		private HashSet<uint> SeenPackageIDActivations = new();
		private readonly object LockObject = new();

		[JsonConstructor]
		internal BotCache() { }

		internal BotCache(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		protected override Task Save() => Save(this);

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

				if (string.IsNullOrWhiteSpace(json) || json.All(static c => c == '\0')) {
					ASF.ArchiLogger.LogGenericWarning($"FreePackages cache is empty or corrupt, recreating: {Path.GetFileName(filePath)}");

					return new BotCache(filePath);
				}

				botCache = json.ToJsonObject<BotCache>();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericWarning($"FreePackages cache is invalid and will be recreated: {Path.GetFileName(filePath)} ({e.GetType().Name})");

				return new BotCache(filePath);
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
			Package? existing = Packages.FirstOrDefault(x => x.Type == package.Type && x.ID == package.ID);
			if (existing != null) {
				bool changed = false;
				if (existing.IsDemo != package.IsDemo) {
					existing.IsDemo = package.IsDemo;
					changed = true;
				}

				if (existing.Priority != package.Priority) {
					existing.Priority = package.Priority;
					changed = true;
				}

				if (changed) {
					Utilities.InBackground(Save);
				}

				return false;
			}

			Packages.Add(package);
			Utilities.InBackground(Save);

			return true;
		}

		internal bool AddPackages(IEnumerable<Package> packages) {
			if (!packages.Except(Packages).Any()) {
				// There are no new packages to add
				return false;
			}

			Packages.UnionWith(packages);
			Utilities.InBackground(Save);

			return true;
		}

		internal bool RemovePackage(Package package) {
			Packages.Remove(package);
			Utilities.InBackground(Save);

			return true;
		}

		internal bool RemoveAppPackages(HashSet<uint> appIDsToRemove) {
			Packages.Where(x => x.Type == EPackageType.App && appIDsToRemove.Contains(x.ID)).ToList().ForEach(x => Packages.Remove(x));
			Utilities.InBackground(Save);

			return true;
		}

		internal Package? GetNextPackage(HashSet<EPackageType> types) {
			// Games, then content for owned games, then everything else, then demos.
			// Within each rank, time-limited packages still go first.
			ulong now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);

			for (byte rank = (byte) EActivationPriority.Game; rank <= (byte) EActivationPriority.Demo; rank++) {
				Package? timed = Packages.FirstOrDefault(x => types.Contains(x.Type) && ActivationPriority.Rank(x) == rank && x.StartTime != null && now > x.StartTime);
				if (timed != null) {
					return timed;
				}

				Package? next = Packages.FirstOrDefault(x => types.Contains(x.Type) && ActivationPriority.Rank(x) == rank && x.StartTime == null);
				if (next != null) {
					return next;
				}
			}

			return null;
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

					int toAdd = (int)Math.Min(Math.Max(count, (uint)numUnseenPackageActivations), 50u);
					for (int i = 0; i < toAdd; i++) {
						Activations.Add(activation.AddSeconds(-1 * i));
					}
				}
			}

			Utilities.InBackground(Save);
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

			Utilities.InBackground(Save);
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
			Utilities.InBackground(Save);
		}

		internal void ClearQueue() {
			Packages.RemoveWhere(package => ActivationQueue.ActivationTypes.Contains(package.Type));
			ChangedApps.Clear();
			ChangedPackages.Clear();
			Utilities.InBackground(Save);
		}

		internal void CancelRemoval() {
			Packages.RemoveWhere(package => RemovalQueue.RemovalTypes.Contains(package.Type));
			Utilities.InBackground(Save);
		}

		internal void AddWaitlistedPlaytest(uint appID) {
			WaitlistedPlaytests.Add(appID);
			
			Utilities.InBackground(Save);
		}

		internal void UpdateSeenPackages(List<SteamApps.LicenseListCallback.License> newLicenses) {
			SeenPackages.UnionWith(newLicenses.Select(license => license.PackageID));

			// Keep track of how many free licenses we activated to enforce the free packages limit
			// This is to catch packages that were activated, but didn't return a success status, or were activated outside of the plugin
			/* NOTE: The below code will not capture all recent activations.  If Steam removes a demo from your account, but you add it back, 
			 then the package will re-appear with the original TimeCreated value. Activations like these are instead logged when steam reports a
			 successful activation.
			 */
			// TODO: Do other PaymentMethod values also count against the free package limit?
			foreach(SteamApps.LicenseListCallback.License license in newLicenses) {
				if (license.PaymentMethod == EPaymentMethod.Complimentary &&
					license.TimeCreated.ToLocalTime() > DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)
				) {
					AddActivation(license.TimeCreated.ToLocalTime(), packageIDs: [ license.PackageID ]);
				}
			}

			Utilities.InBackground(Save);
		}

		internal void IgnoreApp(uint appID) {
			IgnoredApps.Add(appID);

			Utilities.InBackground(Save);
		}

		internal void UpdateASFInfoItemCount(uint currentASFInfoItemCount) {
			LastASFInfoItemCount = currentASFInfoItemCount;

			Utilities.InBackground(Save);
		}

		internal void ResetASFInfoItemCount() {
			LastASFInfoItemCount = 0;

			Utilities.InBackground(Save);
		}
	}
}