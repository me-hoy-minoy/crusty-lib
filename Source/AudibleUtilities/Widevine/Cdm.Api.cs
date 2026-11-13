using System;
using System.Threading.Tasks;

namespace AudibleUtilities.Widevine;

public partial class Cdm
{
	/// <summary>Load locally supplied device data without delegating account authorization to a service.</summary>
	public static Task<Cdm?> GetCdmAsync()
	{
		using var persister = AudibleApiStorage.GetAccountsSettingsPersister();
		return Task.FromResult(LoadLocalCdm(persister.Target.Cdm));
	}

	internal static Cdm? LoadLocalCdm(string? encodedDevice)
	{
		if (string.IsNullOrEmpty(encodedDevice))
			return null;

		try
		{
			return new Cdm(new Device(Convert.FromBase64String(encodedDevice)));
		}
		catch (Exception ex)
		{
			Serilog.Log.Logger.Warning(ex, "Local Widevine device data could not be loaded. Third-party acquisition is disabled.");
			return null;
		}
	}
}
