using AudibleUtilities.Widevine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AudibleUtilities.Tests;

[TestClass]
public class LocalCdmTests
{
	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("invalid base64")]
	[DataRow("bm90IGEgd2lkZXZpbmUgZGV2aWNl")]
	public void Missing_or_invalid_local_device_is_unavailable(string? device)
	{
		Assert.IsNull(Cdm.LoadLocalCdm(device));
	}
}
