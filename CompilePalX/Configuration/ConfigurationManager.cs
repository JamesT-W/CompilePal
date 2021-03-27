using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using CompilePalX.Compilers;
using CompilePalX.Compilers.BSPPack;
using CompilePalX.Compilers.UtilityProcess;
using CompilePalX.Compiling;
using Newtonsoft.Json;

namespace CompilePalX
{

    static class ConfigurationManager
    {
        public static ObservableDictionary<string, ObservableCollection<CompileProcess>> CompileProcesses = new ObservableDictionary<string, ObservableCollection<CompileProcess>>();
        public static ObservableCollection<string> KnownPresets = new ObservableCollection<string>();
        public static ObservableCollection<string> KnownPresetsMaps = new ObservableCollection<string>();

        public static ObservableDictionary<string, ObservableDictionary<string, ObservableCollection<ConfigItem>>> PresetDictionary = new ObservableDictionary<string, ObservableDictionary<string, ObservableCollection<ConfigItem>>>();
        public static ObservableDictionary<string, ObservableDictionary<string, ObservableCollection<ConfigItem>>> PresetMapDictionary = new ObservableDictionary<string, ObservableDictionary<string, ObservableCollection<ConfigItem>>>();

        public static string CurrentPreset = "Fast";
        public static string CurrentPresetMap = string.Empty;

        private static readonly string ParametersFolder = "./Parameters";
        private static readonly string PresetsFolder = "./Presets";
        public static readonly string PresetsMapsFolder = "./PresetsMaps";


