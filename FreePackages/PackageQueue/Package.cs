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
		public ulong? StartTime { get; private init; } = null;

		[JsonInclude]
		public int? FilterHash { get; set; } = null;

		[JsonInclude]
		public bool IsDemo { get; set; }

		[JsonInclude]
		public byte Priority { get; set; }

		public bool ShouldSerializeStartTime() => StartTime != null;
		public bool ShouldSerializeFilterHash() => FilterHash != null;
		public bool ShouldSerializeIsDemo() => IsDemo;
		public bool ShouldSerializePriority() => Priority != 0;
		
		[JsonConstructor]
		public Package(EPackageType type, uint id, ulong? startTime = null, int? filterHash = null, bool isDemo = false, byte priority = 0) {
			Type = type;
			ID = id;
			StartTime = (startTime > 0) ? startTime : null;
			FilterHash = filterHash;
			IsDemo = isDemo;
			Priority = priority;
		}
	}

	public enum EPackageType {
		App = 0,
		Sub = 1,
		Playtest = 2,
		RemoveSub = 3,
		RemoveApp = 4
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