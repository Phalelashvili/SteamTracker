using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using Npgsql;
using System.Data;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Configuration;
using System.Collections.Specialized;

namespace SteamTracker
{
    public class Program
    {
        private static readonly NameValueCollection Config = ConfigurationManager.AppSettings;

        private static readonly string[] ApiKeys = File.ReadAllLines("apiKeys.txt");

        private static long _prevProgress;
        private static long _bruteForceProgress;
        private static long _bforceTarget;

        private static int _apiKeyIndex;
        private static int _apiCalls;
        private static int _callsPerMinute;
        
        private static readonly object SqlLock = new object();
        private static readonly object FillSteamIdsLock = new object();
        private static readonly object GetApiKeyLock = new object();

        private static NpgsqlConnection _psql;
        
        private static void Main(string[] args)
        {
            if (Config.Count == 0)
            {
                Console.WriteLine("App.config is empty");
                Environment.Exit(1);
            }

            var threadCount = int.Parse(Config["Thread Count"]);
            _bforceTarget = long.Parse(Config["Brute Force Target"]);
            Console.WriteLine($"Thread Count: {threadCount}");

            _psql = new NpgsqlConnection(Config["Connection String"]);
            _psql.Open();

            DateTime startTime = DateTime.Now;

            if (Config["Method"] == "BruteForce")
            {
                /* reading range of steamids from file is necessary when program
                 * is deployed on multiple machines, scanning different ranges */

                if (!long.TryParse(File.ReadAllText("bforceProgress"), out _bruteForceProgress))
                {
                    Console.WriteLine("failed parsing bforceProgress, progress set to base");
                    _bruteForceProgress = long.Parse(Config["Brute Force Base"]);
                }

                for (int i = 0; i < threadCount; i++)
                    new Thread(BruteForce).Start();

                while (true)
                {
                    var time = DateTime.Now - startTime;
                    Console.WriteLine($"API Calls: {_apiCalls} ({_callsPerMinute}call/m) | {time} | BForce Progress: {_bruteForceProgress} (+{_bruteForceProgress - long.Parse(Config["Brute Force Base"])})");
                    _callsPerMinute = 0;
                    Thread.Sleep(60000);
                }
            }
            else
            {
                Console.WriteLine("No matching methods in App.config");
                Environment.Exit(1);
            }
            Console.WriteLine("Method: " + Config["Method"]);
        }
        
