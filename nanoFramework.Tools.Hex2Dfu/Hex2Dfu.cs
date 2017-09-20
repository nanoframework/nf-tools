//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) COPYRIGHT 2015 STMicroelectronics
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools
{
    public class Hex2Dfu
    {

        #region constants from STDFUFiles

        /// <summary>
        /// No error.
        /// </summary>
        const uint STDFUFILES_NOERROR = 0x12340000;

        #endregion


        //a block of data to be written
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        unsafe public struct STDFUFILES_DfuImageElement
        {
            public UInt32 dwAddress;
            public UInt32 dwDataLength;
            public IntPtr Data;
        }


        #region imports from STDFUFiles.dll

        // from dumpbin
        // 1    0 00004480 STDFUFILES_AppendImageToDFUFile
        // 2    1 000044E0 STDFUFILES_CloseDFUFile
        // 3    2 00004550 STDFUFILES_CreateImage
        // 4    3 000045B0 STDFUFILES_CreateImageFromMapping
        // 5    4 00004610 STDFUFILES_CreateNewDFUFile
        // 6    5 00004680 STDFUFILES_DestroyImage
        // 7    6 00004700 STDFUFILES_DestroyImageElement
        // 8    7 00004770 STDFUFILES_DuplicateImage
        // 9    8 00004820 STDFUFILES_FilterImageForOperation
        //10    9 000048A0 STDFUFILES_GetImageAlternate
        //11    A 00004900 STDFUFILES_GetImageElement
        //12    B 00004970 STDFUFILES_GetImageName
        //13    C 000049E0 STDFUFILES_GetImageNbElement
        //14    D 00004A40 STDFUFILES_GetImageSize
        //15    E 00004A90 STDFUFILES_ImageFromFile
        //16    F 00004B10 STDFUFILES_ImageToFile
        //17   10 00004B80 STDFUFILES_OpenExistingDFUFile
        //18   11 00004C20 STDFUFILES_ReadImageFromDFUFile
        //19   12 00004CA0 STDFUFILES_SetImageElement
        //20   13 00004D20 STDFUFILES_SetImageName

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_AppendImageToDFUFile", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_AppendImageToDFUFile(IntPtr handle, IntPtr image);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_CloseDFUFile", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_CloseDFUFile(IntPtr handle);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_CreateNewDFUFile", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_CreateNewDFUFile([MarshalAs(UnmanagedType.LPStr)]String szDevicePath, ref IntPtr handle, UInt16 Vid, UInt16 Pid, UInt16 Bcd);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_CreateImage", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_CreateImage(ref IntPtr image, byte nAlternate);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_DestroyImage", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_DestroyImage(ref IntPtr handle);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_ImageFromFile", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_ImageFromFile([MarshalAs(UnmanagedType.LPStr)]String szDevicePath, ref IntPtr image, byte nAlternate);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_SetImageElement", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_SetImageElement(IntPtr handle, UInt32 dwRank, bool bInsert, [MarshalAs(UnmanagedType.Struct)]STDFUFILES_DfuImageElement Element);

        [DllImport("STDFUFiles.DLL", EntryPoint = "STDFUFILES_SetImageName", CharSet = CharSet.Auto)]
        public static extern UInt32 STDFUFILES_SetImageName(IntPtr image, [MarshalAs(UnmanagedType.LPStr)]String pPathFile);

        #endregion

        const UInt16 defaultSTMVid = 0x0483;
        const UInt16 defafultSTMPid = 0xDF11;
        const UInt16 defaultFwVersion = 0x2200;

        public static bool CreateDfuFile(string hexFile, string dfuName, UInt16 vid = defaultSTMVid, UInt16 pid = defafultSTMPid, UInt16 fwVersion = defaultFwVersion)
        {
            IntPtr dfuFileHandle = (IntPtr)0;
            IntPtr imageFileHandle = (IntPtr)0;

            // start creating a new DFU file for output
            var retCode = STDFUFILES_CreateNewDFUFile(dfuName, ref dfuFileHandle, vid, pid, fwVersion);

            if (retCode == STDFUFILES_NOERROR)
            {

                // get image from HEX file
                retCode = STDFUFILES_ImageFromFile(hexFile, ref imageFileHandle, 0);

                if (retCode == STDFUFILES_NOERROR)
                {
                    // add image of HEX file
                    retCode = STDFUFILES_AppendImageToDFUFile(dfuFileHandle, imageFileHandle);

                    if (retCode != STDFUFILES_NOERROR)
                    {
                        // error adding this file
                        Console.WriteLine();
                        Console.WriteLine($"ERROR: adding {hexFile}");
                        Console.WriteLine();

                        return false;
                    }

                    Console.WriteLine($"Adding image for {hexFile}");
                }
            }

            // image file added, close DFU file
            STDFUFILES_CloseDFUFile(dfuFileHandle);

            Console.WriteLine();
            Console.WriteLine($"DFU generated: {dfuName}");
            Console.WriteLine($"Vendor ID: {vid.ToString("X4")}");
            Console.WriteLine($"Product ID: {pid.ToString("X4")}");
            Console.WriteLine($"Version: {fwVersion.ToString("X4")}");
            Console.WriteLine();

            // clean-up
            if (retCode == STDFUFILES_NOERROR)
            {
                // destroy image
                STDFUFILES_DestroyImage(ref imageFileHandle);
            }

            return true;
        }

        public static bool CreateDfuFile(List<BinaryFileInfo> binFiles, string dfuName, UInt16 vid = defaultSTMVid, UInt16 pid = defafultSTMPid, UInt16 fwVersion = defaultFwVersion)
        {
            IntPtr dfuFileHandle = (IntPtr)0;
            IntPtr imageFileHandle = (IntPtr)0;

            // start creating a new DFU file for output
            var retCode = STDFUFILES_CreateNewDFUFile(dfuName, ref dfuFileHandle, vid, pid, fwVersion);

            if (retCode == STDFUFILES_NOERROR)
            {
                retCode = STDFUFILES_CreateImage(ref imageFileHandle, 0);

                retCode = STDFUFILES_SetImageName(imageFileHandle, "nanoFramework");
                uint fileCounter = 0;

                // loop through collection of bin files and add them
                foreach (BinaryFileInfo file in binFiles)
                {
                    byte[] fileData = File.ReadAllBytes(file.FileName);

                    // get required memory size for byte array
                    int size = Marshal.SizeOf(fileData[0]) * fileData.Length;

                    STDFUFILES_DfuImageElement element = new STDFUFILES_DfuImageElement();
                    element.dwAddress = file.Address;
                    element.dwDataLength = (uint)fileData.Length;
                    
                    // allocate memory from the unmanaged memory
                    element.Data = Marshal.AllocHGlobal(size);

                    // copy the byte array to the struct
                    Marshal.Copy(fileData, 0, element.Data, fileData.Length);

                    // get image from HEX file
                    retCode = STDFUFILES_SetImageElement(imageFileHandle, fileCounter++, true, element);

                    // free unmanaged memory
                    Marshal.FreeHGlobal(element.Data);

                    if (retCode != STDFUFILES_NOERROR)
                    {
                        // error adding this file
                        Console.WriteLine();
                        Console.WriteLine($"ERROR: adding {file.FileName}");
                        Console.WriteLine();

                        return false;
                    }

                    Console.WriteLine($"Adding file to image: {file.FileName}");
                }

                // add image to DFU file
                retCode = STDFUFILES_AppendImageToDFUFile(dfuFileHandle, imageFileHandle);

                if (retCode != STDFUFILES_NOERROR)
                {
                    // error adding this file
                    Console.WriteLine();
                    Console.WriteLine($"ERROR: adding image to DFU file");
                    Console.WriteLine();

                    return false;
                }

                // image file added, close DFU file
                STDFUFILES_CloseDFUFile(dfuFileHandle);

                Console.WriteLine();
                Console.WriteLine($"DFU generated: {dfuName}");
                Console.WriteLine($"Vendor ID: {vid.ToString("X4")}");
                Console.WriteLine($"Product ID: {pid.ToString("X4")}");
                Console.WriteLine($"Version: {fwVersion.ToString("X4")}");
                Console.WriteLine();

                // clean-up
                if (retCode == STDFUFILES_NOERROR)
                {
                    // destroy image
                    STDFUFILES_DestroyImage(ref imageFileHandle);
                }
            }

            return true;
        }

    }

    public class BinaryFileInfo
    {
        public string FileName { get; private set; }
        public uint Address { get; private set; }

        public BinaryFileInfo(string fileName, uint address)
        {
            FileName = fileName;
            Address = address;
        }
    }
}
