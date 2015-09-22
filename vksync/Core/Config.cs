using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vksync.Core
{
    public class Config
    {
        string FilePath { get; } = Environment.ExpandEnvironmentVariables(@"%APPDATA%\.vkmusic\defaults");

        public Config()
        {
            if (!Directory.Exists(Path.GetDirectoryName(FilePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            }

            if (!File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, "{}");
            }
        }

        public string Get(string key)
        {
            var content = File.ReadAllText(FilePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            return jsn[key]?.ToObject<string>();
        }

        public void Set(string key, string value)
        {
            var content = File.ReadAllText(FilePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            jsn[key] = value;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(jsn));
        }
    }
}