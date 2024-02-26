using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreePackages {
	internal sealed class FilterConfig {
		[JsonInclude]
		internal bool ImportStoreFilters { get; set; } = false;

		[JsonInclude]
		internal HashSet<string> Types { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> Categories { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> Tags { get; set; } = new();

		[JsonInclude]
		internal HashSet<string> IgnoredTypes { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> IgnoredTags { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> IgnoredCategories { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> IgnoredContentDescriptors { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> IgnoredAppIDs { get; set; } = new();

		[JsonInclude]
		internal bool IgnoreFreeWeekends { get; set; } = false;

		[JsonInclude]
		internal uint MinReviewScore { get; set; } = 0;

		[JsonInclude]
		internal HashSet<string> Languages { get; set; } = new();

		[JsonInclude]
		internal EPlaytestMode PlaytestMode { get; set; } = EPlaytestMode.None;

		[JsonInclude]
		internal bool RequireAllTags { get; set; } = false;
		
		[JsonInclude]
		internal bool RequireAllCategories { get; set; } = false;
		
		[JsonInclude]
		internal bool NoCostOnly { get; set; } = false;

		[JsonInclude]
		internal HashSet<string> Systems { get; set; } = new();

		[JsonInclude]
		internal bool WishlistOnly { get; set; } = false;

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