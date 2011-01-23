﻿using System;
using MIP.Interfaces;

namespace MIPLIB.EndPoints.Output
{
    public class Light : OutputEndpoint
    {
        #region Overrides of OutputEndpoint

        public override IEndpointState State { get; set; }
        public override IConnection ConnectedTo { get; set; }

        public override string Name { get; set; }

        #endregion
    }
}