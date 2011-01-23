﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HAL
{
    public class SwitchEndpoint : IHardwareEndpoint
    {
        [Switch]
        public IEndpointStateMapper Mapper { get; set; }
        public IHardwareEndpointIndentifier ID { get; set; }
    }
}