using System;
using System.Text.RegularExpressions;

namespace FileManager;

/// <summary>Expands home and environment references without interpreting shell syntax.</summary>
public static class ConfiguredPath
{
	private static readonly Regex UnixVariable = new(
		@"\$(?:\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}|(?<name>[A-Za-z_][A-Za-z0-9_]*))",
		RegexOptions.CultureInvariant);

	public static string Expand(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (OperatingSystem.IsWindows())
			return Environment.ExpandEnvironmentVariables(path);

		if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal))
			path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];

		// Replacement values are literal; they do not undergo a second expansion.
		return UnixVariable.Replace(path,
			match => Environment.GetEnvironmentVariable(match.Groups["name"].Value) ?? match.Value);
	}
}
