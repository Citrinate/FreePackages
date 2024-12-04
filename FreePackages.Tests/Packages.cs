using System.Collections.Generic;
using System.IO;
using ArchiSteamFarm.Helpers.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamKit2;

namespace FreePackages.Tests;

[TestClass]
[DeploymentItem("TestData")]
public class Packages {
	internal PackageFilter PackageFilter;

	[TestInitialize]
	public void InitializePackageFilter () {
		PackageFilter = new PackageFilter(new BotCache(), new List<FilterConfig>());
		PackageFilter.UpdateUserData(File.ReadAllText("userdata_empty.json").ToJsonObject<Steam.UserData>());
		PackageFilter.Country = "FOO";
	}

	[TestCleanup]
	public void CleanupPackageFilter() {
		PackageFilter.UpdateUserData(File.ReadAllText("userdata_empty.json").ToJsonObject<Steam.UserData>());
		PackageFilter.Country = "FOO";
	}

    [TestMethod]
    public void CanDetectFreePackage() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_which_is_free.txt"));

		Assert.IsTrue(package.IsFree());
    }


    [TestMethod]
    public void CanDetectPackageDemoState() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_deactivated_demo.txt"));

		Assert.IsTrue(package.DeactivatedDemo);
    }

    [TestMethod]
    public void CanDetectPackageTimeRestrictions() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_timed_activation.txt"));

		Assert.IsTrue(package.ExpiryTime > 0);
		Assert.IsTrue(package.StartTime > 0);
    }

    [TestMethod]
    public void CanDetectPackageDisallowedApp() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_disallowed_app.txt"));

		Assert.IsTrue(package.DontGrantIfAppIDOwned > 0);
    }

    [TestMethod]
    public void CanDetectPackageRestrictedCountry() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_restricted_countries.txt"));

		Assert.IsTrue(package.OnlyAllowRestrictedCountries);
		Assert.IsTrue(package.RestrictedCountries.Contains("DE"));
    }

    [TestMethod]
    public void CanDetectPackagePurchaseRestrictedCountry() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_purchase_restricted_countries.txt"));

		Assert.IsTrue(package.AllowPurchaseFromRestrictedCountries);
		Assert.IsTrue(package.PurchaseRestrictedCountries.Contains("US"));
    }
}
