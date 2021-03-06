﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using CompilePalX.Compilers;

namespace CompilePalX.Configuration
{
	static class OrderManager
	{
		public static ObservableCollection<CompileProcess> CurrentOrder;
		private static object lockObj = new object();

		public static void Init()
		{
			CurrentOrder = new ObservableCollection<CompileProcess>();
			BindingOperations.EnableCollectionSynchronization(CurrentOrder, lockObj);
		}


		public static void UpdateOrder()
		{
			if (string.IsNullOrWhiteSpace(ConfigurationManager.CurrentPresetMap))
				return;

			//Get all default processes for config
			var defaultProcs = new List<CompileProcess>(ConfigurationManager.CompileProcesses[ConfigurationManager.CurrentPresetMap]
				.Where(c => c.Metadata.DoRun
							&& ConfigurationManager.PresetMapDictionary.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap)
							&& ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Keys.Any(x => x == c.Name)
					        && c.Name != "ORDER"
					        && c.Name != "CUSTOM"
				).ToList());

			//Get custom process
			var customProcess = (CustomProcess) ConfigurationManager.CompileProcesses[ConfigurationManager.CurrentPresetMap]
				.FirstOrDefault(c => c.Metadata.DoRun
										&& ConfigurationManager.PresetMapDictionary.Keys.Any(x => x == ConfigurationManager.CurrentPresetMap)
										&& ConfigurationManager.PresetMapDictionary[ConfigurationManager.CurrentPresetMap].Keys.Any(x => x == c.Name)
										&& c.Name == "CUSTOM"
				);

			var newOrder = new ObservableCollection<CompileProcess>(defaultProcs);

			if (customProcess != null)
			{
				foreach (var program in customProcess.BuildProgramList().OrderBy(c => c.CustomOrder))
				{
					if (program.CustomOrder > newOrder.Count)
					{
						newOrder.Add(program);
						MainWindow.Instance.SetOrder(program, newOrder.Count - 1);
					}
					else
					{
						newOrder.Insert(program.CustomOrder, program);
					}
				}
			}

			//Update order
			CurrentOrder.Clear();
			CurrentOrder.AddRange(newOrder);

			MainWindow.Instance.UpdateOrderGridSource(CurrentOrder);
		}
	}
}
