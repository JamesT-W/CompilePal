﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CompilePalX.Compilers;
using CompilePalX.Compiling;
using System.Runtime.InteropServices;
using CompilePalX.Annotations;
using CompilePalX.Configuration;

namespace CompilePalX
{
    internal delegate void CompileCleared();
    internal delegate void CompileStarted();
    internal delegate void CompileFinished();

    class Map : INotifyPropertyChanged
    {
        private string file;

        public string File
        {
            get => file;
            set { file = value; OnPropertyChanged(nameof(File));  }
        }

        private bool compile;
        public bool Compile
        {
            get => compile;
            set { compile = value; OnPropertyChanged(nameof(Compile)); }
        }

        public Map(string file, bool compile = true)
        {
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

    static class CompilingManager
    {
        static CompilingManager()
        {
            CompilePalLogger.OnErrorFound += CompilePalLogger_OnErrorFound;
        }
            
        private static void CompilePalLogger_OnErrorFound(Error e)
        {
            CurrentCompileProcess.CompileErrors.Add(e);

            if (e.Severity == 5 && IsCompiling)
            {
                //We're currently in the thread we would like to kill, so make sure we invoke from the window thread to do this.
                MainWindow.ActiveDispatcher.Invoke(() =>
                {
                    CompilePalLogger.LogLineColor("An error cancelled the compile.", Error.GetSeverityBrush(5));
                    CancelCompile();
                    ProgressManager.ErrorProgress();
                });
            }
        }

        public static event CompileCleared OnClear;
        public static event CompileFinished OnStart;
        public static event CompileFinished OnFinish;

        public static ObservableDictionary<string, Map> MapFiles = new ObservableDictionary<string, Map>();

        private static Thread compileThread;
        private static Stopwatch compileTimeStopwatch = new Stopwatch();
        private static Stopwatch compileSpecificMapTimeStopwatch = new Stopwatch();
        private static Stopwatch compileSpecificProcessTimeStopwatch = new Stopwatch();

        public static bool IsCompiling { get; private set; }

        public static string CurrentMapNameCompiling { get; private set; }

        public static void ToggleCompileState()
        {
            if (IsCompiling)
                CancelCompile();
            else
                StartCompile();
        }

        public static void StartCompile()
        {
            OnStart();

            // Tells windows to not go to sleep during compile
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS | NativeMethods.ES_SYSTEM_REQUIRED);

            AnalyticsManager.Compile();

            IsCompiling = true;

            compileTimeStopwatch.Start();
            compileSpecificMapTimeStopwatch.Start();
            compileSpecificProcessTimeStopwatch.Start();

            OnClear();

            compileThread = new Thread(CompileThreaded);
            compileThread.Start();
        }

        internal static CompileProcess CurrentCompileProcess;

        private static void CompileThreaded()
        {
            try
            {
                ProgressManager.SetProgress(0);

                var mapErrors = new List<MapErrors>();

                var mapsToCompile = MapFiles.Where(x => x.Value != null && x.Value.Compile).Where(x => ConfigurationManager.KnownPresetsMaps.Any(y => y == x.Key)).OrderBy(x => x.Key).Select(x => x.Key).ToList();
                foreach (var mapPresetName in mapsToCompile)
                {
                    CurrentMapNameCompiling = mapPresetName;

                    CompilePalLogger.LogLine($"Starting a '{CurrentMapNameCompiling}' compile.");

                    Map map = MapFiles.FirstOrDefault(x => x.Key == CurrentMapNameCompiling).Value;

                    if (map == null || !map.Compile)
                    {
                        CompilePalLogger.LogDebug($"Skipping {MapFiles.Where(x => x.Value == map).Select(x => x.Key).FirstOrDefault()}");
                        continue;
                    }

                    string mapFile = map.File;
                    string cleanMapName = Path.GetFileNameWithoutExtension(mapFile);

                    var compileErrors = new List<Error>();
                    CompilePalLogger.LogLine($"Starting compilation of {cleanMapName}");

                    //Update the grid so we have the most up to date order
                    OrderManager.UpdateOrder();

                    GameConfigurationManager.BackupCurrentContext();

                    var allProcessesCompilingByMapName = MainWindow.CompileProcessesSubList[mapPresetName].Where(x => x.Metadata.DoRun && ConfigurationManager.PresetMapDictionary[CurrentMapNameCompiling].ContainsKey(x.Name));
                    for (int i = 0; i < allProcessesCompilingByMapName.Count(); i++)
                    {
                        CurrentCompileProcess = allProcessesCompilingByMapName.ElementAt(i);

                        ProgressManager.SetProgress(ProgressManager.Progress, true);

                        CurrentCompileProcess.Run(GameConfigurationManager.BuildContext(mapFile));

                        compileErrors.AddRange(CurrentCompileProcess.CompileErrors);

                        //Portal 2 cannot work with leaks, stop compiling if we do get a leak.
                        if (GameConfigurationManager.GameConfiguration.Name == "Portal 2")
                        {
                            if (CurrentCompileProcess.Name == "VBSP" && CurrentCompileProcess.CompileErrors.Count > 0)
                            {
                                //we have a VBSP error, aka a leak -> stop compiling;
                                break;
                            }
                        }
                        else if (GameConfigurationManager.GameConfiguration.Name == "Counter-Strike: Global Offensive")
                        {
                            if (CurrentCompileProcess.Name == "VBSP" &&
                                (CurrentCompileProcess.CompileErrors.Any(x => x.ShortDescription == ErrorFinder.instanceErrorMessage) ||
                                CurrentCompileProcess.CompileErrors.Any(x => x.ShortDescription == "**** leaked ****")))
                            {
                                //either a leak or instance not found error has occurred -> stop compiling;
                                break;
                            }
                        }

                        ProgressManager.Progress += (1d / MainWindow.CompileProcessesSubList
                                                        .Where(x => MapFiles.Any()
                                                            && MapFiles.Keys.Any(y => y == x.Key)
                                                            && MapFiles[x.Key] != null
                                                            && MapFiles[x.Key].Compile)
                                                        .SelectMany(x => x.Value)
                                                        .Count(c => c.Metadata.DoRun &&
                                                            ConfigurationManager.PresetMapDictionary[CurrentMapNameCompiling]
                                                                .ContainsKey(CurrentCompileProcess.Name))
                        );

                        CompilePalLogger.LogLineColor(
                            $"'{CurrentCompileProcess.Name}' finished for '{CurrentMapNameCompiling}' in {compileSpecificProcessTimeStopwatch.Elapsed.ToString(@"hh\:mm\:ss")}\n", Brushes.ForestGreen);

                        compileSpecificProcessTimeStopwatch.Restart();
                    }

                    mapErrors.Add(new MapErrors { MapName = cleanMapName, Errors = compileErrors });

                    GameConfigurationManager.RestoreCurrentContext();

                    CompilePalLogger.LogLineColor(
                        $"'{CurrentMapNameCompiling}' compile finished in {compileSpecificMapTimeStopwatch.Elapsed.ToString(@"hh\:mm\:ss")}\n", Brushes.ForestGreen);

                    compileSpecificMapTimeStopwatch.Restart();
                }

                MainWindow.ActiveDispatcher.Invoke(() => postCompile(mapErrors));
            }
            catch (ThreadAbortException) { ProgressManager.ErrorProgress(); }
        }

        private static void postCompile(List<MapErrors> errors)
        {
            CompilePalLogger.LogLineColor(
	            $"Finished compiling all maps in {compileTimeStopwatch.Elapsed.ToString(@"hh\:mm\:ss")}", Brushes.ForestGreen);

            if (errors != null && errors.Any())
            {
                int numErrors = errors.Sum(e => e.Errors.Count);
                int maxSeverity = errors.Max(e => e.Errors.Any() ? e.Errors.Max(e2 => e2.Severity) : 0);
                CompilePalLogger.LogLineColor("{0} errors/warnings logged:", Error.GetSeverityBrush(maxSeverity), numErrors);

                foreach (var map in errors)
                {
                    CompilePalLogger.Log("  ");

                    if (!map.Errors.Any())
                    {
                        CompilePalLogger.LogLineColor("No errors/warnings logged for {0}", Error.GetSeverityBrush(0), map.MapName);
                        continue;
                    }

                    int mapMaxSeverity = map.Errors.Max(e => e.Severity);
                    CompilePalLogger.LogLineColor("{0} errors/warnings logged for {1}:", Error.GetSeverityBrush(mapMaxSeverity), map.Errors.Count, map.MapName);

                    var distinctErrors = map.Errors.GroupBy(e => e.ID);
                    foreach (var errorList in distinctErrors)
                    {
                        var error = errorList.First();

                        string errorText = $"{errorList.Count()}x: {error.SeverityText}: {error.ShortDescription}";

                        CompilePalLogger.Log("    ● ");
                        CompilePalLogger.LogCompileError(errorText, error);
                        CompilePalLogger.LogLine();

                        if (error.Severity >= 3)
                            AnalyticsManager.CompileError();
                    }
                }
            }

            OnFinish();

            compileTimeStopwatch.Reset();
            compileSpecificMapTimeStopwatch.Reset();
            compileSpecificProcessTimeStopwatch.Reset();

            IsCompiling = false;

            // Tells windows it's now okay to enter sleep
            NativeMethods.SetThreadExecutionState(NativeMethods.ES_CONTINUOUS);
        }

        public static void CancelCompile()
        {
            try
            {
                compileThread.Abort();
            }
            catch
            {
            }
            IsCompiling = false;

            foreach (var compileProcess in ConfigurationManager.CompileProcesses.SelectMany(x => x.Value).Where(cP => cP.Process != null))
            {
                try
                {
                    compileProcess.Cancel();
                    compileProcess.Process.Kill();

                    CompilePalLogger.LogLineColor("Killed {0}.", Brushes.OrangeRed, compileProcess.Metadata.Name);
                }
                catch (InvalidOperationException) { }
                catch (Exception e) { ExceptionHandler.LogException(e); }
            }

            ProgressManager.SetProgress(0);

            CompilePalLogger.LogLineColor("Compile forcefully ended.", Brushes.OrangeRed);

            postCompile(null);
        }

        public static Stopwatch GetTime()
        {
            return compileTimeStopwatch;
        }

        class MapErrors
        {
            public string MapName { get; set; }
            public List<Error> Errors { get; set; }
        }

        internal static class NativeMethods
        {
            // Import SetThreadExecutionState Win32 API and necessary flags
            [DllImport("kernel32.dll")]
            public static extern uint SetThreadExecutionState(uint esFlags);
            public const uint ES_CONTINUOUS = 0x80000000;
            public const uint ES_SYSTEM_REQUIRED = 0x00000001;
        }
    }
}
