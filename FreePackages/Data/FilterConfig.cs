using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FreePackages {
	internal sealed class FilterConfig {
		[JsonProperty(Required = Required.Default)]
		internal bool ImportStoreFilters = false;

		[JsonProperty(Required = Required.Default)]
		internal HashSet<string> Types = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> Categories = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> Tags = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<string> IgnoredTypes = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredTags = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredCategories = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredContentDescriptors = new();

		[JsonProperty(Required = Required.Default)]
		internal HashSet<uint> IgnoredAppIDs = new();

		[JsonProperty(Required = Required.Default)]
		internal bool IgnoreFreeWeekends = false;

		[JsonProperty(Required = Required.Default)]
		internal uint MinReviewScore = 0;

		[JsonProperty(Required = Required.Default)]
		internal HashSet<string> Languages = new();

		[JsonProperty(Required = Required.Default)]
		internal EPlaytestMode PlaytestMode = EPlaytestMode.None;

		[JsonProperty(Required = Required.Default)]
		internal bool RequireAllTags = false;
		
		[JsonProperty(Required = Required.Default)]
		internal bool RequireAllCategories = false;
		
		[JsonProperty(Required = Required.Default)]
		internal bool NoCostOnly = false;

		[JsonProperty(Required = Required.Default)]
		internal HashSet<string> Systems = new();

		[JsonConstructor]
		internal FilterConfig() { }
	}

	[Flags]
	internal enum EPlaytestMode : byte {
		None = 0,
		Unlimited = 1,
		Limited = 2,
		All = Unlimited | Limited
	}
}