using System.Linq;
using SteamKit2;

namespace FreePackages {
	internal enum EActivationPriority : byte {
		Unspecified = 0,
		Game = 1,
		LibraryContent = 2,
		Other = 3,
		Demo = 4
	}

	internal static class ActivationPriority {
		internal static byte Rank(Package package) {
			if (package.IsDemo) {
				return (byte) EActivationPriority.Demo;
			}

			return package.Priority == 0 ? (byte) EActivationPriority.Other : package.Priority;
		}

		internal static byte ForApp(FilterableApp app, PackageFilter filter) {
			if (app.Type == EAppType.Demo) {
				return (byte) EActivationPriority.Demo;
			}

			if (app.Type is EAppType.Game or EAppType.Application) {
				return (byte) EActivationPriority.Game;
			}

			if (IsLibraryContent(app, filter)) {
				return (byte) EActivationPriority.LibraryContent;
			}

			return (byte) EActivationPriority.Other;
		}

		internal static byte ForPackage(FilterablePackage package, PackageFilter filter) {
			if ((package.PackageContents.Count > 0) && package.PackageContents.All(static app => app.Type == EAppType.Demo)) {
				return (byte) EActivationPriority.Demo;
			}

			if (package.PackageContents.Any(app => (app.Type is EAppType.Game or EAppType.Application) && !filter.OwnsApp(app.ID))) {
				return (byte) EActivationPriority.Game;
			}

			if (package.PackageContents.Any(app => IsLibraryContent(app, filter))) {
				return (byte) EActivationPriority.LibraryContent;
			}

			if ((package.MustOwnAppToPurchase > 0) && filter.OwnsApp(package.MustOwnAppToPurchase)) {
				return (byte) EActivationPriority.LibraryContent;
			}

			return (byte) EActivationPriority.Other;
		}

		internal static byte ForPlaytest(FilterableApp app, PackageFilter filter) {
			if ((app.ParentID != null) && filter.OwnsApp(app.ParentID.Value)) {
				return (byte) EActivationPriority.LibraryContent;
			}

			return (byte) EActivationPriority.Other;
		}

		private static bool IsLibraryContent(FilterableApp app, PackageFilter filter) {
			if (app.Type is not (EAppType.DLC or EAppType.Music or EAppType.Video or EAppType.Series or EAppType.Beta)) {
				return false;
			}

			if ((app.ParentID != null) && filter.OwnsApp(app.ParentID.Value)) {
				return true;
			}

			return (app.MustOwnAppToPurchase > 0) && filter.OwnsApp(app.MustOwnAppToPurchase);
		}
	}
}
