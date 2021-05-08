﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using CompilePalX.Compiling;
using Newtonsoft.Json;

namespace CompilePalX
{
    class CompileProcess
    {
        public string ParameterFolder = "./Parameters";
	    public bool Draggable = true; // set to false if we ever want to disable reordering non custom compile steps
        public List<Error> CompileErrors;

        public CompileProcess(string name)
        {
            string jsonMetadata = Path.Combine(ParameterFolder, name, "meta.json");

            if (File.Exists(jsonMetadata))
            {
                Metadata = JsonConvert.DeserializeObject<CompileMetadata>(File.ReadAllText(jsonMetadata));

                CompilePalLogger.LogLine("Loaded JSON metadata {0} from {1} at order {2}", Metadata.Name, jsonMetadata, Metadata.Order);
            }
            else
            {
                string legacyMetadata = Path.Combine(ParameterFolder, name + ".meta");

                if (File.Exists(legacyMetadata))
                {
                    Metadata = LoadLegacyData(legacyMetadata);

                    Directory.CreateDirectory(Path.Combine(ParameterFolder, name));

                    File.WriteAllText(jsonMetadata, JsonConvert.SerializeObject(Metadata, Formatting.Indented));

                    CompilePalLogger.LogLine("Loaded CSV metadata {0} from {1} at order {2}, converted to JSON successfully.", Metadata.Name, legacyMetadata, Metadata.Order);
                }
                else
                {
                    throw new FileNotFoundException("The metadata file for " + name + " could not be found.");
                }

            }

            ParameterList = ConfigurationManager.GetParameters(Metadata.Name, Metadata.DoRun);
        }

        public static CompileMetadata LoadLegacyData(string csvFile)
        {
            CompileMetadata metadata = new CompileMetadata();

            var lines = File.ReadAllLines(csvFile);

            metadata.Name = lines[0];
            metadata.Path = lines[1];
            metadata.BasisString = lines[3];
            metadata.Order = float.Parse(lines[4], CultureInfo.InvariantCulture);
            metadata.DoRun = bool.Parse(lines[5]);
            metadata.ReadOutput = bool.Parse(lines[6]);
            if (lines.Count() > 7)
                metadata.Warning = lines[7];
            if (lines.Count() > 8)
                metadata.Description = lines[8];

            return metadata;
        }

        public CompileMetadata Metadata;

        public string PresetFile { get { return Metadata.Name + ".csv"; } }

        public double Ordering { get { return Metadata.Order; } }

        // not actually from metadata, only used to display inside CompileProcessesListBox
        public string PreviousTimeTaken {
            get
            {
                if (CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] == null ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes == null ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name] == null ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name].PreviousTimeTaken == null
                )
                    return null;

                return string.Concat("(", CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name].PreviousTimeTaken, ")");
            }
        }

        public bool DoRun
        {
            get
            {
                if (CompilingManager.MapFiles == null ||
                    CompilingManager.MapFiles.Count() == 0 ||
                    !CompilingManager.MapFiles.Any(x => x.Key == ConfigurationManager.CurrentPresetMap) ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] == null ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes.Count() == 0 ||
                    !CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes.Any(x => x.Key == Metadata.Name) ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name] == null
                )
                    return Metadata.DoRun;
                else
                    return CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name].DoRun;
            }
            set
            {
                Metadata.DoRun = value;

                if (CompilingManager.MapFiles == null ||
                    CompilingManager.MapFiles.Count() == 0 ||
                    !CompilingManager.MapFiles.Any(x => x.Key == ConfigurationManager.CurrentPresetMap) ||
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap] == null
                )
                {
                    return;
                }

                if (CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes == null)
                {
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes = new Dictionary<string, MapProcess>();
                }

                if (CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes.Count() == 0 ||
                    !CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes.Keys.Any(x => x == Metadata.Name)
                )
                {
                    CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes.Add(Metadata.Name, new MapProcess());
                }


                CompilingManager.MapFiles[ConfigurationManager.CurrentPresetMap].Processes[Metadata.Name].DoRun = value;
            }
        }
        public string Name { get { return Metadata.Name; } }
        public string Description { get { return Metadata.Description; } }
        public string Warning { get { return Metadata.Warning; } }
		public bool IsDraggable { get { return Draggable; } }

        public Process Process;

        public virtual void Run(CompileContext context)
        {

        }
        public virtual void Cancel()
        {

        }

        public ObservableCollection<ConfigItem> ParameterList = new ObservableCollection<ConfigItem>();


        public string GetParameterString(bool overrideUseCompilingMapName = false)
        {
            var mapName = overrideUseCompilingMapName ? CompilingManager.CurrentMapNameCompiling : ConfigurationManager.CurrentPresetMap;

            string parameters = string.Empty;
            foreach (var parameter in ConfigurationManager.PresetMapDictionary[mapName][Name])
            {
                // if on Windows, set threads to max available in the system by default, if not already set to a value
                if (parameter.Name.ToLower() == "threads" && string.IsNullOrWhiteSpace(parameter.Value) && Environment.OSVersion.Platform == PlatformID.Win32NT)
				{
                    parameter.Value = Environment.ProcessorCount.ToString();
                }

				parameters += parameter.Parameter;

	            if (parameter.CanHaveValue && !string.IsNullOrEmpty(parameter.Value))
	            {
					//Handle additional parameters in CUSTOM process
					if (parameter.Name == "Run Program")
					{
						//Add args
						parameters += " " + parameter.Value;

						//Read Ouput
						if (parameter.ReadOutput)
							parameters += " " + parameter.ReadOutput;
					}
					else
						// protect filepaths in quotes, since they can contain -
						if (parameter.ValueIsFile || parameter.Value2IsFile)
							parameters += $" \"{parameter.Value}\"";
						else
							parameters += " " + parameter.Value;
	            }
            }

            parameters += Metadata.BasisString;

            return parameters;
        }

        public override string ToString()
        {
            return Metadata.Name;
        }
    }

    class CompileMetadata
    {
        public string Name { get; set; }
        public string Path { get; set; }

        public float Order { get; set; }

        public bool DoRun { get; set; }
        public bool ReadOutput { get; set; }

        public string Description { get; set; }
        public string Warning { get; set; }

        public bool PresetDefault { get; set; }

        public string BasisString { get; set; }
    }

    class CompileContext
    {
        public string MapFile;
        public GameConfiguration Configuration;
        public string BSPFile;
        public string CopyLocation;
    }
}
