using System.Collections.Generic;
using Newtonsoft.Json;

namespace FreePackages.IPC {
	public sealed class QueueLicensesRequest {
		[JsonProperty(Required = Required.Default)]
		public HashSet<uint>? AppIDs = null;

		[JsonProperty(Required = Required.Default)]
		public HashSet<uint>? PackageIDs = null;

		[JsonProperty(Required = Required.Default)]
		public bool UseFilter = true;

		[JsonConstructor]
		private QueueLicensesRequest() { }
	}
}