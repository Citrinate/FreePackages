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
using ArchiSteamFarm.Localization;
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
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

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
			Packages.Where(x => appIDsToRemove.Contains(x.ID)).ToList().ForEach(x => Packages.Remove(x));
			Utilities.InBackground(Save);

			return true;
		}

		internal Package? GetNextPackage() {
			ulong now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);
			Package? package = Packages.FirstOrDefault(x => x.StartTime != null && now > x.StartTime);
			if (package != null) {
				return package;
			}

			return Packages.FirstOrDefault(x => x.StartTime == null);
		}

		internal void AddActivation(DateTime activation, uint count = 1) {
			var activationsToPrune = Activations.Where(x => x < DateTime.Now.AddHours(-1)).ToList();
			if (activationsToPrune.Count > 0) {
				activationsToPrune.ForEach(x => Activations.Remove(x));
			}

			for (int i = 0; i < count; i++) {
				Activations.Add(activation.AddSeconds(-1 * i));
			}

			Utilities.InBackground(Save);
		}

		internal int NumActivationsPastHour() {
			return Activations.Where(activation => activation > DateTime.Now.AddHours(-1)).Count();
		}

		internal DateTime? GetLastActivation() {
			// Can't use Activations.Max() because it breaks on non-generic ASF
			DateTime? lastActivation = null;
			foreach (DateTime activation in Activations) {
				if (lastActivation == null || activation > lastActivation) {
					lastActivation = activation;
				}
			}

			return lastActivation;
		}

		internal void AddChanges(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null, HashSet<uint>? newOwnedPackageIDs = null) {
			if (appIDs != null) {
				ChangedApps.UnionWith(appIDs);
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

		internal void UpdateSeenPackages(HashSet<uint> seenPackages) {
			SeenPackages.UnionWith(seenPackages);

			Utilities.InBackground(Save);
		}
	}
}