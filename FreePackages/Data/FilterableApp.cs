using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace FreePackages {
	internal sealed class FilterableApp {
		internal FilterableApp? Parent = null;
		internal uint? ParentID = null;

		internal uint ID;
		internal EAppType Type;
		internal bool IsFreeApp;
		internal string? ReleaseState;
		internal uint MustOwnAppToPurchase;
		internal List<string>? RestrictedCountries;
		internal List<string>? PurchaseRestrictedCountries;
		internal bool AllowPurchaseFromRestrictedCountries;
		internal List<uint> AppTags;
		internal List<uint> Category;
		internal List<uint> ContentDescriptors;
		internal List<string> SupportedLanguages;
		internal uint ReviewScore;
		internal string? ListOfDLC;
		internal uint PlayTestType;

		internal bool MissingToken;
		internal bool MissingCommon;

		internal FilterableApp(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo) {
			ID = productInfo.ID;
			KeyValue kv = productInfo.KeyValues;
			Type = kv["common"]["type"].AsEnum<EAppType>();
			IsFreeApp = kv["extended"]["isfreeapp"].AsBoolean();
			ReleaseState = kv["common"]["releasestate"].AsString();
			MustOwnAppToPurchase = kv["extended"]["mustownapptopurchase"].AsUnsignedInteger();
			RestrictedCountries = kv["common"]["restricted_countries"].AsString()?.ToUpper().Split(",").ToList();
			PurchaseRestrictedCountries = kv["extended"]["purchaserestrictedcountries"].AsString()?.ToUpper().Split(" ").ToList();
			AllowPurchaseFromRestrictedCountries = kv["extended"]["allowpurchasefromrestrictedcountries"].AsBoolean();
			AppTags = kv["common"]["store_tags"].Children.Select(tag => tag.AsUnsignedInteger()).ToList();
			Category = kv["common"]["category"].Children.Select(category => UInt32.Parse(category.Name!.Substring(9))).ToList(); // category numbers are stored in the name as "category_##"
			ContentDescriptors = kv["common"]["content_descriptors"].Children.Select(content_descriptor => content_descriptor.AsUnsignedInteger()).ToList();
			SupportedLanguages = kv["common"]["supported_languges"].Children.Select(supported_language => supported_language.Name!).ToList();
			ReviewScore = kv["common"]["review_score"].AsUnsignedInteger();
			ListOfDLC = kv["extended"]["listofdlc"].AsString();
			PlayTestType = kv["extended"]["playtest_type"].AsUnsignedInteger();
			MissingToken = productInfo.MissingToken;
			MissingCommon = kv["common"] == KeyValue.Invalid;

			// I only want the parents for playtests and demos (because they share a store page with their parents and so should inherit some of their parents properties)
			if (Type == EAppType.Beta || Type == EAppType.Demo) {
				uint parentID = 0;
				if (Type == EAppType.Beta) {
					// This is generally less reliable than ["common"]["parent"] (Ex: https://steamdb.info/app/2420490/ on Oct 17 2023 has "parent" and is redeemable, but doesn't have "betaforappid")
					parentID = kv["extended"]["betaforappid"].AsUnsignedInteger();
				}
				if (parentID == 0) {
					parentID = kv["common"]["parent"].AsUnsignedInteger();
				}

				if (parentID > 0) {
					ParentID = parentID;
				}
			}
		}

		internal void AddParent(SteamApps.PICSProductInfoCallback.PICSProductInfo? productInfo) {
			if (productInfo == null) {
				return;
			}

			Parent = new FilterableApp(productInfo);
		}

		internal bool IsFree() {
			if (IsFreeApp) {
				return true;
			}

			if (Type == EAppType.Demo) {
				return true;
			}

			// Playtest
			if (Type == EAppType.Beta) {
				return true;
			}

			return false;
		}

		internal bool IsAvailable() {
			if (ReleaseState != "released") {
				// App not released yet
				// Note: There's another seemingly relevant field: kv["common"]["steam_release_date"] 
				// steam_release_date is not checked because an app can be "released", still have a future release date, and still be redeemed
				// Example: https://steamdb.info/changelist/20505012/
				return false;
			}

			return true;
		}

		internal bool HasID(IEnumerable<uint> ids) {
			if (ids.Count() == 0) {
				return false;
			}

			if (ids.Contains(ID)) {
				return true;
			}

			// Parent IDs are also used for filtering as only playtests and demos have parents right now
			// I figure if someone doesn't want a certain app, then they also don't want the demo or playtest version of that app
			if (Parent != null && ids.Contains(Parent.ID)) {
				return true;
			}

			return false;
		}

		internal bool HasType(IEnumerable<string> types) {
			if (types.Count() == 0) {
				return false;
			}

			return types.Contains(Type.ToString());
		}

		internal bool HasTag(IEnumerable<uint> tags) {
			if (tags.Count() == 0) {
				return false;
			}

			if (AppTags.Any(tag => tags.Contains(tag))) {
				return true;
			}

			// Also check parent app, because parents can have additional tags defined
			if (Parent != null && Parent.AppTags.Any(tag => tags.Contains(tag))) {
				return true;
			}

			return false;
		}

		internal bool HasCategory(IEnumerable<uint> categories) {
			if (categories.Count() == 0) {
				return false;
			}

			if (Category.Any(category => categories.Contains(category))) {
				return true;
			}

			// Only use parent categories if the app has no categories defined. Ex: Tekken 8 playtest (https://steamdb.info/app/2385860/)
			if (Category.Count == 0 && Parent != null && Parent.Category.Any(category => categories.Contains(category))) {
				return true;
			}

			return false;
		}

		internal bool HasContentDescriptor(IEnumerable<uint> content_descriptors) {
			if (content_descriptors.Count() == 0) {
				return false;
			}

			if (ContentDescriptors.Any(content_descriptor => content_descriptors.Contains(content_descriptor))) {
				return true;
			}
			
			// Also check parent app, because parents may have additional descriptors defined
			if (Parent != null && Parent.ContentDescriptors.Any(content_descriptor => content_descriptors.Contains(content_descriptor))) {
				return true;
			}

			return false;
		}

		internal bool HasLanguage(IEnumerable<string> languages) {
			if (languages.Count() == 0) {
				return false;
			}

			if (SupportedLanguages.Any(language => languages.Contains(language))) {
				return true;
			}

			// Only check the parent's languages if the app has no languages of its own
			// It could be that the parent app naturally has more language support, in demos for example (ex: Grounded Demo supports only English while the full release supports more languages https://steamdb.info/app/1316010/ , https://steamcommunity.com/app/962130/discussions/0/2440336502396337163/)
			// Most playtests don't list supported languages, in which case we do want to use the parent's languages (ex: Tekken 8 playtest https://steamdb.info/app/2385860/)
			if (SupportedLanguages.Count == 0 && Parent != null && Parent.SupportedLanguages.Any(language => languages.Contains(language))) {
				return true;
			}

			return false;
		}
	}
}