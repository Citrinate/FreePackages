using System;
using System.Collections.Generic;
using System.Linq;
using SteamKit2;

namespace FreePackages {
	internal sealed class FilterableApp {
		internal SteamApps.PICSProductInfoCallback.PICSProductInfo ProductInfo;
		internal bool IsFree;
		internal bool IsAvailable;
		internal EAppType Type;
		internal FilterableApp? Parent = null;
		internal uint? ParentID = null;

		internal FilterableApp(SteamApps.PICSProductInfoCallback.PICSProductInfo productInfo) {
			ProductInfo = productInfo;
			IsFree = PackageFilter.IsFreeApp(ProductInfo);
			IsAvailable = PackageFilter.IsAvailableApp(ProductInfo);
			Type = ProductInfo.KeyValues["common"]["type"].AsEnum<EAppType>();

			// There's another parentID field for playtests: ["extended"]["betaforappid"], but it's less reliable
			// Ex: https://steamdb.info/app/2420490/ on Oct 17 2023 has "parent" and is redeemable, but doesn't have "betaforappid"
			uint parentID = ProductInfo.KeyValues["common"]["parent"].AsUnsignedInteger();
			// Right now I only want the parents for playtests and demos
			if (parentID > 0 && (Type == EAppType.Beta || Type == EAppType.Demo)) {
				ParentID = parentID;
			}
		}

		internal void AddParent(SteamApps.PICSProductInfoCallback.PICSProductInfo? productInfo) {
			if (productInfo == null) {
				return;
			}

			Parent = new FilterableApp(productInfo);
		}

		internal bool HasID(IEnumerable<uint> ids) {
			if (ids.Count() == 0) {
				return false;
			}

			if (ids.Contains(ProductInfo.ID)) {
				return true;
			}

			// Parent IDs are also used for filtering as only playtests and demos have parents right now
			// I figure if someone doesn't want a certain app, then they also don't want the demo or playtest version of that app
			if (Parent != null && ids.Contains(Parent.ProductInfo.ID)) {
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

			List<uint> app_tags = ProductInfo.KeyValues["common"]["store_tags"].Children.Select(tag => tag.AsUnsignedInteger()).ToList();

			// Also check parent app, because parents can have additional tags defined
			if (Parent != null) {
				app_tags.AddRange(Parent.ProductInfo.KeyValues["common"]["store_tags"].Children.Select(tag => tag.AsUnsignedInteger()).ToList());
			}

			return app_tags.Any(tag => tags.Contains(tag));
		}

		internal bool HasCategory(IEnumerable<uint> categories) {
			if (categories.Count() == 0) {
				return false;
			}

			List<uint> app_categories = new();
			foreach (KeyValue category in ProductInfo.KeyValues["common"]["category"].Children) {
				// category numbers are stored in the name as "category_##"
				if (UInt32.TryParse(category.Name?.Substring(9), out uint category_number)) {
					app_categories.Add(category_number);
				}
			}

			// Only use parent categories if the app has no categories defined. Ex: Tekken 8 playtest (https://steamdb.info/app/2385860/)
			if (app_categories.Count == 0 && Parent != null) {
				foreach (KeyValue category in Parent.ProductInfo.KeyValues["common"]["category"].Children) {
					// category numbers are stored in the name as "category_##"
					if (UInt32.TryParse(category.Name?.Substring(9), out uint category_number)) {
						app_categories.Add(category_number);
					}
				}
			}

			return app_categories.Any(category => categories.Contains(category));
		}

		internal bool HasContentDescriptor(IEnumerable<uint> content_descriptors) {
			if (content_descriptors.Count() == 0) {
				return false;
			}

			List<uint> app_content_descriptors = ProductInfo.KeyValues["common"]["content_descriptors"].Children.Select(content_descriptor => content_descriptor.AsUnsignedInteger()).ToList();
			
			// Also check parent app, because parents can have additional descriptors defined
			if (Parent != null) {
				app_content_descriptors.AddRange(Parent.ProductInfo.KeyValues["common"]["content_descriptors"].Children.Select(content_descriptor => content_descriptor.AsUnsignedInteger()));
			}

			return app_content_descriptors.Any(content_descriptor => content_descriptors.Contains(content_descriptor));
		}

		internal bool HasMinReviewScore(uint min_review_score) {
			return ProductInfo.KeyValues["common"]["review_score"].AsUnsignedInteger() >= min_review_score;
		}

		internal bool HasLanguage(IEnumerable<string> languages) {
			if (languages.Count() == 0) {
				return false;
			}

			List<string?> app_languages = ProductInfo.KeyValues["common"]["supported_languges"].Children.Select(supported_language => supported_language.Name).ToList();

			// Only include the parent's languages is the app has no languages of its own
			// It could be that the parent app naturally has more language support, In demos for example (ex: Grounded Demo supports only English while the full release supports more languages https://steamdb.info/app/1316010/)
			// Most playtests don't list a supported language in which case we do want to use the parent's languages (ex: Tekken 8 playtest https://steamdb.info/app/2385860/)
			if (app_languages.Count == 0 && Parent != null) {
				app_languages.AddRange(Parent.ProductInfo.KeyValues["common"]["supported_languges"].Children.Select(supported_language => supported_language.Name));
			}

			return app_languages.Any(app_language => app_languages.Contains(app_language));
		}
	}
}