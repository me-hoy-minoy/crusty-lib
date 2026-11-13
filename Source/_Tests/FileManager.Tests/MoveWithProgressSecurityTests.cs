using System;
using System.IO;
using System.Threading.Tasks;
using FileManager;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FileManager.Tests;

[TestClass]
[DoNotParallelize]
public class MoveWithProgressSecurityTests
{
	[TestMethod]
	[DataRow("$(touch marker)", false)]
	[DataRow("`touch marker`", false)]
	[DataRow("$(touch marker)", true)]
	[DataRow("`touch marker`", true)]
	[DataRow("spaces 'quotes' \"double quotes\"\nand newline", false)]
	public async Task Move_treats_source_and_destination_paths_as_literal_data(string folder, bool sourceHasSpecialName)
	{
		if (OperatingSystem.IsWindows())
			Assert.Inconclusive("Unix path and process behavior.");

		var root = Directory.CreateTempSubdirectory("Libation-move-security-").FullName;
		var previousDirectory = Environment.CurrentDirectory;
		try
		{
			Environment.CurrentDirectory = root;
			var sourceDir = Directory.CreateDirectory(Path.Combine(root, sourceHasSpecialName ? folder : "source")).FullName;
			var destinationDir = Directory.CreateDirectory(Path.Combine(root, sourceHasSpecialName ? "destination" : folder)).FullName;
			var source = Path.Combine(sourceDir, "book.m4b");
			var destination = Path.Combine(destinationDir, "book.m4b");
			File.WriteAllText(source, "audiobook fixture");

			Assert.IsTrue(await new MoveWithProgress().MoveAsync(source, destination));
			Assert.AreEqual("audiobook fixture", File.ReadAllText(destination));
			Assert.IsFalse(File.Exists(source));
			Assert.IsFalse(File.Exists(Path.Combine(root, "marker")), "A path must never execute shell substitutions.");
		}
		finally
		{
			Environment.CurrentDirectory = previousDirectory;
			Directory.Delete(root, recursive: true);
		}
	}
}
