using System;
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm.Helpers.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamKit2;

namespace FreePackages.Tests;

[DeploymentItem("TestData")]
[TestClass]
public sealed class Packages : IDisposable {
	private BotCache? BotCache;
	private PackageFilter? PackageFilter;

	[TestInitialize]
	public async Task InitializePackageFilter () {
		Dispose();

		BotCache = new BotCache();
		PackageFilter = new PackageFilter(BotCache, []);

		(Steam.UserData userData, Steam.UserInfo userInfo) = await GetEmptyDetails().ConfigureAwait(false);

		PackageFilter.UpdateUserDetails(userData, userInfo);
		PackageFilter.Country = "FOO";
	}

	[TestCleanup]
	public async Task CleanupPackageFilter() {
		if (PackageFilter == null) {
			throw new InvalidOperationException(nameof(PackageFilter));
		}

		(Steam.UserData userData, Steam.UserInfo userInfo) = await GetEmptyDetails().ConfigureAwait(false);

		PackageFilter.UpdateUserDetails(userData, userInfo);
		PackageFilter.Country = "FOO";
	}

    [TestMethod]
    public void CanDetectFreePackage() {
		KeyValue? kv = KeyValue.LoadAsText("package_which_is_free.txt");

		if (kv == null) {
			throw new InvalidOperationException(nameof(kv));
		}

		FilterablePackage package = new(kv);

		Assert.IsTrue(package.IsFree());
    }

    [TestMethod]
    public void CanDetectPackageDemoState() {
	    KeyValue? kv = KeyValue.LoadAsText("package_with_deactivated_demo.txt");

	    if (kv == null) {
		    throw new InvalidOperationException(nameof(kv));
	    }

	    FilterablePackage package = new(kv);

		Assert.IsTrue(package.DeactivatedDemo);
    }

    [TestMethod]
    public void CanDetectPackageTimeRestrictions() {
	    KeyValue? kv = KeyValue.LoadAsText("package_with_timed_activation.txt");

	    if (kv == null) {
		    throw new InvalidOperationException(nameof(kv));
	    }

	    FilterablePackage package = new(kv);

		Assert.IsGreaterThan<ulong>(0, package.ExpiryTime);
		Assert.IsGreaterThan<ulong>(0, package.StartTime);
    }

    [TestMethod]
    public void CanDetectPackageDisallowedApp() {
	    KeyValue? kv = KeyValue.LoadAsText("package_with_disallowed_app.txt");

	    if (kv == null) {
		    throw new InvalidOperationException(nameof(kv));
	    }

	    FilterablePackage package = new(kv);

		Assert.IsGreaterThan<uint>(0, package.DontGrantIfAppIDOwned);
    }

    [TestMethod]
    public void CanDetectPackageRestrictedCountry() {
	    KeyValue? kv = KeyValue.LoadAsText("package_with_restricted_countries.txt");

	    if (kv == null) {
		    throw new InvalidOperationException(nameof(kv));
	    }

	    FilterablePackage package = new(kv);

		Assert.IsTrue(package.OnlyAllowRestrictedCountries);
		Assert.IsNotNull(package.RestrictedCountries);
		Assert.Contains("DE", package.RestrictedCountries);
    }

    [TestMethod]
    public void CanDetectPackagePurchaseRestrictedCountry() {
	    KeyValue? kv = KeyValue.LoadAsText("package_with_purchase_restricted_countries.txt");

	    if (kv == null) {
		    throw new InvalidOperationException(nameof(kv));
	    }

		FilterablePackage package = new(kv);

		Assert.IsTrue(package.AllowPurchaseFromRestrictedCountries);
		Assert.IsNotNull(package.PurchaseRestrictedCountries);
		Assert.Contains("US", package.PurchaseRestrictedCountries);
    }

	public void Dispose() => BotCache?.Dispose();

	private static async Task<(Steam.UserData UserData, Steam.UserInfo UserInfo)> GetEmptyDetails() {
		Steam.UserData? userData = (await File.ReadAllTextAsync("userdata_empty.json").ConfigureAwait(false)).ToJsonObject<Steam.UserData>();

		if (userData == null) {
			throw new InvalidOperationException(nameof(userData));
		}

		Steam.UserInfo? userInfo = (await File.ReadAllTextAsync("userinfo_empty.json").ConfigureAwait(false)).ToJsonObject<Steam.UserInfo>();

		if (userInfo == null) {
			throw new InvalidOperationException(nameof(userData));
		}

		return (userData, userInfo);
	}
}
