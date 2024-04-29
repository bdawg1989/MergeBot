using System.Collections.Generic;
using System.Text.Json;
using System.IO;
using PKHeX.Core;

namespace SysBot.Pokemon;

public class TradeCodeStorage
{

    public bool AddOrUpdateTradeCode(ulong userID, int tradeCode, string? ot, int tid, int sid)
    {
        LoadFromFile();
        var gameVersion = GameVersion.SWSH;

        if (gameVersion == GameVersion.SWSH)
        {
            // Always save SID as 0 for SWSH
            sid = 0;
        }

        if (_tradeCodeDetails.ContainsKey(userID))
        {
            // If the user already exists, update the trade code and keep the existing OT, TID, and SID
            _tradeCodeDetails[userID].Code = tradeCode;
        }
        else
        {
            // If the user doesn't exist, add a new entry with the specified trade code and OT, TID, SID
            _tradeCodeDetails[userID] = new TradeCodeDetails
            {
                Code = tradeCode,
                OT = ot,
                TID = tid,
                SID = sid,
                TradeCount = 0
            };
        }

        SaveToFile();
        return true;
    }


    private const string FileName = "tradecodes.json";
    private Dictionary<ulong, TradeCodeDetails> _tradeCodeDetails = new Dictionary<ulong, TradeCodeDetails>();

    public class TradeCodeDetails
    {
        public int Code { get; set; }
        public string? OT { get; set; }
        public int TID { get; set; }
        public int SID { get; set; }
        public int TradeCount { get; set; }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public TradeCodeStorage()
    {
        LoadFromFile();
    }

    public int GetTradeCode(ulong trainerID)
    {
        LoadFromFile();

        if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
        {
            details.TradeCount++;
            SaveToFile();
            return details.Code;
        }

        var code = GenerateRandomTradeCode();
        _tradeCodeDetails[trainerID] = new TradeCodeDetails { Code = code, TradeCount = 1 };
        SaveToFile();
        return code;
    }

    private static int GenerateRandomTradeCode()
    {
        var settings = new TradeSettings();
        return settings.GetRandomTradeCode();
    }

    private void LoadFromFile()
    {
        if (File.Exists(FileName))
        {
            string json = File.ReadAllText(FileName);
            _tradeCodeDetails = JsonSerializer.Deserialize<Dictionary<ulong, TradeCodeDetails>>(json, SerializerOptions);
        }
        else
        {
            _tradeCodeDetails = new Dictionary<ulong, TradeCodeDetails>();
        }
    }

    public bool DeleteTradeCode(ulong trainerID)
    {
        LoadFromFile();

        if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
        {
            // Remove only the trade code
            details.Code = 0;
            SaveToFile();
            return true;
        }
        return false;
    }

    private void SaveToFile()
    {
        string json = JsonSerializer.Serialize(_tradeCodeDetails, SerializerOptions);
        File.WriteAllText(FileName, json);
    }

    public int GetTradeCount(ulong trainerID)
    {
        LoadFromFile();

        if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
        {
            return details.TradeCount;
        }
        return 0;
    }

    public TradeCodeDetails? GetTradeDetails(ulong trainerID)
    {
        LoadFromFile();

        if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
        {
            return details;
        }
        return null;
    }

    public void UpdateTradeDetails(ulong trainerID, string? ot, int tid, int sid)
    {
        LoadFromFile();

        if (_tradeCodeDetails.TryGetValue(trainerID, out var details))
        {
                details.OT = ot;
                details.TID = tid;
                details.SID = sid;
                SaveToFile();
            }
        }
    }
