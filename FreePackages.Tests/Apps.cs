using System.Collections.Generic;
using System.IO;
using ArchiSteamFarm.Helpers.Json;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SteamKit2;

namespace FreePackages.Tests;

[TestClass]
[DeploymentItem("TestData")]
public class Apps {
	internal PackageFilter PackageFilter;

	[TestInitialize]
	public void InitializePackageFilter () {
		PackageFilter = new PackageFilter(new BotCache(), new List<FilterConfig>());
		PackageFilter.UpdateUserDetails(File.ReadAllText("userdata_empty.json").ToJsonObject<Steam.UserData>(), File.ReadAllText("userinfo_empty.json").ToJsonObject<Steam.UserInfo>());
		PackageFilter.Country = "FOO";
	}

	[TestCleanup]
	public void CleanupPackageFilter() {
		PackageFilter.UpdateUserDetails(File.ReadAllText("userdata_empty.json").ToJsonObject<Steam.UserData>(), File.ReadAllText("userinfo_empty.json").ToJsonObject<Steam.UserInfo>());
		PackageFilter.Country = "FOO";
	}

	[TestMethod]
	public void CanDetectFreeApp() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_which_is_free.txt"));

		Assert.IsTrue(app.IsFree());
	}

	[TestMethod]
	public void CanDetectAvailableAppByReleaseState() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_release_state.txt"));

		Assert.IsFalse(string.IsNullOrEmpty(app.ReleaseState));
		Assert.IsTrue(app.IsAvailable());
	}

	[TestMethod]
	public void CanDetectAvailableAppByState() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_state.txt"));

		Assert.IsFalse(string.IsNullOrEmpty(app.State));
		Assert.IsTrue(app.IsAvailable());
	}

	[TestMethod]
	public void CanDetectRedeemableAppWithAppRequirement() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_required_app.txt"));
		var userData = File.ReadAllText("userdata_empty.json").ToJsonObject<Steam.UserData>();
		userData.OwnedApps.Add(1086940);
		PackageFilter.UpdateUserDetails(userData, File.ReadAllText("userinfo_empty.json").ToJsonObject<Steam.UserInfo>());

		Assert.IsTrue(app.MustOwnAppToPurchase > 0);
		Assert.IsTrue(PackageFilter.IsRedeemableApp(app));
	}

	[TestMethod]
	public void CanDetectRedeemableAppWithRestrictedCountry() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_restricted_countries.txt"));

		Assert.IsTrue(app.RestrictedCountries.Contains("DE"));
		Assert.IsTrue(PackageFilter.IsRedeemableApp(app));

		PackageFilter.Country = "dE";

		Assert.IsFalse(PackageFilter.IsRedeemableApp(app));
	}

	[TestMethod]
	public void CanDetectRedeemableAppWithPurchaseRestrictedCountry() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_purchase_restricted_countries.txt"));

		Assert.IsTrue(app.AllowPurchaseFromRestrictedCountries);
		Assert.IsTrue(app.PurchaseRestrictedCountries.Contains("US"));
		Assert.IsFalse(PackageFilter.IsRedeemableApp(app));

		PackageFilter.Country = "uS";

		Assert.IsTrue(PackageFilter.IsRedeemableApp(app));
	}

	[TestMethod]
	public void CanFindAppDLC() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_dlc.txt"));
		PackageFilter.Country = "";

		Assert.IsFalse(string.IsNullOrEmpty(app.ListOfDLC));
	}

	[TestMethod]
	public void CanDetectNonRedeemablePlaytestWithHiddenParent() {
		var playtest = new FilterableApp(KeyValue.LoadAsText("playtest_with_hidden_parent.txt"));
		var playtestParent = KeyValue.LoadAsText("playtest_with_hidden_parent_parent.txt");
		playtest.AddParent(playtestParent);

		Assert.IsTrue(playtest.Parent.Hidden);
		Assert.IsFalse(PackageFilter.IsRedeemablePlaytest(playtest));
	}
}
