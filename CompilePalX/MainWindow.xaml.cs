using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
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
using System.Windows.Threading;
using CompilePalX.Compiling;
using MahApps.Metro.Controls.Dialogs;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media.Animation;
using System.Windows.Media.TextFormatting;
using CompilePalX.Compilers;
using CompilePalX.Configuration;
using System.Globalization;
using CompilePalX.Annotations;
using System.Runtime.CompilerServices;

namespace CompilePalX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public static Dispatcher ActiveDispatcher;
        private ObservableCollection<CompileProcess> CompileProcessesSubList = new ObservableCollection<CompileProcess>();
	    private bool processModeEnabled;
        private DispatcherTimer elapsedTimeDispatcherTimer;
		public static MainWindow Instance { get; private set; }

		public MainWindow()
        {
	        Instance = this;

			Application.Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            InitializeComponent();

            ActiveDispatcher = Dispatcher;

            CompilePalLogger.OnWrite += Logger_OnWrite;
            CompilePalLogger.OnBacktrack += Logger_OnBacktrack;
            CompilePalLogger.OnErrorLog += CompilePalLogger_OnError;

            UpdateManager.OnUpdateFound += UpdateManager_OnUpdateFound;
            UpdateManager.CheckVersion();

            AnalyticsManager.Launch();
            PersistenceManager.Init();
            ErrorFinder.Init();

            ConfigurationManager.AssembleParameters();

            ProgressManager.TitleChange += ProgressManager_TitleChange;
            ProgressManager.ProgressChange += ProgressManager_ProgressChange;
            ProgressManager.Init(TaskbarItemInfo);


            SetSources();

            CompileProcessesListBox.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Ordering", System.ComponentModel.ListSortDirection.Ascending));

            CompileProcessesListBox.SelectedIndex = 0;

            PresetMapConfigListBox.SelectedIndex = 0;

            UpdateConfigGrid();

            ConfigSelectButton.Visibility = GameConfigurationManager.GameConfigurations.Count > 1
                ? Visibility.Visible
                : Visibility.Collapsed;

            CompilingManager.OnClear += CompilingManager_OnClear;

            CompilingManager.OnStart += CompilingManager_OnStart;
            CompilingManager.OnFinish += CompilingManager_OnFinish;

			RowDragHelper.RowSwitched += RowDragHelperOnRowSwitched;

            elapsedTimeDispatcherTimer = new DispatcherTimer(new TimeSpan(0, 0, 0, 1), DispatcherPriority.Background,
                this.TickElapsedTimer, Dispatcher.CurrentDispatcher)
            {
                IsEnabled = false
            };

            HandleArgs();
        }


        private object previousPresetMapSelectedItem = null;
        private void SetPreviousPresetMapSelectedItem(object selectedItem)
        {
            if (selectedItem == null)
                return;

            previousPresetMapSelectedItem = selectedItem;
        }

        public Task<MessageDialogResult> ShowModal(string title, string message, MessageDialogStyle style = MessageDialogStyle.Affirmative, MetroDialogSettings settings = null)
		{
			return this.Dispatcher.Invoke(() => this.ShowMessageAsync(title, message, style, settings));
		}

	    private void HandleArgs(bool ignoreWipeArg = false)
        {
            //Handle command line args
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            for (int i = 0; i < commandLineArgs.Length; i++)
            {
	            var arg = commandLineArgs[i];
                try
                {
                    if (!ignoreWipeArg)
                    {
                        // wipes the map list
                        if (arg == "--wipe")
                        {
                            CompilingManager.MapFiles.Clear();
                            // recursive so that wipe doesn't clear maps added through the command line
                            HandleArgs(true);
                            break;
                        }
                    }

                    // adds map
                    if (arg == "--add")
                    {
                        if (i + 1 > commandLineArgs.Length)
	                        break;

                        var argPath = commandLineArgs[i + 1];

                        if (File.Exists(argPath))
                        {
                            if (argPath.EndsWith(".vmf") || argPath.EndsWith(".vmm") || argPath.EndsWith(".vmx"))
                                CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] = new Map(argPath);
                        }
                    }

                }
                catch (ArgumentOutOfRangeException e)
                {
                    //Ignore error
                }
            }
        }

        void CompilePalLogger_OnError(string errorText, Error e)
        {
            Dispatcher.Invoke(() =>
            {
                Hyperlink errorLink = new Hyperlink();

                Run text = new Run(errorText);

                text.Foreground = e.ErrorColor;

                errorLink.Inlines.Add(text);
                if (e.ID >= 0)
                {
                    errorLink.DataContext = e;
                    errorLink.Click += errorLink_Click;
                }

                var underline = new TextDecoration
                {
                    Location = TextDecorationLocation.Underline,
                    Pen = new Pen(e.ErrorColor, 1),
                    PenThicknessUnit = TextDecorationUnit.FontRecommended
                };

                errorLink.TextDecorations = new TextDecorationCollection(new[] { underline });

                OutputParagraph.Inlines.Add(errorLink);
                CompileOutputTextbox.ScrollToEnd();

            });
        }

        static void errorLink_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var link = (Hyperlink)sender;
            Error error = (Error)link.DataContext;

            ErrorFinder.ShowErrorDialog(error);
        }
        

        Run Logger_OnWrite(string s, Brush b = null)
        {
            return Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(s))
                    return null;

                Run textRun = new Run(s);

                if (b != null)
                    textRun.Foreground = b;

                OutputParagraph.Inlines.Add(textRun);

                // scroll to end only if already scrolled to the bottom. 1.0 is an epsilon value for double comparison
                if (CompileOutputTextbox.VerticalOffset + CompileOutputTextbox.ViewportHeight >= CompileOutputTextbox.ExtentHeight - 1.0)
                    CompileOutputTextbox.ScrollToEnd();

                return textRun;
            });
        }

        void Logger_OnBacktrack(List<Run> removals)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (var run in removals)
                {
                    run.Text = "";
                }
            });
        }

        async void UpdateManager_OnUpdateFound()
        {
            UpdateHyperLink.Inlines.Add(
	            $"An update is available. Current version is {UpdateManager.CurrentVersion}, latest version is {UpdateManager.LatestVersion}.");
            UpdateHyperLink.NavigateUri = UpdateManager.UpdateURL;
            UpdateLabel.Visibility = Visibility.Visible;
        }


        void Current_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            ExceptionHandler.LogException(e.Exception);
        }

        void SetSources()
        {
            CompileProcessesListBox.ItemsSource = CompileProcessesSubList;

            // ran during first time setup to populate PresetMapConfigListBox.ItemsSource
            if (PresetMapConfigListBox.ItemsSource == null)
            {
                var presetMapItemSources = new List<PresetMapCheckbox>();
                foreach (var presetMap in ConfigurationManager.KnownPresetsMaps)
                {
                    if (string.IsNullOrWhiteSpace(presetMap))
                        continue;

                    if (!CompilingManager.MapFiles.Any() || !CompilingManager.MapFiles.Keys.Any(x => x == presetMap))
                        CompilingManager.MapFiles.Add(presetMap, null);

                    var map = CompilingManager.MapFiles[presetMap];
                    var file = map == null ? string.Empty : map.File;
                    var compile = map == null ? false : map.Compile;
                    presetMapItemSources.Add(new PresetMapCheckbox(presetMap, file, compile));
                }
                PresetMapConfigListBox.ItemsSource = presetMapItemSources;
            }

            MapListBox.ItemsSource = CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] == null ? new List<Map>() : new List<Map>() { CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] };

			OrderManager.Init();
	        OrderManager.UpdateOrder();


            //BindingOperations.EnableCollectionSynchronization(CurrentOrder, lockObj);
        }

        void ProgressManager_ProgressChange(double progress)
        {
            CompileProgressBar.Value = progress;

            if (progress < 0 || progress >= 100)
                CompileStartStopButton.Content = "Compile";
        }

        void ProgressManager_TitleChange(string title)
        {
            Title = title;
        }


        void CompilingManager_OnClear()
        {
            Dispatcher.Invoke(() =>
            {
                OutputParagraph.Inlines.Clear();
            });

        }

        private void CompilingManager_OnStart()
        {
            ConfigDataGrid.IsEnabled = false;
            ProcessDataGrid.IsEnabled = false;
	        OrderGrid.IsEnabled = false;

            AddParameterButton.IsEnabled = false;
            RemoveParameterButton.IsEnabled = false;

            AddProcessesButton.IsEnabled = false;
            RemoveProcessesButton.IsEnabled = false;
            CompileProcessesListBox.IsEnabled = false;

            AddPresetMapButton.IsEnabled = false;
            RemovePresetMapButton.IsEnabled = false;
            ClonePresetMapButton.IsEnabled = false;
            PresetMapConfigListBox.IsEnabled = false;

            SelectMapButton.IsEnabled = false;
            ClearMapButton.IsEnabled = false;

            // hide update link so elapsed time can be shown
            UpdateLabel.Visibility = Visibility.Collapsed;
            TimeElapsedLabel.Visibility = Visibility.Visible;
            // Tick elapsed timer to display the default string
            TickElapsedTimer(null, null);

            elapsedTimeDispatcherTimer.IsEnabled = true;
        }

        private void CompilingManager_OnFinish()
        {
			//If process grid is enabled, disable config grid
            ConfigDataGrid.IsEnabled = !processModeEnabled;
            ProcessDataGrid.IsEnabled = processModeEnabled;
	        OrderGrid.IsEnabled = true;

            AddParameterButton.IsEnabled = true;
            RemoveParameterButton.IsEnabled = true;

            AddProcessesButton.IsEnabled = true;
            RemoveProcessesButton.IsEnabled = true;
            CompileProcessesListBox.IsEnabled = true;

            AddPresetMapButton.IsEnabled = true;
            RemovePresetMapButton.IsEnabled = true;
            ClonePresetMapButton.IsEnabled = true;
            PresetMapConfigListBox.IsEnabled = true;

            if (CompilingManager.MapFiles.Any() && CompilingManager.MapFiles.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) && CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] != null)
            {
                SelectMapButton.IsEnabled = false;
                ClearMapButton.IsEnabled = true;
            }
            else
            {
                SelectMapButton.IsEnabled = true;
                ClearMapButton.IsEnabled = false;
            }

            TimeElapsedLabel.Visibility = Visibility.Collapsed;
            elapsedTimeDispatcherTimer.IsEnabled = false;

            string logName = DateTime.Now.ToString("s").Replace(":", "-") + ".txt";
            string textLog = new TextRange(CompileOutputTextbox.Document.ContentStart, CompileOutputTextbox.Document.ContentEnd).Text;

            if (!Directory.Exists("CompileLogs"))
                Directory.CreateDirectory("CompileLogs");

            File.WriteAllText(System.IO.Path.Combine("CompileLogs", logName), textLog);

            CompileStartStopButton.Content = "Compile";

            ProgressManager.SetProgress(1);
        }

        private void OnConfigChanged(object sender, RoutedEventArgs e)
        {
            UpdateParameterTextBox();
        }

        private void AddParameterButton_Click(object sender, RoutedEventArgs e)
        {
            if (selectedProcess != null)
            {
				//Skip Paramater Adder for Custom Process
	            if (selectedProcess.Name == "CUSTOM")
	            {
					ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name].Add((ConfigItem)selectedProcess.ParameterList[0].Clone());
	            }
	            else
	            {
					ParameterAdder c = new ParameterAdder(selectedProcess.ParameterList);
					c.ShowDialog();

					if (c.ChosenItem != null)
					{
						if (c.ChosenItem.CanBeUsedMoreThanOnce)
						{
                            // .clone() removes problems with parameters sometimes becoming linked
                            ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name].Add((ConfigItem)c.ChosenItem.Clone());
						} 
						else if (!ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name].Contains(c.ChosenItem))
						{
                            ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name].Add(c.ChosenItem);
						}
					}
	            }

                AnalyticsManager.ModifyPresetMap();

                UpdateParameterTextBox();
            }
        }

        private void RemoveParameterButton_OnClickParameterButton_Click(object sender, RoutedEventArgs e)
        {
	        ConfigItem selectedItem;
	        if (processModeEnabled)
		        selectedItem = (ConfigItem) ProcessDataGrid.SelectedItem;
	        else
				selectedItem = (ConfigItem) ConfigDataGrid.SelectedItem;
            
            if (selectedItem != null)
                ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name].Remove(selectedItem);

            UpdateParameterTextBox();
        }

        private void AddProcessButton_Click(object sender, RoutedEventArgs e)
        {
            ProcessAdder c = new ProcessAdder();
            c.ShowDialog();

            if (c.ProcessDataGrid.SelectedItem != null)
            {
                CompileProcess ChosenProcess = (CompileProcess)c.ProcessDataGrid.SelectedItem;
                ChosenProcess.Metadata.DoRun = true;

                if (!ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].ContainsKey(ChosenProcess.Name))
                {
                    ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Add(ChosenProcess.Name, new ObservableCollection<ConfigItem>());
                }
            }

            AnalyticsManager.ModifyPresetMap();

            UpdateParameterTextBox();
            UpdateProcessList();

			if (processModeEnabled)
				OrderManager.UpdateOrder();
        }

        private void RemoveProcessButton_Click(object sender, RoutedEventArgs e)
        {
            if (CompileProcessesListBox.SelectedItem != null)
            {
                CompileProcess removed = (CompileProcess)CompileProcessesListBox.SelectedItem;
                ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Remove(selectedProcess.Name);
                ConfigurationManager.RemoveProcess(CompileProcessesListBox.SelectedItem.ToString());
            }
            UpdateProcessList();
            CompileProcessesListBox.SelectedIndex = 0;
		}

        private void AddPresetMapButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Map Preset Name");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                string presetName = dialog.Text;

                PresetAdder c = new PresetAdder(ConfigurationManager.PresetDictionary.Keys.ToList());
                c.ShowDialog();

                if (c.ChosenItem != null)
                {
                    ConfigurationManager.NewPresetMap(presetName, c.ChosenItem);
                }

                AnalyticsManager.NewPresetMap();

                SetSources();
                CompileProcessesListBox.SelectedIndex = 0;
                PresetMapConfigListBox.SelectedItem = presetName;
                SetPreviousPresetMapSelectedItem(PresetMapConfigListBox.SelectedItem);
            }
        }

        private async void ClonePresetMapButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (ConfigurationManager.CurrentPresetMap != null)
            {
                var dialog = new InputDialog("Map Preset Name");
                dialog.ShowDialog();

                if (dialog.Result)
                {
                    string presetName = dialog.Text;

                    ConfigurationManager.ClonePresetMap(presetName);

                    AnalyticsManager.NewPresetMap();

                    SetSources();
                    CompileProcessesListBox.SelectedIndex = 0;
                    PresetMapConfigListBox.SelectedItem = presetName;
                    SetPreviousPresetMapSelectedItem(PresetMapConfigListBox.SelectedItem);
                }
            }
        }

        private void RemovePresetMapButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (string)PresetMapConfigListBox.SelectedItem != null ? (string)PresetMapConfigListBox.SelectedItem : previousPresetMapSelectedItem;

            if (selectedItem != null)
                ConfigurationManager.RemovePresetMap(((PresetMapCheckbox)selectedItem).PresetMap);

            SetSources();
            CompileProcessesListBox.SelectedIndex = 0;
            PresetMapConfigListBox.SelectedIndex = 0;

            SetPreviousPresetMapSelectedItem(PresetMapConfigListBox.SelectedItem);
        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // prevent users from accidentally closing during a compile
            if (CompilingManager.IsCompiling)
            {
                MessageBoxResult cancelBoxResult = MessageBox.Show("Compile in progress, are you sure you want to cancel?", "Cancel Confirmation", System.Windows.MessageBoxButton.YesNo);
                if (cancelBoxResult != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            //ConfigurationManager.SavePresets();
            ConfigurationManager.SavePresetsMaps();
            ConfigurationManager.SaveProcesses();

            Environment.Exit(0);//hack because wpf is weird
        }

        private void PresetMapConfigListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var listBox = (ListBox)sender;

            UpdateConfigGrid();
            UpdateProcessList();

			if (processModeEnabled)
				OrderManager.UpdateOrder();

            SetSources();

            if (CompilingManager.MapFiles.Any() && CompilingManager.MapFiles.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) && CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] != null)
            {
                SelectMapButton.IsEnabled = false;
                ClearMapButton.IsEnabled = true;
            }
            else
            {
                SelectMapButton.IsEnabled = true;
                ClearMapButton.IsEnabled = false;
            }

            SetPreviousPresetMapSelectedItem(PresetMapConfigListBox.SelectedItem);
        }

        private void Compile_OnClick(object sender, RoutedEventArgs e)
        {
            var presetMap = ((PresetMapCheckbox)((CheckBox)e.Source).DataContext).PresetMap;
            
            if (!CompilingManager.MapFiles.ContainsKey(presetMap) || CompilingManager.MapFiles[presetMap] == null)
                return;

            CompilingManager.MapFiles[presetMap].Compile = (bool)((CheckBox)e.Source).IsChecked;

            PersistenceManager.ForceMapFilesWrite();
        }

        private void CompileProcessesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateConfigGrid();
        }


        private CompileProcess selectedProcess;

        private void UpdateConfigGrid()
        {
            ConfigurationManager.CurrentPresetMap = PresetMapConfigListBox.SelectedItem == null ? ConfigurationManager.CurrentPresetMap : (string)((PresetMapCheckbox)PresetMapConfigListBox.SelectedItem).PresetMap;

            selectedProcess = (CompileProcess)CompileProcessesListBox.SelectedItem;

            if (selectedProcess != null &&
                ConfigurationManager.CurrentPresetMap != null &&
                ConfigurationManager.PresetMapDictionary.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) &&
                ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Keys.Any(x => x == selectedProcess.Name))
            {
				//Switch to the process grid for custom program screen
	            if (selectedProcess.Name == "CUSTOM")
	            {
					ProcessDataGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(50))));
					processModeEnabled = true;

					ProcessDataGrid.ItemsSource = ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name];

					ConfigDataGrid.IsEnabled = false;
		            ConfigDataGrid.Visibility = Visibility.Hidden;
					ParametersTextBox.Visibility = Visibility.Hidden;

		            ProcessDataGrid.IsEnabled = true;
		            ProcessDataGrid.Visibility = Visibility.Visible;

		            ProcessTab.IsEnabled = true;
		            ProcessTab.Visibility = Visibility.Visible;

					//Hide parameter buttons if ORDER is the current tab
		            if ((string)(ProcessTab.SelectedItem as TabItem)?.Header == "ORDER")
		            {
						AddParameterButton.Visibility = Visibility.Hidden;
						AddParameterButton.IsEnabled = false;

						RemoveParameterButton.Visibility = Visibility.Hidden;
						RemoveParameterButton.IsEnabled = false;
					}
				}
	            else
	            {
					ConfigDataGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(50))));
					processModeEnabled = false;

					ConfigDataGrid.IsEnabled = true;
					ConfigDataGrid.Visibility = Visibility.Visible;
					ParametersTextBox.Visibility = Visibility.Visible;

					ProcessDataGrid.IsEnabled = false;
					ProcessDataGrid.Visibility = Visibility.Hidden;

					ProcessTab.IsEnabled = false;
					ProcessTab.Visibility = Visibility.Hidden;

					ConfigDataGrid.ItemsSource = ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap][selectedProcess.Name];

					//Make buttons visible if they were disabled
		            if (!AddParameterButton.IsEnabled)
		            {
						AddParameterButton.Visibility = Visibility.Visible;
						AddParameterButton.IsEnabled = true;

						RemoveParameterButton.Visibility = Visibility.Visible;
						RemoveParameterButton.IsEnabled = true;
					}

					UpdateParameterTextBox();
	            }


            }
        }

        private void UpdateProcessList()
        {
            CompileProcessesListBox.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(50))));


            int currentIndex = CompileProcessesListBox.SelectedIndex;

            CompileProcessesSubList.Clear();

            CompileProcessesListBox.Items.SortDescriptions.Add(new SortDescription("Ordering", ListSortDirection.Ascending));

            foreach (CompileProcess p in ConfigurationManager.CompileProcesses)
            {
                if (ConfigurationManager.CurrentPresetMap != null)
                    if (ConfigurationManager.PresetMapDictionary.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) && ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Keys.Any(x => x == p.Name))
                        CompileProcessesSubList.Add(p);
            }

            if (currentIndex < CompileProcessesListBox.Items.Count && currentIndex >= 0)
                CompileProcessesListBox.SelectedIndex = currentIndex;
        }

        void UpdateParameterTextBox()
        {
            if (selectedProcess != null)
                ParametersTextBox.Text = selectedProcess.GetParameterString();
        }

        private void MetroWindow_Activated(object sender, EventArgs e)
        {
            ProgressManager.PingProgress();
        }

        private void SelectMapButton_Click(object sender, RoutedEventArgs e)
        {
            // do not allow more than one map file for each map preset
            if (CompilingManager.MapFiles.Any() && CompilingManager.MapFiles.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) && CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] != null)
                return;

            var dialog = new OpenFileDialog();

            if (GameConfigurationManager.GameConfiguration.SDKMapFolder != null)
                dialog.InitialDirectory = GameConfigurationManager.GameConfiguration.SDKMapFolder;

            dialog.Multiselect = true;
            dialog.Filter = "Map files (*.vmf;*.vmm)|*.vmf;*.vmm";

            try
            {
                dialog.ShowDialog();
            }
            catch
            {
                CompilePalLogger.LogDebug($"SelectMapButton dialog failed to open, falling back to {Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)}");
				// if dialog fails to open it's possible its initial directory is in a non existant folder or something
	            dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
	            dialog.ShowDialog();
            }

            var file = dialog.FileNames.FirstOrDefault();

            if (CompilingManager.MapFiles.Any() && CompilingManager.MapFiles.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap))
                CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] = new Map(file);
            else
                CompilingManager.MapFiles.Add(ConfigurationManager.CurrentPresetMap, new Map(file));
            
            SelectMapButton.IsEnabled = false;
            ClearMapButton.IsEnabled = true;

            SetSources();
        }

        private void ClearMapButton_Click(object sender, RoutedEventArgs e)
        {
            // do not allow more than one map file for each map preset
            if (!CompilingManager.MapFiles.Any() || !CompilingManager.MapFiles.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap) || CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] == null)
                return;

            CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] = null;

            SelectMapButton.IsEnabled = true;
            ClearMapButton.IsEnabled = false;

            SetSources();
        }


        private void CompileStartStopButton_OnClick(object sender, RoutedEventArgs e)
        {
            CompilingManager.ToggleCompileState();

            CompileStartStopButton.Content = (string)CompileStartStopButton.Content == "Compile" ? "Cancel" : "Compile";

            OutputTab.Focus();
        }

        private void UpdateLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            Process.Start("http://www.github.com/ruarai/CompilePal/releases/latest");
        }

	    private void ReadOutput_OnChecked(object sender, RoutedEventArgs e)
	    {
		    var selectedItem = (ConfigItem) ProcessDataGrid.SelectedItem;

			//Set readOuput to opposite of it's current value
		    selectedItem.ReadOutput = !selectedItem.ReadOutput;

			//UpdateParameterTextBox();
	    }

	    private void ProcessTab_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	    {
			if (e.Source is TabControl)
				OrderManager.UpdateOrder();

			if (OrderTab.IsSelected)
		    {
				AddParameterButton.Visibility = Visibility.Hidden;
				AddParameterButton.IsEnabled = false;

				RemoveParameterButton.Visibility = Visibility.Hidden;
				RemoveParameterButton.IsEnabled = false;
			}
		    else
		    {
				AddParameterButton.Visibility = Visibility.Visible;
				AddParameterButton.IsEnabled = true;

				RemoveParameterButton.Visibility = Visibility.Visible;
				RemoveParameterButton.IsEnabled = true;
			}
		}

	    private void DoRun_OnClick(object sender, RoutedEventArgs e)
	    {
			if (processModeEnabled)
				OrderManager.UpdateOrder();
		}

	    private void OrderGrid_OnIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
	    {
			if (processModeEnabled)
				OrderManager.UpdateOrder();
		}

	    private void DataGridCell_OnEnter(object sender, MouseEventArgs e)
	    {
			//Only show drag cursor if row is draggable
		    if ((sender as DataGridRow)?.Item is CompileProcess process && process.IsDraggable)
			    Cursor = Cursors.SizeAll;
	    }

	    private void DataGridCell_OnExit(object sender, MouseEventArgs e)
	    {
		    if ((sender as DataGridRow)?.Item is CompileProcess process && process.IsDraggable)
			    Cursor = Cursors.Arrow;
	    }

	    public void UpdateOrderGridSource<T>(ObservableCollection<T> newSrc)
	    {
			//Use dispatcher so this can be called from seperate thread
			this.Dispatcher.Invoke(() =>
			{
				//TODO order grid doesnt seem to want to update, so have to do it manually by resetting the source
				//Update ordergrid by resetting collection
				OrderGrid.ItemsSource = newSrc;
			});
		}

		private void RowDragHelperOnRowSwitched(object sender, RowSwitchEventArgs e)
		{
			var primaryItem = OrderGrid.Items[e.PrimaryRowIndex] as CustomProgram;
			var displacedItem = OrderGrid.Items[e.DisplacedRowIndex] as CustomProgram;

			SetOrder(primaryItem, e.PrimaryRowIndex);
			SetOrder(displacedItem, e.DisplacedRowIndex);
		}

	    public void SetOrder<T>(T target, int newOrder)
	    {
			//Generic T is workaround for CustomProgram being
		    //less accessible than this method.
		    var program = target as CustomProgram;
		    if (program == null)
			    return;

			var programConfig = GetConfigFromCustomProgram(program);

			if (programConfig == null)
				return;

			program.CustomOrder = newOrder;
			programConfig.Warning = newOrder.ToString();
		}


		//Search through ProcDataGrid to find corresponding ConfigItem
		private ConfigItem GetConfigFromCustomProgram(CustomProgram program)
	    {
			foreach (var procSourceItem in ProcessDataGrid.ItemsSource)
			{
				if (program.Equals(procSourceItem))
				{
					return procSourceItem as ConfigItem;
				}
			}

			//Return null on failure
		    return null;
	    }

		private void UpdateHyperLink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri));
			e.Handled = true;
		}

		private void Settings_OnClick(object sender, RoutedEventArgs e)
		{
			throw new NotImplementedException();
		}

		private void ConfigBack_OnClick(object sender, RoutedEventArgs e)
		{
			if (LaunchWindow.Instance == null)
				new LaunchWindow().Show();
			else
				LaunchWindow.Instance.Focus();
		}

        private void BugReportButton_OnClick(object sender, RoutedEventArgs e)
        {
			Process.Start(new ProcessStartInfo("https://github.com/ruarai/CompilePal/issues/"));
            e.Handled = true;
        }

        private void TickElapsedTimer(object sender, EventArgs e)
        {
            var time = CompilingManager.GetTime().Elapsed;
            TimeElapsedLabel.Content = $"Time Elapsed: {(int) time.TotalHours:00}:{time:mm}:{time:ss}";
        }
    }

	public static class ObservableCollectionExtension
	{
		public static ObservableCollection<T> AddRange<T>(this ObservableCollection<T> collection, IEnumerable<T> range)
		{
			foreach (var element in range)
				collection.Add(element);

			return collection;
		}

		public static ObservableCollection<T> RemoveRange<T>(this ObservableCollection<T> collection, IEnumerable<T> range)
		{
			foreach (var element in range)
				collection.Remove(element);

			return collection;
		}
    }

    public class PresetMapCheckbox : INotifyPropertyChanged
    {
        public string PresetMap
        {
            get; set;
        }

        private string file;
        public string File
        {
            get => file;
            set { file = value; OnPropertyChanged(nameof(File)); }
        }

        private bool compile;
        public bool Compile
        {
            get => compile;
            set { compile = value; OnPropertyChanged(nameof(Compile)); }
        }

        public bool Enabled
        {
            get { return !string.IsNullOrWhiteSpace(File); }
        }

        public Visibility Visible
        {
            get { return string.IsNullOrWhiteSpace(File) ? Visibility.Hidden : Visibility.Visible; }
        }

        public PresetMapCheckbox(string presetMap, string file, bool compile)
        {
            PresetMap = presetMap;
            File = file;
            Compile = compile;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
