using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using CompilePalX.Compiling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CompilePalX
{
    static class PersistenceManager
    {
        private static string mapFiles = "mapfiles.json";
        public static void Init()
        {
            if (File.Exists(mapFiles))
            {
                var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(mapFiles));

                if (dictionary.Any())
                {
                    Map map = null;

                    // make this backwards compatible by allowing plain string values for map (old format)
                    foreach (var item in dictionary)
                    {
                        if (item.Value is string mapFile)
                            map = new Map(mapFile);
                        else if (item.Value is JObject obj)
                            map = obj.ToObject<Map>();
                        else if (item.Value is null)
                            map = null;
                        else
                        {
                            CompilePalLogger.LogDebug($"Failed to load item from mapfiles: {item}");
                            continue;
                        }

                        CompilingManager.MapFiles[item.Key] = map;
                    }
                }
            }

            CompilingManager.MapFiles.CollectionChanged +=
                delegate
                {
                    File.WriteAllText(mapFiles, JsonConvert.SerializeObject(CompilingManager.MapFiles,Formatting.Indented));
                };
        }
    }
}
