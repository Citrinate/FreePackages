using Newtonsoft.Json;
using SteamKit2;

namespace FreePackages {
	public sealed class FreeSubResponse {
		[JsonProperty(PropertyName = "Result")]
		EResult Result;

		[JsonProperty(PropertyName = "PurchaseResultDetail")]
		EPurchaseResultDetail PurchaseResultDetail;

		public FreeSubResponse(EResult result, EPurchaseResultDetail purchaseResultDetail) {
			Result = result;
			PurchaseResultDetail = purchaseResultDetail;
		}
	}
}