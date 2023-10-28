using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace FreePackages {
	internal sealed class FilterablePackage {
		internal bool IsNew;
		internal List<FilterableApp> PackageContents = new();
		internal HashSet<uint> PackageContentIDs;
		internal HashSet<uint> PackageContentParentIDs = new();

		internal uint ID;
		internal EBillingType BillingType;
		internal EPackageStatus Status;
		internal ELicenseType LicenseType;
		internal bool DeactivatedDemo;
		internal ulong ExpiryTime;
		internal ulong StartTime;
		internal uint DontGrantIfAppIDOwned;
		internal List<string>? RestrictedCountries;
		internal bool OnlyAllowRestrictedCountries;
		internal List<string>? PurchaseRestrictedCountries;
		internal bool AllowPurchaseFromRestrictedCountries;
		internal bool FreeWeekend;
		
		internal FilterablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo, bool isNew = false) : this(productInfo.ID, productInfo.KeyValues, isNew) {}
		internal FilterablePackage(KeyValue kv, bool isNew = false) : this(Convert.ToUInt32(kv.Name), kv, isNew) {}
		internal FilterablePackage(uint id, KeyValue kv, bool isNew) {
			IsNew = isNew;
			ID = id;
			PackageContentIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger()).ToHashSet();
			BillingType = (EBillingType) kv["billingtype"].AsInteger();
			Status = (EPackageStatus) kv["status"].AsInteger();
			LicenseType = (ELicenseType) kv["licensetype"].AsInteger();
			DeactivatedDemo = kv["extended"]["deactivated_demo"].AsBoolean();
			ExpiryTime = kv["extended"]["expirytime"].AsUnsignedLong();
			StartTime = kv["extended"]["starttime"].AsUnsignedLong();
			DontGrantIfAppIDOwned = kv["extended"]["dontgrantifappidowned"].AsUnsignedInteger();
			RestrictedCountries = kv["extended"]["restrictedcountries"].AsString()?.ToUpper().Split(" ").ToList();
			OnlyAllowRestrictedCountries = kv["extended"]["onlyallowrestrictedcountries"].AsBoolean();
			PurchaseRestrictedCountries = kv["extended"]["purchaserestrictedcountries"].AsString()?.ToUpper().Split(" ").ToList();
			AllowPurchaseFromRestrictedCountries = kv["extended"]["allowpurchasefromrestrictedcountries"].AsBoolean();
			FreeWeekend = kv["extended"]["freeweekend"].AsBoolean();
		}

		internal void AddPackageContents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) => AddPackageContents(productInfos.Select(productInfo => (productInfo.ID, productInfo.KeyValues)));
		internal void AddPackageContents(IEnumerable<KeyValue> kvs) => AddPackageContents(kvs.Select(kv => (kv["appid"].AsUnsignedInteger(), kv)));
		internal void AddPackageContents(IEnumerable<(uint id, KeyValue kv)> packageContents) {
			PackageContents = packageContents.Select(packageContent => new FilterableApp(packageContent.id, packageContent.kv)).ToList();

			// Don't care about the parents of package contents on new packages (we scan new packages for free dlc and nothing else)
			if (!IsNew) {
				PackageContentParentIDs = PackageContents.Where(app => app.ParentID != null).Select(app => app.ParentID!.Value).ToHashSet<uint>();
			}
		}

		internal void AddPackageContentParents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) => AddPackageContentParents(productInfos.Select(productInfo => (productInfo.ID, productInfo.KeyValues)));
		internal void AddPackageContentParents(IEnumerable<KeyValue> kvs) => AddPackageContentParents(kvs.Select(kv => (kv["appid"].AsUnsignedInteger(), kv)));
		internal void AddPackageContentParents(IEnumerable<(uint id, KeyValue kv)> parents) {
			PackageContents.ForEach(app => {
				if (app.ParentID != null) {
					try {
						var parent = parents.First(parent => parent.id == app.ParentID);
						app.AddParent(parent.id, parent.kv);
					} catch (Exception) {
						// Ignore missing parent exception
					}
				}
			});
		}

		internal bool IsFree() {
			if (BillingType == EBillingType.FreeOnDemand || BillingType == EBillingType.NoCost) {
				return true;
			}

			return false;
		}

		internal bool IsAvailable() {
			if (PackageContentIDs.Count == 0) {
				// Package has no apps
				return false;
			}

			if (Status != EPackageStatus.Available) {
				// Package is unavailable
				return false;
			}

			if (LicenseType != ELicenseType.SinglePurchase) {
				// Wrong license type
				return false;
			}

			if (ExpiryTime > 0 && ExpiryTime < DateUtils.DateTimeToUnixTime(DateTime.UtcNow)) {
				// Package was only available for a limited time and is no longer available
				return false;
			}
			
			if (DeactivatedDemo) {
				// Demo package has been disabled
				return false;
			}

			return true;
		}

		internal bool IsAvailablePackageContents() {
			if (PackageContentIDs.Count != PackageContents.Count) {
				// Could not find all of the apps for this package
				return false;
			}

			if (PackageContents.Any(app => !app.IsAvailable())) {
				// At least one of the apps in this package isn't available
				return false;
			}

			return true;
		}
	}
}
