using System.Collections.Generic;
using System.Collections.Immutable;
using Newtonsoft.Json;

namespace FreePackages {
	internal sealed class FilterConfig {
		[JsonProperty(Required = Required.Default)]
		internal bool ImportStoreFilters = false;

		[JsonProperty(Required = Required.Default)]
		internal ImmutableHashSet<string> Types = ImmutableHashSet<string>.Empty;

		[JsonProperty(Required = Required.Default)]
		internal ImmutableHashSet<uint> Categories = ImmutableHashSet<uint>.Empty;

		[JsonProperty(Required = Required.Default)]
		internal ImmutableHashSet<uint> Tags = ImmutableHashSet<uint>.Empty;

		[JsonProperty(Required = Required.Default)]
		internal ImmutableHashSet<string> IgnoredTypes = ImmutableHashSet<string>.Empty;

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredTags = new();

		[JsonProperty(Required = Required.Default)]
		internal ImmutableHashSet<uint> IgnoredCategories = ImmutableHashSet<uint>.Empty;

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredContentDescriptors = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredAppIDs = new();

		[JsonProperty(Required = Required.Default)]
		internal bool IgnoreFreeWeekends = false;

		// [JsonProperty(Required = Required.Default)]
		// internal bool IncludePlaytests = false;

		// TODO: Review score filtering using kv["common"]["review_score"], only applies to: Game, DLC, Application, Music
		// 1: Overwhelmingly nevative (500+ reviews, 0%-19%)
		// 2: Very Negative (50-499 reviews, 0%-19%)
		// 3: Negative (1-49 reviews, 0%-19%)
		// 4: Mostly Negative (1-49 reviews, 20%-39%)
		// 5: Mixed (1-49 reviews, 40%-69%)
		// 6: Mostly Positive (1-49 reviews, 70%-79%)
		// 7: Positive (1-49 reviews, 80%-100%)
		// 8: Very Positive (50-499 reviews, 80%-100%)
		// 9: Overwhelmingly positive (500+ reviews, 95%-100%)

		[JsonConstructor]
		internal FilterConfig() { }
	}
}