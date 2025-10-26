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
	private Steam.UserData? UserData;
	private Steam.UserInfo? UserInfo;

	[TestInitialize]
	public async Task InitializePackageFilter () {
		Dispose();

		if (UserData == null) {
			FileStream fileStream = File.Open("userdata_empty.json", FileMode.Open);

			await using (fileStream.ConfigureAwait(false)) {
				UserData = await fileStream.ToJsonObject<Steam.UserData>().ConfigureAwait(false);
			}

			if (UserData == null) {
				throw new InvalidOperationException(nameof(UserData));
			}
		}

		if (UserInfo == null) {
			FileStream fileStream = File.Open("userinfo_empty.json", FileMode.Open);

			await using (fileStream.ConfigureAwait(false)) {
				UserInfo = await fileStream.ToJsonObject<Steam.UserInfo>().ConfigureAwait(false);
			}

			if (UserInfo == null) {
				throw new InvalidOperationException(nameof(UserInfo));
			}
		}

		BotCache = new BotCache();
		PackageFilter = new PackageFilter(BotCache, []);

		PackageFilter.UpdateUserDetails(UserData, UserInfo);
		PackageFilter.Country = "FOO";
	}

	[TestCleanup]
	public void CleanupPackageFilter() {
		if (UserData == null) {
			throw new InvalidOperationException(nameof(UserData));
		}

		if (UserInfo == null) {
			throw new InvalidOperationException(nameof(UserInfo));
		}

		if (PackageFilter == null) {
			throw new InvalidOperationException(nameof(PackageFilter));
		}

		PackageFilter.UpdateUserDetails(UserData, UserInfo);
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
}
