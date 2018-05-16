using System;
using System.Collections.Generic;
using System.Configuration;

namespace EspFirmwareFlasher
{
	/// <summary>
	/// Main class
	/// </summary>
	internal class Program
	{
		/// <summary>
		/// Entry point
		/// </summary>
		/// <param name="args">You can deliver the following arguments as name=value pairs:
		/// --help or -h for a description which command line parameters can be used.
		/// --port or -p for the serial port to use (e.g. --port=COM1).
		/// --baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).
		/// --chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.
		/// --flash_mode or -fm for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.
		/// --flash_freq or -ff for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.
		/// </param>
		/// <remarks>
		/// If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode "dio" and the flash frequency "40m".
		/// You can set other default values via EspFirmwareFlasher.exe.config (application configuration) file.
		/// </remarks>
		/// <returns>0: successful executed; no error;
		/// -1: an internal exception occured
		/// -2: couldn't connect to the ESP chip or couldn't retrive the infos from the ESP chip
		/// -3: ESP chip has an unexpected flash size; e.g. only 2MB and 4MB flash sizes are supported for the nanoCLR
		/// -4: The firmware couldn't downloaded
		/// -5: Error during flash erase
		/// -6: Error during writing to flash
		/// </returns>
		private static int Main(string[] args)
		{
			try
			{
				string serialPort = null;
				int baudRate = 921600;
				string chipType = "ESP32";
				string flashMode = "dio";
				int flashFrequency = 40000000;
				string firmwareType = "nanoCLR";
				string downloadSource = "https://bintray.com/nfbot/nanoframework-images-dev";
				string boardType = "ESP32_DEVKITC";
				bool showHelp = false;

				Console.WriteLine($"ESP Firmware Flash Tool - {typeof(Program).Assembly.GetName().Version.ToString(3)}");

				// configure from application settings
				string setting = ConfigurationManager.AppSettings.Get("DefaultSerialPort");
				if (!string.IsNullOrEmpty(setting))
				{
					serialPort = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultBaudRate");
				if (!string.IsNullOrEmpty(setting))
				{
					baudRate = Convert.ToInt32(setting);
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultEspChipType");
				if (!string.IsNullOrEmpty(setting))
				{
					chipType = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFlashMode");
				if (!string.IsNullOrEmpty(setting))
				{
					flashMode = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFlashFrequency");
				if (!string.IsNullOrEmpty(setting))
				{
					flashFrequency = setting.ToUpperInvariant().EndsWith("M") ? Convert.ToInt32(setting.Substring(0, setting.Length - 1)) * 1000000 : Convert.ToInt32(setting);
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultFirmwareType");
				if (!string.IsNullOrEmpty(setting))
				{
					firmwareType = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultDownloadSource");
				if (!string.IsNullOrEmpty(setting))
				{
					downloadSource = setting;
				}
				setting = ConfigurationManager.AppSettings.Get("DefaultBoardType");
				if (!string.IsNullOrEmpty(setting))
				{
					boardType = setting;
				}

				// parse command line
				if (args != null || args.Length > 0)
				{
					foreach (string arg in args)
					{
						if (arg == "--help" || arg == "-h")
						{
							showHelp = true;
							break;
						}

						string[] parts = arg.Split('=');
						if (parts.Length != 2)
						{
							continue;
						}
						switch (parts[0].Trim())
						{
							case "--port":
							case "-p":
								serialPort = parts[1].Trim();
								break;
							case "--baud":
							case "-b":
								baudRate = Convert.ToInt32(parts[1].Trim());
								break;
							case "--chip":
							case "-c":
								chipType = parts[1].Trim();
								break;
							case "--flash_mode":
							case "-fm":
								flashMode = setting.Trim();
								break;
							case "--flash_freq":
							case "-ff":
								string trimmed = parts[1].Trim();
								flashFrequency = trimmed.ToUpperInvariant().EndsWith("M") ? Convert.ToInt32(trimmed.Substring(0, trimmed.Length - 1)) * 1000000 : Convert.ToInt32(trimmed);
								break;
						}
					}
				}

				if (showHelp)
				{
					Console.WriteLine();
					Console.WriteLine("You can deliver the following arguments as name = value pairs:");
					Console.WriteLine("--help or -h for a description which command line parameters can be used.");
					Console.WriteLine("--port or -p for the serial port to use (e.g. --port=COM1).");
					Console.WriteLine("--baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).");
					Console.WriteLine("--chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.");
					Console.WriteLine("--flash_mode or -fm for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.");
					Console.WriteLine("--flash_freq or -ff for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.");
					Console.WriteLine();
					Console.WriteLine("If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode \"dio\" and the flash frequency \"40m\".");
					Console.WriteLine("You can set other default values via NanoClrEsp32Flasher.exe.config (application configuration) file.");
					Console.WriteLine();
					Console.WriteLine("The tool delivers the following exit codes:");
					Console.WriteLine("0: successful executed; no error");
					Console.WriteLine("-1: An internal exception occured");
					Console.WriteLine("-2: Couldn't connect to the ESP chip or couldn't retrive the infos from the ESP chip");
					Console.WriteLine("-3: ESP chip has an unexpected flash size; e.g. only 2MB and 4MB flash sizes are supported for the nanoCLR");
					Console.WriteLine("-4: The firmware couldn't downloaded");
					Console.WriteLine("-5: Error during flash erase");
					Console.WriteLine("-6: Error during writing to flash");
					return 0;
				}

				// determine the serial port
				if (serialPort == null)
				{
					Console.WriteLine($"At which serial port is an {chipType} module waiting in download mode? (e.g. COM1)");
					serialPort = Console.ReadLine();
				}

				// print out the used parameters
				Console.WriteLine($"Using {serialPort} with {baudRate} baud for connecting to an {chipType}.");
				Console.WriteLine($"Flashing will be done with mode {flashMode} at {flashFrequency / 1000000} MHz.");
				Console.WriteLine($"{firmwareType} firmware will be downloaded from: {downloadSource}");

				// get alle availabe info form esptool; this tests the connection to the chip
				Console.WriteLine($"Trying to connect and getting infos about the {chipType} chip ...");
				EspTool espTool = new EspTool(serialPort, baudRate, chipType, flashMode, flashFrequency);
				EspTool.Info? info = espTool.TestChip();
				if (!info.HasValue)
				{
					return -2;
				}

				Firmware firmware = null;
				if (firmwareType == "nanoCLR")
				{
					// download nanoCLR firmware from bintray.com
					Console.WriteLine($"Downloading {boardType} nanoCLR firmware from {downloadSource} ...");
					firmware = new NanoClrFirmware(downloadSource, boardType);
				}
				else if (firmwareType == "WifiWaterLevelGauge")
				{
					// download WifiWaterLevelGauge firmware from github.com
					Console.WriteLine($"Downloading WifiWaterLevelGauge firmware from {downloadSource} ...");
					firmware = new WifiWaterLevelGaugeFirmware(downloadSource);
				}
				if (firmware == null || !firmware.CheckSupport(chipType, info.Value.FlashSize))
				{
					return -3;
				}
				Dictionary<int, string> firmwareParts = firmware.DownloadAndExtract(chipType, info.Value.FlashSize);
				if (firmwareParts == null)
				{
					return -4;
				}

				// erase flash
				Console.WriteLine($"Erasing flash ...");
				if (!espTool.EraseFlash())
				{
					return -5;
				}

				// write to flash
				Console.WriteLine($"Flashing firmware ... this can take while ...");
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
	}
}
