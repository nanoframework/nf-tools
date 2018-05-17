# EspFirmwareFlasher

## A little command line tool that does the upload of the nanoCLR automatically

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
```

### What it does
1. Extract the esptool.zip (that's the original esptool.py packaged with pyinstaller)
2. Connect via esptool to the ESP32 and find out information about the connected chip
3. Download the latest firmware from https://bintray.com/nfbot/nanoframework-images-dev and unzip it
4. Erase the entire flash
5. Write the bootloader, nanoCLR and partitionTable into the ESP32 flash

### Optional configuration
You can deliver the following command line arguments as name=value pairs:
```
--help or -h for a description which command line parameters can be used.
--port or -p for the serial port to use (e.g. --port=COM1).
--baud or -b for the baud rate to use for the serial port (e.g. --baud=921600).
--chip or -c for the connected ESP chip type (e.g. --chip=ESP32). Only ESP32 and ESP8266 are allowed.
--flash_mode or -fm for the flash mode to use (e.g. --flash_mode=dio). See https://github.com/espressif/esptool#flash-modes for more details.
--flash_freq or -ff for the flash frequency to use (e.g. --flash_freq=40m). See https://github.com/espressif/esptool#flash-modes for more details.
```
If no arguments are delivered the tool asks for the serial port at the command line. Then it uses the baudrate 921600, the flash mode "dio" and the flash frequency "40m". You can set other default values via EspFirmwareFlasher.exe.config (application configuration) file.