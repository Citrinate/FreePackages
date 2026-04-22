using System;
using System.Collections.Generic;
using System.Linq;

namespace FreePackages {
	internal static class DeterministicHasher {
		private const int FnvOffsetBias = unchecked((int) 2166136261);
		private const int FnvPrime = 16777619;

		internal static int Hash(int value) => Hash(FnvOffsetBias, value);
		internal static int Hash(uint value) => Hash(FnvOffsetBias, value);
		internal static int Hash(bool value) => Hash(FnvOffsetBias, value);
		internal static int Hash(string? str) => Hash(FnvOffsetBias, str);
		internal static int Hash(IEnumerable<string>? collection) => Hash(FnvOffsetBias, collection);
		internal static int Hash(IEnumerable<uint>? collection) => Hash(FnvOffsetBias, collection);
		internal static int Hash(IEnumerable<FilterConfig>? collection) => Hash(FnvOffsetBias, collection);

		internal static int Hash(int hash, int value) => unchecked((hash ^ value) * FnvPrime);
		internal static int Hash(int hash, uint value) => Hash(hash, (int) value);
		internal static int Hash(int hash, bool value) => Hash(hash, value ? 1 : 0);

		internal static int Hash(int hash, string? str) {
			if (str == null) {
				return hash;
			}

			foreach (char c in str) {
				hash = Hash(hash, c);
			}

			return hash;
		}

		internal static int Hash(int hash, IEnumerable<string>? collection) {
			if (collection == null) {
				return hash;
			}

			foreach (string item in collection.OrderBy(static x => x, StringComparer.Ordinal)) {
				hash = Hash(hash, item);
			}

			return hash;
		}

		internal static int Hash(int hash, IEnumerable<uint>? collection) {
			if (collection == null) {
				return hash;
			}

			foreach (uint item in collection.OrderBy(static x => x)) {
				hash = Hash(hash, item);
			}

			return hash;
		}

		internal static int Hash(int hash, IEnumerable<FilterConfig>? collection) {
			if (collection == null) {
				return hash;
			}

			foreach (FilterConfig item in collection.OrderBy(static x => x.GetHashCode())) {
				hash = Hash(hash, item.GetHashCode());
			}

			return hash;
		}
	}
}
