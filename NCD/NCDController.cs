﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using HAL;
using MIP.Interfaces;
using Ninject;

namespace NCD
{
    public class NCDController : IHardwareController, IDisposable
    {
        public NCDController ()
        {
            //Hubs = hubs;
            CurrentState = new Dictionary<int, IEnumerable<bool>>();
            OutputStack = new Stack<ushort>();
            InputStack = new Stack<ushort>();
            //On boot load all the states into the outputendpoints via the state mapper. 
            //Select the hardware states and report those IN ORDER  to the endpoint state mapper.
            //To get the current state.

        }

        #region Controllerthread

        private static void Runner (object ncdController)
        {
            if (ncdController == null) throw new ArgumentNullException ("ncdController");
            var controller = (NCDController)ncdController;
            try
            {
                while (true)
                {
                    if (controller.OutputStack.Count > 0)
                    {
                        //  on/off banknumber    relay
                        //  0-1    0-32          0-7
                        // | 0000 | 0000 | 0000 | 0000 |

                        //  turn relay 4 on bank 3 on
                        // | 0001 | 0000 | 0011 | 0100 |

                        var input = controller.OutputStack.Pop();
                        var relay = (byte) (input & 15);
                        var bank = (byte) (input & 4080 >> 4);
                        var status = (byte) (input >> 12);
                        if (status == 0)
                            controller.NCDComponent.ProXR.RelayBanks.TurnOffRelayInBank(relay, bank);
                        else
                            controller.NCDComponent.ProXR.RelayBanks.TurnOnRelayInBank(relay, bank);

                        controller.NCDComponent.ProXR.RelayBanks.GetRelaysStatusInAllBanks();
                    }
                    else
                    {
                        //| bank        |       value |
                        //| 0000 | 0000 | 0000 | 0000 |
                        
                        for (var contactClosureBank = 0; contactClosureBank < BasicConfiguration.Configuration.NumberOfContactClosureBanks; contactClosureBank++)
                        {
                            controller.InputStack.Push((ushort)(((byte)contactClosureBank << 8) + controller.NCDComponent.ProXR.Scan.ScanValue((byte)contactClosureBank)));
                        }
                    }
                }      
            }
            catch(ThreadAbortException)
            {
                
            }
        }

        private Thread Run { get; set; }

        #endregion

        #region Inputthread

        public Stack<ushort> InputStack { get; set; }

        private static void InputRunner(object ncdController)
        {
            var controller = (NCDController) ncdController;

            try
            {
                while (true)
                {
                    if (controller.InputStack.Count > 0)
                    {
                        var val = controller.InputStack.Pop();
                        var bank = (val & 0xFF00) >> 8;
                        var value = (val & 0x00FF);
                        controller.ReportStates(bank, ParseValue(value));
                    }   
                    Thread.Sleep(5);
                }
            }
            catch(ThreadAbortException)
            {
                
            }
        }

        public IDictionary<int, IEnumerable<bool>> CurrentState { get; set; }

