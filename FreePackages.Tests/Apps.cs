using System.Collections.Generic;
using System.IO;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SteamKit2;

namespace FreePackages.Tests;

[TestClass]
[DeploymentItem("TestData")]
public class Apps {
	internal PackageFilter PackageFilter;

	[TestInitialize]
	public void InitializePackageFilter () {
		PackageFilter = new PackageFilter(new BotCache(), new List<FilterConfig>());
		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json")));
		PackageFilter.Country = "FOO";
	}

	[TestCleanup]
	public void CleanupPackageFilter() {
		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json")));
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

		Assert.IsFalse(app.ReleaseState.IsNullOrEmpty());
		Assert.IsTrue(app.IsAvailable());
    }

    [TestMethod]
    public void CanDetectAvailableAppByState() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_state.txt"));

		Assert.IsFalse(app.State.IsNullOrEmpty());
		Assert.IsTrue(app.IsAvailable());
    }

    [TestMethod]
    public void CanDetectRedeemableAppWithAppRequirement() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_required_app.txt"));
		var userData = JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json"));
		userData.OwnedApps.Add(1086940);
		PackageFilter.UpdateUserData(userData);

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

		Assert.IsFalse(app.ListOfDLC.IsNullOrEmpty());
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
