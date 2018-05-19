//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// Class handles the download of the ESP8266 WifiWaterLevelGauge firmware from github.com
	/// </summary>
	internal class WifiWaterLevelGaugeFirmware : Firmware
	{
		/// <summary>
		/// Download source: currently https://github.com/MatthiasJentsch/WifiWaterLevelGauge
		/// </summary>
		private readonly string _downloadSource;

		/// <summary>
		/// The directory where the firmware was unzipped
		/// </summary>
		private DirectoryInfo _firmwareDirectory = null;

		/// <summary>
		/// The WifiWaterLevelGauge is only for the ESP8266
		/// </summary>
		internal override string[] SupportedChipTypes { get { return new string[] { Program.ESP8266 }; } }

		/// <summary>
		/// The WifiWaterLevelGauge is only for 512KB flash size
		/// </summary>
		internal override int[] SupportedFlashSizes { get { return new int[] { 0x80000 }; } }

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="downloadSource">Download source: currently https://github.com/MatthiasJentsch/WifiWaterLevelGauge </param>
		internal WifiWaterLevelGaugeFirmware(string downloadSource)
		{
			_downloadSource = downloadSource;
		}

		/// <summary>
		/// Download the firmware and get the firmware parts
		/// </summary>
		/// <param name="chipType">Only ESP8266 is allowed</param>
		/// <param name="flashSize">Flashsize in bytes: Only 0x8000 (512KB) is allowed</param>
		/// <returns>a dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</returns>
		internal override Dictionary<int, string> DownloadAndExtract(string chipType, int flashSize)
		{
			// check if chip type / flash size is supported
			if (!CheckSupport(chipType, flashSize))
			{
				return null;
			}

			// delete the destination directory if exists
			_firmwareDirectory = new DirectoryInfo(Program.WifiWaterLevelGauge);
			if (_firmwareDirectory.Exists)
			{
				_firmwareDirectory.Delete(true);
			}
			_firmwareDirectory.Create();

			// load the page with the latest release
			WebClient webClient = new WebClient();
			string latestVersionPage = webClient.DownloadString(string.Join("/", _downloadSource, "releases/latest"));

			// find the download links and the filenames; search for all links
			// regex found at: https://stackoverflow.com/questions/15926142/regular-expression-for-finding-href-value-of-a-a-link
			MatchCollection matches = Regex.Matches(latestVersionPage, @"<a\s+(?:[^>]*?\s+)?href=([""'])(.*?)\1");
			if (matches.Count == 0)
			{
				Console.WriteLine($"Can't find the latest firmware version on {_downloadSource}!");
				return null;
			}

			// parse the links
			string schemaAndServer = new Uri(_downloadSource).GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);
			string linkBootloader = null;
			string linkFirmware = null;
			string filenameBootloader = null;
			string filenameFirmware = null;
			bool found = false;
			foreach (Match match in matches)
			{
				string link = match.Groups[2].ToString().Trim();
				if (link.EndsWith(".bin"))
				{
					if (link.Contains("0x00000"))
					{
						linkBootloader = string.Concat(schemaAndServer, link);
						filenameBootloader = Path.Combine(_firmwareDirectory.FullName, link.Substring(link.LastIndexOf('/') + 1));
					}
					else if (link.Contains("0x10000"))
					{
						linkFirmware = string.Concat(schemaAndServer, link);
						filenameFirmware = Path.Combine(_firmwareDirectory.FullName, link.Substring(link.LastIndexOf('/') + 1));
					}
					if (linkBootloader != null && linkFirmware != null)
					{
						found = true;
						break;
					}
				}
			}

			// found both links?
			if (!found)
			{
				Console.WriteLine($"Can't find the latest firmware version on {_downloadSource}!");
				return null;
			}
			
			// download the firmware files
			webClient.DownloadFile(linkBootloader, filenameBootloader);
			webClient.DownloadFile(linkFirmware, filenameFirmware);

			Dictionary<int, string> partsToFlash = new Dictionary<int, string>();
			// bootloader goes to 0x00000
			partsToFlash.Add(0x00000, filenameBootloader);
			// firmware goes to 0x10000
			partsToFlash.Add(0x10000, filenameFirmware);
			// we also need to flash the default data and the blank block
			partsToFlash.Add(0x7C000, @"esptool\esp_init_data_default.bin");
			partsToFlash.Add(0x7E000, @"esptool\blank.bin");
			return partsToFlash;
		}
	}
}
