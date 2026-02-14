using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ASG.EAT.Plugin.Services
{
    /// <summary>
    /// Manages serial communication with the ASG Electronic Astronomical Tilt (EAT) Arduino device.
    /// Handles connection lifecycle, command transmission, and multi-line response parsing.
    ///
    /// Supported ASG EAT Arduino Commands (from firmware V7):
    /// ────────────────────────────────────────────────────────
    /// Corner Motor Commands (paired tilt - moves 2 motors):
    ///   tr,{steps}  - Top Right corner: TR motor +steps, BL motor -steps
    ///   tl,{steps}  - Top Left corner:  TL motor +steps, BR motor -steps
    ///   br,{steps}  - Bottom Right corner: BR motor +steps, TL motor -steps
    ///   bl,{steps}  - Bottom Left corner:  BL motor +steps, TR motor -steps
    ///
    /// Directional Commands (moves 4 motors):
    ///   tp,{steps}  - Tilt Top/Up:    TL+steps, TR+steps, BL-steps, BR-steps
    ///   bt,{steps}  - Tilt Bottom/Down: TL-steps, TR-steps, BL+steps, BR+steps
    ///   rt,{steps}  - Tilt Right:     TR+steps, BR+steps, BL-steps, TL-steps
    ///   lt,{steps}  - Tilt Left:      TL+steps, BL+steps, TR-steps, BR-steps
    ///
    /// Backfocus Command:
    ///   bf,{steps}  - Move all 4 motors by +steps (backfocus adjustment)
    ///
    /// Utility Commands:
    ///   zr          - Zero/Reset all axes to 0 position
    ///   cp          - Get current motor positions
    ///   ep          - Show current EEPROM saved values
    ///   up          - Save current positions to EEPROM
    ///
    /// Configuration Commands:
    ///   cA,{value}  - Set Speed
    ///   cB,{value}  - Set MaxSpeed
    ///   cC,{value}  - Set Acceleration
    ///   or,{value}  - Set Orientation (1-4)
    /// </summary>
    public class EATSerialService : IDisposable
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();
        private bool _disposed = false;

        public bool IsConnected => _serialPort != null && _serialPort.IsOpen;
        public string ConnectedPort { get; private set; } = string.Empty;
        public int ConnectedBaud { get; private set; } = 0;

        public event EventHandler<string> DataReceived;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler<bool> ConnectionStateChanged;

        // -------------------------------------------------------------------
        //  Static helpers
        // -------------------------------------------------------------------

        public static string[] GetAvailablePorts()
        {
            try
            {
                return SerialPort.GetPortNames().OrderBy(p => p).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        public static int[] SupportedBaudRates => new int[] {
            9600, 19200, 38400, 57600, 115200
        };

        // -------------------------------------------------------------------
        //  Connect / Disconnect
        // -------------------------------------------------------------------

        public bool Connect(string portName, int baudRate = 9600)
        {
            lock (_lock)
            {
                try
                {
                    Disconnect();

                    _serialPort = new SerialPort
                    {
                        PortName = portName,
                        BaudRate = baudRate,
                        DataBits = 8,
                        Parity = Parity.None,
                        StopBits = StopBits.One,
                        Handshake = Handshake.None,
                        ReadTimeout = 3000,
                        WriteTimeout = 3000,
                        NewLine = "\n",
                        DtrEnable = true,
                        RtsEnable = false
                    };

                    _serialPort.DataReceived += OnSerialDataReceived;
                    _serialPort.ErrorReceived += OnSerialError;
                    _serialPort.Open();

                    // Allow the Arduino to reset after DTR toggle
                    Thread.Sleep(2000);

                    // Drain any boot-up messages
                    if (_serialPort.BytesToRead > 0)
                    {
                        _serialPort.DiscardInBuffer();
                    }

                    ConnectedPort = portName;
                    ConnectedBaud = baudRate;

                    ConnectionStateChanged?.Invoke(this, true);
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Connection failed: {ex.Message}");
                    ConnectionStateChanged?.Invoke(this, false);
                    return false;
                }
            }
        }

        public void Disconnect()
        {
            lock (_lock)
            {
                if (_serialPort != null)
                {
                    try
                    {
                        if (_serialPort.IsOpen)
                        {
                            _serialPort.DataReceived -= OnSerialDataReceived;
                            _serialPort.ErrorReceived -= OnSerialError;
                            _serialPort.DiscardInBuffer();
                            _serialPort.DiscardOutBuffer();
                            _serialPort.Close();
                        }
                        _serialPort.Dispose();
                    }
                    catch { /* best-effort cleanup */ }
                    _serialPort = null;
                    ConnectedPort = string.Empty;
                    ConnectedBaud = 0;
                    ConnectionStateChanged?.Invoke(this, false);
                }
            }
        }

        // -------------------------------------------------------------------
        //  Send commands — reads all available lines from the Arduino
        // -------------------------------------------------------------------

        /// <summary>
        /// Sends a command and collects all response lines until the Arduino
        /// stops sending (detected by a quiet period with no new data).
        /// Returns all lines as a list of strings.
        /// </summary>
        public List<string> SendCommand(string command, int timeoutMs = 3000)
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    return new List<string> { "[ERROR] Not connected to ASG EAT device." };
                }

                try
                {
                    // Clear any pending data
                    if (_serialPort.BytesToRead > 0)
                    {
                        _serialPort.DiscardInBuffer();
                    }

                    // Send the command with newline termination
                    _serialPort.WriteLine(command.Trim());

                    var lines = new List<string>();
                    var oldTimeout = _serialPort.ReadTimeout;
                    _serialPort.ReadTimeout = timeoutMs;

                    try
                    {
                        // Read lines until we get a timeout (no more data)
                        while (true)
                        {
                            string line = _serialPort.ReadLine().Trim();
                            if (!string.IsNullOrEmpty(line))
                            {
                                lines.Add(line);
                                DataReceived?.Invoke(this, line);
                            }

                            // After we have at least one line, use a shorter
                            // timeout for subsequent lines so we don't hang
                            // waiting for data that's never coming
                            _serialPort.ReadTimeout = 500;
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Expected — means the Arduino has finished sending
                    }
                    finally
                    {
                        _serialPort.ReadTimeout = oldTimeout;
                    }

                    if (lines.Count == 0)
                    {
                        lines.Add("[TIMEOUT] No response from device.");
                    }

                    return lines;
                }
                catch (Exception ex)
                {
                    string error = $"[ERROR] {ex.Message}";
                    ErrorOccurred?.Invoke(this, error);
                    return new List<string> { error };
                }
            }
        }

        /// <summary>
        /// Sends a command asynchronously on a background thread.
        /// Returns all response lines.
        /// </summary>
        public Task<List<string>> SendCommandAsync(string command, int timeoutMs = 3000, CancellationToken ct = default)
        {
            return Task.Run(() => SendCommand(command, timeoutMs), ct);
        }

        /// <summary>
        /// Fire-and-forget: sends a command without waiting for a response.
        /// </summary>
        public bool SendCommandNoWait(string command)
        {
            lock (_lock)
            {
                if (!IsConnected) return false;
                try
                {
                    _serialPort.WriteLine(command.Trim());
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, $"Send failed: {ex.Message}");
                    return false;
                }
            }
        }

        // -------------------------------------------------------------------
        //  Private event handlers
        // -------------------------------------------------------------------

        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen && _serialPort.BytesToRead > 0)
                {
                    // Asynchronous data is surfaced via the event; synchronous
                    // reads happen inside SendCommand.
                }
            }
            catch { /* suppress */ }
        }

        private void OnSerialError(object sender, SerialErrorReceivedEventArgs e)
        {
            ErrorOccurred?.Invoke(this, $"Serial error: {e.EventType}");
        }

        // -------------------------------------------------------------------
        //  IDisposable
        // -------------------------------------------------------------------

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }
    }
}
