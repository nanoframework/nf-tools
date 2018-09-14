# EspFirmwareFlasher

## A little command line tool that does the upload of the nanoCLR automatically
It makes use of the esptool. You can find the esptool usage and license information here: See usage and license information at http://github.com/espressif/esptool
### Usage
1. Unzip it into a folder
2. Bring your ESP32 into download mode, connect it via USB cable to your PC and find out on which serial port it is connected
3. Start EspFirmwareFlasher.exe and enter the serial port
4. Wait a few seconds. Your ESP32 is flashed with the latest nanoCLR firmware.

### Return codes
If the tool will be used as part of an automatic process the return code can be useful to find out what is gone wrong. Here are the known return codes:
```
0: successful executed; no error;
-1: an internal exception occured
-2: couldn't connect to the ESP chip or couldn't retrive the infos from the ESP chip
-3: ESP chip has an unexpected flash size; e.g. only 2MB and 4MB flash sizes are supported for the nanoCLR
-4: The firmware couldn't downloaded
-5: Error during flash erase
-6: Error during writing to flash
-7: Error during flash backup
-8: Can't find the backup file
-9: Can't find the application binary
```

### What it does
1. Extract the esptool.zip (that's the original esptool.py packaged with pyinstaller)
2. Connect via esptool to the ESP32 and find out information about the connected chip
3. Download the latest firmware from https://bintray.com/nfbot/nanoframework-images-dev and unzip it
4. Erase the entire flash
5. Write the bootloader, nanoCLR, partitionTable and optinally the application that runs on top the nanoFramework into the ESP32 flash

### Optional configuration
You can deliver the following command line arguments as name=value pairs:
```
--help or -h for a description which command line parameters can be used.
--port or -p for the serial port to use (e.g. --port=COM1).
--baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).
--chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.
--flash_mode or -m for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.
--flash_freq or -f for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.
--backup or -s for backup the entire flash into a bin file for later restore (e.g. --backup=LastKnownGood). The backup file will be created in the subdirectory "Backup" with the name %ChipType%_%ChipId%_%Filename%.bin (e.g. ESP32_0x12345678_LastKnownGood.bin). If this file already exists it will be overwritten!
--backup_only or -o if present only the backup will be stored. Makes only senses if the --backup/-s option is also present.
--restore or -r restore the entire flash from a backup file that's created with the --backup/-s parameter (e.g. --restore=LastKnownGood). The backup should be in the \"Backup\" subdirectory an should be named %ChipType%_%ChipId%_%Filename%.bin or %Filename%.bin
--firmware_tag or -t if present the firmware with this tag (e.g. 0.1.0-preview.738) will be downloaded; if not present the latest version will be used.
--application or -a for the application binary that runs on top of nanoFramework (e.g. --application=MyAwesomeApp.bin)
```
If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode "dio" and the flash frequency "40m". You can set other default values via EspFirmwareFlasher.exe.config (application configuration) file.