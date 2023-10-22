using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace FreePackages {
	internal sealed class PackageFilter {
		private readonly Bot Bot;
		private readonly BotCache BotCache;
		internal readonly FilterConfig FilterConfig;
		private HashSet<uint>? OwnedAppIDs = null;
		private UserData? UserData = null;
		private HashSet<uint> ImportedIgnoredAppIDs = new();
		private HashSet<uint> ImportedIgnoredTags = new();
		private HashSet<uint> ImportedIgnoredContentDescriptors = new();
		private string? Country = null;
		private readonly Timer UserDataRefreshTimer;
		internal bool Ready { get { return OwnedAppIDs != null && Country != null && UserData != null; }}

		internal PackageFilter(Bot bot, BotCache botCache, FilterConfig filterConfig) {
			Bot = bot;
			BotCache = botCache;
			FilterConfig = filterConfig;
			UserDataRefreshTimer = new Timer(async e => await UpdateUserData().ConfigureAwait(false), null, Timeout.Infinite, Timeout.Infinite);
		}

		internal void UpdateAccountInfo(SteamUser.AccountInfoCallback callback) {
			Country = callback.Country;
		}
		
		internal async Task UpdateUserData() {
			if (!Bot.IsConnectedAndLoggedOn) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

				return;
			}

			UserData? userData = await WebRequest.GetUserData(Bot).ConfigureAwait(false);
			if (userData == null) {
				UserDataRefreshTimer.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));
				Bot.ArchiLogger.LogGenericError(String.Format(Strings.ErrorObjectIsNull, userData));

				return;
			}

			if (FilterConfig.ImportStoreFilters) {
				ImportedIgnoredAppIDs = userData.IgnoredApps.Where(x => x.Value == 0).Select(x => x.Key).ToHashSet();
				ImportedIgnoredTags = userData.ExcludedTags.Select(x => x.TagID).ToHashSet();
				ImportedIgnoredContentDescriptors = userData.ExcludedContentDescriptorIDs;
			}

			// Get all of the apps that are in each of the owned packages, and merge with explicitly owned apps
			var ownedAppIDs = userData.OwnedApps;
			var ownedPackageIDs = userData.OwnedPackages;
			ownedAppIDs.UnionWith(ASF.GlobalDatabase!.PackagesDataReadOnly.Where(x => ownedPackageIDs.Contains(x.Key) && x.Value.AppIDs != null).SelectMany(x => x.Value.AppIDs!).ToHashSet<uint>());

			OwnedAppIDs = ownedAppIDs;
			UserData = userData;
			UserDataRefreshTimer.Change(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(15));
		}

		internal static bool IsFreeApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			KeyValue kv = app.KeyValues;

			if (kv["extended"]["isfreeapp"].AsBoolean()) {
				return true;
			}

			EAppType type = kv["common"]["type"].AsEnum<EAppType>();

			if (type == EAppType.Demo) {
				return true;
			}

			// Playtest
			if (type == EAppType.Beta) {
				return true;
			}

			return false;
		}

		internal static bool IsAvailableApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			KeyValue kv = app.KeyValues;

			string? releaseState = kv["common"]["releasestate"].AsString();
			if (releaseState != "released") {
				// App not released yet
				// Note: There's another seemingly relevant field: kv["common"]["steam_release_date"] 
				// steam_release_date is not checked because an app can be "released", still have a future release date, and still be redeemed
				// Example: https://steamdb.info/changelist/20505012/
				return false;
			}

			return true;
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

			if (OwnedAppIDs.Contains(app.ProductInfo.ID)) {
				// Already own this app
				return false;
			}

			KeyValue kv = app.ProductInfo.KeyValues;

			uint mustOwnAppToPurchase = kv["extended"]["mustownapptopurchase"].AsUnsignedInteger();
			if (mustOwnAppToPurchase > 0 && !OwnedAppIDs.Contains(mustOwnAppToPurchase)) {
				// Missing a necessary app
				return false;
			}

			string? restrictedCountries = kv["common"]["restricted_countries"].AsString();
			if (restrictedCountries != null && restrictedCountries.ToUpper().Split(",").Contains(Country.ToUpper())) {
				// App is restricted in this bot's country
				return false;
			}

			string? purchaseRestrictedCountries = kv["extended"]["purchaserestrictedcountries"].AsString();
			if (purchaseRestrictedCountries != null) {
				bool isPurchaseRestricted = purchaseRestrictedCountries.ToUpper().Split(" ").Contains(Country.ToUpper());
				bool onlyAllowPurchaseFromRestricted = kv["extended"]["allowpurchasefromrestrictedcountries"].AsBoolean();
				if (isPurchaseRestricted != onlyAllowPurchaseFromRestricted) {
					// App is purchase restricted in this bot's country
					return false;
				}
			}

			return true;
		}

		internal bool IsWantedApp(FilterableApp app) {
			KeyValue kv = app.ProductInfo.KeyValues;

			if (FilterConfig.Types.Count > 0) {
				if (!FilterConfig.Types.Contains(app.Type.ToString())) {
					// App isn't a wanted type
					return false;
				}
			}

			if (FilterConfig.Categories.Count > 0) {
				bool has_matching_category = kv["common"]["category"].Children.Any(category => UInt32.TryParse(category.Name?.Substring(9), out uint category_number) && FilterConfig.Categories.Contains(category_number)); // category numbers are stored in the name as "category_##"
				if (!has_matching_category) {
					// Unwanted due to missing categories
					return false;
				}
			}

			if (FilterConfig.Tags.Count > 0) {
				bool has_matching_tag = kv["common"]["store_tags"].Children.Any(tag => FilterConfig.Tags.Contains(tag.AsUnsignedInteger()));
				bool parent_has_matching_tags = app.Parent != null && app.Parent.ProductInfo.KeyValues["common"]["store_tags"].Children.Any(tag => FilterConfig.Tags.Contains(tag.AsUnsignedInteger()));
				if (!has_matching_tag && !parent_has_matching_tags) {
					// Unwanted due to missing tags (also check parent app, because parents can have more tags defined)
					return false;
				}
			}

			if (FilterConfig.MinReviewScore > 0 && app.Type != EAppType.Demo && app.Type != EAppType.Beta) {
				// Not including demos and playtests here because they don't really have review scores.  They can, but only from abnormal behavior
				uint review_score = kv["common"]["review_score"].AsUnsignedInteger();
				if (review_score < FilterConfig.MinReviewScore) {
					// Unwanted due to low or missing review score
					return false;
				}
			}

			if (FilterConfig.Languages.Count > 0) {
				bool has_matching_language = kv["common"]["supported_languges"].Children.Any(supported_language => supported_language.Name != null && FilterConfig.Languages.Contains(supported_language.Name));
				if (!has_matching_language) {
					// Unwanted due to missing supported language
					return false;
				}
			}

			return true;
		}

		internal bool IsIgnoredApp(FilterableApp app) {
			KeyValue kv = app.ProductInfo.KeyValues;

			if (FilterConfig.IgnoredTypes.Contains(app.Type.ToString())) {
				// App is an unwanted type
				return true;
			}

			if (FilterConfig.IgnoredTags.Count > 0 || ImportedIgnoredTags.Count > 0) {
				bool has_matching_tag = kv["common"]["store_tags"].Children.Any(tag => FilterConfig.IgnoredTags.Contains(tag.AsUnsignedInteger()) || ImportedIgnoredTags.Contains(tag.AsUnsignedInteger()));
				bool parent_has_matching_tags = app.Parent != null && app.Parent.ProductInfo.KeyValues["common"]["store_tags"].Children.Any(tag => FilterConfig.Tags.Contains(tag.AsUnsignedInteger()) || ImportedIgnoredTags.Contains(tag.AsUnsignedInteger()));
				if (has_matching_tag || parent_has_matching_tags) {
					// App contains an unwanted tag (also check parent app, because parents can have more tags defined)
					return true;
				}
			}

			if (FilterConfig.IgnoredCategories.Count > 0) {
				bool has_matching_category = kv["common"]["category"].Children.Any(category => UInt32.TryParse(category.Name?.Substring(9), out uint category_number) && FilterConfig.IgnoredCategories.Contains(category_number)); // category numbers are stored in the name as "category_##"
				if (has_matching_category) {
					// App contains unwanted categories
					return true;
				}
			}

			if (FilterConfig.IgnoredContentDescriptors.Count > 0 || ImportedIgnoredContentDescriptors.Count > 0) {
				bool has_matching_mature_content_descriptor = kv["common"]["content_descriptors"].Children.Any(content_descriptor => FilterConfig.IgnoredContentDescriptors.Contains(content_descriptor.AsUnsignedInteger()) || ImportedIgnoredContentDescriptors.Contains(content_descriptor.AsUnsignedInteger()));
				bool parent_has_matching_mature_content_descriptor = app.Parent != null && app.Parent.ProductInfo.KeyValues["common"]["content_descriptors"].Children.Any(content_descriptor => FilterConfig.IgnoredContentDescriptors.Contains(content_descriptor.AsUnsignedInteger()) || ImportedIgnoredContentDescriptors.Contains(content_descriptor.AsUnsignedInteger()));
				if (has_matching_mature_content_descriptor || parent_has_matching_mature_content_descriptor) {
					// App contains an unwanted content descriptor (also check parent app, because parents can have more descriptors defined)
					return true;
				}
			}

			if (FilterConfig.IgnoredAppIDs.Contains(app.ProductInfo.ID) || ImportedIgnoredAppIDs.Contains(app.ProductInfo.ID) || (app.Parent != null && (FilterConfig.IgnoredAppIDs.Contains(app.Parent.ProductInfo.ID) || ImportedIgnoredAppIDs.Contains(app.Parent.ProductInfo.ID)))) {
				// App is explicity ignored
				return true;
			}

			return false;
		}

		internal static bool IsFreePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package) {
			KeyValue kv = package.KeyValues;

			var billingType = (EBillingType) kv["billingtype"].AsInteger();
			if (billingType == EBillingType.FreeOnDemand || billingType == EBillingType.NoCost) {
				return true;
			}

			return false;
		}

		internal static bool IsAvailablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package) {
			KeyValue kv = package.KeyValues;			

			if (kv["appids"].Children.Count == 0) {
				// Package has no apps
				return false;
			}

			if ((EPackageStatus) kv["status"].AsInteger() != EPackageStatus.Available) {
				// Package is unavailable
				return false;
			}

			if ((ELicenseType) kv["licensetype"].AsInteger() != ELicenseType.SinglePurchase) {
				// Wrong license type
				return false;
			}

			var expiryTime = kv["extended"]["expirytime"].AsUnsignedLong();
			var now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);
			if (expiryTime > 0 && expiryTime < now) {
				// Package was only available for a limited time and is no longer available
				return false;
			}
			
			if (kv["extended"]["deactivated_demo"].AsBoolean()) {
				// Demo package has been disabled
				return false;
			}

			return true;
		}

		internal static bool IsAvailablePackageContents(FilterablePackage package) {
			KeyValue kv = package.ProductInfo.KeyValues;

			if (kv["appids"].Children.Count != package.PackageContents.Count()) {
				// Could not find all of the apps for this package
				return false;
			}

			if (package.PackageContents.Any(app => !IsAvailableApp(app.ProductInfo))) {
				// At least one of the apps in this package isn't available
				return false;
			}

			return true;
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

			if (UserData.OwnedPackages.Contains(package.ProductInfo.ID)) {
				// Already own this package
				return false;
			}

			if (package.PackageContents.All(x => OwnedAppIDs.Contains(x.ProductInfo.ID))) {
				// Already own all of the apps in this package
				return false;
			}

			KeyValue kv = package.ProductInfo.KeyValues;

			uint dontGrantIfAppidOwned = kv["extended"]["dontgrantifappidowned"].AsUnsignedInteger();
			if (dontGrantIfAppidOwned > 0 && OwnedAppIDs.Contains(dontGrantIfAppidOwned)) {
				// Don't own required app
				return false;
			}

			string? restrictedCountries = kv["extended"]["restrictedcountries"].AsString();
			if (restrictedCountries != null) {
				bool isRestricted = restrictedCountries.ToUpper().Split(" ").Contains(Country.ToUpper());
				bool onlyAllowRestricted = kv["extended"]["onlyallowrestrictedcountries"].AsBoolean();
				if (isRestricted != onlyAllowRestricted) {
					// Package is restricted in this bot's country
					return false;
				}
			}

			string? purchaseRestrictedCountries = kv["extended"]["purchaserestrictedcountries"].AsString();
			if (purchaseRestrictedCountries != null) {
				bool isPurchaseRestricted = purchaseRestrictedCountries.ToUpper().Split(" ").Contains(Country.ToUpper());
				bool onlyAllowPurchaseFromRestricted = kv["extended"]["allowpurchasefromrestrictedcountries"].AsBoolean();
				if (isPurchaseRestricted != onlyAllowPurchaseFromRestricted) {
					// Package is purchase restricted in this bot's country
					return false;
				}
			}

			if (package.PackageContents.Any(app => !OwnedAppIDs.Contains(app.ProductInfo.ID) && !IsRedeemableApp(app))) {
				// At least one of the unowned apps in this package isn't redeemable
				return false;
			}

			return true;
		}

		internal bool IsWantedPackage(FilterablePackage package) {
			KeyValue kv = package.ProductInfo.KeyValues;

			bool has_wanted_app = package.PackageContents.Any(app => IsWantedApp(app));
			if (!has_wanted_app) {
				return false;
			}

			return true;
		}

		internal bool IsIgnoredPackage(FilterablePackage package) {
			KeyValue kv = package.ProductInfo.KeyValues;

			if (FilterConfig.IgnoreFreeWeekends && kv["extended"]["freeweekend"].AsBoolean()) {
				return true;
			}

			bool has_ignored_app = package.PackageContents.Any(app => IsIgnoredApp(app));
			if (has_ignored_app) {
				return true;
			}

			return false;
		}

		internal bool IsRedeemablePlaytest(FilterableApp app) {
			// More than half of playtests we try to join will be invalid.
			// Some of these will be becase there's no free packages (which we can't determine here), Ex: playtest is activated by key: https://steamdb.info/sub/858277/
			// For most, There seems to be no difference at all between invalid playtest and valid ones.

			if (app.Parent == null) {
				return false;
			}

			if (!IsRedeemableApp(app)) {
				return false;
			}

			KeyValue parentKv = app.Parent.ProductInfo.KeyValues;
			if (app.Parent.ProductInfo.MissingToken && parentKv["common"] == KeyValue.Invalid) {
				// Hidden app
				return false;
			}
			
			return true;
		}

		internal bool IsWantedPlaytest(FilterableApp app) {
			if (app.Parent == null) {
				return false;
			}

			if (!IsWantedApp(app)) {
				return false;
			}

			if (FilterConfig.PlaytestMode == EPlaytestMode.None) {
				// User doesnt want any playtests
				return false;
			}

			KeyValue kv = app.ProductInfo.KeyValues;
			uint playtestType = kv["extended"]["playtest_type"].AsUnsignedInteger();

			// playtest_type 0 or missing = limited
			bool wantsLimitedPlaytests = (FilterConfig.PlaytestMode & EPlaytestMode.Limited) == EPlaytestMode.Limited;
			if (playtestType == 0 && !wantsLimitedPlaytests) {
				// User doesn't want limited playtests
				return false;
			}

			// playtest_type 1 = unlimited
			bool wantsUnlimitedPlaytests = (FilterConfig.PlaytestMode & EPlaytestMode.Unlimited) == EPlaytestMode.Unlimited;
			if (playtestType == 1 && !wantsUnlimitedPlaytests) {
				// User doesn't want unlimited playtests
				return false;
			}

			if (BotCache.WaitlistedPlaytests.Contains(app.Parent.ProductInfo.ID)) {
				// Unwanted because we're already on the waitlist for this playtest
				return false;
			}

			return true;
		}
	}
}