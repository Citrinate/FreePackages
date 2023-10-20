
using Newtonsoft.Json;

namespace FreePackages {
	internal sealed class PlaytestAccessResponse {
		[JsonProperty(PropertyName = "granted", Required = Required.AllowNull)]
		internal bool? Granted = null;

		[JsonProperty(PropertyName = "success", Required = Required.Always)]
		internal bool Success = false;

		[JsonConstructor]
		internal PlaytestAccessResponse() {}
	}
}