        public static void AssembleParameters()
        {
            var allPresetMapNames = new List<string>();

            if (!Directory.Exists(PresetsMapsFolder))
                Directory.CreateDirectory(PresetsMapsFolder);

            // get all preset map names, as there is no knowledge of them at this point on startup
            var presetsMaps = Directory.GetDirectories(PresetsMapsFolder);
            foreach (string presetPath in presetsMaps)
            {
                allPresetMapNames.Add(Path.GetFileName(presetPath));
            }


            CompileProcesses.Clear();

            string tempPresetName = "presetMapUsedToFillPresetDictionaryOnly";
            var presetMapNames = allPresetMapNames.Any() ? allPresetMapNames : new List<string>() { tempPresetName };
            foreach (var presetMap in presetMapNames)
            {
                CompileProcesses.Add(presetMap, new ObservableCollection<CompileProcess>());

                CompileProcesses[presetMap].Add(new BSPPack());
                CompileProcesses[presetMap].Add(new CubemapProcess());
                CompileProcesses[presetMap].Add(new NavProcess());
                CompileProcesses[presetMap].Add(new ShutdownProcess());
                CompileProcesses[presetMap].Add(new UtilityProcess());
                CompileProcesses[presetMap].Add(new CustomProcess());

                //collect new metadatas

                var metadatas = Directory.GetDirectories(ParametersFolder);

                foreach (var metadata in metadatas)
                {
                    string folderName = Path.GetFileName(metadata);

                    if (CompileProcesses[presetMap].Any(c => String.Equals(c.Metadata.Name, folderName, StringComparison.CurrentCultureIgnoreCase)))
                        continue;

                    var compileProcess = new CompileExecutable(folderName);

                    CompileProcesses[presetMap].Add(compileProcess);
                }

                //collect legacy metadatas
                var csvMetaDatas = Directory.GetFiles(ParametersFolder + "\\", "*.meta");

                foreach (var metadata in csvMetaDatas)
                {
                    string name = Path.GetFileName(metadata).Replace(".meta", "");

                    if (CompileProcesses[presetMap].Any(c => String.Equals(c.Metadata.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                        continue;

                    var compileProcess = new CompileExecutable(name);

                    CompileProcesses[presetMap].Add(compileProcess);
                }

                CompileProcesses[presetMap] = new ObservableCollection<CompileProcess>(CompileProcesses[presetMap].OrderBy(c => c.Metadata.Order));

                AssemblePreset(presetMap);
            }
            AssemblePresetMaps();

            // remove temp preset map name if used
            if (CompileProcesses.Any(x => x.Key == tempPresetName))
                CompileProcesses.Remove(tempPresetName);
        }

        private static void AssemblePreset(string presetMap)
        {
            if (!Directory.Exists(PresetsFolder))
                Directory.CreateDirectory(PresetsFolder);

            //get a list of presets from the directories in the preset folder
            var presets = Directory.GetDirectories(PresetsFolder);

            //clear old lists
            KnownPresets.Clear();
            PresetDictionary.Clear();

            foreach (string presetPath in presets)
            {
                string preset = Path.GetFileName(presetPath);

                foreach (var process in CompileProcesses[presetMap])
                {
                    string file = Path.Combine(presetPath, process.PresetFile);
                    if (File.Exists(file))
                    {
						var processDictionary = new ObservableDictionary<string, ObservableCollection<ConfigItem>>
						{
							{ process.Name, new ObservableCollection<ConfigItem>() }
						};
						//read the list of preset parameters
						var lines = File.ReadAllLines(file);

                        foreach (var line in lines)
                        {
	                        var item = ParsePresetLine(line);

                            if (process.ParameterList.Any(c => c.Parameter == item.Parameter))
                            {
                                //remove .clone if you are a masochist and wish to enter the object oriented version of hell
                                var equivalentItem = (ConfigItem)process.ParameterList.FirstOrDefault(c => c.Parameter == item.Parameter).Clone();

                                equivalentItem.Value = item.Value;

								//Copy extra information stored for custom programs
	                            if (item.Parameter == "program")
	                            {
									equivalentItem.Value2 = item.Value2;
									equivalentItem.WaitForExit= item.WaitForExit;
		                            equivalentItem.Warning = item.Warning;
	                            }

                                processDictionary[process.Name].Add(equivalentItem);
                            }
                        }

                        if (PresetDictionary.ContainsKey(preset))
                        {
                            foreach (var p in processDictionary)
                                PresetDictionary[preset].Add(p.Key, p.Value);
                        }
                        else
                        {
                            PresetDictionary.Add(preset, processDictionary);
                        }
                    }
                }
                CompilePalLogger.LogLine("Added preset {0} for processes {1}", preset, string.Join(", ", CompileProcesses));
                CurrentPreset = preset;
                KnownPresets.Add(preset);
            }
        }

        private static void AssemblePresetMaps()
        {
           if (!Directory.Exists(PresetsMapsFolder))
                Directory.CreateDirectory(PresetsMapsFolder);

            //get a list of map presets from the directories in the preset folder
            var presetsMaps = Directory.GetDirectories(PresetsMapsFolder);

            //clear old lists
            KnownPresetsMaps.Clear();
            PresetMapDictionary.Clear();

            foreach (string presetMapPath in presetsMaps)
            {
                string presetMap = Path.GetFileName(presetMapPath);

                PresetMapDictionary.Add(presetMap, new ObservableDictionary<string, ObservableCollection<ConfigItem>>());

                foreach (var process in CompileProcesses[presetMap])
                {
                    string file = Path.Combine(presetMapPath, process.PresetFile);
                    if (File.Exists(file))
                    {
                        var processDictionary = new ObservableDictionary<string, ObservableCollection<ConfigItem>>
                        {
                            { process.Name, new ObservableCollection<ConfigItem>() }
                        };
                        //read the list of preset map parameters
                        var lines = File.ReadAllLines(file);

                        foreach (var line in lines)
                        {
	                        var item = ParsePresetLine(line);

                            if (process.ParameterList.Any(c => c.Parameter == item.Parameter))
                            {
                                //remove .clone if you are a masochist and wish to enter the object oriented version of hell
                                var equivalentItem = (ConfigItem)process.ParameterList.FirstOrDefault(c => c.Parameter == item.Parameter).Clone();

                                equivalentItem.Value = item.Value;

								//Copy extra information stored for custom programs
	                            if (item.Parameter == "program")
	                            {
									equivalentItem.Value2 = item.Value2;
									equivalentItem.WaitForExit= item.WaitForExit;
		                            equivalentItem.Warning = item.Warning;
	                            }

                                processDictionary[process.Name].Add(equivalentItem);
                            }
                        }

                        foreach (var p in processDictionary)
                            PresetMapDictionary[presetMap].Add(p.Key, p.Value);
                    }
                }
                CompilePalLogger.LogLine("Added preset map {0} for processes {1}", presetMap, string.Join(", ", CompileProcesses[presetMap]));
                CurrentPresetMap = presetMap;
                KnownPresetsMaps.Add(presetMap);
            }
        }

        public static void SavePresetsMaps()
        {
            foreach (var knownPreset in KnownPresetsMaps)
            {
                string presetMapFolder = Path.Combine(PresetsMapsFolder, knownPreset);

                foreach (var presetMap in CompileProcesses)
                {
                    foreach (var compileProcess in presetMap.Value)
                    {
                        if (PresetMapDictionary[knownPreset].ContainsKey(compileProcess.Name))
                        {
                            var lines = new List<string>();
                            foreach (var item in PresetMapDictionary[knownPreset][compileProcess.Name])
                            {
                                string line = WritePresetLine(item);
                                lines.Add(line);
                            }

                            string presetMapPath = Path.Combine(presetMapFolder, compileProcess.PresetFile);

                            File.WriteAllLines(presetMapPath, lines);
                        }
                    }
                }
            }
        }

        public static void SaveProcesses()
        {
            foreach (var presetMap in CompileProcesses)
            {
                foreach (var compileProcess in presetMap.Value)
                {
                    string jsonMetadata = Path.Combine("./Parameters", compileProcess.Metadata.Name, "meta.json");

                    File.WriteAllText(jsonMetadata, JsonConvert.SerializeObject(compileProcess.Metadata, Formatting.Indented));
                }
            }
        }

        public static void SaveParameters(CompileProcess process)
        {
            string presetMapFolder = Path.Combine(PresetsMapsFolder, CurrentPresetMap);

            var lines = new List<string>();
            foreach(var parameter in PresetMapDictionary[CurrentPresetMap][process.Name])
            {
                string line = string.Concat(parameter.Parameter, ",");

                if (parameter.Value != null)
                    line += parameter.Value;

                /**** TODO: Check if this is the correct format? ****/
                if (parameter.Value2 != null)
                    line += string.Concat(",", parameter.Value2);
                /**** ****/

                lines.Add(line);
            }

            string processMapPath = Path.Combine(presetMapFolder, process.PresetFile);

            File.WriteAllLines(processMapPath, lines);
        }

        public static void NewPresetMap(string nameUnchecked, ConfigItem chosenItem)
        {
            string presetName = chosenItem.Name;
            string folderUnchecked = Path.Combine(PresetsMapsFolder, nameUnchecked);

            var increment = GetIncrementValueByCheckingForFilenameMatches(folderUnchecked);
            string name = increment == 0 ? nameUnchecked : string.Concat(nameUnchecked, $"({increment})");
            string folder = increment == 0 ? folderUnchecked : string.Concat(folderUnchecked, $"({increment})");

            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);

                CurrentPresetMap = name;

                var preset = PresetDictionary.FirstOrDefault(x => x.Key == presetName);
                if (preset.Key != null && preset.Value != null)
                {
                    var processes = preset.Value;
                    foreach (var process in processes)
                    {
                        if (process.Key != null)
                        {
                            string path = Path.ChangeExtension(Path.Combine(folder, process.Key), "csv");
                            string presetPath = Path.ChangeExtension(Path.Combine(PresetsFolder, preset.Key, process.Key), "csv");
                            File.Copy(presetPath, path);
                        }
                    }
                }
            }

            CompilingManager.MapFiles.Add(name, null);

            AssembleParameters();
        }

        public static void ClonePresetMap(string nameUnchecked)
        {
            string newFolderUnchecked = Path.Combine(PresetsMapsFolder, nameUnchecked);

            var increment = GetIncrementValueByCheckingForFilenameMatches(newFolderUnchecked);
            string newFolder = increment == 0 ? newFolderUnchecked : string.Concat(newFolderUnchecked, $"({increment})");

            string oldFolder = Path.Combine(PresetsMapsFolder, CurrentPresetMap);
            if (!Directory.Exists(newFolder))
            {
                SavePresetsMaps();

                DirectoryCopy(oldFolder, newFolder, true);

                AssembleParameters();
            }
        }

        private static int GetIncrementValueByCheckingForFilenameMatches(string folderUnchecked)
		{
            var increment = 0;
            string folder = folderUnchecked;
            while (Directory.Exists(folder))
            {
                increment++;
                folder = string.Concat(folderUnchecked, $"({increment})");
            }

            return increment;
        }

        public static void RemovePresetMap(string name)
        {
            string folder = Path.Combine(PresetsMapsFolder, name);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            CompilingManager.MapFiles.Remove(name);
            PersistenceManager.ForceMapFilesWrite();

            PresetMapDictionary.Remove(name);

            AssembleParameters();
        }

        public static void RemoveProcess(string name)
        {
            string presetPath = Path.Combine(PresetsMapsFolder, CurrentPresetMap, name.ToLower() + ".csv");
            if (File.Exists(presetPath))
            {
                File.Delete(presetPath);
            }
        }

        public static ObservableCollection<ConfigItem> GetParameters(string processName, bool doRun = false)
        {
            var list = new ObservableCollection<ConfigItem>();

            string jsonParameters = Path.Combine(ParametersFolder, processName, "parameters.json");

            if (File.Exists(jsonParameters))
            {
                ConfigItem[] items = JsonConvert.DeserializeObject<ConfigItem[]>(File.ReadAllText(jsonParameters));
                foreach (var configItem in items)
                {
                    list.Add(configItem);
                }

                // add custom parameter to all runnable steps
                if (doRun)
                {
                    list.Add(new ConfigItem()
                    {
                        Name = "Command Line Argument",
                        CanHaveValue = true,
                        CanBeUsedMoreThanOnce = true,
                        Description = "Passes value as a command line argument",
                    });
                }
            }
            else
            {
                string csvParameters = Path.Combine(ParametersFolder, processName + ".csv");

                if (File.Exists(csvParameters))
                {
                    var baselines = File.ReadAllLines(csvParameters);

                    for (int i = 2; i < baselines.Length; i++)
                    {
                        string baseline = baselines[i];

                        var item = ParseBaseLine(baseline);

                        list.Add(item);
                    }

                    ConfigItem[] items = list.ToArray();

                    File.WriteAllText(jsonParameters, JsonConvert.SerializeObject(items, Formatting.Indented));
                }
                else
                {
                    throw new FileNotFoundException("Parameter files could not be found for " + processName);
                }
            }


            return list;
        }

        private static ConfigItem ParsePresetLine(string line)
        {
            var item = new ConfigItem();

            var pieces = line.Split(',');

            if (pieces.Any())
            {
                // Custom parameter stores name as first value instead of parameter, because it has no parameter
                if (pieces[0] == "Command Line Argument")
                    item.Name = pieces[0];
                else
                    item.Parameter = pieces[0];

                if (pieces.Count() >= 2)
                    item.Value = pieces[1];
				//Handle extra information stored for custom programs
	            if (pieces.Count() >= 3)
		            item.Value2 = pieces[2];
	            if (pieces.Length >= 4)
		            item.ReadOutput = Convert.ToBoolean(pieces[3]);
	            if (pieces.Length >= 5)
					item.WaitForExit= Convert.ToBoolean(pieces[4]);
	            if (pieces.Length >= 6)
		            item.Warning = pieces[5];
            }
            return item;
        }

		private static string WritePresetLine(ConfigItem item)
        {
			//Handle extra information stored for custom programs
	        if (item.Name == "Run Program")
		        return $"{item.Parameter},{item.Value},{item.Value2},{item.ReadOutput},{item.WaitForExit},{item.Warning}";
            else if (item.Name == "Command Line Argument") // Command line arguments have no parameter value
                return $"{item.Name},{item.Value}";
            return $"{item.Parameter},{item.Value}";
        }

        private static ConfigItem ParseBaseLine(string line)
        {
            var item = new ConfigItem();

            var pieces = line.Split(';');

            if (pieces.Any())
            {
                item.Name = pieces[0];
                if (pieces.Count() >= 2)
                    item.Parameter = pieces[1];
                if (pieces.Count() >= 3)
                    item.CanHaveValue = bool.Parse(pieces[2]);
                if (pieces.Count() >= 4)
                    item.Description = pieces[3];
                if (pieces.Count() >= 5)
                    item.Warning = pieces[4];
            }
            return item;
        }

        private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);
            DirectoryInfo[] dirs = dir.GetDirectories();

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            // If the destination directory doesn't exist, create it. 
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string temppath = Path.Combine(destDirName, file.Name);
                file.CopyTo(temppath, false);
            }

            // If copying subdirectories, copy them and their contents to new location. 
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs);
                }
            }
        }
    }
}
