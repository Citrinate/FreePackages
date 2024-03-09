using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreePackages {
	public sealed class Package {
		[JsonInclude]
		[JsonRequired]
		public EPackageType Type { get; private init; }

		[JsonInclude]
		[JsonRequired]
		public uint ID { get; private init; }

		[JsonInclude]
		[JsonRequired]
		public ulong? StartTime { get; private init; } = null;
		
		[JsonConstructor]
		public Package(EPackageType type, uint id, ulong? startTime = null) {
			Type = type;
			ID = id;

			if (startTime != null && startTime > 0) {
				StartTime = startTime;
			}
		}
	}

	public enum EPackageType {
		App = 0,
		Sub = 1,
		Playtest = 2
	}

	public class PackageComparer : IEqualityComparer<Package> {
		public bool Equals(Package? x, Package? y) {
			return x?.ID == y?.ID && x?.Type == y?.Type;
		}

		public int GetHashCode(Package obj) {
			return HashCode.Combine(obj.ID, obj.Type);
		}
	}
}