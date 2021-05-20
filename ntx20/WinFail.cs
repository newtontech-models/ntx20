using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ntx20
{
    public static class WinFail
    {
        [DllImport("kernel32.dll")]
        public static extern int SetErrorMode(ErrorModes newMode);


        [Flags]
        public enum ErrorModes
        {
            Default = 0x0,
            FailCriticalErrors = 0x1, //tohle
            NoGpFaultErrorBox = 0x2, // + tohle
            NoAlignmentFaultExcept = 0x4,
            NoOpenFileErrorBox = 0x8000,//+ tohle
        }
    }

}
