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

		[JsonProperty(Required = Required.Default)]
		internal uint MinReviewScore = 0;

		[JsonConstructor]
		internal FilterConfig() { }
	}
}