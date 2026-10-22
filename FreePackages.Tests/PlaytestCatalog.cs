using System.Collections.Generic;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FreePackages.Tests;

[TestClass]
[DeploymentItem("TestData")]
public class PlaytestCatalogTests {
	// Parser: the search response lists the BASE game appID in data-ds-appid, which is
	// exactly the value the plugin posts to /ajaxrequestplaytestaccess/<id> and stores
	// as app.Parent.ID. These tests cover the parsing without touching the network.
	[TestMethod]
	public void CanParsePlaytestCatalogAppIDs() {
		string resultsHtml = File.ReadAllText("playtest_search_results.txt");

		HashSet<uint> appIDs = PlaytestCatalog.ParseAppIDs(resultsHtml);

		// Three distinct real appIDs; duplicate 1873120 collapsed, id 0 ignored.
		Assert.IsTrue(appIDs.SetEquals(new uint[] { 1873120, 2385860, 16792 }));
		Assert.IsFalse(appIDs.Contains(0), "appID 0 must not be added to the live set");
	}

	[TestMethod]
	public void CanParsePageRejectsUnusableResponse() {
		// success != 1 -> the page is unusable, the whole fetch must abort (null).
		Assert.IsNull(PlaytestCatalog.ParsePage(new PlaytestCatalog.SearchResults(success: 0, totalCount: 100, resultsHtml: "<a data-ds-appid=\"1\">")));

		// null page -> unusable.
		Assert.IsNull(PlaytestCatalog.ParsePage(null));

		// success == 1 with no appids -> empty but usable (end-of-list page).
		HashSet<uint>? empty = PlaytestCatalog.ParsePage(new PlaytestCatalog.SearchResults(success: 1, totalCount: 0, resultsHtml: "<div>no results</div>"));
		Assert.IsNotNull(empty);
		Assert.AreEqual(0, empty!.Count);
	}

	// IsFetchComplete is the safety net that decides whether a fetch is safe to prune
	// from. A failed/empty/incomplete fetch MUST report false so DoUpdate keeps the
	// previous live set and never wipes RequestedPlaytests (which would re-request the
	// whole catalog on a network blip or a Steam markup change).
	[TestMethod]
	public void IsFetchCompleteRejectsIncompleteFetches() {
		// Healthy fetch: parsed roughly what Steam reports.
		Assert.IsTrue(PlaytestCatalog.IsFetchComplete(new HashSet<uint> { 1, 2, 3, 4 }, totalCount: 4));
		Assert.IsTrue(PlaytestCatalog.IsFetchComplete(BuildSet(2807), totalCount: 2807));

		// Empty fetch (nothing parsed — e.g. markup changed) -> never complete.
		Assert.IsFalse(PlaytestCatalog.IsFetchComplete(new HashSet<uint>(), totalCount: 2807));

		// total_count 0 -> never complete.
		Assert.IsFalse(PlaytestCatalog.IsFetchComplete(BuildSet(10), totalCount: 0));

		// Parsed far fewer than total_count (less than half) -> likely markup breakage.
		Assert.IsFalse(PlaytestCatalog.IsFetchComplete(BuildSet(100), totalCount: 2807));
	}

	// Prune: a playtest that left the catalog is removed from RequestedPlaytests and
	// WaitlistedPlaytests; one that stayed is kept. A playtest that re-opens later
	// re-enters the catalog, was pruned while absent, and is requested again.
	[TestMethod]
	public void PruneRemovesPlaytestsThatLeftLiveSet() {
		BotCache botCache = new();

		botCache.AddRequestedPlaytest(100); // leaves the catalog
		botCache.AddRequestedPlaytest(200); // stays live
		botCache.AddWaitlistedPlaytest(300); // leaves
		botCache.AddWaitlistedPlaytest(400); // stays live

		botCache.PrunePlaytests(new HashSet<uint> { 200, 400, 500 });

		Assert.IsFalse(botCache.RequestedPlaytests.Contains(100), "requested playtest that left must be pruned");
		Assert.IsTrue(botCache.RequestedPlaytests.Contains(200), "requested playtest that stayed must be kept");
		Assert.IsFalse(botCache.WaitlistedPlaytests.Contains(300), "waitlisted playtest that left must be pruned");
		Assert.IsTrue(botCache.WaitlistedPlaytests.Contains(400), "waitlisted playtest that stayed must be kept");

		// A re-opening (100 re-enters the live set) is requestable again because it was pruned.
		Assert.IsFalse(botCache.RequestedPlaytests.Contains(100));
	}

	[TestMethod]
	public void PruneHandlesEmptySets() {
		BotCache botCache = new();

		// Pruning a cache with no recorded playtests must not throw and must stay empty.
		botCache.PrunePlaytests(new HashSet<uint> { 1, 2, 3 });

		Assert.AreEqual(0, botCache.RequestedPlaytests.Count);
		Assert.AreEqual(0, botCache.WaitlistedPlaytests.Count);
	}

	// IsUnconstrainedAllPlaytestsFilter gates the proactive catalog path: it must be
	// true only for a filter that accepts every playtest (PlaytestMode All, nothing else
	// set), so the catalog path — which has only BASE appIDs and can't honor any finer
	// filter — never bypasses the user's filters. Any constraint falls back to the PICS
	// path (HandlePlaytest), which resolves metadata and applies the filter first.
	[TestMethod]
	public void IsUnconstrainedAllPlaytestsFilterAcceptsAllOnlyConfig() {
		// A filter with only PlaytestMode = All. IgnoredTypes defaults to {"Demo"}, which
		// never rejects a playtest (playtests are not demos), so the default is allowed.
		Assert.IsTrue(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All }));

		// An empty IgnoredTypes is allowed too.
		Assert.IsTrue(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredTypes = new HashSet<string>() }));
	}

	[TestMethod]
	public void IsUnconstrainedAllPlaytestsFilterRejectsPlaytestModeOtherThanAll() {
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.None }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.Unlimited }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.Limited }));
	}

	[TestMethod]
	public void IsUnconstrainedAllPlaytestsFilterRejectsAnyOtherConstraint() {
		// All mode plus any app-level constraint -> fall back to the PICS path.
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, Types = new HashSet<string> { "Game" } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, Categories = new HashSet<uint> { 1 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, Tags = new HashSet<uint> { 19 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredAppIDs = new HashSet<uint> { 700 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredTags = new HashSet<uint> { 19 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredCategories = new HashSet<uint> { 1 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredContentDescriptors = new HashSet<uint> { 2 } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, ImportStoreFilters = true }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoreFreeWeekends = true }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, MinReviewScore = 80 }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, Languages = new HashSet<string> { "english" } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, NoCostOnly = true }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, Systems = new HashSet<string> { "linux" } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, WishlistOnly = true }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, MaxDaysOld = 30 }));
	}

	[TestMethod]
	public void IsUnconstrainedAllPlaytestsFilterTreatsNonDemoIgnoredTypeAsConstraint() {
		// The default {"Demo"} is allowed, but any other ignored type suppresses the
		// proactive path, since the catalog can't tell whether it would reject a playtest.
		Assert.IsTrue(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredTypes = new HashSet<string> { "Demo" } }));
		Assert.IsFalse(PackageFilter.IsUnconstrainedAllPlaytestsFilter(new FilterConfig { PlaytestMode = EPlaytestMode.All, IgnoredTypes = new HashSet<string> { "Beta" } }));
	}

	private static HashSet<uint> BuildSet(int count) {
		HashSet<uint> set = new(count);

		for (uint i = 1; i <= count; i++) {
			set.Add(i);
		}

		return set;
	}
}