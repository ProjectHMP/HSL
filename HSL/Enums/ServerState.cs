﻿using System;
using System.Collections.Generic;
using System.Text;

namespace HSL.Enums
{
    public enum ServerState : byte
    {
        Stopped = 0x0,
        Started = 0x2,
        Restarting = 0x4
    }
}
