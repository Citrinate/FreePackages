using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SteamKit2;

namespace FreePackages.Tests;

[TestClass]
[DeploymentItem("TestData")]
public class Filters {
	internal PackageFilter PackageFilter;
	internal FilterConfig Filter;

	[TestInitialize]
	public void InitializePackageFilter () {
		PackageFilter = new PackageFilter(new BotCache(), new List<FilterConfig>());
		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json")));
		Filter = new FilterConfig();
	}

	[TestCleanup]
	public void CleanupPackageFilter() {
		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json")));
		Filter = new FilterConfig();
	}

	[TestMethod]
	public void CanFilterAppByType() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_type.txt"));

		Filter.Types.Add("Foo");

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Types.Add("GaMe");

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.IgnoredTypes.Add("GaMe");

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppByTag() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_tags.txt"));

		Filter.Tags.Add(8000);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Tags.Add(113);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.RequireAllTags = true;

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Tags.Remove(8000);
		Filter.Tags.Add(19);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.IgnoredTags.Add(113);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppAppByParentTag() {
		var demo = new FilterableApp(KeyValue.LoadAsText("demo_with_fewer_tags_than_parent.txt"));
		var demoParent = KeyValue.LoadAsText("demo_with_fewer_tags_than_parent_parent.txt");

		Filter.Tags.Add(1742);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));

		demo.AddParent(demoParent);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(demo, Filter));

		Filter.RequireAllTags = true;
		Filter.Tags.Add(19);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(demo, Filter));

		Filter.Tags.Add(8000);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));
	}

	[TestMethod]
	public void CanFilterAppByCategory() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_categories.txt"));

		Filter.Categories.Add(8000);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Categories.Add(8);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.RequireAllCategories = true;

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Categories.Remove(8000);
		Filter.Categories.Add(1);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.IgnoredCategories.Add(8);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppWithNoCategoryByParentCategory() {
		var playtest = new FilterableApp(KeyValue.LoadAsText("playtest_with_no_categories.txt"));
		var playtestParent = KeyValue.LoadAsText("playtest_with_no_categories_parent.txt");

		Filter.Categories.Add(2);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(playtest, Filter));

		playtest.AddParent(playtestParent);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(playtest, Filter));

		Filter.RequireAllCategories = true;
		Filter.Categories.Add(1);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(playtest, Filter));

		Filter.Categories.Add(8000);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(playtest, Filter));
	}

	[TestMethod]
	public void CanFilterAppByParentCategory() {
		var demo = new FilterableApp(KeyValue.LoadAsText("demo_with_fewer_categories_than_parent.txt"));
		var demoParent = KeyValue.LoadAsText("demo_with_fewer_categories_than_parent_parent.txt");

		Filter.Categories.Add(22);

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));

		demo.AddParent(demoParent);

		Assert.IsTrue(demo.Parent.Category.Contains(22));
		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));
	}

	[TestMethod]
	public void CanFilterAppByLanguage() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_language_support.txt"));

		Filter.Languages.Add("foo");

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.Languages.Add("eNgLiSh");

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppWithNoLanguageByParentLanguage() {
		var playtest = new FilterableApp(KeyValue.LoadAsText("playtest_with_no_languages.txt"));
		var playtestParent = KeyValue.LoadAsText("playtest_with_no_languages_parent.txt");

		Filter.Languages.Add("eNgLiSh");

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(playtest, Filter));

		playtest.AddParent(playtestParent);

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(playtest, Filter));
	}

	[TestMethod]
	public void CanFilterAppByParentLanguage() {
		var demo = new FilterableApp(KeyValue.LoadAsText("demo_with_fewer_languages_than_parent.txt"));
		var demoParent = KeyValue.LoadAsText("demo_with_fewer_languages_than_parent_parent.txt");

		Filter.Languages.Add("fReNcH");

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));

		demo.AddParent(demoParent);

		Assert.IsTrue(demo.Parent.SupportedLanguages.Contains("french"));
		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(demo, Filter));
	}

	[TestMethod]
	public void CanFilterAppByReviewScore() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_review_score.txt"));

		Filter.MinReviewScore = 10;

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.MinReviewScore = 5;

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppByContentDescriptor() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_content_descriptors.txt"));

		Assert.IsFalse(PackageFilter.IsAppIgnoredByFilter(app, Filter));

		Filter.IgnoredContentDescriptors.Add(2);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppAppByParentContentDescriptor() {
		var demo = new FilterableApp(KeyValue.LoadAsText("demo_with_fewer_content_descriptors_than_parent.txt"));
		var demoParent = KeyValue.LoadAsText("demo_with_fewer_content_descriptors_than_parent_parent.txt");

		Filter.IgnoredContentDescriptors.Add(2);

		Assert.IsFalse(PackageFilter.IsAppIgnoredByFilter(demo, Filter));

		demo.AddParent(demoParent);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(demo, Filter));
	}

	[TestMethod]
	public void CanFilterAppByID() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_type.txt"));

		Assert.IsFalse(PackageFilter.IsAppIgnoredByFilter(app, Filter));

		Filter.IgnoredAppIDs.Add(440);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanFilterAppByParentID() {
		var demo = new FilterableApp(KeyValue.LoadAsText("demo_with_fewer_tags_than_parent.txt"));
		var demoParent = KeyValue.LoadAsText("demo_with_fewer_tags_than_parent_parent.txt");

		Filter.IgnoredAppIDs.Add(400);

		Assert.IsFalse(PackageFilter.IsAppIgnoredByFilter(demo, Filter));

		demo.AddParent(demoParent);

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(demo, Filter));
	}

	[TestMethod]
	public void CanFilterAppByPlaytest() {
		var playtest = new FilterableApp(KeyValue.LoadAsText("playtest_with_no_waitlist.txt"));
		playtest.AddParent(KeyValue.LoadAsText("playtest_with_no_waitlist_parent.txt"));

		Assert.IsFalse(PackageFilter.IsPlaytestWantedByFilter(playtest, Filter));

		Filter.PlaytestMode = EPlaytestMode.Unlimited;

		Assert.IsTrue(PackageFilter.IsPlaytestWantedByFilter(playtest, Filter));
	}

	[TestMethod]
	public void CanFilterPackageByFreeWeekend() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_free_weekend.txt"));

		Assert.IsFalse(PackageFilter.IsPackageIgnoredByFilter(package, Filter));

		Filter.IgnoreFreeWeekends = true;

		Assert.IsTrue(PackageFilter.IsPackageIgnoredByFilter(package, Filter));
	}

	[TestMethod]
	public void CanFilterPackageByContents() {
		var package = new FilterablePackage(KeyValue.LoadAsText("package_with_single_app.txt"));
		var package_app_1 = KeyValue.LoadAsText("package_with_single_app_app_1.txt");

		Filter.Types.Add("GaMe");
		Filter.IgnoredTypes.Add("GaMe");

		Assert.IsFalse(PackageFilter.IsPackageWantedByFilter(package, Filter));
		Assert.IsFalse(PackageFilter.IsPackageIgnoredByFilter(package, Filter));

		package.AddPackageContents(new List<KeyValue>() { package_app_1 });

		Assert.IsTrue(PackageFilter.IsPackageWantedByFilter(package, Filter));
		Assert.IsTrue(PackageFilter.IsPackageIgnoredByFilter(package, Filter));
	}

	[TestMethod]
	public void CanFilterByStoreData() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_type.txt"));

		Filter.ImportStoreFilters = true;

		Assert.IsFalse(PackageFilter.IsAppIgnoredByFilter(app, Filter));

		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_with_ignored_apps.json")));

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));

		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_with_excluded_tags.json")));

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));

		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_with_excluded_content_descriptors.json")));

		Assert.IsTrue(PackageFilter.IsAppIgnoredByFilter(app, Filter));
	}

	[TestMethod]
	public void CanUseMultipleFilters() {	
		var app = new FilterableApp(KeyValue.LoadAsText("app_with_type.txt"));

		var filterA = new FilterConfig();
		filterA.Types.Add("Foo");
		filterA.IgnoredTypes.Add("Game");

		var filterB = new FilterConfig();
		filterB.Types.Add("Game");
		
		var packageFilter = new PackageFilter(new BotCache(), new List<FilterConfig>() { filterA, filterB });
		packageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_empty.json")));

		Assert.IsFalse(packageFilter.IsAppWantedByFilter(app, filterA));
		Assert.IsTrue(packageFilter.IsAppIgnoredByFilter(app, filterA));

		Assert.IsTrue(packageFilter.IsAppWantedByFilter(app, filterB));
		Assert.IsFalse(packageFilter.IsAppIgnoredByFilter(app, filterB));

		Assert.IsTrue(packageFilter.IsWantedApp(app));
	}

	[TestMethod]
	public void CanFilterAppBySystem() {
		var deck_verified_app = new FilterableApp(KeyValue.LoadAsText("app_with_deck_verified.txt"));
		var deck_playable_app = new FilterableApp(KeyValue.LoadAsText("app_with_deck_playable.txt"));
		var deck_unsuppored_app = new FilterableApp(KeyValue.LoadAsText("app_with_deck_unsupported.txt"));
		var deck_unknown_app = new FilterableApp(KeyValue.LoadAsText("app_with_deck_unknown.txt"));

		Assert.AreEqual(deck_verified_app.DeckCompatibility, (uint) 3);
		Assert.AreEqual(deck_playable_app.DeckCompatibility, (uint) 2);
		Assert.AreEqual(deck_unsuppored_app.DeckCompatibility, (uint) 1);
		Assert.AreEqual(deck_unknown_app.DeckCompatibility, (uint) 0);

		Filter.Systems.Add("DeckVerified");
		Filter.Systems.Add("DeckPlayable");
		Filter.Systems.Add("DeckUnsupported");
		Filter.Systems.Add("DeckUnknown");

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(deck_verified_app, Filter));
		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(deck_playable_app, Filter));
		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(deck_unsuppored_app, Filter));
		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(deck_unknown_app, Filter));

		Filter.Systems.Remove("DeckVerified");
		Filter.Systems.Remove("DeckPlayable");
		Filter.Systems.Remove("DeckUnsupported");
		Filter.Systems.Remove("DeckUnknown");

		var windows_app = new FilterableApp(KeyValue.LoadAsText("app_with_type.txt"));

		Filter.Systems.Add("Foo");

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(windows_app, Filter));

		Filter.Systems.Add("WiNdOwS");

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(windows_app, Filter));
	}

	[TestMethod]
	public void CanFilterPackageByNoCost() {
		var free_package = new FilterablePackage(KeyValue.LoadAsText("package_which_is_free.txt"));
		var no_cost_package = new FilterablePackage(KeyValue.LoadAsText("package_which_is_no_cost.txt"));

		Assert.IsFalse(PackageFilter.FilterOnlyAllowsPackages(Filter));

		Filter.NoCostOnly = true;

		Assert.IsTrue(PackageFilter.IsPackageIgnoredByFilter(free_package, Filter));
		Assert.IsFalse(PackageFilter.IsPackageIgnoredByFilter(no_cost_package, Filter));
		Assert.IsTrue(PackageFilter.FilterOnlyAllowsPackages(Filter));
	}

	[TestMethod]
	public void CanFilterByWishlist() {
		var app = new FilterableApp(KeyValue.LoadAsText("app_which_is_free.txt"));

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		Filter.WishlistOnly = true;

		Assert.IsFalse(PackageFilter.IsAppWantedByFilter(app, Filter));

		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_with_wishlist_apps.json")));

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));

		PackageFilter.UpdateUserData(JsonConvert.DeserializeObject<UserData>(File.ReadAllText("userdata_with_followed_apps.json")));

		Assert.IsTrue(PackageFilter.IsAppWantedByFilter(app, Filter));
	}
}
