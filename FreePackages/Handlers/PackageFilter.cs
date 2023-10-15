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
		private readonly FilterConfig FilterConfig;
		private HashSet<uint>? OwnedAppIDs = null;
		private UserData? UserData = null;
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
				FilterConfig.IgnoredAppIDs.UnionWith(userData.IgnoredApps.Where(x => x.Value == 0).Select(x => x.Key));
				FilterConfig.IgnoredTags.UnionWith(userData.ExcludedTags.Select(x => x.TagID));
				FilterConfig.IgnoredContentDescriptors.UnionWith(userData.ExcludedContentDescriptorIDs);
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

			string? releaseState = kv["common"]["releasestate"].AsString();
			if (releaseState != "released") {
				// App not released yet
				// Note: There's another seemingly relevant field: kv["common"]["steam_release_date"] 
				// steam_release_date is not checked because an app can be "released", still have a future release date, and still be redeemed
				// Example: https://steamdb.info/changelist/20505012/
				return false;
			}

			if (kv["extended"]["isfreeapp"].AsBoolean()) {
				return true;
			}

			EAppType type = kv["common"]["type"].AsEnum<EAppType>();

			if (type == EAppType.Demo) {
				return true;
			}

			// TODO: Playtest stuff
			// if (type == EAppType.Beta) {
			// 	return true;
			// }

			return false;
		}

		internal bool IsRedeemableApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			if (UserData == null) {
				throw new InvalidOperationException(nameof(UserData));
			}

			if (OwnedAppIDs == null) {
				throw new InvalidOperationException(nameof(OwnedAppIDs));
			}

			if (Country == null) {
				throw new InvalidOperationException(nameof(Country));
			}

			// It's impossible to tell for certain if an app is redeemable with the information we have here
			// For an app to be redeemable it needs a package that's also redeemable, but we can't see which packages grant an app
			// Some examples: Deactivated demo: https://steamdb.info/app/1316010
			// App isn't region locked but with package that is: https://steamdb.info/app/2147450

			if (OwnedAppIDs.Contains(app.ID)) {
				// Already own this app
				return false;
			}

			KeyValue kv = app.KeyValues;

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

		internal bool IsWantedApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			KeyValue kv = app.KeyValues;

			EAppType type = kv["common"]["type"].AsEnum<EAppType>();
			if (!FilterConfig.Types.Contains(type.ToString())) {
				// App isn't a wanted type
				return false;
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
				if (!has_matching_tag) {
					// Unwanted due to missing tags
					return false;
				}
			}

			// TODO: playtest stuff
			// kv["extended"]["betaforappid"] or kv["common"]["parent"]
			// kv["extended"]["playtest_type"]: 0 or undefined = waitlist, 1 = open signup

			return true;
		}

		internal bool IsIgnoredApp(SteamApps.PICSProductInfoCallback.PICSProductInfo app) {
			KeyValue kv = app.KeyValues;

			EAppType type = kv["common"]["type"].AsEnum<EAppType>();
			if (FilterConfig.IgnoredTypes.Contains(type.ToString())) {
				// App is an unwanted type
				return true;
			}

			if (FilterConfig.IgnoredTags.Count > 0) {
				bool has_matching_tag = kv["common"]["store_tags"].Children.Any(tag => FilterConfig.IgnoredTags.Contains(tag.AsUnsignedInteger()));
				if (!has_matching_tag) {
					// App contains an unwanted tag
					return true;
				}
			}

			if (FilterConfig.IgnoredContentDescriptors.Count > 0) {
				bool has_matching_mature_content_descriptor = kv["common"]["content_descriptors"].Children.Any(content_descriptor => FilterConfig.IgnoredContentDescriptors.Contains(content_descriptor.AsUnsignedInteger()));
				if (has_matching_mature_content_descriptor) {
					// App contains an unwanted content descriptor
					return true;
				}
			}

			if (FilterConfig.IgnoredAppIDs.Contains(app.ID)) {
				// App is explicity ignored
				return true;
			}

			return false;
		}

		internal static bool IsFreePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package) {
			KeyValue kv = package.KeyValues;

			var billingType = (EBillingType) kv["billingtype"].AsInteger();
			if (billingType != EBillingType.FreeOnDemand 
				&& billingType != EBillingType.NoCost
			) {
				return false;
			}

			var appIDs = kv["appids"].Children.Select(x => x.AsUnsignedInteger());
			if (appIDs.Count() == 0) {
				return false;
			}

			if ((EPackageStatus) kv["status"].AsInteger() != EPackageStatus.Available) {
				return false;
			}

			if ((ELicenseType) kv["licensetype"].AsInteger() != ELicenseType.SinglePurchase) {
				return false;
			}

			var expiryTime = kv["extended"]["expirytime"].AsUnsignedLong();
			var now = DateUtils.DateTimeToUnixTime(DateTime.UtcNow);
			if (expiryTime > 0 && expiryTime < now) {
				return false;
			}
			
			if (kv["extended"]["deactivated_demo"].AsBoolean()) {
				return false;
			}

			return true;
		}

		internal bool IsRedeemablePackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package, IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> apps) {			
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

			if (apps.All(x => OwnedAppIDs.Contains(x.ID))) {
				// Already own all of the apps in this package
				return false;
			}

			KeyValue kv = package.KeyValues;

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

			return true;
		}

		internal bool IsWantedPackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package, IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> apps) {
			KeyValue kv = package.KeyValues;

			bool has_wanted_app = apps.Any(app => IsWantedApp(app));
			if (!has_wanted_app) {
				return false;
			}

			return true;
		}

		internal bool IsIgnoredPackage(SteamApps.PICSProductInfoCallback.PICSProductInfo package, IEnumerable<SteamApps.PICSProductInfoCallback.PICSProductInfo> apps) {
			KeyValue kv = package.KeyValues;

			if (FilterConfig.IgnoreFreeWeekends && kv["extended"]["freeweekend"].AsBoolean()) {
				return true;
			}

			bool has_ignored_app = apps.Any(app => IsIgnoredApp(app));
			if (has_ignored_app) {
				return true;
			}

			return false;
		}
	}
}