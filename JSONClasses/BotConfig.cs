using Newtonsoft.Json;

namespace Badger.Class
{
    public class BotConfig
    {
        [JsonProperty("token")]
        public string Token { get; private set; }

        [JsonProperty("connection_string")]
        public string ConnectionString { get; private set; }
    }
}
