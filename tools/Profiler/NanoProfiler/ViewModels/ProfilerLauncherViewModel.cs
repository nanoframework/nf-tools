////
// Copyright (c) .NET Foundation and Contributors.
// See LICENSE file in the project root for full license information.
////

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using nanoFramework.Tools.NanoProfiler.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using _DBG = nanoFramework.Tools.Debugger;
using _PRF = nanoFramework.Tools.NanoProfiler;
using _WP = nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.NanoProfiler.ViewModels
{
    public partial class ProfilerLauncherViewModel : ObservableObject , IDisplayableObject
    {
        private const string _connectLabel = "Connect";
        private const string _connectingLabel = "Connecting...";
        private const string _disconnectLabel = "Disconnect";
        private const string _disconnectingLabel = "Disconnecting...";
        private const string _launchLabel = "Launch...";
        private const string _cancelLabel = "Cancel";

        #region Events

        public event NotifyDelegate<bool> EventViewLoaded;

        #endregion

        #region Commands

        [RelayCommand]
        private void ClearClicked()
        {
            WeakReferenceMessenger.Default.Send(new ClearLogTextMessage());
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
        private async Task ConnectClicked()
        {
            await Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
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
                    // disable button
                    ConnectButtonEnabled = false;

                    // update label
                    ConnectButtonContent = _connectingLabel;

                    if (await Connect())
                    {
                        _session.SetProfilingOptions(CallsChecked, AllocationsChecked);
                        _engine.ResumeExecution();

                        ConnectComplete();

                        // update label
                        ConnectButtonContent = _disconnectLabel;

                        // enable button
                        ConnectButtonEnabled = true;
                    }
                    else
                    {
                        LogText($"ERROR: failed to connect to device @ {ComPortName}");

                        Disconnect();
                    }
                }
            });
        }

        [RelayCommand]
        private void ViewLoaded(object item)
        {
            _closing = true;
            Disconnect();
        }


        [RelayCommand]
        private void BrowseLogFile()
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "nanoProfiler log files (*.log)|*.log",
                AddExtension = true,
                DefaultExt = "log",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            // show dialog
            DialogResult result = saveFileDialog.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(saveFileDialog.FileName))
            {
                // looks like we have a valid path
                OutputFileName = saveFileDialog.FileName;
            }
            else
            {
                // any other outcome from save file dialog doesn't require processing
            }
        }

        #endregion

        #region Observable Properties

        [ObservableProperty]
        private string _textBlockContent = string.Empty;
        [ObservableProperty]
        private string _header = "Profiler";
        [ObservableProperty]
        private string _iconName = "Profiler";


        [ObservableProperty]
        private string _connectButtonContent = _connectLabel;

        [ObservableProperty]
        private SolidColorBrush _backgroundProfileLauncher = Brushes.AliceBlue;

        [ObservableProperty]
        bool _rebootChecked = true;

        [ObservableProperty]
        private bool _callsChecked = false;

        [ObservableProperty]
        private bool _allocationsChecked = true;

        [ObservableProperty]
        private bool _heapAbsoluteAddressChecked = true;

        [ObservableProperty]
        private bool _connectButtonEnabled = true;

        [ObservableProperty]
        private string _comPortName = "COM1";

        [ObservableProperty]
        private string _outputFileName;

        [ObservableProperty]
        private string _debugOutputFileName;

        [ObservableProperty]
        private bool _traceProfilesEvents;

        #endregion

        #region Properties

        private bool _closing = true;
        private ProfilingState _state;

        private NanoDeviceBase _nanoDevice;
        private readonly PortBase _serialDebuggerPort;

        private _PRF.ProfilerSession _session = null;
        private _PRF.Exporter _exporter = null;
        private _DBG.Engine _engine = null;

        private CLRProfiler.MainForm _clrProfiler = new CLRProfiler.MainForm();
        private StreamWriter _debugLogWriter;

 

        #endregion

        #region Constructor
        public ProfilerLauncherViewModel()
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
                goto doneHere;
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

                    _engine.OnCommand -= new _DBG.CommandEventHandler(OnWPCommand);
                    _engine.OnMessage -= new _DBG.MessageEventHandler(OnWPMessage);
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

            // clear log file name
            OutputFileName = string.Empty;
            DebugOutputFileName = string.Empty;

        doneHere:
            // update label
            ConnectButtonContent = _connectLabel;
            // OK to enable button
            ConnectButtonEnabled = true;
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
                    SoftDisconnect();
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
                ConnectButtonContent = _disconnectingLabel;

                _session.OnDisconnect += SoftDisconnectDone;
                _session.Disconnect();
            }

        }

        private void SoftDisconnectDone(object sender, EventArgs args)
        {
#if DEBUG
            LogText($"INFO: Profiling Session Length: {((_session != null) ? _session.BitsReceived : 0)} bits.");
#endif

            LogText("");
            LogText($"INFO: Profile data saved to {OutputFileName}");
            
            CloseDebugLog();
            LogText($"INFO: Device log saved to {DebugOutputFileName}");
            LogText("");

            Disconnect();
        }

        public void LogText(string text)
        {
            // update log text
            _debugLogWriter?.WriteLine(text);
            WeakReferenceMessenger.Default.Send(new UpdateLogTextMessage(text));
        }

        private void ConnectComplete()
        {
            _state = ProfilingState.Connected;
            ConnectButtonContent = _disconnectLabel;

            AllocationsChecked = AllocationsChecked && _engine.Capabilities.ProfilingAllocations;
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

        public async Task<bool> Connect()
        {
            // string outputFileName = $"E:\\temp\\nano\\profile-{DateTime.UtcNow.ToString("yyyyMMddHHmmss")}.log";

            // connect to specified serial port
            try
            {
                await Task.Run(() =>  _serialDebuggerPort.AddDevice(ComPortName));
            }
#if DEBUG
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to add device: {ex.Message}");

                return false;
            }
#else
            catch
            {
                return false;

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

                bool failure = false;

                // connect to the device
                if (_engine.Connect(
                    false,
                    true))
                {
                    LogText("INFO: Successfully connected to nanoCLR.");

                    // check that we are in CLR
                    if (_engine.IsConnectedTonanoCLR)
                    {
                        // sanity checks against desired options

                        if (_engine.Capabilities.Profiling == false)
                        {
                            LogText("ERROR: This device is running a nanoCLR build that does not support profiling.");
                            failure = true;
                        }
                        if (CallsChecked && _engine.Capabilities.ProfilingCalls == false)
                        {
                            LogText("ERROR: This device is running a nanoCLR build that does not support profiling function calls.");
                            failure = true;
                        }
                        if (AllocationsChecked && _engine.Capabilities.ProfilingAllocations == false)
                        {
                            LogText("ERROR: This device is running a nanoCLR build that does not support profiling allocations.");
                            failure = true;
                        }

                        // check if we're good to go
                        if (failure)
                        {
                            Disconnect();

                            return false;
                        }

                    checInitState:

                        if (_engine.SetExecutionMode(0, 0))
                        {
                            var currentExecutionMode = _engine.GetExecutionMode();

                            if (currentExecutionMode.IsDeviceInInitializeState())
                            {
                                _engine.ThrowOnCommunicationFailure = true;

                                _engine.OnCommand -= new _DBG.CommandEventHandler(OnWPCommand);
                                _engine.OnCommand += new _DBG.CommandEventHandler(OnWPCommand);
                                _engine.OnMessage -= new _DBG.MessageEventHandler(OnWPMessage);
                                _engine.OnMessage += new _DBG.MessageEventHandler(OnWPMessage);

                                _session = new _PRF.ProfilerSession(_engine, HeapAbsoluteAddressChecked);

                                if (TraceProfilesEvents)
                                {
                                    _session.LogText = LogText;
                                }

                                if (_exporter != null)
                                {
                                    _exporter.Close();
                                }

                                CheckOutpuFileName();

                                _exporter = new _PRF.Exporter_CLRProfiler(
                                    _session,
                                    OutputFileName);

                                CloseDebugLog();
                                LogText($"Saving profile data to {OutputFileName}");
                                LogText($"Saving device log to {DebugOutputFileName}");
                                _debugLogWriter = File.CreateText(DebugOutputFileName);
                                _session.EnableProfiling();
                            }
                            else
                            {
                                bool rebootSuccessful = _engine.RebootDevice(RebootOptions.ClrOnly | RebootOptions.WaitForDebugger);

                                goto checInitState;

                            }

                            // done here
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private void CheckOutpuFileName()
        {
            if (string.IsNullOrEmpty(OutputFileName))
            {
                OutputFileName = Path.GetTempFileName().Replace(".tmp", ".log");
            }

            string outputDirectory = Path.GetDirectoryName(OutputFileName);
            string outputBasename = Path.GetFileNameWithoutExtension(OutputFileName);
            DebugOutputFileName = Path.Combine(outputDirectory, outputBasename + ".debug.txt");
        }

        private void CloseDebugLog()
        {
            if (_debugLogWriter != null)
            {
                _debugLogWriter.Close();
                _debugLogWriter.Dispose();
                _debugLogWriter = null;
            }
        }

        #endregion
    }
}
