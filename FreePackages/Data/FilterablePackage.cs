using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace FreePackages {
	internal sealed class FilterablePackage {
		internal SteamApps.PICSProductInfoCallback.PICSProductInfo ProductInfo;
		internal bool IsFree;
		internal bool IsAvailable;
		internal bool IsNew;
		internal List<FilterableApp> PackageContents = new();
		internal HashSet<uint> PackageContentIDs;
		internal HashSet<uint> PackageContentParentIDs = new();

		internal FilterablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo, bool isNew) {
			ProductInfo = productInfo;
			IsFree = PackageFilter.IsFreePackage(ProductInfo);
			IsAvailable = PackageFilter.IsAvailablePackage(ProductInfo);
			IsNew = isNew;
			PackageContentIDs = ProductInfo.KeyValues["appids"].Children.Select(x => x.AsUnsignedInteger()).ToHashSet();
		}

		internal void AddPackageContents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) {
			PackageContents = productInfos.Select(productInfo => new FilterableApp(productInfo)).ToList();

			// Don't care about the parents of package contents on new packages
			if (!IsNew) {
				PackageContentParentIDs = PackageContents.Where(app => app.ParentID != null).Select(app => app.ParentID!.Value).ToHashSet<uint>();
			}
		}

		internal void AddPackageContentParents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) {
			PackageContents.ForEach(app => {
				if (app.ParentID != null) {
					app.AddParent(productInfos.FirstOrDefault(parent => parent.ID == app.ParentID));
				}
			});
		}
	}
}