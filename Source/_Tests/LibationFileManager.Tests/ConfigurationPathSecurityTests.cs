using System;
using System.IO;
using FileManager;
using LibationFileManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace LibationFileManager.Tests;

[TestClass]
[DoNotParallelize]
public class ConfigurationPathSecurityTests
{
	[TestMethod]
	[DataRow("$(touch marker)")]
	[DataRow("`touch marker`")]
	[DataRow("folder; touch marker")]
	[DataRow("spaces 'quotes' \"double quotes\"")]
	public void Appsettings_path_is_literal_and_resolves_relative_to_settings(string relativePath)
	{
		if (OperatingSystem.IsWindows())
			Assert.Inconclusive("Unix configuration path behavior.");
		var root = Directory.CreateTempSubdirectory("Libation-path-security-").FullName;
		var previousDirectory = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = root;
			var settings = Path.Combine(root, "appsettings.json");
			File.WriteAllText(settings, new JObject { [LibationFiles.LIBATION_FILES_KEY] = relativePath }.ToString());
			var files = new LibationFiles(settings);

			Assert.AreEqual(Path.Combine(root, relativePath), files.Location.Path);
			Assert.IsFalse(File.Exists(Path.Combine(root, "marker")));
		}
		finally
		{
			Environment.CurrentDirectory = previousDirectory;
			Directory.Delete(root, recursive: true);
		}
	}

	[TestMethod]
	public void Unix_environment_expansion_is_single_pass_and_preserves_unknown_variables()
	{
		if (OperatingSystem.IsWindows())
			Assert.Inconclusive("Unix environment variable syntax.");
		var name = "LIBATION_PATH_TEST_" + Guid.NewGuid().ToString("N");
		try
		{
			Environment.SetEnvironmentVariable(name, "literal $HOME $(touch marker) ~/folder");
			Assert.AreEqual("literal $HOME $(touch marker) ~/folder/books", ConfiguredPath.Expand("${" + name + "}/books"));
			Assert.AreEqual("literal $HOME $(touch marker) ~/folder/books", ConfiguredPath.Expand("$" + name + "/books"));
			Assert.AreEqual("${" + name + "_UNSET}/books", ConfiguredPath.Expand("${" + name + "_UNSET}/books"));
			Assert.AreEqual(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "books"), ConfiguredPath.Expand("~/books"));
		}
		finally
		{
			Environment.SetEnvironmentVariable(name, null);
		}
	}
}
