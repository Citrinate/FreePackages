using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Web.Responses;

// PICS can't tell us whether a playtest's signup is currently open, so more than half
// of the playtest requests the plugin used to issue came back as 401 ("no playtest for
// appID") and got re-issued on every PICS change. To stop that storm without a TTL
// (which would risk missing a playtest that re-opens several times over a weekend),
// we periodically fetch Steam's own "Playtests" store search (category1=989). That
// category only ever lists apps whose playtest signup is currently visible, so list
// membership is the re-trigger: a playtest that leaves the list (dev hid the signup)
// and later returns (re-opened) is requested again, every time.
//
// The live set is the source of truth for "joinable right now". PICS is kept only to
// (a) resolve playtest_type for the limited/unlimited filter and (b) wake us early via
// RequestRefresh when a Beta app changes. See the plan in
// ~/.claude/plans/snappy-weaving-aurora.md for the full rationale.

namespace FreePackages {
	internal static class PlaytestCatalog {
		// Steam's search endpoint caps `count` at 100 per request (verified 2026-07-18:
		// count=200 returns the same 100 entries as count=100). 100/page is the most we can
		// get, so a full ~3,005-playtest fetch is 31 pages — and Steam's anonymous-search
		// burst limit is a HARD ~30-request cap (count-based, not rate-based: spacing
		// 0s and 1s both die on exactly the 31st request). So the 31st/final page trips the
		// limit; we ride it out with PageRetryBackoff instead of aborting the whole fetch.
		private const int PageSize = 100;
		private const int MaxPages = 200; // 200 * 100 = 20,000 — a safety bound well above the ~3,000 observed
		// Run the first fetch almost immediately at startup (not after the full interval): until
		// the live set is populated HasFetched stays false, the gate falls back to PICS-only, and
		// the persisted queue rains 401 Invalid. A few seconds lets ASF.WebBrowser settle, then
		// we fetch. PLAYTEST activations are paused for the duration of the fetch so even that
		// window is dry; apps/subs keep going.
		private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
		// Fetch the catalog once per hour. Each fetch is ~31 paginated GETs, and a playtest can
		// only re-open a handful of times over a weekend, so an hourly cadence keeps our request
		// volume low for a negligible increase in the chance we miss a briefly-open window.
		private static readonly TimeSpan RefreshFrequency = TimeSpan.FromMinutes(60);
		// Early-wake debounce. PICS fires Beta-app changes in bursts, and each wake re-fetches
		// the WHOLE catalog (all ~31 pages). Bounding this to the same hourly cadence keeps PICS
		// storms from turning into a re-fetch every few minutes, which both wasted requests and
		// pushed us into Steam's anonymous-search rate limit. A re-opening is still caught within
		// an hour — the same window the periodic cycle already targets.
		private static readonly TimeSpan EarlyWakeDebounce = TimeSpan.FromMinutes(60);
		// Pace the paginated fetch. Steam's anonymous-search burst limit is a HARD ~30-request
		// cap inside a ~5-minute (300s) SLIDING window (count-based, not rate-based: spacing 0s
		// and 1s both die on exactly the 31st request). The window slides, so what matters is
		// request DENSITY inside any 300s span, not the total fetch duration: "spread the fetch
		// past 5 minutes" does NOT help on its own, since a sliding window still catches every
		// request that lands within 300s of another.
		//
		// At S seconds spacing, any 300s sliding window holds at most floor(300/S)+1 requests,
		// so S=25 => ~12 requests/window = 40% of the 30-cap, leaving headroom for other
		// anonymous store-search calls. A full 31-page fetch then takes 31 * 25s ~ 13 min, well
		// inside the hourly cadence (RefreshFrequency below). The backoff stays as a safety net
		// in case the real window is longer than observed.
		private static readonly TimeSpan InterPageDelay = TimeSpan.FromSeconds(25);
		// Back off and retry the SAME page when Steam rate-limits us (HTTP -1), instead of
		// aborting the whole fetch at page 30. The cooldown looks like a ~5-min rolling
		// window, so a few 120s retries give it time to clear and let the final page through.
		// Worst case (all retries exhausted) we give up, resume activations, and try again
		// next cycle — the live set stays stale but the queue is never held forever.
		private const int MaxPageRetries = 4;
		private static readonly TimeSpan PageRetryBackoff = TimeSpan.FromSeconds(120);
		// Per-request timeout. A hung Steam GET used to hold RefreshSemaphore forever, which
		// made every later DoUpdate return silently (no log at all) — indistinguishable from
		// "the catalog never runs". Bailing after this keeps the timer honest.
		private static readonly TimeSpan PerRequestTimeout = TimeSpan.FromSeconds(30);
		// How many pages between progress reports. The fetch is long enough (~60s+) that
		// reporting collected count + ETA every N pages is useful telemetry, and the reports
		// also prove the fetch is actually advancing (vs. silently stuck).
		private const int ProgressReportEvery = 10;

