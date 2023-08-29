using nanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using _PRF = nanoFramework.Tools.NanoProfiler;
using _DBG = nanoFramework.Tools.Debugger;
using _WP = nanoFramework.Tools.Debugger.WireProtocol;
using nanoFramework.Tools.Debugger.WireProtocol;
using System.Windows.Forms;
using static System.Resources.ResXFileRef;
using nanoFramework.Tools.Debugger.Extensions;

namespace nanoFramework.Tools.NanoProfiler
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class ProfilerLauncher : Window
    {
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

        private enum ProfilingState
        {
            Disconnected,
            Connecting,
            Connected,
        }

        public ProfilerLauncher()
        {
            InitializeComponent();

            _serialDebuggerPort = PortBase.CreateInstanceForSerial(false);
        }

        private void Window_Unloaded(object sender, RoutedEventArgs e)
        {
            _closing = true;

            Disconnect();
        }

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
                    Dispatcher.Invoke(() =>
                    {
                        SoftDisconnect();
                    });
                    break;
            }
        }

        private void SoftDisconnect()
        {
            /* A 'soft disconnect' is where we don't want to record any more events, but we do want to
             * finish decoding as much of the data as possible before pulling the plug to the device. */

            if (_session != null)
            {
                ConnectButton.IsEnabled = false;
                _session.OnDisconnect += SoftDisconnectDone;
                _session.Disconnect();
            }

        }

        private void SoftDisconnectDone(object sender, EventArgs args)
        {
#if DEBUG
            LogText($"INFO: Profiling Session Length: {((_session != null) ? _session.BitsReceived : 0)} bits.");
#endif
            Dispatcher.Invoke(() =>
            {
                Disconnect();
            });

        }

        public void LogText(string text)
        {
            Dispatcher.Invoke(() =>
            {
                textLog.Text += text + "\r\n";
                textLog.SelectionStart = textLog.Text.Length;
                textLog.ScrollToEnd();
            });
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
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
                if(await Connect())
                {
                    //if (result)
                    //{
                        _session.SetProfilingOptions(checkCalls.IsChecked.Value, checkAllocations.IsChecked.Value);
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
        private void ConnectComplete()
        {
            _state = ProfilingState.Connected;

            ConnectButton.Content = c_Disconnect;

            checkAllocations.IsChecked = checkAllocations.IsChecked.Value && _engine.Capabilities.ProfilingAllocations;
            checkCalls.IsChecked = checkCalls.IsChecked.Value && _engine.Capabilities.ProfilingCalls;
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
                        if (checkCalls.IsChecked.Value && _engine.Capabilities.ProfilingCalls == false)
                        {
                            throw new ApplicationException("This device is running a nanoCLR build that does not support profiling function calls.");
                        }
                        if (checkAllocations.IsChecked.Value && _engine.Capabilities.ProfilingAllocations == false)
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
                                _session = new _PRF.ProfilerSession(_engine, HeapAbsoluteAddress.IsChecked.Value);

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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            textLog.Text = string.Empty;
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "nanoProfiler log files (*.log)|*.log",
                FilterIndex = 1,
                RestoreDirectory = true
            };

            // show dialog
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
    }
}
