using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;
using SteamKit2;

namespace FreePackages {
	internal sealed class BotCache : SerializableFile {
		[JsonProperty(Required = Required.DisallowNull)]
		internal ConcurrentHashSet<Package> Packages { get; } = new(new PackageComparer());

		[JsonProperty(Required = Required.DisallowNull)]
		internal ConcurrentHashSet<DateTime> Activations = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal ConcurrentHashSet<uint> ChangedApps = new();

		[JsonProperty(Required = Required.DisallowNull)]
		internal ConcurrentHashSet<uint> ChangedPackages = new();

		[JsonConstructor]
		private BotCache() { }

		internal BotCache(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
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
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				botCache = JsonConvert.DeserializeObject<BotCache>(json);
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

		internal void AddChanges(HashSet<uint>? appIDs = null, HashSet<uint>? packageIDs = null) {
			if (appIDs != null) {
				ChangedApps.UnionWith(appIDs);
			}

			if (packageIDs != null) {
				ChangedPackages.UnionWith(packageIDs);
			}

			Utilities.InBackground(Save);
		}

		internal void RemoveChange(uint? appID = null, uint? packageID = null) {
			if (appID != null) {
				ChangedApps.Remove(appID.Value);
			}

			if (packageID != null) {
				ChangedPackages.Remove(packageID.Value);
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
	}
}