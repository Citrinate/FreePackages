using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FreePackages {
	internal sealed class AppDetails {
		[JsonProperty(PropertyName = "success", Required = Required.Always)]
		internal bool Success = false;

		[JsonProperty(PropertyName = "data", Required = Required.Default)]
		internal AppDetailsData? Data = null;

		[JsonConstructor]
		internal AppDetails() {}
	}

	internal sealed class AppDetailsData {
		[JsonProperty(PropertyName = "is_free", Required = Required.Default)]
		internal bool IsFree = false;

		[JsonProperty(PropertyName = "packages", Required = Required.Default)]
		internal HashSet<uint> Packages = new();

		[JsonProperty(PropertyName = "release_date", Required = Required.Default)]
		internal AppDetailsReleaseDate? ReleaseDate = null;

		[JsonExtensionData]
		internal Dictionary<string, JToken> AdditionalData = new();

		[JsonConstructor]
		internal AppDetailsData() {}
	}

	internal sealed class AppDetailsReleaseDate {
		[JsonProperty(PropertyName = "coming_soon", Required = Required.Default)]
		internal bool ComingSoon = true;

		[JsonExtensionData]
		internal Dictionary<string, JToken> AdditionalData = new();

		[JsonConstructor]
		internal AppDetailsReleaseDate() {}
	}
}