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
			// Return the package which should be activated first, prioritizing first packages which have a start and end date
			ulong now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);
			Package? package = Packages.FirstOrDefault(x => x.StartTime != null && now > x.StartTime && types.Contains(x.Type));
			if (package != null) {
				return package;
			}

			return Packages.FirstOrDefault(x => x.StartTime == null);
		}

		internal void AddActivation(DateTime activation, uint count = 1) {
			var activationsToPrune = Activations.Where(x => x < DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)).ToList();
			if (activationsToPrune.Count > 0) {
				activationsToPrune.ForEach(x => Activations.Remove(x));
			}

			for (int i = 0; i < count; i++) {
				Activations.Add(activation.AddSeconds(-1 * i));
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

		internal void Clear() {
			Packages.Clear();
			ChangedApps.Clear();
			ChangedPackages.Clear();
			Utilities.InBackground(Save);
		}

		internal void AddWaitlistedPlaytest(uint appID) {
			WaitlistedPlaytests.Add(appID);
			
			Utilities.InBackground(Save);
		}

		internal void UpdateSeenPackages(List<SteamApps.LicenseListCallback.License> newLicenses) {
			SeenPackages.UnionWith(newLicenses.Select(license => license.PackageID));

			// Keep track of how many free licenses we activated to enforce the free packages limit
			// TODO: Do other PaymentMethod values also count against the free package limit?
			foreach(SteamApps.LicenseListCallback.License license in newLicenses) {
				if (license.PaymentMethod == EPaymentMethod.Complimentary &&
					license.TimeCreated.ToLocalTime() > DateTime.Now.AddMinutes(-1 * ActivationQueue.ActivationPeriodMinutes)
				) {
					AddActivation(license.TimeCreated.ToLocalTime());
				}
			}

			Utilities.InBackground(Save);
		}

		internal void IgnoreApp(uint appID) {
			IgnoredApps.Add(appID);

			Utilities.InBackground(Save);
		}
	}
}