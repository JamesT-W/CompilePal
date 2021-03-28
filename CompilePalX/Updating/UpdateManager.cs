﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CompilePalX.Compiling;

namespace CompilePalX
{
    internal delegate void UpdateFound();
    static class UpdateManager
    {
        public static event UpdateFound OnUpdateFound;

        private static Version currentVersion;
        public static string CurrentVersion => currentVersion.ToString(isPrerelease ? 2 : 1);

        private static Version latestVersion;
        public static string LatestVersion => latestVersion.ToString(isPrerelease ? 2 : 1);

        private const string LatestVersionURL = "https://raw.githubusercontent.com/JamesT-W/CompilePal/master/CompilePalX/version.txt";
        private const string LatestPrereleaseVersionURL = "https://raw.githubusercontent.com/JamesT-W/CompilePal/master/CompilePalX/version_prerelease.txt";

        private static string MajorUpdateURL = "https://github.com/JamesT-W/CompilePal/releases/latest";
        // Tags must be in form: v0major.minor
        private static string PrereleaseUpdateURL => $"https://github.com/JamesT-W/CompilePal/releases/tag/v0{LatestVersion}";
        public static Uri UpdateURL => new Uri(isPrerelease ? PrereleaseUpdateURL : MajorUpdateURL);

        private static bool isPrerelease = false;


        public static void CheckVersion()
        {
            string currentVersionString = GetValidVersionString(File.ReadAllText("./version.txt"));
            string currentPrereleaseVersionString = GetValidVersionString(File.ReadAllText("./version_prerelease.txt") + ".0.0");

            currentVersion = Version.Parse(currentVersionString);
            Version currentPrereleaseVersion = Version.Parse(currentPrereleaseVersionString);

            if (currentPrereleaseVersion > currentVersion)
            {
	            currentVersion = currentPrereleaseVersion;
                isPrerelease = true;
            }

            // store version info in registry
            RegistryManager.Write("Version", currentVersionString);
            RegistryManager.Write("PrereleaseVersion", currentPrereleaseVersionString);

            Thread updaterThread = new Thread(ThreadedCheck);
            updaterThread.Start();
        }

        static void ThreadedCheck()
        {
            try
            {
                CompilePalLogger.LogLine("Fetching update information...");

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                var c = new WebClient();
                string newVersion = GetValidVersionString(c.DownloadString(new Uri(isPrerelease ? LatestPrereleaseVersionURL : LatestVersionURL)));

                latestVersion = Version.Parse(newVersion);

                if (currentVersion < latestVersion)
                {
                    MainWindow.ActiveDispatcher.Invoke(OnUpdateFound);

                    CompilePalLogger.LogLine("Updater found that Compile Pal Multi is outdated.");
                }
                else
                {
                    CompilePalLogger.LogLine("Updater found that Compile Pal Multi is up to date.");
                }

                ProgressManager.SetProgress(ProgressManager.Progress);
            }
            catch (WebException e)
            {
                CompilePalLogger.LogLine("Failed to find update information as an error was returned:");
                CompilePalLogger.LogLine(e.ToString());
            }
        }

        private static string GetValidVersionString(string str)
        {
            // Ensures string is always in format: major.minor.build.revision
            return str + string.Concat(Enumerable.Repeat(".0", 3 - str.Count(s => s == '.')));
        }
    }
}