        private void ReportStates(int bank, IEnumerable<bool> states)
        {
            if (CurrentState.ContainsKey(bank))
            {
                var curBankState = CurrentState[bank];
                for (var i = 0; i < 8; i++)
                {
                    var i1 = i;
                    if (curBankState.ElementAt(i) != states.ElementAt(i))
                    {
                        CurrentState[bank] = states;
                        Console.WriteLine("Button " + i + " on Bank " + bank + " was pushed at " + DateTime.Now.Second + ":" + DateTime.Now.Millisecond + " state " + states.ElementAt(i));
                        if (CouplingInformation != null)
                        {
                            foreach (var couple in CouplingInformation.EndpointCouples)
                            {
                                if (couple.Item2.HardwareEndpointIndentifiers.Where(a => a.ID == "B" + bank + ":" + i && a.Type == HardwareEndpointType.Input).Count() > 0)
                                {
                                    foreach (var hub in Hubs)
                                    {
                                        var couple1 = couple;
                                        var endpoint = hub.RegisteredEndPoints.First(e => e.Name == couple1.Item1);
                                        hub.Trigger(endpoint, couple.Item2.Mapper.DetermineState(SelectState(couple.Item2)));
                                    }
                                    //Console.WriteLine(couple.Item1);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                CurrentState.Add(bank, states);
            }
        }

        private static IEnumerable<bool> ParseValue(int value)
        {
            yield return (value & 1) > 0 ? true : false;
            yield return (value & 2) > 0 ? true : false;
            yield return (value & 4) > 0 ? true : false;
            yield return (value & 8) > 0 ? true : false;
            yield return (value & 16) > 0 ? true : false;
            yield return (value & 32) > 0 ? true : false;
            yield return (value & 64) > 0 ? true : false;
            yield return (value & 128) > 0 ? true : false;
        }

        public Thread Input { get; set; }

        #endregion

        public Stack<ushort> OutputStack { get; set; }

        public NCDComponent NCDComponent { get; set; }

        public void Initialize()
        {
            var configfile = ConfigurationManager.AppSettings["ConfigurationFilePath"];
            if (File.Exists(configfile))
                BasicConfiguration.Load(configfile);
            else
            {
                new BasicConfiguration
                    {
                        Path = configfile
                    }.FillExample();
                
                BasicConfiguration.Save();
                throw new Exception("EmptyConfigurationException, Please fill the configuration with the right information and start again.");
            }
            NCDComponent = new NCDComponent {BaudRate = 38400, PortName = BasicConfiguration.Configuration.Comport};
            //ncdComponent.Port = 1;
            NCDComponent.OpenPort();
            if (!NCDComponent.IsOpen) throw new HardwareInitializationException();
        }
        
        public void InitializeEndpoints()
        {
            CoupleEndpoints (new NCDEndPointCouplingInformation ());
            foreach (var hub in Hubs)
            {
                foreach (var registeredEndPoint in hub.RegisteredEndPoints)
                {
                    foreach (var couple in CouplingInformation.EndpointCouples)
                    {
                        if (registeredEndPoint is OutputEndpoint && couple.Item1 == registeredEndPoint.Name)
                        {
                            var outputEndpoint = registeredEndPoint as OutputEndpoint;
                            outputEndpoint.StateChanged += OutputEndpointStateChanged;
                            outputEndpoint.CurrentState = couple.Item2.Mapper.DetermineState (SelectState (couple.Item2));
                        }
                    }
                }
            }
        }

        public void Start()
        {
            Run = new Thread(Runner);
            Run.Start(this);
            Input = new Thread(InputRunner);
            Input.Start(this);
        }

        public void Stop()
        {
            Run.Abort();
            Input.Abort();
        }

        public IEndPointCouplingInformation CouplingInformation { get; set; }

        public IEnumerable<IHardwareEndpointIndentifier> GetIdentifiers()
        {
            for (var i = 0; i < BasicConfiguration.Configuration.NumberOfContactClosureBanks; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    yield return new NCDHardwareIdentifier
                                     {
                                         ID = "B" + i + ":" + j,
                                         Type = HardwareEndpointType.Input
                                     };
                }
            }
            foreach (var relayBank in BasicConfiguration.Configuration.AvailableRelayBanks)
            {
                for (var i = 0; i < relayBank.AvailableRelays; i++)
                {
                    yield return new NCDHardwareIdentifier
                                     {
                                         ID = "B" + relayBank.Number + ":" + i,
                                         Type = HardwareEndpointType.Output
                                     };
                }
            }
        }

        public void CoupleEndpoints(IEndPointCouplingInformation endPointCouplingInformation)
        {
            CouplingInformation = endPointCouplingInformation.Load();
        }

        [Inject]
        public IEnumerable<IHub> Hubs { get; set; }

        void OutputEndpointStateChanged(object sender, StateChangedEventArgs eventArgs)
        {
            Console.WriteLine(eventArgs.Endpoint.Name + " is changed to " + eventArgs.Endpoint.CurrentState);
            //Find the HardwareEndpointID's corresponding to the endpoint where the state has changed.
            //Locate the HardwareEndpoint and ask the mapper for the corresponding state to put on the stack.
            //put it on the stack and "There will be light!". Maybe we should be talking in messages here.
            //To accomodate for dimmers and stuff
            //Locate hardware endpoint
            var hwe = CouplingInformation.EndpointCouples.First(c => c.Item1 == eventArgs.Endpoint.Name).Item2;
            //ask it for some control messages
            var contolMessages = hwe.Mapper.GetControllMessagesForEndpointState(eventArgs.Endpoint.CurrentState, hwe);

            foreach (var controlMessage in contolMessages)
            {
                Thread.Sleep(controlMessage.WaitTime);
                controlMessage.Enter();
            }
        }

        private Dictionary<int, bool> SelectState(IHardwareEndpoint endpoint)
        {
            var retval = new Dictionary<int, bool>();
            foreach (var hardwareEndpointIndentifier in endpoint.HardwareEndpointIndentifiers)
            {
                var hwid = hardwareEndpointIndentifier.ID;
                var bank = ushort.Parse(hwid.Substring(1, hwid.IndexOf(":") - 1));
                var relayid = ushort.Parse(hwid.Substring(hwid.IndexOf(":") + 1));
                retval.Add(CurrentState.First(kv => kv.Key == bank).Key,
                           CurrentState.First(kv => kv.Key == bank).Value.ElementAt(relayid));
            }
            return retval;
        } 

        public void Dispose()
        {
            BasicConfiguration.Save();
        }
    }

    public class HardwareInitializationException : Exception
    {
    }
}
