using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Core;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageFilter {
		private readonly BotCache BotCache;
		internal readonly List<FilterConfig> FilterConfigs;
		private HashSet<uint>? OwnedAppIDs = null;
		private UserData? UserData = null;
		private HashSet<uint> ImportedIgnoredAppIDs = new();
		private HashSet<uint> ImportedIgnoredTags = new();
		private HashSet<uint> ImportedIgnoredContentDescriptors = new();
		internal string? Country = null;
		internal bool Ready { get { return OwnedAppIDs != null && Country != null && UserData != null; }}

		internal PackageFilter(BotCache botCache, List<FilterConfig> filterConfigs) {
			BotCache = botCache;
			FilterConfigs = filterConfigs;
		}

		internal void UpdateAccountInfo(SteamUser.AccountInfoCallback callback) {
			Country = callback.Country;
		}
		
		internal void UpdateUserData(UserData userData) {
			UserData = userData;
			ImportedIgnoredAppIDs = UserData.IgnoredApps.Where(x => x.Value == 0).Select(x => x.Key).ToHashSet();
			ImportedIgnoredTags = UserData.ExcludedTags.Select(x => x.TagID).ToHashSet();
			ImportedIgnoredContentDescriptors = UserData.ExcludedContentDescriptorIDs;
			OwnedAppIDs = UserData.OwnedApps;

			// Get all of the apps that are in each of the owned packages, and merge with explicitly owned apps
			if (ASF.GlobalDatabase != null) {
				var ownedPackageIDs = UserData.OwnedPackages;
				OwnedAppIDs.UnionWith(ASF.GlobalDatabase.PackagesDataReadOnly.Where(x => ownedPackageIDs.Contains(x.Key) && x.Value.AppIDs != null).SelectMany(x => x.Value.AppIDs!).ToHashSet<uint>());
			}
		}

		internal bool IsRedeemableApp(FilterableApp app) {
			if (OwnedAppIDs == null) {
				throw new InvalidOperationException(nameof(OwnedAppIDs));
			}

			if (Country == null) {
				throw new InvalidOperationException(nameof(Country));
			}

			// It's impossible to tell for certain if an app is redeemable by this account with the information we have here
			// For an app to be redeemable it needs a package that's also redeemable, but we can't see which packages grant an app
			// Some examples: Deactivated demo: https://steamdb.info/app/1316010
			// App isn't region locked but with package that is: https://steamdb.info/app/2147450
			// Free games, but that can only be obtained from bundles with non-free games: https://steamdb.info/app/2119270/ https://steamdb.info/bundle/30994/

			if (OwnedAppIDs.Contains(app.ID)) {
				// Already own this app
				return false;
			}

			if (app.MustOwnAppToPurchase > 0 && !OwnedAppIDs.Contains(app.MustOwnAppToPurchase)) {
				// Missing a necessary app
				return false;
			}

			if (app.RestrictedCountries != null && app.RestrictedCountries.Contains(Country, StringComparer.OrdinalIgnoreCase)) {
				// App is restricted in this bot's country
				return false;
			}

			if (app.PurchaseRestrictedCountries != null) {
				bool isPurchaseRestricted = app.PurchaseRestrictedCountries.Contains(Country, StringComparer.OrdinalIgnoreCase);
				if (isPurchaseRestricted != app.AllowPurchaseFromRestrictedCountries) {
					// App is purchase restricted in this bot's country
					return false;
				}
			}

			return true;
		}

		internal bool IsAppWantedByFilter(FilterableApp app, FilterConfig filter) {
			if (filter.Types.Count > 0 && app.Type != EAppType.Beta && !app.HasType(filter.Types)) {
				// Don't require user to specify they want playtests (Beta), this is already implied by the PlaytestMode filter
				// App isn't a wanted type
				return false;
			}

			if (filter.Categories.Count > 0 && !app.HasCategory(filter.Categories)) {
				// Unwanted due to missing categories
				return false;
			}

			if (filter.Tags.Count > 0 && !app.HasTag(filter.Tags)) {
				// Unwanted due to missing tags (also check parent app, because parents can have more tags defined)
				return false;
			}

			if (filter.MinReviewScore > 0 && app.ReviewScore < filter.MinReviewScore && app.Type != EAppType.Demo && app.Type != EAppType.Beta) {
				// Not including demos and playtests here because they don't really have review scores.  They can, but only from abnormal behavior
				// Unwanted due to low or missing review score
				return false;
			}

			if (filter.Languages.Count > 0 && !app.HasLanguage(filter.Languages)) {
				// Unwanted due to missing supported language
				return false;
			}

			return true;
		}

		internal bool IsAppIgnoredByFilter(FilterableApp app, FilterConfig filter) {
			if (UserData == null) {
				throw new InvalidOperationException(nameof(UserData));
			}

			if (app.HasType(filter.IgnoredTypes)) {
				// App is an unwanted type
				return true;
			}

			if (app.HasTag(filter.IgnoredTags)) {
				// App contains an unwanted tag
				return true;
			}

			if (app.HasCategory(filter.IgnoredCategories)) {
				// App contains unwanted categories
				return true;
			}

			if (app.HasContentDescriptor(filter.IgnoredContentDescriptors)) {
				// App contains an unwanted content descriptor (also check parent app, because parents can have more descriptors defined)
				return true;
			}

			if (app.HasID(filter.IgnoredAppIDs)) {
				// App is explicity ignored
				return true;
			}

			if (filter.ImportStoreFilters) {
				if (app.HasTag(ImportedIgnoredTags)) {
					// App contains a tag which the user has ignored on Steam
					return true;
				}

				if (app.HasContentDescriptor(ImportedIgnoredContentDescriptors)) {
					// App contains a content descriptor which the user has ignored on Steam
					return true;
				}

				if (app.HasID(ImportedIgnoredAppIDs)) {
					// User has ignored this app on Steam
					return true;
				}
			}

			return false;
		}

		internal bool IsRedeemablePackage(FilterablePackage package) {			
			if (UserData == null) {
				throw new InvalidOperationException(nameof(UserData));
			}

			if (OwnedAppIDs == null) {
				throw new InvalidOperationException(nameof(OwnedAppIDs));
			}

			if (Country == null) {
				throw new InvalidOperationException(nameof(Country));
			}

			if (UserData.OwnedPackages.Contains(package.ID)) {
				// Already own this package
				return false;
			}

			if (package.PackageContents.All(x => OwnedAppIDs.Contains(x.ID))) {
				// Already own all of the apps in this package
				return false;
			}

			if (package.DontGrantIfAppIDOwned > 0 && OwnedAppIDs.Contains(package.DontGrantIfAppIDOwned)) {
				// Don't own required app
				return false;
			}

			if (package.RestrictedCountries != null) {
				bool isRestricted = package.RestrictedCountries.Contains(Country, StringComparer.OrdinalIgnoreCase);
				if (isRestricted != package.OnlyAllowRestrictedCountries) {
					// Package is restricted in this bot's country
					return false;
				}
			}

			if (package.PurchaseRestrictedCountries != null) {
				bool isPurchaseRestricted = package.PurchaseRestrictedCountries.Contains(Country, StringComparer.OrdinalIgnoreCase);
				if (isPurchaseRestricted != package.AllowPurchaseFromRestrictedCountries) {
					// Package is purchase restricted in this bot's country
					return false;
				}
			}

			if (package.PackageContents.Any(app => !OwnedAppIDs.Contains(app.ID) && !IsRedeemableApp(app))) {
				// At least one of the unowned apps in this package isn't redeemable
				return false;
			}

			return true;
		}

		internal bool IsPackageWantedByFilter(FilterablePackage package, FilterConfig filter) {
			bool hasWantedApp = package.PackageContents.Any(app => IsAppWantedByFilter(app, filter));
			if (!hasWantedApp) {
				return false;
			}

			return true;
		}

		internal bool IsPackageIgnoredByFilter(FilterablePackage package, FilterConfig filter) {
			if (filter.IgnoreFreeWeekends && package.FreeWeekend) {
				return true;
			}

			bool hasIgnoredApp = package.PackageContents.Any(app => IsAppIgnoredByFilter(app, filter));
			if (hasIgnoredApp) {
				return true;
			}

			return false;
		}

		internal bool IsRedeemablePlaytest(FilterableApp app) {
			// More than half of playtests we try to join will be invalid.
			// Some of these will be becase there's no free packages (which we can't determine here), Ex: playtest is activated by key: https://steamdb.info/sub/858277/
			// For most, There seems to be no difference at all between invalid playtest and valid ones.  The only way to resolve these would be to scrape the parent's store page.

			if (app.Parent == null) {
				return false;
			}

			if (!IsRedeemableApp(app)) {
				return false;
			}

			if (app.Parent.Hidden) {
				// Hidden app
				return false;
			}

			if (BotCache.WaitlistedPlaytests.Contains(app.Parent.ID)) {
				// We're already on the waitlist for this playtest
				return false;
			}
			
			return true;
		}

		internal bool IsPlaytestWantedByFilter(FilterableApp app, FilterConfig filter) {
			if (app.Parent == null) {
				return false;
			}

			if (filter.PlaytestMode == EPlaytestMode.None) {
				// User doesnt want any playtests
				return false;
			}

			if (!IsAppWantedByFilter(app, filter)) {
				return false;
			}

			// playtest_type 0 = limited (default)
			bool wantsLimitedPlaytests = (filter.PlaytestMode & EPlaytestMode.Limited) == EPlaytestMode.Limited;
			if (app.PlayTestType == 0 && !wantsLimitedPlaytests) {
				// User doesn't want limited playtests
				return false;
			}

			// playtest_type 1 = unlimited
			bool wantsUnlimitedPlaytests = (filter.PlaytestMode & EPlaytestMode.Unlimited) == EPlaytestMode.Unlimited;
			if (app.PlayTestType == 1 && !wantsUnlimitedPlaytests) {
				// User doesn't want unlimited playtests
				return false;
			}

			return true;
		}

		internal bool IsWantedApp(FilterableApp app) => FilterConfigs.Count == 0 || FilterConfigs.Any(filter => IsAppWantedByFilter(app, filter) && !IsAppIgnoredByFilter(app, filter));
		internal bool IsWantedPackage(FilterablePackage package) => FilterConfigs.Count == 0 || FilterConfigs.Any(filter => IsPackageWantedByFilter(package, filter) && !IsPackageIgnoredByFilter(package, filter));
		internal bool IsWantedPlaytest(FilterableApp app) => FilterConfigs.Count > 0 && FilterConfigs.Any(filter => IsPlaytestWantedByFilter(app, filter) && !IsAppIgnoredByFilter(app, filter));
	}
}
