//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Utility.CommandLine;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// Main class
	/// </summary>
	internal class Program
	{
		// constants
		internal const string ESP8266 = "ESP8266";
		internal const string ESP32 = "ESP32";
		internal const string nanoCLR = "nanoCLR";
		internal const string WifiWaterLevelGauge = "WifiWaterLevelGauge";

		// filled from the command line arguments
		[Argument('h', "help")]
		private static bool ShowHelp { get; set; } = false;

		[Argument('p', "port")]
		private static string SerialPort { get; set; } = null;

		[Argument('b', "baud")]
		private static int BaudRate { get; set; } = 921600;

		[Argument('c', "chip")]
		private static string ChipType { get; set; } = ESP32;

		[Argument('m', "flash_mode")]
		private static string FlashMode { get; set; } = "dio";

		[Argument('f', "flash_freq")]
		private static int FlashFrequency { get; set; } = 40000000;

		[Argument('o', "backup_only")]
		private static bool BackupOnly { get; set; } = false;

		[Argument('s', "backup")]
		private static string BackupFilename { get; set; } = null;

		[Argument('r', "restore")]
		private static string RestoreFilename { get; set; } = null;

		private static string FirmwareType { get; set; } = nanoCLR;

		private static string DownloadSource { get; set; } = "https://bintray.com/nfbot/nanoframework-images-dev";

		private static string BoardType { get; set; } = "ESP32_DEVKITC";

		/// <summary>
		/// Entry point
		/// </summary>
		/// <param name="args">You can deliver the following arguments as name=value pairs:
		/// --help or -h for a description which command line parameters can be used.
		/// --port or -p for the serial port to use (e.g. --port=COM1).
		/// --baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).
		/// --chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.
		/// --flash_mode or -m for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.
		/// --flash_freq or -f for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.
		/// --backup or -s for backup the entire flash into a bin file for later restore (e.g. --backup=LastKnownGood). The backup file will be created in the subdirectory "Backup"
		/// with the name %ChipType%_%ChipId%_%Filename%.bin (e.g. ESP32_0x12345678_LastKnownGood.bin). If this file already exists it will be overwritten!
		/// --backup_only or -o if present only the backup will be stored. Makes only senses if the --backup/-s option is also present.
		/// --restore or -r restore the entire flash from a backup file that's created with the --backup/-s parameter (e.g. --restore=LastKnownGood).
		/// The backup should be in the "Backup" subdirectory an should be named %ChipType%_%ChipId%_%Filename%.bin or %Filename%.bin
		/// </param>
		/// <remarks>
		/// If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode "dio" and the flash frequency "40m".
		/// You can set other default values via EspFirmwareFlasher.exe.config (application configuration) file. You can find an example for such a file in the source code file App.config.
		/// </remarks>
		/// <returns>0: successful executed; no error;
		/// -1: an internal exception occured
		/// -2: couldn't connect to the ESP chip or couldn't retrive the infos from the ESP chip
		/// -3: ESP chip has an unexpected flash size; e.g. only 2MB and 4MB flash sizes are supported for the nanoCLR
		/// -4: The firmware couldn't downloaded
		/// -5: Error during flash erase
		/// -6: Error during writing to flash
		/// -7: Error during flash backup
		/// -8: Can't find the backup file
		/// </returns>
		private static int Main(string[] args)
		{
			try
			{
				Console.WriteLine($"ESP Firmware Flash Tool - {typeof(Program).Assembly.GetName().Version.ToString(3)}");
				Console.WriteLine($"Copyright (c) 2018 The nanoFramework project contributors");
				Console.WriteLine($"Using the esptool. See usage and license information at http://github.com/espressif/esptool");
				Console.WriteLine();

				// configure from application settings
				string setting = ConfigurationManager.AppSettings.Get("DefaultSerialPort");
				if (!string.IsNullOrEmpty(setting))
				{
					SerialPort = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultBaudRate");
				if (!string.IsNullOrEmpty(setting))
				{
					BaudRate = Convert.ToInt32(setting);
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultEspChipType");
				if (!string.IsNullOrEmpty(setting))
				{
					ChipType = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFlashMode");
				if (!string.IsNullOrEmpty(setting))
				{
					FlashMode = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFlashFrequency");
				if (!string.IsNullOrEmpty(setting))
				{
					FlashFrequency = setting.ToUpperInvariant().EndsWith("M") ? Convert.ToInt32(setting.Substring(0, setting.Length - 1)) * 1000000 : Convert.ToInt32(setting);
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFirmwareType");
				if (!string.IsNullOrEmpty(setting))
				{
					FirmwareType = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultDownloadSource");
				if (!string.IsNullOrEmpty(setting))
				{
					DownloadSource = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultBoardType");
				if (!string.IsNullOrEmpty(setting))
				{
					BoardType = setting;
				}

				// parse command line
				Arguments.Populate();

				if (ShowHelp)
				{
					Console.WriteLine();
					Console.WriteLine("You can deliver the following arguments as name = value pairs:");
					Console.WriteLine("--help or -h for a description which command line parameters can be used.");
					Console.WriteLine("--port or -p for the serial port to use (e.g. --port=COM1).");
					Console.WriteLine("--baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).");
					Console.WriteLine("--chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.");
					Console.WriteLine("--flash_mode or -m for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.");
					Console.WriteLine("--flash_freq or -f for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.");
					Console.WriteLine("--backup or -s for backup the entire flash into a bin file for later restore (e.g. --backup=LastKnownGood). The backup file will be created in the subdirectory \"Backup\"");
					Console.WriteLine("    with the name %ChipType%_%ChipId%_%Filename%.bin (e.g. ESP32_0x12345678_LastKnownGood.bin). If this file already exists it will be overwritten!");
					Console.WriteLine("--backup_only or -o if present only the backup will be stored. Makes only senses if the --backup/-s option is also present.");
					Console.WriteLine("--restore or -r restore the entire flash from a backup file that's created with the --backup/-s parameter (e.g. --restore=LastKnownGood).");
					Console.WriteLine("    The backup should be in the \"Backup\" subdirectory an should be named %ChipType%_%ChipId%_%Filename%.bin or %Filename%.bin");
					Console.WriteLine();
					Console.WriteLine("If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode \"dio\" and the flash frequency \"40m\".");
					Console.WriteLine("You can set other default values via EspFirmwareFlasher.exe.config (application configuration) file. You can find an example for such a file in the source code file App.config.");
					Console.WriteLine();
					Console.WriteLine("The tool delivers the following exit codes:");
					Console.WriteLine("0: successful executed; no error");
					Console.WriteLine("-1: An internal exception occured");
					Console.WriteLine("-2: Couldn't connect to the ESP chip or couldn't retrive the infos from the ESP chip");
					Console.WriteLine("-3: ESP chip has an unexpected flash size; e.g. only 2MB and 4MB flash sizes are supported for the nanoCLR");
					Console.WriteLine("-4: The firmware couldn't downloaded");
					Console.WriteLine("-5: Error during flash erase");
					Console.WriteLine("-6: Error during writing to flash");
					Console.WriteLine("-7: Error during flash backup");
					Console.WriteLine("-8: Can't find the backup file");
					return 0;
				}

				// determine the serial port
				if (SerialPort == null)
				{
					Console.WriteLine($"At which serial port is an {ChipType} module waiting in download mode? (e.g. COM1)");
					SerialPort = Console.ReadLine();
				}

				// print out the used parameters
				Console.WriteLine($"Using {SerialPort} with {BaudRate} baud for connecting to an {ChipType}.");
				if (!BackupOnly)
				{
					Console.WriteLine($"Flashing will be done with mode {FlashMode} at {FlashFrequency / 1000000} MHz.");
					if (RestoreFilename == null)
					{
						Console.WriteLine($"{FirmwareType} firmware will be downloaded from: {DownloadSource}");
					}
				}

				// get alle availabe info form esptool; this tests the connection to the chip
				Console.WriteLine($"Trying to connect and getting infos about the {ChipType} chip ...");
				EspTool espTool = new EspTool(SerialPort, BaudRate, ChipType, FlashMode, FlashFrequency);
				EspTool.Info? info = espTool.TestChip();
				if (!info.HasValue)
				{
					return -2;
				}

				// Backup the flash?
				if (!string.IsNullOrEmpty(BackupFilename))
				{
					if (!Directory.Exists("Backup"))
					{
						Directory.CreateDirectory("Backup");
					}
					string fileName = Path.Combine("Backup", $"{ChipType}_0x{info.Value.ChipId:X}_{BackupFilename}.bin");
					if (File.Exists(fileName))
					{
						File.Delete(fileName);
					}
					Console.WriteLine($"Backing up the firmware ...");
					if (!espTool.BackupFlash(new FileInfo(fileName).FullName, info.Value.FlashSize))
					{
						return -7;
					}

					// after backup the ESP8266 need an additional reset because the chip is stuck in the stub from the read_flash command
					if (ChipType == ESP8266 && !BackupOnly)
					{
						WorkaroundEspToolIssue310();
					}
				}
				// only backup?
				if (BackupOnly)
				{
					return 0;
				}

				Dictionary<int, string> firmwareParts;
				// Restore the flash?
				if (!string.IsNullOrEmpty(RestoreFilename))
				{
					string fileName = Path.Combine("Backup", $"{ChipType}_0x{info.Value.ChipId:X}_{RestoreFilename}.bin");
					if (!File.Exists(fileName))
					{
						fileName = Path.Combine("Backup", $"{RestoreFilename}.bin");
						if (!File.Exists(fileName))
						{
							Console.WriteLine("Can't find the backup file for restoring!");
							return -8;
						}
					}
					Console.WriteLine($"Firmware will be restored from: {fileName}");
					firmwareParts = new Dictionary<int, string>() { { 0x000000, new FileInfo(fileName).FullName } };
				}
				else // download firmware from internet
				{
					Firmware firmware = null;
					if (FirmwareType == nanoCLR)
					{
						// download nanoCLR firmware from bintray.com
						Console.WriteLine($"Downloading {BoardType} {nanoCLR} firmware from {DownloadSource} ...");
						firmware = new NanoClrFirmware(DownloadSource, BoardType);
					}
					else if (FirmwareType == WifiWaterLevelGauge)
					{
						// download WifiWaterLevelGauge firmware from github.com
						Console.WriteLine($"Downloading {WifiWaterLevelGauge} firmware from {DownloadSource} ...");
						firmware = new WifiWaterLevelGaugeFirmware(DownloadSource);
					}

					// is the chip and flash size supported?
					if (firmware == null || !firmware.CheckSupport(ChipType, info.Value.FlashSize))
					{
						return -3;
					}
					// download from internet and extract it
					firmwareParts = firmware.DownloadAndExtract(ChipType, info.Value.FlashSize);
					if (firmwareParts == null)
					{
						return -4;
					}
				}

				// erase flash
				Console.WriteLine($"Erasing flash ...");
				if (!espTool.EraseFlash())
				{
					return -5;
				}

				// after erasing the ESP8266 need an additional reset because the chip is stuck in the stub from the erase_flash command
				if (ChipType == ESP8266)
				{
					WorkaroundEspToolIssue310();
				}

				// write to flash
				Console.WriteLine($"Flashing firmware ...");
				if (!espTool.WriteFlash(firmwareParts))
				{
					return -6;
				}
				Console.WriteLine("Successfully flashed!");
				return 0;
			}
			catch (Exception exception)
			{
				Console.WriteLine(exception.Message);
				Console.WriteLine(exception.StackTrace);
				return -1;
			}
			finally
			{
				if (args == null || args.Length == 0)
				{
					Console.WriteLine("Press any key to exit.");
					Console.ReadKey();
				}
			}
		}

		/// <summary>
		/// See https://github.com/espressif/esptool/issues/310
		/// There is a bug in the esptool ESP8266 stub program. After the first command the chip is frozen in the bootloader.
		/// </summary>
		private static void WorkaroundEspToolIssue310()
		{
			Console.WriteLine($"!!! Please reset the {ESP8266} and bring it in download now again, because the chip is stuck after executing the stub !!!");
			Console.WriteLine("See also: https://github.com/espressif/esptool/issues/310");
			Console.WriteLine($"Press any key if the {ESP8266} is again in download mode.");
			Console.ReadKey();
		}
	}
}
