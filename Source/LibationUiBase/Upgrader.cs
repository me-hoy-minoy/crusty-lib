using AppScaffolding;
using Dinah.Core.Net.Http;
using LibationFileManager;
using System;
using System.IO;
using System.Threading.Tasks;

namespace LibationUiBase;

/// <summary>Whether Libation may replace its own install files, and what to say when it may not.</summary>
internal readonly record struct UpgradeCapability(bool CapUpgrade, string? Reason, string? Summary);

public class UpgradeEventArgs
{
	public required UpgradeProperties UpgradeProperties { get; init; }
	public bool CapUpgrade { get; internal init; }
	/// <summary>Why Libation cannot install this upgrade itself, to show in place of the update prompt. Null when it can.</summary>
	public string? UpgradeUnavailableReason { get; internal init; }
	/// <summary>
	/// The same thing in one line, for Classic, whose dialog has a single line of room above the
	/// release notes. Null when Libation can install the upgrade.
	/// </summary>
	public string? UpgradeUnavailableSummary { get; internal init; }
	private bool _ignore = false;
	private bool _installUpgrade = true;
	public bool Ignore
	{
		get => _ignore;
		set
		{
			_ignore = value;
			_installUpgrade &= !Ignore;
		}
	}
	public bool InstallUpgrade
	{
		get => _installUpgrade;
		set
		{
			_installUpgrade = value;
			_ignore &= !InstallUpgrade;
		}
	}
}

public class Upgrader : UpgraderBase
{
	internal const string SourceUpdateMessage =
		"This fork uses source-only updates. Review and build a pinned source revision to update it.";

	protected override Task<VersionCheckResult> CheckForUpgradeAsync()
	{
		OnUpgradeFailed(SourceUpdateMessage, null);
		return Task.FromResult(new VersionCheckResult(VersionCheckOutcome.UnableToDetermine));
	}

	protected override Task<string?> DownloadUpgradeAsync(UpgradeProperties upgradeProperties)
	{
		OnUpgradeFailed(SourceUpdateMessage, null);
		return Task.FromResult<string?>(null);
	}
}

public class MockUpgrader : UpgraderBase
{
	public int DownloadTimeMs { get; set; } = 3000;
	public int DownloadSizeInBytes { get; set; } = 150 * 1024 * 1024;
	public bool CheckForUpgradeSucceeds { get; set; } = true;
	public bool DownloadUpgradeSucceeds { get; set; } = true;
	public string? MockUpgradeBundle { get; set; }

	protected override Task<VersionCheckResult> CheckForUpgradeAsync()
	{
		if (!CheckForUpgradeSucceeds)
		{
			OnUpgradeFailed("Mock Check For Upgrade Failed", null);
			return Task.FromResult(new VersionCheckResult(VersionCheckOutcome.UnableToDetermine));
		}
		return Task.FromResult(new VersionCheckResult(VersionCheckOutcome.UpdateAvailable, new UpgradeProperties(
					"http://fake.url/to/bundle.zip",
					"",
					Path.GetFileName(MockUpgradeBundle) ?? "",
					LibationScaffolding.BuildVersion ?? new(1, 0, 0, 0),
					"<RELEASE NOTES>")));
	}

	protected override async Task<string?> DownloadUpgradeAsync(UpgradeProperties upgradeProperties)
	{
		if (!File.Exists(MockUpgradeBundle))
		{
			OnUpgradeFailed("Mock Download bundle file not found", null);
			return null;
		}

		for (int i = 1; i <= 100; i++)
		{
			await Task.Delay(DownloadTimeMs / 100);
			OnDownloadProgress(new()
			{
				BytesReceived = DownloadSizeInBytes / 100,
				ProgressPercentage = i,
				TotalBytesToReceive = DownloadSizeInBytes * i / 100
			});
		}
		if (!DownloadUpgradeSucceeds)
		{
			OnUpgradeFailed("Mock Download Upgrade Failed", null);
			return null;
		}

		return MockUpgradeBundle;
	}
}

public abstract class UpgraderBase
{
	internal const string ApplicationControlUpgradeMessage =
		$"""
		A new version is available, but Libation cannot install it itself: Smart App Control is On for this PC, and it blocks files Windows does not recognise. Replacing Libation's files in place is what leaves it unable to start.

		Download the release below and install it yourself. If Windows blocks that too, see:
		{StartupAssemblyBootstrap.TroubleshootApplicationControlUrl}
		""";

	internal const string ApplicationControlUpgradeSummary =
		"Libation cannot install this update while Smart App Control is On.";

	public event EventHandler? DownloadBegin;
	public event EventHandler<DownloadProgress>? DownloadProgress;
	public event EventHandler<bool>? DownloadCompleted;
	public event EventHandler<string>? UpgradeFailed;

	protected void OnDownloadProgress(DownloadProgress args) => DownloadProgress?.Invoke(this, args);
	protected void OnUpgradeFailed(string message, Exception? ex)
		=> UpgradeFailed?.Invoke(this, (message + Environment.NewLine + Environment.NewLine + ex?.Message).Trim());
	protected abstract Task<VersionCheckResult> CheckForUpgradeAsync();
	protected abstract Task<string?> DownloadUpgradeAsync(UpgradeProperties upgradeProperties);

	/// <summary>
	/// Whether Libation may replace its own install files, and what to show instead of the update
	/// prompt when it may not. An overlay upgrade writes files Windows has never seen, and
	/// Application Control blocks unsigned files it does not recognise, so upgrading in place under
	/// enforcement is what turns a working install into one that cannot start.
	/// Kept separate from the upgrade flow so the enforcing case is testable away from Windows.
	/// </summary>
	internal static UpgradeCapability ResolveUpgradeCapability(bool platformCanUpgrade, bool applicationControlEnforcing)
		=> applicationControlEnforcing
			? new(false, ApplicationControlUpgradeMessage, ApplicationControlUpgradeSummary)
			: new(platformCanUpgrade, null, null);

