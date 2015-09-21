using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace vksync.Core
{
    public class Config
    {
        string filePath { get; set; } = Environment.ExpandEnvironmentVariables(@"%APPDATA%\.vkmusic\defaults");

        public Config()
        {
            if (!Directory.Exists(Path.GetDirectoryName(filePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            }

            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, "{}");
            }
        }

        public string Get(string key)
        {
            var content = File.ReadAllText(filePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            return jsn[key]?.ToObject<string>();
        }

        public void Set(string key, string value)
        {
            var content = File.ReadAllText(filePath);
            JToken jsn = JsonConvert.DeserializeObject(content) as JToken;
            jsn[key] = value;
            File.WriteAllText(filePath, JsonConvert.SerializeObject(jsn));
        }
    }
}