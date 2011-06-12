﻿using System;
using System.Collections.Generic;
using MIP.Interfaces;
using Ninject;

namespace MIPLIB.EndPoints.Output
{
    public class Light : OutputEndpoint
    {
        #region Overrides of OutputEndpoint
       
        public Light(IEnumerable<IEndpointState> states)
        {
            Hubs = new List<IHub>();
            States = states;
        }

        public override IEnumerable<IEndpointState> States { get; set; }
        //public override IEndpointState CurrentState { get; set; }
        public override bool DetermineNextState()
        {
              throw new NotImplementedException();
        }

        public override IList<IHub> Hubs { get; set; }   
        public override string Name { get; set; }
        public override void SetHub(IHub hub)
        {
            Hubs.Add(hub);
        }

        #endregion
    }
}
