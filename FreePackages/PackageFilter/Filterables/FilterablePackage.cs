using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using SteamKit2;

namespace FreePackages {
	internal sealed class FilterablePackage {
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
		internal uint MustOwnAppToPurchase;
		internal List<string>? RestrictedCountries;
		internal bool OnlyAllowRestrictedCountries;
		internal List<string>? PurchaseRestrictedCountries;
		internal bool AllowPurchaseFromRestrictedCountries;
		internal bool FreeWeekend;
		internal bool BetaTesterPackage;
		
		internal FilterablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo) : this(productInfo.ID, productInfo.KeyValues) {}
		internal FilterablePackage(KeyValue kv) : this(Convert.ToUInt32(kv.Name), kv) {}
		internal FilterablePackage(uint id, KeyValue kv) {
			ID = id;
			PackageContentIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger()).ToHashSet();
			BillingType = (EBillingType) kv["billingtype"].AsInteger();
			Status = (EPackageStatus) kv["status"].AsInteger();
			LicenseType = (ELicenseType) kv["licensetype"].AsInteger();
			DeactivatedDemo = kv["extended"]["deactivated_demo"].AsBoolean();
			ExpiryTime = kv["extended"]["expirytime"].AsUnsignedLong();
			StartTime = kv["extended"]["starttime"].AsUnsignedLong();
			DontGrantIfAppIDOwned = kv["extended"]["dontgrantifappidowned"].AsUnsignedInteger();
			MustOwnAppToPurchase = kv["extended"]["mustownapptopurchase"].AsUnsignedInteger();
			RestrictedCountries = kv["extended"]["restrictedcountries"].AsString()?.ToUpper().Split(" ").ToList();
			OnlyAllowRestrictedCountries = kv["extended"]["onlyallowrestrictedcountries"].AsBoolean();
			PurchaseRestrictedCountries = kv["extended"]["purchaserestrictedcountries"].AsString()?.ToUpper().Split(" ").ToList();
			AllowPurchaseFromRestrictedCountries = kv["extended"]["allowpurchasefromrestrictedcountries"].AsBoolean();
			FreeWeekend = kv["extended"]["freeweekend"].AsBoolean();
			BetaTesterPackage = kv["extended"]["betatesterpackage"].AsBoolean();
		}

		internal static async Task<List<FilterablePackage>?> GetFilterables(List<SteamApps.PICSProductInfoCallback> productInfos, Func<FilterablePackage, bool>? onNonFreePackage = null, CancellationToken? cancellationToken = null) {
			var packageProductInfos = productInfos.SelectMany(static result => result.Packages.Values);
			if (packageProductInfos.Count() == 0) {
				return [];
			}
			
			List<FilterablePackage> packages = packageProductInfos.Select(x => new FilterablePackage(x)).ToList();

			// Filter out non-free, non-new packages
			packages.RemoveAll(package => {
				if (!package.IsFree() || !package.IsAvailable()) {
					if (onNonFreePackage?.Invoke(package) == false) {
						return false;
					}

					return true;
				}

				return false;
			});

			// Get the apps contained in each package
			HashSet<uint> packageContentsIDs = packages.SelectMany(package => package.PackageContentIDs).ToHashSet();
			var packageContentProductInfos = (await ProductInfo.GetProductInfo(appIDs: packageContentsIDs, cancellationToken: cancellationToken).ConfigureAwait(false))?.SelectMany(static result => result.Apps.Values);
			if (packageContentProductInfos == null) {
				ASF.ArchiLogger.LogNullError(packageContentProductInfos);

				return null;
			}

			packages.ForEach(package => package.AddPackageContents(packageContentProductInfos.Where(x => package.PackageContentIDs.Contains(x.ID))));

			// Filter out any packages which contain unavailable apps
			packages.RemoveAll(package => {
				if (!package.IsAvailablePackageContents() && package.BillingType != EBillingType.NoCost) {
					// Ignore this check for NoCost packages; assume that everything is available
					// Ex: https://steamdb.info/sub/1011710 is redeemable even though it contains https://steamdb.info/app/235901/ (which as of Feb 12 2024 is some unknown app)
					if (onNonFreePackage?.Invoke(package) == false) {
						return false;
					}

					return true;
				}

				return false;
			});

			// Get the parents for the apps in each package
			HashSet<uint> parentIDs = packages.SelectMany(package => package.PackageContentParentIDs).ToHashSet();
			var parentProductInfos = (await ProductInfo.GetProductInfo(appIDs: parentIDs, cancellationToken: cancellationToken).ConfigureAwait(false))?.SelectMany(static result => result.Apps.Values);
			if (parentProductInfos == null) {
				ASF.ArchiLogger.LogNullError(parentProductInfos);

				return null;
			}

			if (parentProductInfos.Count() > 0) {
				packages.ForEach(package => {
					if (package.PackageContentParentIDs.Count != 0) {
						package.AddPackageContentParents(parentProductInfos.Where(parent => package.PackageContentParentIDs.Contains(parent.ID)));
					}
				});
			}

			return packages;
		}

		internal void AddPackageContents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) => AddPackageContents(productInfos.Select(productInfo => (productInfo.ID, productInfo.KeyValues)));
		internal void AddPackageContents(IEnumerable<KeyValue> kvs) => AddPackageContents(kvs.Select(kv => (kv["appid"].AsUnsignedInteger(), kv)));
		internal void AddPackageContents(IEnumerable<(uint id, KeyValue kv)> packageContents) {
			PackageContents = packageContents.Select(packageContent => new FilterableApp(packageContent.id, packageContent.kv)).ToList();
			PackageContentParentIDs = PackageContents.Where(app => app.ParentInfoRequired && app.ParentID != null).Select(app => app.ParentID!.Value).ToHashSet<uint>();
		}

		internal void AddPackageContentParents(IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> productInfos) => AddPackageContentParents(productInfos.Select(productInfo => (productInfo.ID, productInfo.KeyValues)));
		internal void AddPackageContentParents(IEnumerable<KeyValue> kvs) => AddPackageContentParents(kvs.Select(kv => (kv["appid"].AsUnsignedInteger(), kv)));
		internal void AddPackageContentParents(IEnumerable<(uint id, KeyValue kv)> parents) {
			PackageContents.ForEach(app => {
				if (app.ParentInfoRequired && app.ParentID != null) {
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

			if (BetaTesterPackage) {
				// Playtests can't be activated through packages
				return false;
			}

			if (ID == 17906) {
				// Special case: Anonymous Dedicated Server Comp (https://steamdb.info/sub/17906/)
				// This always returns AccessDenied/InvalidPackage
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
