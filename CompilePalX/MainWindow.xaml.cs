﻿using System;
using System.Collections.Generic;
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
using Microsoft.Win32;

namespace CompilePalX
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        public static Dispatcher ActiveDispatcher;
        public MainWindow()
        {
            ActiveDispatcher = Dispatcher;

            InitializeComponent();

            AnalyticsManager.Launch();
            PersistenceManager.Init();
            ErrorFinder.Init();

            ConfigurationManager.AssembleParameters();

            ProgressManager.TitleChange += ProgressManager_TitleChange;
            ProgressManager.ProgressChange += ProgressManager_ProgressChange;
            ProgressManager.Init(TaskbarItemInfo);


            CompileProcessesListBox.ItemsSource = ConfigurationManager.CompileProcesses;

            PresetConfigListBox.ItemsSource = ConfigurationManager.KnownPresets;

            MapListBox.ItemsSource = CompilingManager.MapFiles;

            CompileProcessesListBox.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("Order", System.ComponentModel.ListSortDirection.Ascending));

            CompileProcessesListBox.SelectedIndex = 0;
            PresetConfigListBox.SelectedIndex = 0;

            UpdateConfigGrid();

            CompilingManager.OnWrite += CompilingManager_OnWrite;
            CompilingManager.OnClear += CompilingManager_OnClear;

        }


        void ProgressManager_ProgressChange(double progress)
        {
            CompileProgressBar.Value = progress;
        }

        void ProgressManager_TitleChange(string title)
        {
            Title = title;
        }

        void CompilingManager_OnWrite(string line)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrEmpty(line))
                    return;

                //OutputTab.Focus();

                CompileOutputTextbox.AppendText(line);
                CompileOutputTextbox.ScrollToEnd();
            });

        }

        void CompilingManager_OnClear()
        {
            Dispatcher.Invoke(() =>
            {
                CompileOutputTextbox.Document.Blocks.Clear();
                CompileOutputTextbox.AppendText(Environment.NewLine);
            });

        }

        private void OnConfigChanged(object sender, RoutedEventArgs e)
        {
            UpdateParameterTextBox();
        }

        private void AddParameterButton_Click(object sender, RoutedEventArgs e)
        {
            ParameterAdder c = new ParameterAdder(selectedProcess.ParameterList);
            c.ShowDialog();

            if (c.ChosenItem != null && !selectedProcess.PresetDictionary[ConfigurationManager.CurrentPreset].Contains(c.ChosenItem))
                selectedProcess.PresetDictionary[ConfigurationManager.CurrentPreset].Add(c.ChosenItem);

            UpdateParameterTextBox();
        }

        private void RemoveParameterButton_OnClickParameterButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (ConfigItem)ConfigDataGrid.SelectedItem;
            if (selectedItem != null)
                selectedProcess.PresetDictionary[ConfigurationManager.CurrentPreset].Remove(selectedItem);

            UpdateParameterTextBox();
        }

        private void AddPresetButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new DialogBox("Name for the new preset:");
            dialog.ShowDialog();

            string presetName = dialog.TextReturned;

            ConfigurationManager.NewPreset(presetName);

            CompileProcessesListBox.SelectedIndex = 0;
            PresetConfigListBox.SelectedItem = presetName;

        }

        private void RemovePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = (string)PresetConfigListBox.SelectedItem;

            if (selectedItem != null)
                ConfigurationManager.RemovePreset(selectedItem);

            CompileProcessesListBox.SelectedIndex = 0;
            PresetConfigListBox.SelectedIndex = 0;

        }

        private void MetroWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            ConfigurationManager.SavePresets();
            ConfigurationManager.SaveProcesses();

            Environment.Exit(0);//hack because wpf is weird
        }

        private void PresetConfigListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateConfigGrid();
        }
        private void CompileProcessesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateConfigGrid();
        }


        private CompileProcess selectedProcess;

        private void UpdateConfigGrid()
        {
            ConfigurationManager.CurrentPreset = (string)PresetConfigListBox.SelectedItem;

            selectedProcess = (CompileProcess)CompileProcessesListBox.SelectedItem;
            if (selectedProcess != null && ConfigurationManager.CurrentPreset != null && selectedProcess.PresetDictionary.ContainsKey(ConfigurationManager.CurrentPreset))
            {
                ConfigDataGrid.ItemsSource = selectedProcess.PresetDictionary[ConfigurationManager.CurrentPreset];

                UpdateParameterTextBox();

                DoRunCheckBox.IsChecked = selectedProcess.DoRun;
            }
        }
        void UpdateParameterTextBox()
        {
            if (selectedProcess != null)
                ParametersTextBox.Text = selectedProcess.GetParameterString();
        }

        private void DoRunCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            selectedProcess.DoRun = DoRunCheckBox.IsChecked.GetValueOrDefault(false);
        }

        private void MetroWindow_Activated(object sender, EventArgs e)
        {
            ProgressManager.PingProgress();
        }

        private void AddMapButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog();

            if (GameConfigurationManager.GameConfiguration.SDKMapFolder != null)
                dialog.InitialDirectory = GameConfigurationManager.GameConfiguration.SDKMapFolder;

            dialog.Multiselect = true;
            dialog.Filter = "Map files (*.vmf)|*.vmf";

            dialog.ShowDialog();

            foreach (var file in dialog.FileNames)
            {
                CompilingManager.MapFiles.Add(file);
            }
        }

        private void RemoveMapButton_Click(object sender, RoutedEventArgs e)
        {
            string selectedMap = (string)MapListBox.SelectedItem;

            if (selectedMap != null)
                CompilingManager.MapFiles.Remove(selectedMap);
        }


        private void CompileStartStopButton_OnClick(object sender, RoutedEventArgs e)
        {
            CompilingManager.ToggleCompileState();
        }
    }
}
