﻿namespace GameOffsets.Objects
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct LoadedFilesRootObject
    {
        [FieldOffset(0x00)] public float IsValid;
        //[FieldOffset(0x04)] public int intUseless;
        [FieldOffset(0x08)] public StdList FilesList; //FileInfoPtr
        [FieldOffset(0x18)] public StdVector FilesVectorUseless;
        [FieldOffset(0x30)] public long TemplateId1;
        [FieldOffset(0x38)] public long TemplateId2; // Not sure but works!
        public static int TotalCount = 128;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct FilesKeyValueStruct
    {
        [FieldOffset(0x00)] public IntPtr KeyPtr;
        [FieldOffset(0x08)] public IntPtr ValuePtr; //FileInfoValueStruct
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct FileInfoValueStruct
    {
        [FieldOffset(0x08)] public StdWString Name;
        //[FieldOffset(0x28)] public int FileType;
        //[FieldOffset(0x30)] public IntPtr UnknownPtr;

        //[FieldOffset(0x08)] public int Unknown1;
        //[FieldOffset(0x0C)] public int Unknown2;
        //[FieldOffset(0x10)] public IntPtr IgnoreMe;
        [FieldOffset(0x18)] public StdWString Name2;

        [FieldOffset(0x38)] public int AreaChangeCount;
        //[FieldOffset(0x3C)] public int CounterRelatedToAreaChange;
        //[FieldOffset(0x40)] public int CounterWhichStopsWhenGameIsMinimized;

        // This saves a hell lot of memory but for debugging purposes
        // Feel free to set it to 0.
        public static readonly int IGNORE_FIRST_X_AREAS = 2;
    }
}