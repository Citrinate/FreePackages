using System.Text.Json.Serialization;

namespace FreePackages {
	internal sealed class PlaytestAccessResponse {
		[JsonInclude]
		[JsonPropertyName("granted")]
		[JsonRequired]
		internal bool? Granted  { get; private init; } = null;

		[JsonInclude]
		[JsonPropertyName("success")]
		[JsonRequired]
		internal bool Success  { get; private init; } = false;

		[JsonConstructor]
		internal PlaytestAccessResponse() {}
	}
}