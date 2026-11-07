using System;

// Duplicates of SteamKit2 enums, necessary for non-generic ASF support

namespace FreePackages {
	[Flags]
	public enum EAppType
	{
		Invalid = 0,
		Game = 1,
		Application = 2,
		Tool = 4,
		Demo = 8,
		Deprected = 0x10,
		DLC = 0x20,
		Guide = 0x40,
		Driver = 0x80,
		Config = 0x100,
		Hardware = 0x200,
		Franchise = 0x400,
		Video = 0x800,
		Plugin = 0x1000,
		Music = 0x2000,
		Series = 0x4000,
		Comic = 0x8000,
		Beta = 0x10000,
		Shortcut = 0x40000000,
		[Obsolete]
		DepotOnly = int.MinValue
	}

	public enum EBillingType
	{
		NoCost,
		BillOnceOnly,
		BillMonthly,
		ProofOfPrepurchaseOnly,
		GuestPass,
		HardwarePromo,
		Gift,
		AutoGrant,
		OEMTicket,
		RecurringOption,
		BillOnceOrCDKey,
		Repurchaseable,
		FreeOnDemand,
		Rental,
		CommercialLicense,
		FreeCommercialLicense,
		NumBillingTypes
	}

	public enum EPackageStatus
	{
		Available,
		Preorder,
		Unavailable,
		Invalid
	}
}