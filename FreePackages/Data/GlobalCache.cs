using System;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace FreePackages {
	internal sealed class GlobalCache : SerializableFile {
		private static string SharedFilePath => Path.Combine(ArchiSteamFarm.SharedInfo.ConfigDirectory, $"{nameof(FreePackages)}.cache");

		[JsonProperty(Required = Required.DisallowNull)]
		internal uint LastChangeNumber;
		
		public bool ShouldSerializeLastChangeNumber() => LastChangeNumber > 0;

		internal GlobalCache() {
			FilePath = SharedFilePath;
		}

		internal static async Task<GlobalCache?> CreateOrLoad() {
			if (!File.Exists(SharedFilePath)) {
				return new GlobalCache();
			}

			GlobalCache? globalCache;
			try {
				string json = await File.ReadAllTextAsync(SharedFilePath).ConfigureAwait(false);

				if (string.IsNullOrEmpty(json)) {
					ASF.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsEmpty, nameof(json)));

					return null;
				}

				globalCache = JsonConvert.DeserializeObject<GlobalCache>(json);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return null;
			}

			if (globalCache == null) {
				ASF.ArchiLogger.LogNullError(globalCache);

				return null;
			}
			
			return globalCache;
		}

		internal void UpdateChangeNumber(uint currentChangeNumber) {
			LastChangeNumber = currentChangeNumber;

			Utilities.InBackground(Save);
		}
	}
}