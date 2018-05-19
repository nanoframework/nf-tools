//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using EspFirmwareFlasher.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// Class the handles all the calls to the esptool.exe.
	/// </summary>
	/// <remarks>
	/// The esptool.py Python script with all its dependencies has to be converted into an folder where the esptool can be called without a Python installed.
	/// That's done by the pyinstaller program. The resulting folder should be zipped and distributed as esptool.zip in the same folder where this program is.
	/// </remarks>
	internal class EspTool
	{
		/// <summary>
		/// The serial port over which all the communication goes
		/// </summary>
		private readonly string _serialPort = null;

		/// <summary>
		/// The baud rate for the serial port; 921600 baud is the default
		/// </summary>
		private readonly int _baudRate = 0;

		/// <summary>
		/// ESP chip type. Only ESP32 and ESP8266 are allowed.
		/// </summary>
		private readonly string _chipType = null;

		/// <summary>
		/// The flash mode for the esptool: See https://github.com/espressif/esptool#flash-modes for more details
		/// </summary>
		private readonly string _flashMode = null;

		/// <summary>
		/// The flash frequency for the esptool: See https://github.com/espressif/esptool#flash-modes for more details
		/// </summary>
		/// <remarks>This value should be in Hz; 40 MHz = 40.000.000 Hz</remarks>
		private readonly int _flashFrequency = 0;

		/// <summary>
		/// The size of the flash in bytes; 4 MB = 0x40000 bytes
		/// </summary>
		private int _flashSize = -1;

		/// <summary>
		/// Structure for holding the information about the connected ESP32 together
		/// </summary>
		internal struct Info
		{
			/// <summary>
			/// Version of the esptool.py
			/// </summary>
			internal Version ToolVersion { get; private set; }

			/// <summary>
			/// Name of the ESP32/ESP8266 chip
			/// </summary>
			internal string ChipName { get; private set; }

			/// <summary>
			/// ESP32/ESP8266 chip features
			/// </summary>
			internal string ChipFeatures { get; private set; }

			/// <summary>
			/// ID of the ESP32/ESP8266 chip
			/// </summary>
			internal long ChipId { get; private set; }

			/// <summary>
			/// MAC address of the ESP32/ESP8266 chip
			/// </summary>
			internal PhysicalAddress ChipMacAddress { get; private set; }

			/// <summary>
			/// Flash manufacturer ID: See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details
			/// </summary>
			internal byte FlashManufacturerId { get; private set; }

			/// <summary>
			/// Flash device type ID: See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details
			/// </summary>
			internal short FlashDeviceModelId { get; private set; }

			/// <summary>
			/// The size of the flash in bytes; 4 MB = 0x40000 bytes
			/// </summary>
			internal int FlashSize { get; private set; }

			/// <summary>
			/// Constructor
			/// </summary>
			/// <param name="toolVersion">Version of the esptool.py</param>
			/// <param name="chipName">Name of the ESP32/ESP8266 chip</param>
			/// <param name="chipFeatures">ESP32/ESP8266 chip features</param>
			/// <param name="chipId">ID of the ESP32/ESP8266 chip</param>
			/// <param name="chipMacAddress">MAC address of the ESP32/ESP8266 chip</param>
			/// <param name="flashManufacturerId">Flash manufacturer ID: See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details</param>
			/// <param name="flashDeviceModelId">Flash device type ID: See http://code.coreboot.org/p/flashrom/source/tree/HEAD/trunk/flashchips.h for more details</param>
			/// <param name="flashSize">The size of the flash in bytes; e.g. 4 MB = 0x40000 bytes</param>
			internal Info(Version toolVersion, string chipName, string chipFeatures, long chipId, PhysicalAddress chipMacAddress, byte flashManufacturerId, short flashDeviceModelId, int flashSize)
			{
				ToolVersion = toolVersion;
				ChipName = chipName;
				ChipFeatures = chipFeatures;
				ChipId = chipId;
				ChipMacAddress = chipMacAddress;
				FlashManufacturerId = flashManufacturerId;
				FlashDeviceModelId = flashDeviceModelId;
				FlashSize = flashSize;
			}
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="serialPort">The serial port over which all the communication goes.</param>
		/// <param name="baudRate">The baud rate for the serial port.</param>
		/// <param name="chipType">ESP chip type. Only ESP32 and ESP8266 are allowed.</param>
		/// <param name="flashMode">The flash mode for the esptool: See https://github.com/espressif/esptool#flash-modes for more details.</param>
		/// <param name="flashFrequency">The flash frequency for the esptool: See https://github.com/espressif/esptool#flash-modes for more details.</param>
		internal EspTool(string serialPort, int baudRate, string chipType, string flashMode, int flashFrequency)
		{
			// test serial port settings
			if (string.IsNullOrEmpty(serialPort))
			{
				throw new ArgumentNullException("serialPort");
			}
			// open/close the port to see if it is available
			using (SerialPort test = new SerialPort(serialPort, baudRate))
			{
				test.Open();
				test.Close();
			}

			// save the settings
			_serialPort = serialPort;
			_baudRate = baudRate;
			_chipType = chipType;
			_flashMode = flashMode;
			_flashFrequency = flashFrequency;

			// extract esptool.zip if not already done
			if (!Directory.Exists("esptool"))
			{
				Console.WriteLine("Extracting esptool ...");
				File.WriteAllBytes("esptool.zip", Resources.esptool);
				ZipFile.ExtractToDirectory("esptool.zip", "esptool");
			}
			// if we flash the ESP8266 we need the default data and a blank block
			if (_chipType == Program.ESP8266)
			{
				if (!File.Exists(@"esptool\esp_init_data_default.bin"))
				{
					File.WriteAllBytes(@"esptool\esp_init_data_default.bin", Resources.esp_init_data_default);
				}
				if (!File.Exists(@"esptool\blank.bin"))
				{
					File.WriteAllBytes(@"esptool\blank.bin", Resources.blank);
				}
			}
		}

		/// <summary>
		/// Tests the connection to the ESP32/ESP8266 chip
		/// </summary>
		/// <returns>The filled info structure with all the information about the connected ESP32/ESP8266 chip or null if an error occured</returns>
		internal Info? TestChip()
		{
			// execute chip_id command and parse the result
			if (!RunEspTool("chip_id", true, null, out string messages))
			{
				Console.WriteLine(messages);
				return null;
			}
			Match match = Regex.Match(messages, "(esptool.py v)(?<version>[0-9.]+)(.*?[\r\n]*)*(Chip is )(?<name>.*)(.*?[\r\n]*)*(Features: )(?<features>.*)(.*?[\r\n]*)*(Chip ID: )(?<id>.*)");
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return null;
			}
			// that gives us the version of the esptool.py, the chip name and the chip ID
			string version = match.Groups["version"].ToString().Trim();
			string name = match.Groups["name"].ToString().Trim();
			string features = match.Groups["features"].ToString().Trim();
			string id = match.Groups["id"].ToString().Trim();
			Console.WriteLine($"Executed esptool.py version {version}");
			Console.WriteLine($"Found {name} with ID {id} and features {features}");

			// execute read_mac command and parse the result
			if (!RunEspTool("read_mac", true, null, out messages))
			{
				Console.WriteLine(messages);
				return null;
			}
			match = Regex.Match(messages, "(MAC: )(?<mac>.*)");
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return null;
			}
			// that gives us the MAC address
			string mac = match.Groups["mac"].ToString().Trim();
			Console.WriteLine($"MAC address: {mac}");

			// execute flash_id command and parse the result
			if (!RunEspTool("flash_id", true, null, out messages))
			{
				Console.WriteLine(messages);
				return null;
			}
			match = Regex.Match(messages, $"(Manufacturer: )(?<manufacturer>.*)(.*?[\r\n]*)*(Device: )(?<device>.*)(.*?[\r\n]*)*(Detected flash size: )(?<size>.*)");
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return null;
			}
			// that gives us the flash manufacturer, flash device type ID and flash size
			string manufacturer = match.Groups["manufacturer"].ToString().Trim();
			string device = match.Groups["device"].ToString().Trim();
			string size = match.Groups["size"].ToString().Trim();
			Console.WriteLine($"Flash information: manufacturer 0x{manufacturer} device 0x{device} size {size}");

			// collect and return all information
			// convert the flash size into bytes
			string unit = size.Substring(size.Length - 2).ToUpperInvariant();
			_flashSize = int.Parse(size.Remove(size.Length - 2)) * (unit == "MB" ? 0x100000 : unit == "KB" ? 0x400 : 1);
			return new Info(
				new Version(version), // esptool.py version
				name, // ESP32/ESP8266 name
				features, // ESP32/ESP8266 chip features
				long.Parse(id.Substring(2), NumberStyles.HexNumber), // ESP32/ESP8266 Chip-ID
				PhysicalAddress.Parse(mac.Replace(':', '-').ToUpperInvariant()), // ESP32/ESP8266 MAC address
				byte.Parse(manufacturer, NumberStyles.AllowHexSpecifier), // flash manufacturer ID
				short.Parse(device, NumberStyles.HexNumber),  // flash device ID
				_flashSize);  // flash size in bytes (converted from megabytes)
		}

		/// <summary>
		/// Backup the entire flash into a bin file
		/// </summary>
		/// <param name="backupFilename">Backup file incl. full path</param>
		/// <param name="flashSize">Flash size in bytes</param>
		/// <returns>true if successful</returns>
		internal bool BackupFlash(string backupFilename, int flashSize)
		{
			// execute read_flash command and parse the result; progress message can be found be searching for backspaces (ASCII code 8)
			if (!RunEspTool($"read_flash 0 0x{flashSize:X} \"{backupFilename}\"", false, (char)8, out string messages))
			{
				Console.WriteLine(messages);
				return false;
			}
			Match match = Regex.Match(messages, "(?<message>Read .*)(.*?\n)*");
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return false;
			}
			Console.WriteLine(match.Groups["message"].ToString().Trim());
			return true;
		}

		/// <summary>
		/// Erase the entire flash of the ESP32/ESP8266 chip
		/// </summary>
		/// <returns>true if successful</returns>
		internal bool EraseFlash()
		{
			// execute erase_flash command and parse the result
			if (!RunEspTool("erase_flash", false, null, out string messages))
			{
				Console.WriteLine(messages);
				return false;
			}
			Match match = Regex.Match(messages, "(?<message>Chip erase completed successfully.*)(.*?\n)*");
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return false;
			}
			Console.WriteLine(match.Groups["message"].ToString().Trim());
			return true;
		}

		/// <summary>
		/// Write to the flash
		/// </summary>
		/// <param name="partsToWrite">dictionary which keys are the start addresses and the values are the complete filenames (the bin files)</param>
		/// <returns>true if successful</returns>
		internal bool WriteFlash(Dictionary<int, string> partsToWrite)
		{
			// put the parts to flash together and prepare the regex for parsing the output
			StringBuilder partsArguments = new StringBuilder();
			StringBuilder regexPattern = new StringBuilder();
			int counter = 1;
			List<string> regexGroupNames = new List<string>();
			foreach (KeyValuePair<int, string> part in partsToWrite)
			{
				// start address followed by filename
				partsArguments.Append($"0x{part.Key:X} \"{part.Value}\" ");
				// test for message in output
				regexPattern.Append($"(?<wrote{counter}>Wrote.*[\r\n]*Hash of data verified.)(.*?[\r\n]*)*");
				regexGroupNames.Add($"wrote{counter}");
				counter++;
			}
			// if flash size was detected already use it for the --flash_size parameter; otherwise use the default "detect"
			string flashSize = "detect";
			if (_flashSize >= 0x100000)
			{
				flashSize = $"{_flashSize / 0x100000}MB";
			}
			else if (_flashSize > 0)
			{
				flashSize = $"{_flashSize / 0x400}KB";
			}
			// execute write_flash command and parse the result; progress message can be found be searching for linefeed
			if (!RunEspTool($"write_flash --flash_mode {_flashMode} --flash_freq {_flashFrequency / 1000000}m --flash_size {flashSize} {partsArguments.ToString().Trim()}", false, '\r', out string messages))
			{
				Console.WriteLine(messages);
				return false;
			}
			Match match = Regex.Match(messages, regexPattern.ToString());
			if (!match.Success)
			{
				Console.WriteLine(messages);
				return false;
			}
			foreach (string groupName in regexGroupNames)
			{
				Console.WriteLine(match.Groups[groupName].ToString().Trim());
			}
			return true;
		}

		/// <summary>
		/// Run the esptool one time
		/// </summary>
		/// <param name="commandWithArguments">the esptool command (e.g. write_flash) incl. all arguments (if needed)</param>
		/// <param name="noStub">if true --no-stub will be added; the chip_id, read_mac and flash_id commands can be quicker executes without uploading the stub program to the chip</param>
		/// <param name="tryToShowProgress">Tries to show progress every second if possible</param>
		/// <param name="messages">StandardOutput and StandardError messages that the esptool prints out</param>
		/// <returns>true if the esptool exit code was 0; false otherwise</returns>
		private bool RunEspTool(string commandWithArguments, bool noStub, char? progressTestChar, out string messages)
		{
			// create the process start info
			// if we can directly talt to the ROM bootloader without a stub program use the --no-stub option
			// --nostub requires to not change the baudrate (ROM doesn't support changing baud rate. Keeping initial baud rate 115200)
			string noStubParameter = null;
			string baudRateParameter = null;
			if (noStub)
			{
				// using no stub and can't change the baud rate
				noStubParameter = "--no-stub";
			}
			else
			{
				// using the stub that supports changing the baudrate
				baudRateParameter = $"--baud {_baudRate}";
			}

			// prepare the process start of the esptool
			Process espTool = new Process();
			espTool.StartInfo = new ProcessStartInfo(@"esptool\esptool.exe", $"--port {_serialPort} {baudRateParameter} --chip {_chipType.ToLowerInvariant()} {noStubParameter} --after no_reset {commandWithArguments}");
			espTool.StartInfo.UseShellExecute = false;
			espTool.StartInfo.RedirectStandardError = true;
			espTool.StartInfo.RedirectStandardOutput = true;

			// start esptool and wait for exit
			if (espTool.Start())
			{
				// if no progress output needed wait unlimited time until esptool exit
				if (!progressTestChar.HasValue)
				{
					espTool.WaitForExit();
				}
			}
			else
			{
				Console.WriteLine("Error starting esptool!");
			}

			StringBuilder messageBuilder = new StringBuilder();
			// showing progress is a little bit tricky
			if (progressTestChar.HasValue)
			{
				// loop until esptool exit
				while (!espTool.HasExited)
				{
					// loop until there is no next char to read from standard output
					while (true)
					{
						int next = espTool.StandardOutput.Read();
						if (next != -1)
						{
							// append the char to the message buffer
							char nextChar = (char)next;
							messageBuilder.Append((char)next);
							// try to find a progress message
							string progress = FindProgress(messageBuilder, progressTestChar.Value);
							if (progress != null)
							{
								// print progress and set the cursor to the beginning of the line (\r)
								Console.Write(progress);
								Console.Write("\r");
							}
						}
						else
						{
							break;
						}
					}
				}
				// collect the last messages
				messageBuilder.AppendLine(espTool.StandardOutput.ReadToEnd());
				messageBuilder.Append(espTool.StandardError.ReadToEnd());
			}
			else
			{
				// collect all messages
				messageBuilder.AppendLine(espTool.StandardOutput.ReadToEnd());
				messageBuilder.Append(espTool.StandardError.ReadToEnd());
			}
			// true if exit code was 0 (success)
			messages = messageBuilder.ToString();
			return espTool.ExitCode == 0;
		}

		/// <summary>
		/// Try to find a progress message in the esptool output
		/// </summary>
		/// <param name="messageBuilder">esptool output</param>
		/// <param name="progressTestChar">search char for the progress message delimiter (backspace or linefeed)</param>
		/// <returns></returns>
		private string FindProgress(StringBuilder messageBuilder, char progressTestChar)
		{
			// search for the given char (backspace or linefeed)
			// only if we have 100 chars at minimum and only if the last char is the test char
			if (messageBuilder.Length > 100 && messageBuilder[messageBuilder.Length - 1] == progressTestChar && messageBuilder[messageBuilder.Length - 2] != progressTestChar)
			{
				// trim the test char and convert \r\n into \r
				string progress = messageBuilder.ToString().Trim(progressTestChar).Replace("\r\n", "\r");
				// another test char in the message?
				int delimiter = progress.LastIndexOf(progressTestChar);
				if (delimiter > 0)
				{
					// then we found a progress message; pad the message to 110 chars because no message is longer than 110 chars
					return progress.Substring(delimiter + 1).PadRight(110);
				}
			}
			// no progress message found
			return null;
		}
	}
}