	/// <summary>
	/// Whether the flow may go on to download and install. The dialog's answer is not enough on its
	/// own: when Libation cannot install the upgrade itself, the prompt is a notice with a download
	/// link, so a UI that reports acceptance anyway must not be able to start an install that was
	/// never on offer - or, under Application Control, one that leaves Libation unable to start.
	/// </summary>
	internal static bool MayInstallUpgrade(bool userAccepted, bool capUpgrade)
		=> userAccepted && capUpgrade;

	/// <summary>
	/// The check both GUIs run when their main window opens, skipped when the user has turned off
	/// <see cref="Configuration.CheckForUpgradesAtStartup"/>. Only this automatic check is optional:
	/// the About window's "Check for Upgrade" button asks for a check outright and calls
	/// <see cref="CheckForUpgradeAsync(Func{UpgradeEventArgs, Task})"/> regardless of the setting.
	/// </summary>
	public async Task CheckForUpgradeAtStartupAsync(Func<UpgradeEventArgs, Task> upgradeAvailableHandler)
	{
		if (!Configuration.Instance.CheckForUpgradesAtStartup)
		{
			Serilog.Log.Logger.Information("Skipping the startup upgrade check: {Setting} is off.", nameof(Configuration.CheckForUpgradesAtStartup));
			return;
		}

		await CheckForUpgradeAsync(upgradeAvailableHandler);
	}

	/// <summary>Check for upgrade and invoke <paramref name="upgradeAvailableHandler"/> if an update is available. Returns the check outcome so the UI can show "up to date", "update available", or "unable to determine".</summary>
	public async Task<VersionCheckResult> CheckForUpgradeAsync(Func<UpgradeEventArgs, Task> upgradeAvailableHandler)
	{
		try
		{
			var result = await CheckForUpgradeAsync();

			if (result.Outcome != VersionCheckOutcome.UpdateAvailable || result.UpgradeProperties is not UpgradeProperties upgradeProperties)
				return result;

			const string ignoreUpgrade = "IgnoreUpgrade";
			var config = Configuration.Instance;

			if (config.GetString(propertyName: ignoreUpgrade) == upgradeProperties.LatestRelease.ToString())
				return result;

			var interop = InteropFactory.Create();

			if (!interop.CanUpgrade)
				Serilog.Log.Logger.Information("Can't perform upgrade automatically");

			var applicationControlBlocksUpgrade = ApplicationControlPolicy.IsEnforcing;
			if (applicationControlBlocksUpgrade)
				Serilog.Log.Logger.Information("Windows Application Control is enforcing. Offering the download instead of an in-app upgrade.");

			var capability = ResolveUpgradeCapability(interop.CanUpgrade, applicationControlBlocksUpgrade);

			var upgradeEventArgs = new UpgradeEventArgs
			{
				UpgradeProperties = upgradeProperties,
				CapUpgrade = capability.CapUpgrade,
				UpgradeUnavailableReason = capability.Reason,
				UpgradeUnavailableSummary = capability.Summary,
			};

			await upgradeAvailableHandler(upgradeEventArgs);

			if (upgradeEventArgs.Ignore)
				config.SetString(upgradeProperties.LatestRelease.ToString(), ignoreUpgrade);

			if (!MayInstallUpgrade(upgradeEventArgs.InstallUpgrade, capability.CapUpgrade))
			{
				if (upgradeEventArgs.InstallUpgrade)
					Serilog.Log.Logger.Information(
						"Skipped the in-app upgrade to {LatestRelease}: {Reason}.",
						upgradeProperties.LatestRelease,
						applicationControlBlocksUpgrade ? "Windows Application Control is enforcing" : "this install cannot upgrade itself");

				return result;
			}

			//Download the upgrade file in the background,
			DownloadBegin?.Invoke(this, EventArgs.Empty);
			string? upgradeBundle = await DownloadUpgradeAsync(upgradeProperties);

			if (string.IsNullOrEmpty(upgradeBundle) || !File.Exists(upgradeBundle))
			{
				DownloadCompleted?.Invoke(this, false);
			}
			else
			{
				DownloadCompleted?.Invoke(this, true);

				Serilog.Log.Logger.Information($"Begin running auto-upgrader");
				try
				{
					await interop.InstallUpgradeAsync(upgradeBundle, upgradeProperties.LatestRelease);
					Serilog.Log.Logger.Information($"Completed running auto-upgrader");
				}
				catch (InstallUpgradeIntegrityException ex)
				{
					Serilog.Log.Logger.Error(ex, "In-app upgrade failed integrity check and was rolled back");
					OnUpgradeFailed(
						$"The in-app upgrade did not complete successfully. Libation restored your previous install files from backup.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
						ex);
				}
				catch (Exception ex)
				{
					Serilog.Log.Logger.Error(ex, "Auto-upgrader did not complete successfully");
					OnUpgradeFailed("The upgrade installer did not complete successfully. You can install the downloaded package manually from your temp folder.", ex);
				}
			}

			return result;
		}
		catch (Exception ex)
		{
			var message = "An error occurred while checking for app upgrades.";
			Serilog.Log.Logger.Error(ex, message);
			OnUpgradeFailed(message, ex);
			return new VersionCheckResult(VersionCheckOutcome.UnableToDetermine);
		}
	}
}
