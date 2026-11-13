using AppScaffolding;
using LibationUiBase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LibationUiBase.Tests;

[TestClass]
public class SourceOnlyUpdaterTests
{
	[TestMethod]
	public async Task Requested_update_explains_source_workflow_without_offering_a_binary()
	{
		var updater = new Upgrader();
		var failures = new List<string>();
		updater.UpgradeFailed += (_, message) => failures.Add(message);
		updater.DownloadBegin += (_, _) => Assert.Fail("Binary download must not start.");
		var result = await updater.CheckForUpgradeAsync(_ =>
		{
			Assert.Fail("No binary installation should be offered.");
			return Task.CompletedTask;
		});

		Assert.AreEqual(VersionCheckOutcome.UnableToDetermine, result.Outcome);
		Assert.AreEqual(1, failures.Count);
		StringAssert.Contains(failures[0], "source-only updates");
	}
}
