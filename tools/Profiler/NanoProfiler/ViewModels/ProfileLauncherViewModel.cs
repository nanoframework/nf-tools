using CommunityToolkit.Mvvm.ComponentModel;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.NanoProfiler.Helpers;
using _DBG = nanoFramework.Tools.Debugger;
using _PRF = nanoFramework.Tools.NanoProfiler;
using _WP = nanoFramework.Tools.Debugger.WireProtocol;
using System.Windows.Media;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using System;
using nanoFramework.Tools.Debugger.WireProtocol;
using System.Windows.Threading;
using nanoFramework.Tools.Debugger.Extensions;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class ProfileLauncherViewModel: ObservableObject
    {
        #region Events
        public event NotifyDelegate<bool> EventViewLoaded;
        #endregion

        #region Commands

        [RelayCommand]
        private void ClearClicked()
        {
            TextBlockContent = string.Empty;
        }

        [RelayCommand]
        private void OpenLogFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "nanoProfiler log files (*.log)|*.log",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            // show dialog - THIS IS WINFORMS!!!!
            DialogResult result = openFileDialog.ShowDialog();

            if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(openFileDialog.FileName))
            {
                // looks like we have a valid path

                _clrProfiler.LoadLogFile(openFileDialog.FileName);

                EnableDisableViewMenuItems();

            }
            else
            {
                // any other outcome from folder browser dialog doesn't require processing
            }
        }

        [RelayCommand]
        private async void ConnectClicked()
        {
            //if (Connecter.IsBusy && !Connecter.CancellationPending)
            //{
            //    ConnectButton.IsEnabled = false;
            //}
            //else 
            if (_state == ProfilingState.Connected)
            {
                UserDisconnect();
            }
            else
            {
                if (await Connect())
                {
                    //if (result)
                    //{
                    _session.SetProfilingOptions(CallsChecked, AllocationsChecked);
                    _engine.ResumeExecution();

                    LogText("INFO: Successfully connected to nanoCLR.");
                    LogText("INFO: Using file: " + _exporter.FileName);

                    ConnectComplete();
                    //}
                    //else
                    //{
                    //    LogText("Device is unresponsive or cannot be found.");
                    //    Disconnect();
                    //}
                }
                else
                {
                    //Exception ex = (Exception)e.Error;

                    LogText(string.Format("INFO: Error connecting to device:\r\n{0}", "???"));

                    Disconnect();
                }
            }
        }

        [RelayCommand]
        private void ViewLoaded(object item)
        {
            _closing = true;
            Disconnect();
        }
        #endregion

        #region Observable Properties

        [ObservableProperty]
        private string _textBlockContent = string.Empty;


        [ObservableProperty]
        private string _connectButtonContent = "Connect";

        [ObservableProperty]
        private SolidColorBrush _backgroundProfileLauncher = Brushes.AliceBlue;

        [ObservableProperty]
        bool _rebootChecked;

        [ObservableProperty]
        private bool _callsChecked = true;

        [ObservableProperty]
        private bool _allocationsChecked;

        [ObservableProperty]
        private bool _absoluteAddressChecked = true;

        [ObservableProperty]
        private bool _connectButtonEnabled = true;

        #endregion

        #region Properties
        internal string c_Connect = "Connect";
        internal string c_Disconnect = "Disconnect";
        internal string c_Launch = "Launch...";
        internal string c_Cancel = "Cancel";

        private bool _closing;
        private ProfilingState _state;

        private NanoDeviceBase _nanoDevice;
        private readonly PortBase _serialDebuggerPort;

        private _PRF.ProfilerSession _session = null;
        private _PRF.Exporter _exporter = null;
        private _DBG.Engine _engine = null;

        private CLRProfiler.MainForm _clrProfiler = new CLRProfiler.MainForm();


        #endregion


        #region Constructor
        public ProfileLauncherViewModel()
        {
            _serialDebuggerPort = PortBase.CreateInstanceForSerial(false);
        }
        #endregion

        #region Funcs

        //partial void OnRebootCheckedChanged(bool value){}

        private void Disconnect()
        {
            if (_state == ProfilingState.Disconnected)
            {
                return;
            }

            if (_state == ProfilingState.Connected)
            {
                if (_session != null)
                {
                    _session.Disconnect();
                }

                if (_exporter != null)
                {
                    _exporter.Close();
                }
            }

            try
            {
                lock (_engine)
                {
                    _engine.Stop();
                }
            }
            catch
            {
                //Depending on when we get called, stopping the engine throws anything from NullReferenceException, ArgumentNullException, IOException, etc.
            }

            _engine = null;

            if (_state == ProfilingState.Connected)
            {
#if DEBUG
                LogText($"INFO: Max Profiler packet length: {_session.MaxProfilePayloadLength}");
#endif 

                LogText("INFO: Disconnected from nanoCLR.");
            }

            KillEmulator();

            ProfilingState oldstate = _state;
            _state = ProfilingState.Disconnected;

            EnableUI();

            if (!_closing && oldstate == ProfilingState.Connected && _exporter is _PRF.Exporter_CLRProfiler)
            {
                _clrProfiler.LoadLogFile(_exporter.FileName);

                EnableDisableViewMenuItems();
            }

        }


        private void EnableUI()
        {
            // TODO
        }

        private void KillEmulator()
        {
            // TODO
        }

        private void EnableDisableViewMenuItems()
        {
            // TODO
        }

        private void OnWPMessage(IncomingMessage message, string text)
        {
            char[] NEWLINE_CHARS = { '\r', '\n' };
            text = text.TrimEnd(NEWLINE_CHARS);

            if (string.IsNullOrEmpty(text))
            {
                LogText("");
            }
            else
            {
                LogText(text);
            }
        }

        private void OnWPCommand(IncomingMessage message, bool reply)
        {
            switch (message.Header.Cmd)
            {
                case _WP.Commands.c_Monitor_ProgramExit:
                    //Dispatcher.Invoke(() =>
                    //{
                        SoftDisconnect();
                    //});
                    break;
            }
        }

        private void SoftDisconnect()
        {
            /* A 'soft disconnect' is where we don't want to record any more events, but we do want to
             * finish decoding as much of the data as possible before pulling the plug to the device. */

            if (_session != null)
            {
                ConnectButtonEnabled = false;
                _session.OnDisconnect += SoftDisconnectDone;
                _session.Disconnect();
            }

        }

        private void SoftDisconnectDone(object sender, EventArgs args)
        {
#if DEBUG
            LogText($"INFO: Profiling Session Length: {((_session != null) ? _session.BitsReceived : 0)} bits.");
#endif
            //Dispatcher.Invoke(() =>
            //{
                Disconnect();
            //});

        }

        public void LogText(string text)
        {
            //Dispatcher.Invoke(() =>
            //{
            TextBlockContent = text + "\r\n";
            //TextBlockContent.SelectionStart = textLog.Text.Length;
            //TextBlockContent.ScrollToEnd();
            //});
        }

        private void ConnectComplete()
        {
            _state = ProfilingState.Connected;
            ConnectButtonContent = c_Disconnect;

            AllocationsChecked = AllocationsChecked  && _engine.Capabilities.ProfilingAllocations;
            CallsChecked = CallsChecked && _engine.Capabilities.ProfilingCalls;
        }

        private void UserDisconnect()
        {
            // TODO

            //if (m_emuProcess != null && m_emuLaunched && !m_emuProcess.HasExited)
            //{
            //    m_killEmulator = (MessageBox.Show(string.Format("Emulator process {0} is still running. Do you wish to have it terminated?", m_emuProcess.Id),
            //        "Kill emulator?", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes);
            //}

            SoftDisconnect();
        }

        public Task<bool> Connect()
        {
            string serialPort = "COM15";
            string outputFileName = $"E:\\temp\\nano\\profile-{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.log";

            // connect to specified serial port
            try
            {
                _serialDebuggerPort.AddDevice(serialPort);
            }
#if DEBUG
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to add device: {ex.Message}");

                return Task.FromResult(false);
            }
