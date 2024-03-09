using System.Text.Json.Serialization;

namespace FreePackages {
	internal sealed class PlaytestAccessResponse {
		[JsonInclude]
		[JsonPropertyName("granted")]
		[JsonRequired]
		internal int? Granted  { get; private init; } = null;

		[JsonInclude]
		[JsonPropertyName("success")]
		[JsonRequired]
		internal int Success  { get; private init; } = 0;

		[JsonConstructor]
		internal PlaytestAccessResponse() {}
	}
}