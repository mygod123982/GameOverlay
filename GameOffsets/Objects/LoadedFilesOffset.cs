﻿namespace GameOffsets.Objects
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct LoadedFilesRootObject
    {
        [FieldOffset(0x00)] public IntPtr FilesArray; // FilesArrayStructure
        [FieldOffset(0x08)] public long ArrayCapacity;
        [FieldOffset(0x10)] public int Unknown0;
        [FieldOffset(0x18)] public long TotalElements;
        [FieldOffset(0x20)] public long PAD_20;
        public static int TotalCount = 0x10;
        public static int SkipBytes = 0x08;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FilesArrayStructure
    {
        public byte Flag0;
        public byte Flag1;
        public byte Flag2;
        public byte Flag3;
        public byte Flag4;
        public byte Flag5;
        public byte Flag6;
        public byte Flag7;

        public FilesPointerStructure Pointer0;
        public FilesPointerStructure Pointer1;
        public FilesPointerStructure Pointer2;
        public FilesPointerStructure Pointer3;
        public FilesPointerStructure Pointer4;
        public FilesPointerStructure Pointer5;
        public FilesPointerStructure Pointer6;
        public FilesPointerStructure Pointer7;
        public static byte InValidPointerFlagValue = 0xFF;
        public static int MaximumBuckets = 0x200;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FilesPointerStructure
    {
        public IntPtr Useless0;
        public IntPtr FilesPointer;
        public IntPtr Useless1;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct FileInfoValueStruct
    {
        [FieldOffset(0x08)] public StdWString Name;
        //[FieldOffset(0x28)] public int FileType;
        //[FieldOffset(0x30)] public IntPtr UnknownPtr;
        [FieldOffset(0x38)] public int AreaChangeCount;

        // This saves a hell lot of memory but for debugging purposes
        // Feel free to set it to 0.
        public static readonly int IGNORE_FIRST_X_AREAS = 2;
    }
}