#else
            catch
            {
                return Task.FromResult(false);

            }
#endif
            // get nano device (there should be only one)
            _nanoDevice = _serialDebuggerPort.NanoFrameworkDevices.FirstOrDefault();

            if (_nanoDevice != null)
            {
                // check if debugger engine exists
                if (_nanoDevice.DebugEngine == null)
                {
                    _nanoDevice.CreateDebugEngine();
                }

                _engine = _nanoDevice.DebugEngine;

                _engine.StopDebuggerOnConnect = true;
                _engine.OnCommand += new _DBG.CommandEventHandler(OnWPCommand);
                _engine.OnMessage += new _DBG.MessageEventHandler(OnWPMessage);

                // connect to the device
                if (_engine.Connect(
                    false,
                    true))
                {
                    // check that we are in CLR
                    if (_engine.IsConnectedTonanoCLR)
                    {
                        // sanity checks against desired options

                        if (_engine.Capabilities.Profiling == false)
                        {
                            throw new ApplicationException("This device is running a nanoCLR build that does not support profiling.");
                        }
                        if (CallsChecked && _engine.Capabilities.ProfilingCalls == false)
                        {
                            throw new ApplicationException("This device is running a nanoCLR build that does not support profiling function calls.");
                        }
                        if (AllocationsChecked && _engine.Capabilities.ProfilingAllocations == false)
                        {
                            throw new ApplicationException("This device is running a nanoCLR build that does not support profiling allocations.");
                        }

                        checInitState:

                        if (_engine.SetExecutionMode(0, 0))
                        {

                            var currentExecutionMode = _engine.GetExecutionMode();

                            if (currentExecutionMode.IsDeviceInInitializeState())
                            {
                                _engine.ThrowOnCommunicationFailure = true;
                                //_session = new _PRF.ProfilerSession(_engine, HeapAbsoluteAddress.IsChecked.Value);

                                if (_exporter != null)
                                {
                                    _exporter.Close();
                                }

                                _exporter = new _PRF.Exporter_CLRProfiler(_session, outputFileName);
                                _session.EnableProfiling();
                            }
                            else
                            {
                                bool rebootSuccessful = _engine.RebootDevice(RebootOptions.ClrOnly | RebootOptions.WaitForDebugger);

                                goto checInitState;

                            }

                            return Task.FromResult(true);
                            // done here
                            //break;
                        }

                        try
                        {
                            //// get device info
                            //var deviceInfo = _nanoDevice.GetDeviceInfo(true);

                            //// we have to have a valid device info
                            //if (deviceInfo.Valid)
                            //{
                            //    // done here
                            //}
                            //else
                            //{
                            //    // report issue
                            //    throw new Exception("Couldn't retrieve device details from nano device.");
                            //}
                        }
                        catch
                        {
                            // report issue 
                            throw new Exception("Couldn't retrieve device details from nano device.");
                        }
                    }
                }
                else
                {
                    // report issue 
                    throw new Exception("Couldn't connect to specified nano device.");
                }

                if (_engine.IsConnectedTonanoCLR)
                {
                    // we have to have a valid device info
                    if (_nanoDevice.DeviceInfo.Valid)
                    {


                        return Task.FromResult(true);
                    }
                    else
                    {
                        // report issue
                        throw new Exception("Couldn't retrieve device details from nano device.");
                    }
                }

            }

            return Task.FromResult(false);
        }
        #endregion

    }
}
