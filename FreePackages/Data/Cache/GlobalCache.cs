using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;

namespace FreePackages {
	internal sealed class GlobalCache : SerializableFile {
		private static string SharedFilePath => Path.Combine(ArchiSteamFarm.SharedInfo.ConfigDirectory, $"{nameof(FreePackages)}.cache");

		[JsonInclude]
		internal uint LastChangeNumber { get; private set; }

		[JsonInclude]
		internal uint LastASFInfoItemCount { get; private set; }
		
		public bool ShouldSerializeLastChangeNumber() => LastChangeNumber > 0;
		public bool ShouldSerializeLastASFInfoItemCount() => LastASFInfoItemCount > 0;

		[JsonConstructor]
		internal GlobalCache() {
			FilePath = SharedFilePath;
		}

		protected override Task Save() => Save(this);

		internal static async Task<GlobalCache> CreateOrLoad() {
			if (!File.Exists(SharedFilePath)) {
				return new GlobalCache();
			}

			GlobalCache? globalCache;
			try {
				string json = await File.ReadAllTextAsync(SharedFilePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(ArchiSteamFarm.Localization.Strings.ErrorIsEmpty, nameof(json)));

					return new GlobalCache();
				}

				globalCache = json.ToJsonObject<GlobalCache>();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return new GlobalCache();
			}

			if (globalCache == null) {
				ASF.ArchiLogger.LogNullError(globalCache);

				return new GlobalCache();
			}
			
			return globalCache;
		}

		internal void UpdateChangeNumber(uint currentChangeNumber) {
			LastChangeNumber = currentChangeNumber;

			Utilities.InBackground(Save);
		}
		
		internal void UpdateASFInfoItemCount(uint currentASFInfoItemCount) {
			LastASFInfoItemCount = currentASFInfoItemCount;

			Utilities.InBackground(Save);
		}
	}
}