using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FreePackages {
	internal sealed class AppDetails {
		[JsonInclude]
		[JsonPropertyName("success")]
		[JsonRequired]
		internal bool Success { get; private init; } = false;

		[JsonInclude]
		[JsonPropertyName("data")]
		internal AppDetailsData? Data { get; private init; } = null;

		[JsonConstructor]
		internal AppDetails() {}
	}

	internal sealed class AppDetailsData {
		[JsonInclude]
		[JsonPropertyName("is_free")]
		internal bool IsFree { get; private init; } = false;

		[JsonInclude]
		[JsonPropertyName("packages")]
		internal HashSet<uint> Packages { get; private init; } = new();

		[JsonInclude]
		[JsonPropertyName("release_date")]
		internal AppDetailsReleaseDate? ReleaseDate { get; private init; } = null;

		[JsonInclude]
		[JsonExtensionData]
		internal Dictionary<string, JsonElement> AdditionalData { get; private init; } = new();

		[JsonConstructor]
		internal AppDetailsData() {}
	}

	internal sealed class AppDetailsReleaseDate {
		[JsonInclude]
		[JsonPropertyName("coming_soon")]
		internal bool ComingSoon { get; private init; } = true;

		[JsonInclude]
		[JsonExtensionData]
		internal Dictionary<string, JsonElement> AdditionalData { get; private init; } = new();

		[JsonConstructor]
		internal AppDetailsReleaseDate() {}
	}
}