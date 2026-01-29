using Newtonsoft.Json;
using System;
using System.Collections.Generic;


namespace Nine.Design.PollingTool
{
    public class HistoryItem
    {
        public string Endpoint { get; set; }
        public string MachineCount { get; set; }
        public string PollInterval { get; set; }
        public string ParametersJson { get; set; }

        [JsonProperty("saved_time")]
        public DateTime SavedTime { get; set; } 
    }

    public class HistoryData
    {
        [JsonProperty("history")]
        public List<HistoryItem> Items { get; set; }

        public HistoryData()
        {
            Items = new List<HistoryItem>();
        }
    }

    public class MachineItem
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
    }
}