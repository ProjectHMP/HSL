using HSL.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace HSL
{
    internal static class Constants
    {

        public readonly static Episode[] Episodes = new Episode[] { Episode.IV, Episode.TLAD, Episode.TBOGT };
        public readonly static LogLevel[] LogLevels = new LogLevel[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warn, LogLevel.Error, LogLevel.Critical, LogLevel.Off };

    }
}
