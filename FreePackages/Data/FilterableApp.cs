using SteamKit2;

namespace FreePackages {
	internal sealed class FilterableApp {
		internal SteamApps.PICSProductInfoCallback.PICSProductInfo ProductInfo;
		internal bool IsFree;
		internal bool IsAvailable;
		internal EAppType Type;
		internal FilterableApp? Parent = null;
		internal uint? ParentID = null;

		internal FilterableApp(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo) {
			ProductInfo = productInfo;
			IsFree = PackageFilter.IsFreeApp(ProductInfo);
			IsAvailable = PackageFilter.IsAvailableApp(ProductInfo);
			Type = ProductInfo.KeyValues["common"]["type"].AsEnum<EAppType>();

			// There's another parentID field for playtests: ["extended"]["betaforappid"], but it's less reliable
			// Ex: https://steamdb.info/app/2420490/ on Oct 17 2023 has "parent" and is redeemable, but doesn't have "betaforappid"
			uint parentID = ProductInfo.KeyValues["common"]["parent"].AsUnsignedInteger();
			// Right now I only want the parents for playtest and demos
			if (parentID > 0 && (Type == EAppType.Beta || Type == EAppType.Demo)) {
				ParentID = parentID;
			}
		}

		internal void AddParent(SteamApps.PICSProductInfoCallback.PICSProductInfo? productInfo) {
			if (productInfo == null) {
				return;
			}

			Parent = new FilterableApp(productInfo);
		}
	}
}