		// data-ds-appid on each search result is the BASE game appID — the same value the
		// plugin posts to /ajaxrequestplaytestaccess/<id> and stores as app.Parent.ID.
		private static readonly Regex AppIDRegex = new("data-ds-appid=\"(\\d+)\"", RegexOptions.CultureInvariant | RegexOptions.Compiled);

		// The URL form verified on 2026-07-17: anonymous, paginated via start=, count=,
		// category1=989 (Playtests). `infinite=1&json=1` returns {success,total_count,results_html}.
		// sort_by MUST be a real deterministic field (Name_ASC). The earlier `sort_by=_ASC`
		// (empty field) made Steam fall back to a non-deterministic order, so paginating by
		// `start` returned overlapping slices — a full fetch collected only ~74% of total_count
		// with unique-per-page declining across the run. Name_ASC partitions cleanly: 100/page,
		// zero overlap between start=0 and start=100 (verified 2026-07-18).
		private const string SearchUrl = "https://store.steampowered.com/search/results/?query&start={0}&count={1}&dynamic_data=&sort_by=Name_ASC&snr=1_7_7_230_150&category1=989&infinite=1&json=1";

		private static HashSet<uint> LivePlaytests = new();
		private static readonly object LiveLock = new();
		internal static bool HasFetched; // True once at least one fetch has replaced the live set
		private static long LastRefreshRequestTicks; // For the early-wake debounce (DateTime.UtcNow.Ticks)

		private static readonly SemaphoreSlim RefreshSemaphore = new(1, 1);
		private static readonly Timer UpdateTimer = new(async _ => await DoUpdate().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);

		internal static void Update() {
			ASF.ArchiLogger.LogGenericInfo($"PlaytestCatalog.Update() called — arming timer (first fetch in {InitialDelay.TotalSeconds:F0}s, then every {RefreshFrequency.TotalMinutes:F0}m)");

			UpdateTimer.Change(InitialDelay, RefreshFrequency);
		}

		// Wake the catalog immediately (debounced to the hourly cadence) so a Beta-app PICS
		// change can be reflected sooner than the next periodic cycle instead of waiting up
		// to an hour. The debounce keeps PICS storms from re-fetching every few minutes.
		internal static void RequestRefresh() {
			long now = DateTime.UtcNow.Ticks;
			long last = Interlocked.Read(ref LastRefreshRequestTicks);

			if (now - last < EarlyWakeDebounce.Ticks) {
				return;
			}

			Interlocked.Exchange(ref LastRefreshRequestTicks, now);
			UpdateTimer.Change(TimeSpan.Zero, RefreshFrequency);
		}

		internal static bool IsLive(uint baseAppID) {
			lock (LiveLock) {
				return LivePlaytests.Contains(baseAppID);
			}
		}

		internal static HashSet<uint> GetLiveSet() {
			lock (LiveLock) {
				return new HashSet<uint>(LivePlaytests);
			}
		}

		// Parse the BASE appIDs out of one page of results_html. Exposed for unit tests
		// (the fetch itself hits the network and is not covered there). Returns null
		// when the page is unusable so the caller can abort the whole fetch cleanly.
		internal static HashSet<uint>? ParsePage(SearchResults? page) {
			if (page == null || page.Success != 1) {
				return null;
			}

			return ParseAppIDs(page.ResultsHtml);
		}

		internal static HashSet<uint> ParseAppIDs(string resultsHtml) {
			HashSet<uint> appIDs = new();

			foreach (Match match in AppIDRegex.Matches(resultsHtml)) {
				if (uint.TryParse(match.Groups[1].Value, out uint appID) && appID > 0) {
					appIDs.Add(appID);
				}
			}

			return appIDs;
		}

