//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// abstract base class for download / extract an ESP32 / ESP8266 firmware from the internet
	/// </summary>
	internal abstract class Firmware
	{
		/// <summary>
		/// Gets the supported ESP chip types for this type of firmware
		/// </summary>
		internal abstract string[] SupportedChipTypes { get; }

		/// <summary>
		/// Gets the supported flash sizes for this type of firmware
		/// </summary>
		internal abstract int[] SupportedFlashSizes { get; }

		/// <summary>
		/// Download the firmware and extract it if needed
		/// </summary>
		/// <param name="chipType">ESP chip types</param>
		/// <param name="flashSize">Flashsize in bytes</param>
		/// <returns>dictionary which keys are the start addresses and the values are the complete filenames (the bin files); or null if not successfully downloaded/extracted</returns>
		internal abstract Dictionary<int, string> DownloadAndExtract(string chipType, int flashSize);

		/// <summary>
		/// Checks if there is a firmware theoretically (not checked in the internet) available for the combination of ESP chip type and flash size
		/// </summary>
		/// <param name="chipType">ESP chip type</param>
		/// <param name="flashSize">Flashsize in bytes</param>
		/// <returns>true if firmware is available; false otherwise</returns>
		internal bool CheckSupport(string chipType, int flashSize)
		{
			if (!SupportedChipTypes.Contains(chipType))
			{
				Console.WriteLine($"There is no firmware available for the {chipType}!{Environment.NewLine}Only the following ESP chips are supported: {string.Join(", ", SupportedChipTypes)}");
				return false;
			}
			if (!SupportedFlashSizes.Contains(flashSize))
			{
				string humanReadable = flashSize >= 0x10000 ? $"{flashSize / 0x10000} MB" : $"{flashSize / 0x400} KB";
				Console.WriteLine($"There is no firmware available for the {chipType} with {humanReadable} flash size!{Environment.NewLine}Only {chipType} with the following flash sizes are supported: {string.Join(", ", SupportedFlashSizes.Select(size => size >= 0x10000 ? $"{size / 0x10000} MB" : $"{size / 0x400} KB"))}");
				return false;
			}
			return true;
		}
	}
}
