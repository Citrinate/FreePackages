using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FreePackages.IPC {
	public sealed class QueueLicensesRequest {
		[JsonInclude]
		public HashSet<uint>? AppIDs { get; private init; } = null;

		[JsonInclude]
		public HashSet<uint>? PackageIDs { get; private init; } = null;

		[JsonInclude]
		public bool UseFilter { get; private init; } = true;

		[JsonConstructor]
		private QueueLicensesRequest() { }
	}
}