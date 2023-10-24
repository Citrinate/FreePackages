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

		internal FilterablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo, bool isNew) {
			IsNew = isNew;
			ID = productInfo.ID;
			KeyValue kv = productInfo.KeyValues;
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

		internal void AddPackageContents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) {
			PackageContents = productInfos.Select(productInfo => new FilterableApp(productInfo)).ToList();

			// Don't care about the parents of package contents on new packages (we scan new packages for free dlc and nothing else)
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