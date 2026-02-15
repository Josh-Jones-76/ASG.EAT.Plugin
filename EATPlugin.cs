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
[assembly: AssemblyMetadata("Homepage", "https://www.asgastronomy.com")]
[assembly: AssemblyMetadata("Repository", "https://github.com/asg-astronomy/eat-plugin")]
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
        private EATSerialService _serial;
        private EATSettings _settings;
        private static readonly object _portsLock = new object();
        private static readonly object _activityLogLock = new object();
        private ObservableCollection<string> _availablePorts;
        private ObservableCollection<string> _activityLog;

        [ImportingConstructor]
        public EATDockablePanel(IProfileService profileService) : base(profileService)
        {
            Title = "ASG Electronic Tilt";

            // CRITICAL: Force all initialization onto UI thread synchronously
            // This blocks MEF construction but ensures no cross-thread binding issues
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                // We're on a background thread - marshal to UI thread and block until complete
                dispatcher.Invoke(() =>
                {
                    InitializeOnUIThread();
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            else
            {
                // Already on UI thread or no dispatcher available
                InitializeOnUIThread();
            }
        }

        private void InitializeOnUIThread()
        {
            // Set plugin icon - Simple 5-pointed star for astronomy
            // MUST be created on UI thread since Geometry is a DependencyObject
            var starGeometry = Geometry.Parse("M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z");
            var iconGroup = new GeometryGroup();
            iconGroup.Children.Add(starGeometry);
            ImageGeometry = iconGroup;

            // Initialize observable collections BEFORE enabling synchronization
            _availablePorts = new ObservableCollection<string>();
            _activityLog = new ObservableCollection<string>();

            // Enable cross-thread access to observable collections
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_availablePorts, _portsLock);
            System.Windows.Data.BindingOperations.EnableCollectionSynchronization(_activityLog, _activityLogLock);

            // Initialize settings and serial service
            _settings = EATSettings.Load();
            _serial = EATConnectionManager.Instance.SerialService;

            // Initialize all commands immediately on UI thread
            InitializeCommands();

            // Wire up serial events
            _serial.ConnectionStateChanged += (s, connected) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsConnected = connected;

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

            // Subscribe to orientation changes to update rotated position displays
            ViewModels.ViewModelManager.Instance.OptionsViewModel.OrientationChanged += (s, e) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    // Update all display position properties when orientation changes
                    RaisePropertyChanged(nameof(DisplayPositionTL));
                    RaisePropertyChanged(nameof(DisplayPositionTR));
                    RaisePropertyChanged(nameof(DisplayPositionBL));
                    RaisePropertyChanged(nameof(DisplayPositionBR));
                });
            };

            // Subscribe to sensor color changes to update sensor background
            ViewModels.ViewModelManager.Instance.OptionsViewModel.SensorColorChanged += (s, e) =>
            {
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    RaisePropertyChanged(nameof(SensorColorBrush));
                });
            };

            // Subscribe to visibility setting changes
            ViewModels.ViewModelManager.Instance.OptionsViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.EATOptionsViewModel.ShowRawCommand))
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        RaisePropertyChanged(nameof(ShowRawCommand));
                    });
                }
                else if (e.PropertyName == nameof(ViewModels.EATOptionsViewModel.ShowActivityLog))
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        RaisePropertyChanged(nameof(ShowActivityLog));
                    });
                }
            };

            // Perform initial setup
            DoRefreshPorts();
            AppendLog("[ASG EAT Plugin Ready]");
            LoadDefaultStepSizes();

            if (_settings.AutoConnectOnStartup && !string.IsNullOrEmpty(_settings.SelectedPort))
            {
                DoConnect();
            }
        }

        private void InitializeCommands()
        {
            // Initialize all commands
            ConnectCommand = new RelayCommand(_ => DoConnect(),
                _ => !IsConnected && !string.IsNullOrEmpty(SelectedPort));
            DisconnectCommand = new RelayCommand(_ => DoDisconnect(),
                _ => IsConnected);
            RefreshPortsCommand = new RelayCommand(_ => DoRefreshPorts());

            MoveTopCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(TopSteps, "Top")) DoCmd(MapDirectionCommand("tp", TopSteps));
            }, _ => IsConnected && !IsMoving);

            MoveBottomCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(BottomSteps, "Bottom")) DoCmd(MapDirectionCommand("bt", BottomSteps));
            }, _ => IsConnected && !IsMoving);

            MoveLeftCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(LeftSteps, "Left")) DoCmd(MapDirectionCommand("lt", LeftSteps));
            }, _ => IsConnected && !IsMoving);

            MoveRightCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(RightSteps, "Right")) DoCmd(MapDirectionCommand("rt", RightSteps));
            }, _ => IsConnected && !IsMoving);

            MoveTopLeftCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(TopLeftSteps, "Top-Left")) DoCmd(MapCornerCommand("tl", TopLeftSteps));
            }, _ => IsConnected && !IsMoving);

            MoveTopRightCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(TopRightSteps, "Top-Right")) DoCmd(MapCornerCommand("tr", TopRightSteps));
            }, _ => IsConnected && !IsMoving);

            MoveBottomLeftCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(BottomLeftSteps, "Bottom-Left")) DoCmd(MapCornerCommand("bl", BottomLeftSteps));
            }, _ => IsConnected && !IsMoving);

            MoveBottomRightCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(BottomRightSteps, "Bottom-Right")) DoCmd(MapCornerCommand("br", BottomRightSteps));
            }, _ => IsConnected && !IsMoving);

            BackfocusInCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(BackfocusSteps, "Backfocus In")) DoCmd($"bf,{BackfocusSteps}");
            }, _ => IsConnected && !IsMoving);

            BackfocusOutCommand = new RelayCommand(_ =>
            {
                if (ValidateStepSize(BackfocusSteps, "Backfocus Out")) DoCmd($"bf,{-BackfocusSteps}");
            }, _ => IsConnected && !IsMoving);

            ZeroAllCommand = new RelayCommand(_ =>
            {
                var result = MessageBox.Show(
                    "⚠ Important: Zero All only resets the software position values to zero.\n\n" +
                    "To physically reset the device:\n" +
                    "1. Remove power from the EAT controller\n" +
                    "2. Manually push the device fully in to bottom out\n" +
                    "3. Restore power\n\n" +
                    "Do you want to zero the software values?",
                    "Zero All - Physical Reset Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    DoCmd("zr");
                }
            }, _ => IsConnected && !IsMoving);

            GetPositionsCommand = new RelayCommand(_ => DoCmd("cp"), _ => IsConnected && !IsMoving);
            SaveEEPROMCommand = new RelayCommand(_ => DoCmd("up"), _ => IsConnected && !IsMoving);

            SendRawCommand = new RelayCommand(_ => DoSendRaw(),
                _ => IsConnected && !string.IsNullOrWhiteSpace(RawCommandText) && !IsMoving);
            ClearLogCommand = new RelayCommand(_ =>
            {
                ActivityLog.Clear();
                RaisePropertyChanged(nameof(ActivityLog));
            });
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

        public ObservableCollection<string> AvailablePorts
        {
            get => _availablePorts;
            set { _availablePorts = value; RaisePropertyChanged(); }
        }

        private string _selectedPort = string.Empty;
        public string SelectedPort
        {
            get => _selectedPort ?? string.Empty;
            set
            {
                _selectedPort = value;
                RaisePropertyChanged();
                if (value != null && _settings != null)
                    _settings.SelectedPort = value;
            }
        }

        private int _selectedBaud = 9600;
        public int SelectedBaud
        {
            get => _selectedBaud;
            set
            {
                _selectedBaud = value;
                RaisePropertyChanged();
                if (_settings != null)
                    _settings.BaudRate = value;
            }
        }

        public int[] BaudRates => new[] { 9600, 19200, 38400, 57600, 115200 };

        private bool _isConnected;
        public bool IsConnected
        {
            get => _isConnected;
            set { _isConnected = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(ConnectionStatus)); }
        }

        // Visibility toggles from settings
        public bool ShowRawCommand => ViewModels.ViewModelManager.Instance.OptionsViewModel.ShowRawCommand;
        public bool ShowActivityLog => ViewModels.ViewModelManager.Instance.OptionsViewModel.ShowActivityLog;

        public string ConnectionStatus
        {
            get
            {
                try
                {
                    if (!IsConnected)
                        return "Disconnected";
                    if (IsMoving)
                    {
                        // Show specific status based on current operation
                        if (!string.IsNullOrEmpty(CurrentOperation))
                            return $"{CurrentOperation} — {_serial?.ConnectedPort ?? "N/A"} @ {_serial?.ConnectedBaud ?? 0}";
                        return $"Moving — {_serial?.ConnectedPort ?? "N/A"} @ {_serial?.ConnectedBaud ?? 0}";
                    }
                    return $"Connected — {_serial?.ConnectedPort ?? "N/A"} @ {_serial?.ConnectedBaud ?? 0}";
                }
                catch
                {
                    return "Disconnected";
                }
            }
        }

        // Sensor color from settings
        public System.Windows.Media.Brush SensorColorBrush
        {
            get
            {
                try
                {
                    var colorString = ViewModels.ViewModelManager.Instance.OptionsViewModel.SensorColor;
                    return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom(colorString);
                }
                catch
                {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(42, 42, 42));
                }
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
        public string PositionTL
        {
            get => _positionTL;
            set
            {
                _positionTL = value;
                RaisePropertyChanged();
                // Update all rotated display properties
                RaisePropertyChanged(nameof(DisplayPositionTL));
                RaisePropertyChanged(nameof(DisplayPositionTR));
                RaisePropertyChanged(nameof(DisplayPositionBL));
                RaisePropertyChanged(nameof(DisplayPositionBR));
                // Update settings panel physical positions
                ViewModels.ViewModelManager.Instance.OptionsViewModel.PositionTL = value;
            }
        }

        private string _positionTR = "—";
        public string PositionTR
        {
            get => _positionTR;
            set
            {
                _positionTR = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(DisplayPositionTL));
                RaisePropertyChanged(nameof(DisplayPositionTR));
                RaisePropertyChanged(nameof(DisplayPositionBL));
                RaisePropertyChanged(nameof(DisplayPositionBR));
                // Update settings panel physical positions
                ViewModels.ViewModelManager.Instance.OptionsViewModel.PositionTR = value;
            }
        }

        private string _positionBL = "—";
        public string PositionBL
        {
            get => _positionBL;
            set
            {
                _positionBL = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(DisplayPositionTL));
                RaisePropertyChanged(nameof(DisplayPositionTR));
                RaisePropertyChanged(nameof(DisplayPositionBL));
                RaisePropertyChanged(nameof(DisplayPositionBR));
                // Update settings panel physical positions
                ViewModels.ViewModelManager.Instance.OptionsViewModel.PositionBL = value;
            }
        }

        private string _positionBR = "—";
        public string PositionBR
        {
            get => _positionBR;
            set
            {
                _positionBR = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(DisplayPositionTL));
                RaisePropertyChanged(nameof(DisplayPositionTR));
                RaisePropertyChanged(nameof(DisplayPositionBL));
                RaisePropertyChanged(nameof(DisplayPositionBR));
                // Update settings panel physical positions
                ViewModels.ViewModelManager.Instance.OptionsViewModel.PositionBR = value;
            }
        }

        // Computed properties that rotate the position display based on orientation
        // Orientation 1 = 0°, 2 = 90° CW, 3 = 180°, 4 = 270° CW
        // Rotation follows same logic as movement commands
        public string DisplayPositionTL
        {
            get
            {
                var orientation = ViewModels.ViewModelManager.Instance.OptionsViewModel.Orientation;
                return orientation switch
                {
                    1 => _positionTL, // 0° - no rotation
                    2 => _positionTR, // 90° CW - physical TR is now in TL display position
                    3 => _positionBR, // 180° - physical BR is in TL position
                    4 => _positionBL, // 270° CW - physical BL is in TL position
                    _ => _positionTL
                };
            }
        }

        public string DisplayPositionTR
        {
            get
            {
                var orientation = ViewModels.ViewModelManager.Instance.OptionsViewModel.Orientation;
                return orientation switch
                {
                    1 => _positionTR, // 0° - no rotation
                    2 => _positionBR, // 90° CW - physical BR is now in TR position
                    3 => _positionBL, // 180° - physical BL is in TR position
                    4 => _positionTL, // 270° CW - physical TL is in TR position
                    _ => _positionTR
                };
            }
        }

        public string DisplayPositionBL
        {
            get
            {
                var orientation = ViewModels.ViewModelManager.Instance.OptionsViewModel.Orientation;
                return orientation switch
                {
                    1 => _positionBL, // 0° - no rotation
                    2 => _positionTL, // 90° CW - physical TL is now in BL position
                    3 => _positionTR, // 180° - physical TR is in BL position
                    4 => _positionBR, // 270° CW - physical BR is in BL position
                    _ => _positionBL
                };
            }
        }

        public string DisplayPositionBR
        {
            get
            {
                var orientation = ViewModels.ViewModelManager.Instance.OptionsViewModel.Orientation;
                return orientation switch
                {
                    1 => _positionBR, // 0° - no rotation
                    2 => _positionBL, // 90° CW - physical BL is now in BR position
                    3 => _positionTL, // 180° - physical TL is in BR position
                    4 => _positionTR, // 270° CW - physical TR is in BR position
                    _ => _positionBR
                };
            }
        }

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

        public ObservableCollection<string> ActivityLog => _activityLog;

        // ────────────────────────────────────────────────────────────────
        //  Commands
        // ────────────────────────────────────────────────────────────────

        // Initialize all commands with dummy commands to prevent null reference during binding
        // These will be replaced with real commands in InitializeAsync()
        private static readonly ICommand DummyCommand = new RelayCommand(_ => { }, _ => false);

        public ICommand ConnectCommand { get; private set; } = DummyCommand;
        public ICommand DisconnectCommand { get; private set; } = DummyCommand;
        public ICommand RefreshPortsCommand { get; private set; } = DummyCommand;

        public ICommand MoveTopCommand { get; private set; } = DummyCommand;
        public ICommand MoveBottomCommand { get; private set; } = DummyCommand;
        public ICommand MoveLeftCommand { get; private set; } = DummyCommand;
        public ICommand MoveRightCommand { get; private set; } = DummyCommand;

        public ICommand MoveTopLeftCommand { get; private set; } = DummyCommand;
        public ICommand MoveTopRightCommand { get; private set; } = DummyCommand;
        public ICommand MoveBottomLeftCommand { get; private set; } = DummyCommand;
        public ICommand MoveBottomRightCommand { get; private set; } = DummyCommand;

        public ICommand BackfocusInCommand { get; private set; } = DummyCommand;
        public ICommand BackfocusOutCommand { get; private set; } = DummyCommand;

        public ICommand ZeroAllCommand { get; private set; } = DummyCommand;
        public ICommand GetPositionsCommand { get; private set; } = DummyCommand;
        public ICommand SaveEEPROMCommand { get; private set; } = DummyCommand;

        public ICommand SendRawCommand { get; private set; } = DummyCommand;
        public ICommand ClearLogCommand { get; private set; } = DummyCommand;

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
        /// Orientation 2 (90° CW): tp→lt, bt→rt, lt→bt, rt→tp
        /// Orientation 3 (180°): tp→bt, bt→tp, lt→rt, rt→lt
        /// Orientation 4 (270° CW): tp→rt, bt→lt, lt→tp, rt→bt
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
                        "tp" => "rt", // Top camera edge → Right motor edge (TR+BR)
                        "bt" => "lt", // Bottom camera edge → Left motor edge (TL+BL)
                        "lt" => "tp", // Left camera edge → Top motor edge (TL+TR)
                        "rt" => "bt", // Right camera edge → Bottom motor edge (BL+BR)
                        _ => logicalDirection
                    };
                    break;
                case 3: // 180°
                    physicalDirection = logicalDirection switch
                    {
                        "tp" => "bt", // Top camera edge → Bottom motor edge
                        "bt" => "tp", // Bottom camera edge → Top motor edge
                        "lt" => "rt", // Left camera edge → Right motor edge
                        "rt" => "lt", // Right camera edge → Left motor edge
                        _ => logicalDirection
                    };
                    break;
                case 4: // 270° clockwise (or 90° counter-clockwise)
                    physicalDirection = logicalDirection switch
                    {
                        "tp" => "lt", // Top camera edge → Left motor edge (TL+BL)
                        "bt" => "rt", // Bottom camera edge → Right motor edge (TR+BR)
                        "lt" => "bt", // Left camera edge → Bottom motor edge (BL+BR)
                        "rt" => "tp", // Right camera edge → Top motor edge (TL+TR)
                        _ => logicalDirection
                    };
                    break;
            }

            return $"{physicalDirection},{steps}";
        }

        /// <summary>
        /// Validates step size to prevent extremely large moves that could damage the device
        /// </summary>
        private bool ValidateStepSize(int steps, string moveName)
        {
            if (Math.Abs(steps) > 2000)
            {
                var result = MessageBox.Show(
                    $"⚠ Warning: {moveName} move is {steps} steps.\n\n" +
                    $"This is a very large move (>{Math.Abs(steps)} steps) that could cause issues or damage the device.\n\n" +
                    $"Are you sure you want to proceed?",
                    "Large Move Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                return result == MessageBoxResult.Yes;
            }
            return true;
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
                // Clear moving state on error
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CurrentOperation = string.Empty;
                    IsMoving = false;
                    IsBusy = false;
                });
            }
            // Note: IsMoving is cleared when "***finished movement***" is received in ParseResponseLines
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
                // Clear moving state on error
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IsMoving = false;
                    IsBusy = false;
                });
            }
            finally
            {
                RawCommandText = string.Empty;
            }
            // Note: IsMoving is cleared when "***finished movement***" is received in ParseResponseLines
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
                // Check for movement finished message
                if (line.Contains("***finished movement***"))
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CurrentOperation = string.Empty;
                        IsMoving = false;
                        IsBusy = false;
                    });
                    continue;
                }

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

                    // Clear moving state when position query completes
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        CurrentOperation = string.Empty;
                        IsMoving = false;
                        IsBusy = false;
                    });
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

        public void Dispose()
        {
            _serial?.Dispose();
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
