using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shell;

namespace CompilePalX
{
    internal delegate void OnTitleChange(string title);
    internal delegate void OnProgressChange(double progress);
    static class ProgressManager
    {
        public static event OnTitleChange TitleChange;
        public static event OnProgressChange ProgressChange;

        private static TaskbarItemInfo taskbarInfo;
        private static bool ready;
        private static string defaultTitle = "Compile Pal Multi";

        static public void Init(TaskbarItemInfo _taskbarInfo)
        {
            taskbarInfo = _taskbarInfo;
            ready = true;

            TitleChange(
	            $"{defaultTitle} {UpdateManager.CurrentVersion}X {GameConfigurationManager.GameConfiguration.Name}");
        }


        static public double Progress
        {
            get
            {
                return taskbarInfo.Dispatcher.Invoke(() => { return ready ? taskbarInfo.ProgressValue : 0; });
            }
            set { SetProgress(value); }
        }

        static public void SetProgress(double progress, bool forceUseCompileTaskbar = false, bool useNextCompileProcessName = false)
        {
            if (ready)
            {
                taskbarInfo.Dispatcher.Invoke(() =>
                {
                    taskbarInfo.ProgressState = TaskbarItemProgressState.Normal;

                    taskbarInfo.ProgressValue = progress;
                    ProgressChange(progress * 100);

                    var compileProcessName = useNextCompileProcessName ? CompilingManager.NextCompileProcess : CompilingManager.CurrentCompileProcess;

                    if (progress >= 1)
                    {
                        TitleChange($"{Math.Floor(progress * 100d)}% - {CompilingManager.CurrentMapNameCompiling} - {compileProcessName} - {defaultTitle} {UpdateManager.CurrentVersion}");

                        System.Media.SystemSounds.Exclamation.Play();
                    }
                    else if (progress <= 0 && !forceUseCompileTaskbar)
                    {
                        taskbarInfo.ProgressState = TaskbarItemProgressState.None;
                        TitleChange(
	                        $"{defaultTitle} {UpdateManager.CurrentVersion}X {GameConfigurationManager.GameConfiguration.Name}");
                    }
                    else
                    {
                        TitleChange($"{Math.Floor(progress * 100d)}% - {CompilingManager.CurrentMapNameCompiling} - {compileProcessName} - {defaultTitle} {UpdateManager.CurrentVersion}");
                    }
                });

            }
        }

        static public void ErrorProgress()
        {
            taskbarInfo.Dispatcher.Invoke(() =>
                                          {
                                              if (ready)
                                              {
                                                  SetProgress(1);
                                                  taskbarInfo.ProgressState = TaskbarItemProgressState.Error;
                                              }
                                          });

        }

        static public void PingProgress()
        {
            taskbarInfo.Dispatcher.Invoke(() =>
                                          {
                                              if (ready)
                                              {
                                                  if (taskbarInfo.ProgressValue >= 1)
                                                      SetProgress(0);
                                              }
                                          });
        }
    }
}