		// A fetch is only "complete" (and therefore safe to prune from) when we actually
		// parsed a set close to what Steam reports. Parsing far fewer entries than
		// total_count usually means the store markup changed and we scraped nothing — in
		// that case we must NOT replace the live set or prune, otherwise a markup change
		// would wipe RequestedPlaytests and re-request the entire catalog.
		internal static bool IsFetchComplete(HashSet<uint> fetched, int totalCount) {
			if (fetched.Count == 0 || totalCount <= 0) {
				return false;
			}

			if (fetched.Count < totalCount / 2) {
				return false;
			}

			return true;
		}

		private static async Task DoUpdate() {
			HashSet<uint> fetched = new();
			int totalCount = 0;
			int totalPages = 0;
			bool failed = false;
			Stopwatch stopwatch = Stopwatch.StartNew();

			try {
				ArgumentNullException.ThrowIfNull(ASF.WebBrowser);

				// Acquire the semaphore exactly ONCE. SemaphoreSlim is non-reentrant: a second
				// WaitAsync(0) from the same caller while it already holds the slot returns
				// false. An earlier version of this method had a duplicate acquisition (left over
				// from wrapping the body in an outer try/catch) — the second WaitAsync(0)
				// returned false, the method returned here, the inner finally that releases the
				// semaphore never ran, the slot was leaked, and every later DoUpdate silently
				// bailed at this same check. Net effect: zero catalog logs, no activation pause,
				// and the persisted queue drained as the 401 storm we were trying to fix.
				if (!await RefreshSemaphore.WaitAsync(0).ConfigureAwait(false)) {
					return;
				}

				// Hold PLAYTEST activations for the duration of the fetch: the persisted playtest
				// queue is stale until the live set is refreshed, and claiming from it is the 401
				// storm. Apps/subs keep activating normally. The log is inside the inner try so the
				// inner finally (Resume) is guaranteed to run once we've paused, even if the log
				// line itself ever throws.
				PackageHandler.PausePlaytestActivations();

				try {
					ASF.ArchiLogger.LogGenericInfo("PlaytestCatalog fetch beginning — playtest activations paused");

					int start = 0;
					for (int page = 0; page < MaxPages && !failed; page++) {
						// We know the list length only after the first page; once we do, stop
						// before walking past it (the last page returns a partial result set).
						if (page > 0) {
							if (totalCount > 0 && start >= totalCount) {
								break;
							}

							await Task.Delay(InterPageDelay).ConfigureAwait(false);
						}

						Uri request = new(string.Format(SearchUrl, start, PageSize));
						ObjectResponse<SearchResults>? response = null;
						HashSet<uint>? pageAppIDs = null;

						// Retry the SAME page on a rate-limit/timeout instead of aborting the whole
						// fetch. The anonymous-search burst cap (~30 requests) means the final
						// page of a full fetch will be rejected once; backing off lets the rolling
						// window clear so that page comes through and the fetch completes.
						for (int attempt = 0; attempt <= MaxPageRetries && pageAppIDs == null; attempt++) {
							if (attempt > 0) {
								ASF.ArchiLogger.LogGenericWarning($"PlaytestCatalog rate-limited at page {page} (start={start}); backing off {PageRetryBackoff.TotalSeconds:F0}s (retry {attempt}/{MaxPageRetries})");
								await Task.Delay(PageRetryBackoff).ConfigureAwait(false);
							}

							Task<ObjectResponse<SearchResults>?> getTask = ASF.WebBrowser.UrlGetToJsonObject<SearchResults>(request);
							Task delayTask = Task.Delay(PerRequestTimeout);

							// Don't let a single hung Steam GET hold RefreshSemaphore forever — that
							// would silently skip every later DoUpdate and produce no logs at all.
							if (await Task.WhenAny(getTask, delayTask).ConfigureAwait(false) == delayTask) {
								ASF.ArchiLogger.LogGenericError($"PlaytestCatalog fetch timed out at page {page} (start={start}) after {PerRequestTimeout.TotalSeconds:F0}s");

								continue;
							}

							response = await getTask.ConfigureAwait(false);
							pageAppIDs = ParsePage(response?.Content);

							if (pageAppIDs == null) {
								int statusCode = response == null ? -1 : (int) response.StatusCode;
								ASF.ArchiLogger.LogGenericWarning($"PlaytestCatalog got HTTP {statusCode} at page {page} (start={start})");
							}
						}

						if (pageAppIDs == null) {
							ASF.ArchiLogger.LogGenericError($"PlaytestCatalog fetch failed at page {page} (start={start}) after {MaxPageRetries + 1} attempt(s) — giving up");

							failed = true;
							break;
						}

						if (page == 0) {
							totalCount = response!.Content!.TotalCount;

							if (totalCount <= 0) {
								ASF.ArchiLogger.LogGenericError("PlaytestCatalog fetch failed: total_count was 0");

								failed = true;
								break;
							}

							totalPages = (totalCount + PageSize - 1) / PageSize;
							ASF.ArchiLogger.LogGenericInfo($"PlaytestCatalog fetch started: total_count={totalCount} across {totalPages} page(s)");
						}

						int before = fetched.Count;
						fetched.UnionWith(pageAppIDs);

						// Advance by the ACTUAL yield, not PageSize: Steam caps count at 100
						// regardless of what we request, and the last page returns fewer. Walking
						// by what we actually got means we never skip entries even if count were
						// silently clamped, and we stop cleanly when a page comes back empty.
						start += pageAppIDs.Count;

						// No new entries, or an empty page, means we've walked past the end.
						if (pageAppIDs.Count == 0 || fetched.Count == before) {
							break;
						}

						if ((page + 1) % ProgressReportEvery == 0) {
							double elapsedS = stopwatch.Elapsed.TotalSeconds;
							double avgPerPage = elapsedS / (page + 1);
							int remainingPages = Math.Max(0, totalPages - (page + 1));
							double etaS = remainingPages * avgPerPage;

							ASF.ArchiLogger.LogGenericInfo($"PlaytestCatalog fetch progress: page {page + 1}/{totalPages} — {fetched.Count} appID(s) collected, {elapsedS:F0}s elapsed, ~{etaS:F0}s remaining");
						}
					}
				} finally {
					RefreshSemaphore.Release();
					PackageHandler.ResumePlaytestActivations();
					ASF.ArchiLogger.LogGenericInfo("PlaytestCatalog fetch finished — playtest activations resumed");
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				failed = true;
			}

			// A failed or incomplete fetch keeps the previous live set and MUST NOT prune.
			// See IsFetchComplete for the markup-change safety net.
			if (failed || !IsFetchComplete(fetched, totalCount)) {
				int previousCount;
				lock (LiveLock) {
					previousCount = LivePlaytests.Count;
				}

				ASF.ArchiLogger.LogGenericError($"PlaytestCatalog refresh did not complete cleanly (failed={failed}, fetched={fetched.Count}, total_count={totalCount}); keeping previous live set of {previousCount} playtest(s)");

				return;
			}

			HashSet<uint> snapshot;
			lock (LiveLock) {
				LivePlaytests = fetched;
				HasFetched = true;
				snapshot = new HashSet<uint>(fetched);
			}

			ASF.ArchiLogger.LogGenericInfo($"PlaytestCatalog fetched {snapshot.Count} live playtest(s) across {totalPages} page(s) in {stopwatch.Elapsed.TotalSeconds:F0}s (total_count={totalCount})");

			// Fan out the prune + proactive enqueue off the timer thread.
			Utilities.InBackground(() => PackageHandler.OnPlaytestCatalogUpdated(snapshot));
		}

		// Mirrors the Json.cs pattern: private init + JsonConstructor so ASF's source-gen
		// serializer populates these from the anonymous search response.
		internal sealed class SearchResults {
			[JsonInclude]
			[JsonPropertyName("success")]
			[JsonRequired]
			internal int Success { get; private init; } = 0;

			[JsonInclude]
			[JsonPropertyName("total_count")]
			[JsonRequired]
			internal int TotalCount { get; private init; } = 0;

			[JsonInclude]
			[JsonPropertyName("results_html")]
			[JsonRequired]
			internal string ResultsHtml { get; private init; } = "";

			[JsonConstructor]
			internal SearchResults() { }

			// For unit tests: the private init setters are only reachable from within the
			// type, so tests build instances through this constructor instead of an
			// object initializer. The serializer uses the parameterless one above.
			internal SearchResults(int success, int totalCount, string resultsHtml) {
				Success = success;
				TotalCount = totalCount;
				ResultsHtml = resultsHtml;
			}
		}
	}
}