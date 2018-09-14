//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// Class handles the download of the ESP32 nanoCLR firmware from bintray.com
	/// </summary>
	internal class NanoClrFirmware : Firmware
	{
		/// <summary>
		/// Download source: currently https://bintray.com/nfbot/nanoframework-images-dev
		/// </summary>
		private readonly string _downloadSource;

		/// <summary>
		/// Board type: currently ESP32_DEVKITC
		/// </summary>
		private readonly string _boardType;

		/// <summary>
		/// The directory where the firmware was unzipped
		/// </summary>
		private DirectoryInfo _firmwareDirectory = null;

		/// <summary>
		/// The nanoCLR is only for the ESP32
		/// </summary>
		internal override string[] SupportedChipTypes { get { return new string[] { Program.ESP32 }; } }

		/// <summary>
		/// The nanoCLR is only for 2MB and 4MB flash sizes
		/// </summary>
		internal override int[] SupportedFlashSizes { get { return new int[] { 0x200000, 0x400000 }; } }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="downloadSource">Download source: currently https://bintray.com/nfbot/nanoframework-images-dev </param>
		/// <param name="boardType">Board type: currently ESP32_DEVKITC</param>
		internal NanoClrFirmware(string downloadSource, string boardType)
		{
			_downloadSource = downloadSource;
			_boardType = boardType;
		}

		/// <summary>
		/// Download the firmware zip, extract this zip file, and get the firmware parts
		/// </summary>
		/// <param name="firmwareTag">if null the latest version will be downloaded; otherwise the version with this tag (e.g. 0.1.0-preview.738) will be downloaded.</param>
		/// <param name="chipType">Only ESP32 is allowed</param>
		/// <param name="flashSize">Flashsize in bytes: Only 0x200000 (2MB) and 0x400000 (4MB) is allowed</param>
		/// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
		internal override Dictionary<int, string> DownloadAndExtract(string firmwareTag, string chipType, int flashSize)
		{
			// check if chip type / flash size is supported
			if (!CheckSupport(chipType, flashSize))
			{
				return null;
			}

			// find out what the latest version is or try for find a special version by tag; that's written at the overview page
			WebClient webClient = new WebClient();
			string latestVersionPage = webClient.DownloadString(string.Join("/", _downloadSource, _boardType, string.IsNullOrEmpty(firmwareTag) ? "_latestVersion" : firmwareTag));

			// find the filename
			Match match = Regex.Match(latestVersionPage, $"(/{_boardType}/)(?<filename>[\\d]+[^\"'/]*)");
			if (!match.Success)
			{
				Console.WriteLine($"Can't find the latest firmware version on {_downloadSource}!");
				return null;
			}
			string filename = match.Groups["filename"].ToString().Trim();
			string filenameWithExtension = string.Concat(filename, ".zip");
			if (File.Exists(filenameWithExtension))
			{
				File.Delete(filenameWithExtension);
			}
			
			// download the firmware file
			webClient.DownloadFile(string.Join("/", _downloadSource, $"download_file?file_path={_boardType}-{filenameWithExtension}"), filenameWithExtension);

			// delete directory if already exists
			_firmwareDirectory = new DirectoryInfo(filename);
			if (_firmwareDirectory.Exists)
			{
				_firmwareDirectory.Delete(true);
			}

			// unzip the firmware
			Console.WriteLine($"Extracting {filenameWithExtension} ...");
			ZipFile.ExtractToDirectory(filenameWithExtension, _firmwareDirectory.FullName);

			// Get the parts that should be written to ESP32 flash
			Dictionary<int, string> partsToFlash = new Dictionary<int, string>()
			{
				// bootloader goes to 0x1000
				{ 0x1000, Path.Combine(_firmwareDirectory.FullName, "bootloader.bin") },
				// nanoCLR goes to 0x10000
				{ 0x10000, Path.Combine(_firmwareDirectory.FullName, "NanoCLR.bin") },
				// partition table goes to 0x8000; there is on partition table for 2MB flash and one for 4MB flash
				{ 0x8000, Path.Combine(_firmwareDirectory.FullName, flashSize == 0x200000 ? "partitions_2mb.bin" : "partitions_4mb.bin") }
			};
			return partsToFlash;
		}

		/// <summary>
		/// Gets the start address for the application that runs on top of the firmware
		/// </summary>
		/// <param name="chipType">ESP chip type</param>
		/// <param name="flashSize">Flashsize in bytes</param>
		/// <returns>start address for the application that runs on top of the firmware</returns>
		internal override int GetApplicationStartAddress(string chipType, int flashSize)
		{
			// it's 0x110000 for both flash sizes; 2MB and 4MB
			return 0x110000;
		}
	}
}