        private static DataTable Query(NpgsqlCommand command)
        {
            var dt = new DataTable();
            command.CommandTimeout = 0;
            lock (SqlLock)
            {
                try
                {
                    NpgsqlDataReader reader = command.ExecuteReader();
                    dt.Load(reader);
                    reader.Close();
                }
                catch (InvalidOperationException) // temp fix. for some reason, server randomly drops connection 
                {
                    _psql.Open();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
            return dt;
        }

        static dynamic ValueOrNull(dynamic obj)
        {
            return obj == null ? DBNull.Value : obj.Value;
        }

        private static void BruteForce()
        {
            while (true)
            {
                var steamIDs = FillSteamIDs();

                while (true)
                {
                    try
                    {
                        AddUsersToDatabase(
                                GetPlayerSummaries(steamIDs),
                                ref steamIDs
                            );
                        break;
                    }
                    catch (AggregateException)
                    {
                    }
                    catch (Exception e)
                    {
                        if (e is DataNotReturnedException || e is HttpRequestException || e is Newtonsoft.Json.JsonReaderException)
                            Console.WriteLine(e.GetType());
                        else
                            Console.WriteLine(e);
                    }
                }
            }
        }

        private static List<string> FillSteamIDs()
        {
            lock (FillSteamIdsLock)
            {
                // takes next 100 steamid 
                var steamIDs = new List<string>();
                _prevProgress = _bruteForceProgress;
                while (steamIDs.Count < 100)
                {
                    if (++_bruteForceProgress - _prevProgress > 1000)
                        File.WriteAllText("bforceProgress", _bruteForceProgress.ToString());

                    var steamId = _bruteForceProgress.ToString();

                    steamIDs.Add(steamId);
                }
                if (_bruteForceProgress > _bforceTarget)
                {
                    Console.WriteLine("Scan finished");
                    Environment.Exit(0);
                }
                File.WriteAllText("bforceProgress", _bruteForceProgress.ToString());
                
                return steamIDs;
            }
        }
        static dynamic GetPlayerSummaries(List<string> steamIDs)
        {
            string url = "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2" +
                         $"?key={GetApiKey()}&steamids={String.Join(",", steamIDs)}";
            string result = GetHttpResponse(url);

            dynamic data = Newtonsoft.Json.JsonConvert.DeserializeObject(result);
            if (data == null || data.Count == 0) throw new DataNotReturnedException();

            data = data.response.players;

            return data;
        }

        static void AddUsersToDatabase(dynamic data, ref List<string> steamIDs)
        {
            NpgsqlCommand cmd = new NpgsqlCommand() { Connection = _psql };

            int count = 0;

            long updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string query = "INSERT INTO users" +
                           "(steam64id,avatar,personaname,profileurl,communityvisibilitystate,timecreated," +
                           "loccountrycode,locstatecode,updated) VALUES ";

            // if steamid still remains after this loop, profile doesn't exist
            foreach (JObject player in data)
            {
                if (player.Count == 0) continue;

                query += $"(@steam64id{count},@avatar{count},@personaname{count},@profileurl{count}," +
                         $"@communityvisibilitystate{count},@timecreated{count},@loccountrycode{count}," +
                         $"@locstatecode{count},@updated{count}),";
                
                string avatar = (string)player["avatar"];
                avatar = avatar.Substring(69, 43);
                cmd.Parameters.AddWithValue($"steam64id{count}", (Int64)player["steamid"]);
                cmd.Parameters.AddWithValue($"avatar{count}", avatar);
                cmd.Parameters.AddWithValue($"@updated{count}", updated);
                cmd.Parameters.AddWithValue($"@personaname{count}", (string)player["personaname"]);
                cmd.Parameters.AddWithValue($"@profileurl{count}", (string)player["profileurl"]);
                cmd.Parameters.AddWithValue($"@communityvisibilitystate{count}", (int)player["communityvisibilitystate"]);
                cmd.Parameters.AddWithValue($"@timecreated{count}", ValueOrNull(player["timecreated"]));
                cmd.Parameters.AddWithValue($"@loccountrycode{count}", ValueOrNull(player["loccountrycode"]));
                cmd.Parameters.AddWithValue($"@locstatecode{count}", ValueOrNull(player["locstatecode"]));

                count++;
            }

            query = query.Remove(query.Length - 1);
            query += @"ON CONFLICT (steam64id) DO UPDATE " + 
                        "SET avatar = EXCLUDED.avatar, " +
                        "updated = EXCLUDED.updated," +
                        "personaname = EXCLUDED.personaname," +
                        "profileurl = EXCLUDED.profileurl," +
                        "communityvisibilitystate = EXCLUDED.communityvisibilitystate," +
                        "timecreated = EXCLUDED.timecreated," +
                        "loccountrycode = EXCLUDED.loccountrycode," +
                        "locstatecode = EXCLUDED.locstatecode," +
                        "saved = false " +
                        "WHERE users.avatar IS DISTINCT FROM EXCLUDED.avatar";

            cmd.CommandText = query;
            Query(cmd);
        }
        
        static string GetApiKey()
        {
            lock (GetApiKeyLock)
            {
                if (_apiKeyIndex == ApiKeys.Length) _apiKeyIndex = 0;
                return ApiKeys[_apiKeyIndex++];
            }
        }

        static string GetHttpResponse(string url)
        {
#if useProxy
            WebProxy proxy = Proxy.Get();
            HttpClient client = new HttpClient(new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
#else
            HttpClient client = new HttpClient();
#endif

            client.BaseAddress = new Uri(url);
            client.Timeout = TimeSpan.FromSeconds(5);

            HttpResponseMessage httpResponse = client.GetAsync("").Result;
            string content = httpResponse.Content.ReadAsStringAsync().Result;

            _callsPerMinute++;
            _apiCalls++;
            return content;
        }
    }
}
