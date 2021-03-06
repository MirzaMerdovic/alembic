﻿using System;

namespace Alembic.Common.Contracts
{
    public class HealthLog
    {
        public DateTime Start { get; set; }

        public DateTime End { get; set; }

        public int ExitCode { get; set; }

        public string Output { get; set; }
    }
}