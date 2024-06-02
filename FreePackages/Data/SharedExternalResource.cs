using System;
using System.Threading;
using System.Threading.Tasks;

// This resource may be used zero or more times independently and, when used, needs to be fetched from an external source.
// If it's used zero times we don't fetch it at all.
// If it's used once or more then we only fetch it once.

namespace FreePackages {
	internal sealed class SharedExternalResource<T> {
		private SemaphoreSlim FetchSemaphore = new SemaphoreSlim(1, 1);
		private T? Resource;
		private bool Fetched = false;

		internal SharedExternalResource() {}

		internal async Task<T?> Fetch(Func<Task<T?>> fetchResource) {
			if (Fetched) {
				return Resource;
			}

			await FetchSemaphore.WaitAsync().ConfigureAwait(false);
			try {
				if (Fetched) {
					return Resource;
				}

				Resource = await fetchResource().ConfigureAwait(false);
				Fetched = true;

				return Resource;
			} finally {
				FetchSemaphore.Release();
			}
		}
	}
}
