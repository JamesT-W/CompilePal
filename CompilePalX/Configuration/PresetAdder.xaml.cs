﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace CompilePalX
{
    /// <summary>
    /// Interaction logic for PresetAdder.xaml
    /// </summary>
    public partial class PresetAdder
    {
        public ConfigItem ChosenItem;
        public PresetAdder(List<string> configPresetNames )
        {
            ObservableCollection<ConfigItem> configPresets = new ObservableCollection<ConfigItem>();
            foreach (var preset in configPresetNames)
                configPresets.Add(new ConfigItem() { Name = preset });

            InitializeComponent();
            ConfigDataGrid.ItemsSource = configPresets;
        }

        private void ConfigDataGrid_MouseUp(object sender, MouseButtonEventArgs e)
        {

            ChosenItem = (ConfigItem) ConfigDataGrid.SelectedItem;

            Close();
        }
	}
}
