using Microsoft.Win32.SafeHandles;
using Serilog;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace FileManager;

public class MoveFileProgressEventArgs : EventArgs
{
	public long TotalFileSize { get; }
	public long TotalBytesTransferred { get; }
	public long BytesMoved { get; }
	public bool Continue { get; set; } = true;

	internal MoveFileProgressEventArgs(long bytesMoved, long totalBytesTransferred, long totalFileSize)
	{
		BytesMoved = bytesMoved;
		TotalBytesTransferred = totalBytesTransferred;
		TotalFileSize = totalFileSize;
	}
}

public class MoveWithProgress
{
	public event EventHandler<MoveFileProgressEventArgs>? MoveProgress;

	public async Task<bool> MoveAsync(LongPath source, LongPath destination, bool overwrite = false, CancellationToken cancellation = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(source, nameof(source));
		ArgumentException.ThrowIfNullOrEmpty(destination, nameof(destination));
		var sourceFileInfo = new FileInfo(source);

		if (!sourceFileInfo.Exists)
			throw new FileNotFoundException($"Source file '{source}' does not exist.", source);

		var destinationFile = new FileInfo(destination);
		var sourceDevice = GetDeviceId(sourceFileInfo);
		var destinationDevice = GetDeviceId(destinationFile.Directory);

		if (sourceDevice is not null && sourceDevice == destinationDevice)
		{
			File.Move(sourceFileInfo.FullName, destinationFile.FullName, overwrite);
			MoveProgress?.Invoke(this, new MoveFileProgressEventArgs(destinationFile.Length, destinationFile.Length, sourceFileInfo.Length));
			return true;
		}

		if (destinationFile.Exists && !overwrite)
			throw new IOException("The file exists.");

		bool success = false;
		try
		{
			success = await CopyWithProgressAsync(sourceFileInfo, destinationFile, cancellation);
		}
		finally
		{
			if (success)
				FileUtility.SaferDelete(sourceFileInfo.FullName);
			else
				FileUtility.SaferDelete(destinationFile.FullName);
		}
		return success;
	}

	private static string? GetDeviceId(FileSystemInfo? fsEntry)
		=> fsEntry?.FullName is not string path ? null
		: LongPath.IsWindows ? GetDriveSerialNumber(path)
		: GetUnixDeviceId(path);

	private async Task<bool> CopyWithProgressAsync(FileInfo sourceFileInfo, FileInfo destinationFile, CancellationToken cancellation)
	{
		const int BlockSizeMb = 8;
		const int BlockSizeBytes = BlockSizeMb * (1 << 20);
		using FileStream sourceStream = sourceFileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
		using FileStream destinationStream = destinationFile.Open(FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);

		using IMemoryOwner<byte> pool = MemoryPool<byte>.Shared.Rent(2 * BlockSizeBytes);
		Memory<byte> readBuff = pool.Memory.Slice(0, BlockSizeBytes);
		Memory<byte> writeBuff = pool.Memory.Slice(BlockSizeBytes, BlockSizeBytes);

		long totalCopied = 0, bytesMovedSinceLastReport = 0;
		DateTime nextReport = default;
		int bytesRead = await sourceStream.ReadAsync(writeBuff, cancellation);
		while (bytesRead > 0)
		{
			totalCopied += bytesRead;
			bytesMovedSinceLastReport += bytesRead;

			var readTask = sourceStream.ReadAsync(readBuff, cancellation);
			await destinationStream.WriteAsync(writeBuff[..bytesRead], cancellation);

			if (DateTime.UtcNow >= nextReport)
			{
				var args = new MoveFileProgressEventArgs(bytesMovedSinceLastReport, totalCopied, sourceFileInfo.Length);
				bytesMovedSinceLastReport = 0;
				MoveProgress?.Invoke(this, args);
				if (!args.Continue)
					break;
				nextReport = DateTime.UtcNow.AddMilliseconds(200.0);
			}
			bytesRead = await readTask;
			(readBuff, writeBuff) = (writeBuff, readBuff);
		}

		destinationStream.SetLength(totalCopied);
		MoveProgress?.Invoke(this, new MoveFileProgressEventArgs(bytesMovedSinceLastReport, totalCopied, sourceFileInfo.Length));
		return totalCopied == sourceFileInfo.Length;
	}

	private static string? GetUnixDeviceId(string path)
	{
		var psi = new ProcessStartInfo
		{
			FileName = "/usr/bin/stat",
			RedirectStandardOutput = true,
			UseShellExecute = false,
			CreateNoWindow = true,
			ArgumentList = { "-L", LongPath.IsOSX ? "-f" : "-c", "%d", path }
		};
		try
		{
			using var proc = Process.Start(psi);
			if (proc is null)
				return null;
			var device = proc.StandardOutput.ReadToEnd().Trim();
			proc.WaitForExit();
			return proc.ExitCode == 0 && device.Length > 0 ? device : null;
		}
		catch (Exception e)
		{
			Log.Logger.Error(e, "Failed to read file device with stat.");
			return null;
		}
	}

	private static string? GetDriveSerialNumber(string path)
	{
		const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
		const uint OPEN_EXISTING = 3;
		var handle = CreateFile(path, FileAccess.Read, FileShare.Read, 0, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, 0);
		if (handle.IsInvalid)
			return null;

		try
		{
			BY_HANDLE_FILE_INFORMATION info = default;
			if (!GetFileInformationByHandle(handle, ref info))
			{
				return null;
			}
			return info.dwVolumeSerialNumber.ToString("x8");
		}
		finally
		{
			handle.Close();
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, ref BY_HANDLE_FILE_INFORMATION lpFileInformation);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern SafeFileHandle CreateFile(string fileName, FileAccess fileAccess, FileShare fileShare, nint lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, nint hTemplateFile);

	[StructLayout(LayoutKind.Sequential, Pack = 4)]
	private struct BY_HANDLE_FILE_INFORMATION
	{
		private readonly uint dwFileAttributes;
		private readonly long ftCreationTime;
		private readonly long ftLastAccessTime;
		private readonly long ftLastWriteTime;
		public uint dwVolumeSerialNumber;
		private readonly uint nFileSizeHigh;
		private readonly uint nFileSizeLow;
		private readonly uint nNumberOfLinks;
		private readonly uint nFileIndexHigh;
		private readonly uint nFileIndexLow;
	}
}
