using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreePackages {
	internal sealed class FilterConfig : IJsonOnDeserialized {
		[JsonInclude]
		internal bool ImportStoreFilters { get; set; } = false;

		[JsonInclude]
		internal HashSet<string> Types { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> Categories { get; set; } = new();

		[JsonInclude]
		internal HashSet<uint> Tags { get; set; } = new();

		[JsonInclude]
		internal HashSet<string> IgnoredTypes { get; set; } = new() {"Demo"};

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

		[JsonInclude]
		internal uint MinDaysOld { get; set; } = 0; // Not used, only exists as a typo of MaxDaysOld, and is only here to support old configs

		[JsonInclude]
		internal uint MaxDaysOld { get; set; } = 0;

		[JsonConstructor]
		internal FilterConfig() { }

		public void OnDeserialized() {
			// Handles filter config changes made in V1.5.4.10
			if (Types.Contains("Demo") && IgnoredTypes.Contains("Demo")) {
				IgnoredTypes.Remove("Demo");
			}

			// Handles filter config changes made in V1.5.5.0
			if (MaxDaysOld == 0 && MinDaysOld > 0) {
				MaxDaysOld = MinDaysOld;
			}
		}

		public override int GetHashCode() {
			int hash = DeterministicHasher.Hash(ImportStoreFilters);
			hash = DeterministicHasher.Hash(hash, Types);
			hash = DeterministicHasher.Hash(hash, Categories);
			hash = DeterministicHasher.Hash(hash, Tags);
			hash = DeterministicHasher.Hash(hash, IgnoredTypes);
			hash = DeterministicHasher.Hash(hash, IgnoredTags);
			hash = DeterministicHasher.Hash(hash, IgnoredCategories);
			hash = DeterministicHasher.Hash(hash, IgnoredContentDescriptors);
			hash = DeterministicHasher.Hash(hash, IgnoredAppIDs);
			hash = DeterministicHasher.Hash(hash, IgnoreFreeWeekends);
			hash = DeterministicHasher.Hash(hash, MinReviewScore);
			hash = DeterministicHasher.Hash(hash, Languages);
			hash = DeterministicHasher.Hash(hash, (int) PlaytestMode);
			hash = DeterministicHasher.Hash(hash, RequireAllTags);
			hash = DeterministicHasher.Hash(hash, RequireAllCategories);
			hash = DeterministicHasher.Hash(hash, NoCostOnly);
			hash = DeterministicHasher.Hash(hash, Systems);
			hash = DeterministicHasher.Hash(hash, WishlistOnly);
			hash = DeterministicHasher.Hash(hash, MaxDaysOld);

			return hash;
		}
	}

	[Flags]
	internal enum EPlaytestMode : byte {
		None = 0,
		Unlimited = 1,
		Limited = 2,
		All = Unlimited | Limited
	}
}