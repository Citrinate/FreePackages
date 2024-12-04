using System.Text.Json.Serialization;
using SteamKit2;

namespace FreePackages.IPC {
	public sealed class FreeSubResponse {
		[JsonInclude]
		[JsonPropertyName("Result")]
		public EResult Result { get; private init; }

		[JsonInclude]
		[JsonPropertyName("PurchaseResultDetail")]
		public EPurchaseResultDetail PurchaseResultDetail { get; private init; }

		public FreeSubResponse(EResult result, EPurchaseResultDetail purchaseResultDetail) {
			Result = result;
			PurchaseResultDetail = purchaseResultDetail;
		}
	}
}