//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Utility.CommandLine;

namespace nanoFramework.Tools
{
    class Program
    {
        [Argument('h', "hexfile")]
        private static string HexFile { get; set; }

        [Argument('b', "binfile")]
        private static List<string> BinFiles { get; set; }

        [Argument('a', "address")]
        private static List<string> Addresses { get; set; }


        [Argument('o', "outputdfu")]
        private static string OutputDfuFile { get; set; }

        [Argument('v', "vid")]
        private static string Vid { get; set; }

        [Argument('p', "pid")]
        private static string Pid { get; set; }

        [Argument('f', "fwversion")]
        private static string FirmwareVersion { get; set; }


        private static ushort _Vid => ushort.Parse(Vid, System.Globalization.NumberStyles.HexNumber);
        private static ushort _Pid => ushort.Parse(Pid, System.Globalization.NumberStyles.HexNumber);
        private static ushort _FirmwareVersion => ushort.Parse(FirmwareVersion, System.Globalization.NumberStyles.HexNumber);

        static void Main(string[] args)
        {
            Arguments.Populate();

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine($"nanoFramework HEX2DFU converter v{Assembly.GetExecutingAssembly().GetName().Version}");
            Console.WriteLine($"Copyright (c) 2017 nanoFramework project contributors");
            Console.WriteLine();


            // output usage help if no arguments are specified
            if (args.Count() == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine(" adding a single HEX file: hex2dfu -h=hex_file_name -o=output_DFU_image_file_name");
                Console.WriteLine(" adding one or more BIN files: hex2dfu -b=bin_file_name -a=address_to_flash [-b=bin_file_name_N -a=address_to_flash_N] -o=output_DFU_image_file_name");
                Console.WriteLine();
                Console.WriteLine("  options:");
                Console.WriteLine();
                Console.WriteLine(@"     [-v=""0000""] (VID of target USB device (hexadecimal format), leave empty to use STM default)");
                Console.WriteLine(@"     [-p=""0000""] (PID of target USB device (hexadecimal format), leave empty to use STM default)");
                Console.WriteLine(@"     [-f=""0000""] (Firmware version of the target USB device (hexadecimal format), leave empty to use default)");
                Console.WriteLine();
            }

            // args check

            // need, at least, one hex file
            if (HexFile == null && BinFiles == null)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Need at least one HEX or BIN file to create DFU target image.");
                Console.WriteLine();
                Console.WriteLine(@"Use -h=""path-to-hex-file"" for each HEX file to add to the DFU target.");
                Console.WriteLine(@"Use -b=bin_file_name -a=address_to_flash [-b=bin_file_name_N -a=address_to_flash_N] for each BIN file to add to the DFU target.");
                Console.WriteLine();
                Console.WriteLine();
            }

            if (BinFiles != null)
            {
                // need the addresses too
                if (Addresses == null)
                {
                    Console.WriteLine();
                    Console.WriteLine("ERROR: For BIN files the addresses to flash are mandatory.");
                    Console.WriteLine();
                    Console.WriteLine(@"Use -b=bin_file_name -a=address_to_flash [-b=bin_file_name_N -a=address_to_flash_N] for each BIN file to add to the DFU target.");
                    Console.WriteLine();
                    Console.WriteLine();
                }
            }

            // output DFU file name is mandatory
            if (OutputDfuFile == null)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: Output DFU target file name is required.");
                Console.WriteLine();
                Console.WriteLine(@"Use -h=""path-to-dfu-file""");
                Console.WriteLine();
                Console.WriteLine();
            }

            if (HexFile != null && OutputDfuFile != null)
            {
                // compose the call to CreateDfuFile according to the requested parameters
                if (Vid != null && Pid != null && FirmwareVersion != null)
                {
                    Hex2Dfu.CreateDfuFile(HexFile, OutputDfuFile, _Vid, _Pid, _FirmwareVersion);
                }
                else if (Vid != null && Pid != null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(HexFile, OutputDfuFile, _Vid, _Pid);
                }
                else if (Vid != null && Pid == null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(HexFile, OutputDfuFile, _Vid);
                }
                else if (Vid == null && Pid == null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(HexFile, OutputDfuFile);
                }
            }

            if (BinFiles != null && OutputDfuFile != null)
            {
                // combine BIN files and addresses
                List<BinaryFileInfo> binFiles = new List<BinaryFileInfo>();

                var addressEnum = Addresses.GetEnumerator();

                foreach (string file in BinFiles)
                {
                    addressEnum.MoveNext();
                    binFiles.Add(new BinaryFileInfo(file, uint.Parse(addressEnum.Current, System.Globalization.NumberStyles.HexNumber)));
                }

                // compose the call to CreateDfuFile according to the requested parameters
                if (Vid != null && Pid != null && FirmwareVersion != null)
                {
                    Hex2Dfu.CreateDfuFile(binFiles, OutputDfuFile, _Vid, _Pid, _FirmwareVersion);
                }
                else if (Vid != null && Pid != null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(binFiles, OutputDfuFile, _Vid, _Pid);
                }
                else if (Vid != null && Pid == null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(binFiles, OutputDfuFile, _Vid);
                }
                else if (Vid == null && Pid == null && FirmwareVersion == null)
                {
                    Hex2Dfu.CreateDfuFile(binFiles, OutputDfuFile);
                }
            }
        }
    }
}
