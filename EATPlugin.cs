using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASG.EAT.Plugin.Services;
using ASG.EAT.Plugin.ViewModels;
using ASG.EAT.Plugin.Views;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.ViewModel;
using NINA.WPF.Base.ViewModel;

[assembly: AssemblyTitle("ASG Electronically Assisted Tilt")]
[assembly: AssemblyDescription("Controls the ASG Astronomy electronically assisted tilt device for precise sensor tilt correction.")]
[assembly: AssemblyCompany("ASG Astronomy")]
[assembly: AssemblyProduct("ASG.EAT.Plugin")]
[assembly: AssemblyCopyright("Copyright © ASG Astronomy 2025")]
[assembly: AssemblyVersion("1.0.0.1")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: System.Runtime.InteropServices.Guid("DBCFE5A5-AF6E-4465-949E-7DFD5E20355B")]
[assembly: System.Runtime.InteropServices.ComVisible(false)]

[assembly: AssemblyMetadata("Identifier", "DBCFE5A5-AF6E-4465-949E-7DFD5E20355B")]
[assembly: AssemblyMetadata("Name", "ASG Electronically Assisted Tilt")]
[assembly: AssemblyMetadata("Author", "ASG Astronomy")]
[assembly: AssemblyMetadata("Description", "Controls the ASG Astronomy electronically assisted tilt device for precise sensor tilt correction.")]
[assembly: AssemblyMetadata("License", "MIT")]
[assembly: AssemblyMetadata("LicenseURL", "https://opensource.org/licenses/MIT")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/asg-astronomy/eat-plugin")]
[assembly: AssemblyMetadata("Repository", "https://github.com/asg-astronomy/eat-plugin")]
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/asg-astronomy/eat-plugin/blob/main/CHANGELOG.md")]
[assembly: AssemblyMetadata("Tags", "Tilt,Alignment,Hardware,Arduino")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.9001")]

namespace ASG.EAT.Plugin
{
    // ════════════════════════════════════════════════════════════════════
    //  Plugin Manifest
    // ════════════════════════════════════════════════════════════════════

    [Export(typeof(IPluginManifest))]
    public class EATPlugin : PluginBase
    {
        [ImportingConstructor]
        public EATPlugin() { }

        public override Task Initialize() => Task.CompletedTask;
        public override Task Teardown() => Task.CompletedTask;
    }

    // ════════════════════════════════════════════════════════════════════
    //  Dockable Panel — this IS the ViewModel for the control panel.
    //  NINA's DataTemplate sets DataContext = this, so all {Binding}
    //  properties must live here directly.
    // ════════════════════════════════════════════════════════════════════

    [Export(typeof(IDockableVM))]
    public class EATDockablePanel : DockableVM, IDisposable
    {
        private readonly EATSerialService _serial;
        private readonly EATSettings _settings;

        [ImportingConstructor]
        public EATDockablePanel(IProfileService profileService) : base(profileService)
        {
            Title = "ASG Electronic Tilt";

            // Set plugin icon - Simple 5-pointed star for astronomy
            var starGeometry = Geometry.Parse("M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z");
            var iconGroup = new GeometryGroup();
            iconGroup.Children.Add(starGeometry);
            ImageGeometry = iconGroup;

            _settings = EATSettings.Load();
            _serial = EATConnectionManager.Instance.SerialService;

            // Wire up serial events
            _serial.ConnectionStateChanged += (s, connected) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = connected;

                    // When connected (from any source), initialize the panel
                    if (connected)
                    {
                        OnConnected();
                    }
                    else
                    {
                        OnDisconnected();
                    }
                });
            };

            _serial.ErrorOccurred += (s, msg) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    AppendLog($"⚠ {msg}");
                });
            };

            // ── Connection commands ────────────────────────────────────
            ConnectCommand = new RelayCommand(_ => DoConnect(),
                _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(),
                _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => DoRefreshPorts());

            // ── Directional tilt (4-motor): tp, bt, lt, rt ────────────
            // Commands are mapped based on orientation to match camera position
            MoveTopCommand = new RelayCommand(_ => DoCmd(MapDirectionCommand("tp", TopSteps)), _ => IsConnected && !IsMoving);
            MoveBottomCommand = new RelayCommand(_ => DoCmd(MapDirectionCommand("bt", BottomSteps)), _ => IsConnected && !IsMoving);
            MoveLeftCommand = new RelayCommand(_ => DoCmd(MapDirectionCommand("lt", LeftSteps)), _ => IsConnected && !IsMoving);
            MoveRightCommand = new RelayCommand(_ => DoCmd(MapDirectionCommand("rt", RightSteps)), _ => IsConnected && !IsMoving);

            // ── Corner tilt (2-motor paired): tl, tr, bl, br ──────────
            // Commands are mapped based on orientation to match camera position
            MoveTopLeftCommand = new RelayCommand(_ => DoCmd(MapCornerCommand("tl", TopLeftSteps)), _ => IsConnected && !IsMoving);
            MoveTopRightCommand = new RelayCommand(_ => DoCmd(MapCornerCommand("tr", TopRightSteps)), _ => IsConnected && !IsMoving);
            MoveBottomLeftCommand = new RelayCommand(_ => DoCmd(MapCornerCommand("bl", BottomLeftSteps)), _ => IsConnected && !IsMoving);
            MoveBottomRightCommand = new RelayCommand(_ => DoCmd(MapCornerCommand("br", BottomRightSteps)), _ => IsConnected && !IsMoving);

            // ── Backfocus (all 4 motors): bf ───────────────────────────
            BackfocusInCommand = new RelayCommand(_ => DoCmd($"bf,{BackfocusSteps}"), _ => IsConnected && !IsMoving);
            BackfocusOutCommand = new RelayCommand(_ => DoCmd($"bf,{-BackfocusSteps}"), _ => IsConnected && !IsMoving);

            // ── Utility: zr, cp, up ────────────────────────────────────
            ZeroAllCommand = new RelayCommand(_ => DoCmd("zr"), _ => IsConnected && !IsMoving);
            GetPositionsCommand = new RelayCommand(_ => DoCmd("cp"), _ => IsConnected && !IsMoving);
            SaveEEPROMCommand = new RelayCommand(_ => DoCmd("up"), _ => IsConnected && !IsMoving);

            // ── Raw command + log ──────────────────────────────────────
            SendRawCommand = new RelayCommand(_ => DoSendRaw(),
                _ => IsConnected && !string.IsNullOrWhiteSpace(RawCommandText) && !IsMoving);
            ClearLogCommand = new RelayCommand(_ =>
            {
                ActivityLog.Clear();
                RaisePropertyChanged(nameof(ActivityLog));
            });

            // Initialize port list
            DoRefreshPorts();
            AppendLog("[ASG EAT Plugin Ready]");

            // Load default step sizes from settings
            LoadDefaultStepSizes();

            // Auto-connect if configured
            if (_settings.AutoConnectOnStartup && !string.IsNullOrEmpty(_settings.SelectedPort))
            {
                DoConnect();
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Connection event handlers
        // ────────────────────────────────────────────────────────────────

        private void OnConnected()
        {
            AppendLog("Connection established.");

            // Request current positions
            System.Threading.Tasks.Task.Run(async () =>
            {
                // Small delay to let Arduino settle
                await System.Threading.Tasks.Task.Delay(500);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    DoCmd("cp");
                });
            });
        }

        private void OnDisconnected()
        {
            AppendLog("Disconnected.");

            // Reset positions on disconnect
            PositionTL = "—";
            PositionTR = "—";
            PositionBL = "—";
            PositionBR = "—";
        }

        private void LoadDefaultStepSizes()
        {
            int defaultSteps = _settings.DefaultStepSize;

            // Set all step sizes to the default from settings
            TopSteps = defaultSteps;
            BottomSteps = defaultSteps;
            LeftSteps = defaultSteps;
            RightSteps = defaultSteps;
            TopLeftSteps = defaultSteps;
            TopRightSteps = defaultSteps;
            BottomLeftSteps = defaultSteps;
            BottomRightSteps = defaultSteps;
            BackfocusSteps = defaultSteps;
        }

        // ────────────────────────────────────────────────────────────────
        //  Connection properties
        // ────────────────────────────────────────────────────────────────

        private ObservableCollection<string> _availablePorts = new ObservableCollection<string>();
        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set { _availablePorts = value; RaisePropertyChanged(); }
        }

        private string _selectedPort;
        public string SelectedPort
        {
            get => _selectedPort;
            set { _selectedPort = value; RaisePropertyChanged(); if (value != null) _settings.SelectedPort = value; }
        }

        private int _selectedBaud = 9600;
        public int SelectedBaud
        {
            get => _selectedBaud;
            set { _selectedBaud = value; RaisePropertyChanged(); _settings.BaudRate = value; }
        }

        public int[] BaudRates => new[] { 9600, 19200, 38400, 57600, 115200 };

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ConnectionStatus)); }
        }

        public string ConnectionStatus
        {
            get
            {
                if (!IsConnected)
                    return "Disconnected";
                if (IsMoving)
                {
                    // Show specific status based on current operation
                    if (!string.IsNullOrEmpty(CurrentOperation))
                        return $"{CurrentOperation} — {_serial.ConnectedPort} @ {_serial.ConnectedBaud}";
                    return $"Moving — {_serial.ConnectedPort} @ {_serial.ConnectedBaud}";
                }
                return $"Connected — {_serial.ConnectedPort} @ {_serial.ConnectedBaud}";
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Movement state — drives the LED color (gray/green/red)
        // ────────────────────────────────────────────────────────────────

        private bool _isMoving;
        public bool IsMoving
        {
            get => _isMoving;
            set
            {
                _isMoving = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ConnectionStatus));
                // Force WPF to re-evaluate all command CanExecute predicates
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private string _currentOperation = string.Empty;
        public string CurrentOperation
        {
            get => _currentOperation;
            set
            {
                _currentOperation = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ConnectionStatus));
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Current position properties (read from Arduino responses)
        // ────────────────────────────────────────────────────────────────

        private string _positionTL = "—";
        public string PositionTL { get => _positionTL; set { _positionTL = value; RaisePropertyChanged(); } }

        private string _positionTR = "—";
        public string PositionTR { get => _positionTR; set { _positionTR = value; RaisePropertyChanged(); } }

        private string _positionBL = "—";
        public string PositionBL { get => _positionBL; set { _positionBL = value; RaisePropertyChanged(); } }

        private string _positionBR = "—";
        public string PositionBR { get => _positionBR; set { _positionBR = value; RaisePropertyChanged(); } }

        // ────────────────────────────────────────────────────────────────
        //  Step size properties
        // ────────────────────────────────────────────────────────────────

        // Directional
        private int _topSteps = 25;
        public int TopSteps { get => _topSteps; set { _topSteps = value; RaisePropertyChanged(); } }

        private int _bottomSteps = 25;
        public int BottomSteps { get => _bottomSteps; set { _bottomSteps = value; RaisePropertyChanged(); } }

        private int _leftSteps = 25;
        public int LeftSteps { get => _leftSteps; set { _leftSteps = value; RaisePropertyChanged(); } }

        private int _rightSteps = 25;
        public int RightSteps { get => _rightSteps; set { _rightSteps = value; RaisePropertyChanged(); } }

        // Corner
        private int _topLeftSteps = 25;
        public int TopLeftSteps { get => _topLeftSteps; set { _topLeftSteps = value; RaisePropertyChanged(); } }

        private int _topRightSteps = 25;
        public int TopRightSteps { get => _topRightSteps; set { _topRightSteps = value; RaisePropertyChanged(); } }

        private int _bottomLeftSteps = 25;
        public int BottomLeftSteps { get => _bottomLeftSteps; set { _bottomLeftSteps = value; RaisePropertyChanged(); } }

        private int _bottomRightSteps = 25;
        public int BottomRightSteps { get => _bottomRightSteps; set { _bottomRightSteps = value; RaisePropertyChanged(); } }

        // Backfocus
        private int _backfocusSteps = 25;
        public int BackfocusSteps { get => _backfocusSteps; set { _backfocusSteps = value; RaisePropertyChanged(); } }

        // Raw command
        private string _rawCommandText = string.Empty;
        public string RawCommandText { get => _rawCommandText; set { _rawCommandText = value; RaisePropertyChanged(); } }

        private string _lastResponse = string.Empty;
        public string LastResponse { get => _lastResponse; set { _lastResponse = value; RaisePropertyChanged(); } }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set { _isBusy = value; RaisePropertyChanged(); } }

        public ObservableCollection<string> ActivityLog { get; } = new ObservableCollection<string>();

        // ────────────────────────────────────────────────────────────────
        //  Commands
        // ────────────────────────────────────────────────────────────────

        public ICommand ConnectCommand { get; }
        public ICommand DisconnectCommand { get; }
        public ICommand RefreshPortsCommand { get; }

        public ICommand MoveTopCommand { get; }
        public ICommand MoveBottomCommand { get; }
        public ICommand MoveLeftCommand { get; }
        public ICommand MoveRightCommand { get; }

        public ICommand MoveTopLeftCommand { get; }
        public ICommand MoveTopRightCommand { get; }
        public ICommand MoveBottomLeftCommand { get; }
        public ICommand MoveBottomRightCommand { get; }

        public ICommand BackfocusInCommand { get; }
        public ICommand BackfocusOutCommand { get; }

        public ICommand ZeroAllCommand { get; }
        public ICommand GetPositionsCommand { get; }
        public ICommand SaveEEPROMCommand { get; }

        public ICommand SendRawCommand { get; }
        public ICommand ClearLogCommand { get; }

        // ────────────────────────────────────────────────────────────────
        //  Command implementations
        // ────────────────────────────────────────────────────────────────

        private void DoRefreshPorts()
        {
            var ports = EATSerialService.GetAvailablePorts();

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                AvailablePorts.Clear();
                foreach (var p in ports)
                {
                    AvailablePorts.Add(p);
                }

                // Restore saved port selection
                if (!string.IsNullOrEmpty(_settings.SelectedPort) && AvailablePorts.Contains(_settings.SelectedPort))
                {
                    SelectedPort = _settings.SelectedPort;
                }
                else if (AvailablePorts.Count > 0)
                {
                    SelectedPort = AvailablePorts.First();
                }

                SelectedBaud = _settings.BaudRate;
            });
        }

        private void DoConnect()
        {
            if (string.IsNullOrEmpty(SelectedPort))
            {
                AppendLog("No port selected.");
                return;
            }

            AppendLog($"Connecting to {SelectedPort} @ {SelectedBaud}...");
            bool ok = _serial.Connect(SelectedPort, SelectedBaud);

            if (ok)
            {
                _settings.Save();
                // OnConnected() will be called by the ConnectionStateChanged event handler
            }
            else
            {
                AppendLog("Connection failed. Check port and device.");
            }
        }

        private void DoDisconnect()
        {
            _serial.Disconnect();
            // OnDisconnected() will be called by the ConnectionStateChanged event handler
        }

        // ── Orientation Mapping ─────────────────────────────────────────

        /// <summary>
        /// Maps a corner command based on device orientation.
        /// Orientation 1 (0°): tl→tl, tr→tr, bl→bl, br→br
        /// Orientation 2 (90°): tl→tr, tr→br, bl→tl, br→bl
        /// Orientation 3 (180°): tl→br, tr→bl, bl→tr, br→tl
        /// Orientation 4 (270°): tl→bl, tr→tl, bl→br, br→tr
        /// </summary>
        private string MapCornerCommand(string logicalCorner, int steps)
        {
            var settings = EATSettings.Load();
            int orientation = settings.Orientation;

            string physicalCorner = logicalCorner; // Default: Orientation 1

            switch (orientation)
            {
                case 1: // 0° - No rotation
                    physicalCorner = logicalCorner;
                    break;
                case 2: // 90° clockwise
                    physicalCorner = logicalCorner switch
                    {
                        "tl" => "tr", // Top-Left camera → Top-Right motor
                        "tr" => "br", // Top-Right camera → Bottom-Right motor
                        "bl" => "tl", // Bottom-Left camera → Top-Left motor
                        "br" => "bl", // Bottom-Right camera → Bottom-Left motor
                        _ => logicalCorner
                    };
                    break;
                case 3: // 180°
                    physicalCorner = logicalCorner switch
                    {
                        "tl" => "br", // Top-Left camera → Bottom-Right motor
                        "tr" => "bl", // Top-Right camera → Bottom-Left motor
                        "bl" => "tr", // Bottom-Left camera → Top-Right motor
                        "br" => "tl", // Bottom-Right camera → Top-Left motor
                        _ => logicalCorner
                    };
                    break;
                case 4: // 270° clockwise (or 90° counter-clockwise)
                    physicalCorner = logicalCorner switch
                    {
                        "tl" => "bl", // Top-Left camera → Bottom-Left motor
                        "tr" => "tl", // Top-Right camera → Top-Left motor
                        "bl" => "br", // Bottom-Left camera → Bottom-Right motor
                        "br" => "tr", // Bottom-Right camera → Top-Right motor
                        _ => logicalCorner
                    };
                    break;
            }

            return $"{physicalCorner},{steps}";
        }

        /// <summary>
        /// Maps a directional command based on device orientation.
        /// Orientation 1 (0°): tp→tp, bt→bt, lt→lt, rt→rt
        /// Orientation 2 (90°): tp→rt, bt→lt, lt→tp, rt→bt
        /// Orientation 3 (180°): tp→bt, bt→tp, lt→rt, rt→lt
        /// Orientation 4 (270°): tp→lt, bt→rt, lt→bt, rt→tp
        /// </summary>
        private string MapDirectionCommand(string logicalDirection, int steps)
        {
            var settings = EATSettings.Load();
            int orientation = settings.Orientation;

            string physicalDirection = logicalDirection; // Default: Orientation 1

            switch (orientation)
            {
                case 1: // 0° - No rotation
                    physicalDirection = logicalDirection;
                    break;
                case 2: // 90° clockwise
                    physicalDirection = logicalDirection switch
                    {
                        "tp" => "rt", // Top camera → Right motor
                        "bt" => "lt", // Bottom camera → Left motor
                        "lt" => "tp", // Left camera → Top motor
                        "rt" => "bt", // Right camera → Bottom motor
                        _ => logicalDirection
                    };
                    break;
                case 3: // 180°
                    physicalDirection = logicalDirection switch
                    {
                        "tp" => "bt", // Top camera → Bottom motor
                        "bt" => "tp", // Bottom camera → Top motor
                        "lt" => "rt", // Left camera → Right motor
                        "rt" => "lt", // Right camera → Left motor
                        _ => logicalDirection
                    };
                    break;
                case 4: // 270° clockwise (or 90° counter-clockwise)
                    physicalDirection = logicalDirection switch
                    {
                        "tp" => "lt", // Top camera → Left motor
                        "bt" => "rt", // Bottom camera → Right motor
                        "lt" => "bt", // Left camera → Bottom motor
                        "rt" => "tp", // Right camera → Top motor
                        _ => logicalDirection
                    };
                    break;
            }

            return $"{physicalDirection},{steps}";
        }

        private async void DoCmd(string cmd)
        {
            AppendLog($">> {cmd}");

            // Set operation-specific status text
            if (cmd.StartsWith("zr"))
                CurrentOperation = "Resetting Values";
            else if (cmd.StartsWith("cp"))
                CurrentOperation = "Getting Positions";
            else if (cmd.StartsWith("up"))
                CurrentOperation = "Saving to EEPROM";
            else
                CurrentOperation = "Moving";

            IsMoving = true;
            IsBusy = true;
            try
            {
                List<string> responseLines = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                foreach (var line in responseLines)
                {
                    AppendLog($"<< {line}");
                }
                LastResponse = string.Join(" | ", responseLines);

                // Parse the response lines for position data and movement status
                ParseResponseLines(responseLines);
            }
            catch (Exception ex)
            {
                AppendLog($"!! {ex.Message}");
            }
            finally
            {
                CurrentOperation = string.Empty;
                IsMoving = false;
                IsBusy = false;
            }
        }

        private async void DoSendRaw()
        {
            string cmd = RawCommandText?.Trim();
            if (string.IsNullOrEmpty(cmd)) return;

            AppendLog($">> {cmd}");
            IsMoving = true;
            IsBusy = true;
            try
            {
                List<string> responseLines = await _serial.SendCommandAsync(cmd, _settings.CommandTimeoutMs);
                foreach (var line in responseLines)
                {
                    AppendLog($"<< {line}");
                }
                LastResponse = string.Join(" | ", responseLines);
                ParseResponseLines(responseLines);
            }
            catch (Exception ex)
            {
                AppendLog($"!! {ex.Message}");
            }
            finally
            {
                IsMoving = false;
                IsBusy = false;
                RawCommandText = string.Empty;
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Response parsing — extracts position data from Arduino output
        //
        //  Arduino sends position data in this format:
        //    ***Get Current Positions***
        //    <TL value>
        //    <TR value>
        //    <BL value>
        //    <BR value>
        //    ***End Current Positions***
        // ────────────────────────────────────────────────────────────────

        private void ParseResponseLines(List<string> lines)
        {
            bool inPositionBlock = false;
            var positionValues = new List<string>();

            foreach (var line in lines)
            {
                if (line.Contains("***Get Current Positions***"))
                {
                    inPositionBlock = true;
                    positionValues.Clear();
                    continue;
                }

                if (line.Contains("***End Current Positions***"))
                {
                    inPositionBlock = false;

                    // We expect exactly 4 values: TL, TR, BL, BR
                    if (positionValues.Count >= 4)
                    {
                        Application.Current?.Dispatcher?.Invoke(() =>
                        {
                            PositionTL = positionValues[0];
                            PositionTR = positionValues[1];
                            PositionBL = positionValues[2];
                            PositionBR = positionValues[3];
                        });
                    }
                    continue;
                }

                if (inPositionBlock)
                {
                    // Each line inside the position block is a numeric value
                    string trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        positionValues.Add(trimmed);
                    }
                }
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  Activity log
        // ────────────────────────────────────────────────────────────────

        private void AppendLog(string message)
        {
            string ts = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ActivityLog.Add(ts);
                while (ActivityLog.Count > 500)
                    ActivityLog.RemoveAt(0);
            });

            if (_settings.LogSerialTraffic)
            {
                System.Diagnostics.Debug.WriteLine($"[ASG-EAT] {ts}");
            }
        }

        // ────────────────────────────────────────────────────────────────
        //  INotifyPropertyChanged helper
        // ────────────────────────────────────────────────────────────────

        new public void RaisePropertyChanged([CallerMemberName] string name = null)
        {
            base.RaisePropertyChanged(name);
        }

        // ────────────────────────────────────────────────────────────────
        //  IDisposable
        // ────────────────────────────────────────────────────────────────

        public new void Dispose()
        {
            _serial?.Dispose();
            //base.Dispose();
        }
    }

    // ════════════════════════════════════════════════════════════════════
    //  RelayCommand
    // ════════════════════════════════════════════════════════════════════

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object parameter) => _execute(parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}
