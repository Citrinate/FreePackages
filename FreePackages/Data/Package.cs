using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FreePackages {
	public sealed class Package {
		[JsonProperty(Required = Required.DisallowNull)]
		public EPackageType Type;
		[JsonProperty(Required = Required.DisallowNull)]
		public uint ID;
		[JsonProperty(Required = Required.AllowNull)]
		public ulong? StartTime = null;
		
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