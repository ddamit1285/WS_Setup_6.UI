using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace WS_Setup_6.Core.Models
{
    [SupportedOSPlatform("windows")]
    public enum UninstallPhase
    {
        StoppingProcesses,
        RunningSilent,
        ForcingDelete,
        Completed
    }

}
