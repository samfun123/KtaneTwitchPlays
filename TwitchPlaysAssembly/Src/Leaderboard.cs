﻿extern alias Newton;
using Newton::Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using UnityEngine;

public class Leaderboard
{
	public class LeaderboardEntry
	{
		public string UserName
		{
			get;
			set;
		}

		public Color UserColor
		{
			get;
			set;
		}

		public int SolveCount
		{
			get;
			set;
		}

		public int StrikeCount
		{
			get;
			set;
		}

		public int SolveScore
		{
			get;
			set;
		}

		public DateTime LastAction
		{
			get;
			set;
		}

		public int Rank
		{
			get;
			set;
		}

		public float RecordSoloTime
		{
			get;
			set;
		}

		public float TotalSoloTime
		{
			get;
			set;
		}

		public int TotalSoloClears
		{
			get;
			set;
		}

		public int SoloRank
		{
			get;
			set;
		}

		public float TimePerSoloSolve
		{
			get
			{
				if (TotalSoloClears == 0)
				{
					return 0;
				}
				return RecordSoloTime / RequiredSoloSolves;
			}
		}

		public void AddSolve(int num = 1) => SolveCount += num;

		public void AddStrike(int num = 1) => StrikeCount += num;

		public void AddScore(int num) => SolveScore += num;
	}

	private Color SafeGetColor(string userName) => IRCConnection.GetUserColor(userName);

	private bool GetEntry(string UserName, out LeaderboardEntry entry) => _entryDictionary.TryGetValue(UserName.ToLowerInvariant(), out entry);

	private LeaderboardEntry GetEntry(string userName)
	{
		DebugHelper.Log($"Getting entry for user {userName}");
		if (!GetEntry(userName, out LeaderboardEntry entry))
		{
			entry = new LeaderboardEntry();
			_entryDictionary[userName.ToLowerInvariant()] = entry;
			_entryList.Add(entry);
			entry.UserColor = SafeGetColor(userName);
		}
		entry.UserName = userName;
		return entry;
	}

	private LeaderboardEntry GetEntry(string userName, Color userColor)
	{
		LeaderboardEntry entry = GetEntry(userName);
		entry.UserName = userName;
		entry.UserColor = userColor;
		return entry;
	}

	public LeaderboardEntry AddSoloClear(string userName, float newRecord, out float previousRecord)
	{
		LeaderboardEntry entry = _entryDictionary[userName.ToLowerInvariant()];
		previousRecord = entry.RecordSoloTime;
		if ((entry.TotalSoloClears < 1) || (newRecord < previousRecord))
		{
			entry.RecordSoloTime = newRecord;
		}
		entry.TotalSoloClears++;
		entry.TotalSoloTime += newRecord;
		ResetSortFlag();

		if (entry.TotalSoloClears == 1)
		{
			_entryListSolo.Add(entry);
		}

		SoloSolver = entry;
		return entry;
	}

	public void AddSolve(string userName, int numSolve = 1) => AddSolve(userName, SafeGetColor(userName), numSolve);
	public void AddSolve(string userName, Color userColor, int numSolve = 1)
	{
		LeaderboardEntry entry = GetEntry(userName, userColor);

		entry.AddSolve(numSolve);
		entry.LastAction = DateTime.Now;
		ResetSortFlag();

		string name = userName.ToLowerInvariant();
		CurrentSolvers[name] = CurrentSolvers.TryGetValue(name, out int value) ? value + numSolve : numSolve;
	}
	public void AddStrike(string userName, int numStrikes = 1) => AddStrike(userName, SafeGetColor(userName), numStrikes);

	public void AddStrike(string userName, Color userColor, int numStrikes = 1)
	{
		LeaderboardEntry entry = GetEntry(userName, userColor);

		entry.AddStrike(numStrikes);
		entry.LastAction = DateTime.Now;
		ResetSortFlag();
	}

	public void AddScore(string userName, int numScore) => AddScore(userName, SafeGetColor(userName), numScore);

	public void AddScore(string userName, Color userColor, int numScore)
	{
		LeaderboardEntry entry = GetEntry(userName, userColor);
		entry.AddScore(numScore);
		entry.LastAction = DateTime.Now;
		ResetSortFlag();
	}

	public IEnumerable<LeaderboardEntry> GetSortedEntries(int count)
	{
		CheckAndSort();
		return _entryList.Take(count);
	}

	public IEnumerable<LeaderboardEntry> GetSortedSoloEntries(int count)
	{
		CheckAndSort();
		return _entryListSolo.Take(count);
	}

	public IEnumerable<LeaderboardEntry> GetSortedEntriesIncluding(Dictionary<string, int>.KeyCollection extras, int count)
	{
		var entries = new List<LeaderboardEntry>();

		foreach (string name in extras)
			if (GetEntry(name, out LeaderboardEntry entry))
				entries.Add(entry);

		if (entries.Count < count)
		{
			entries.AddRange(GetSortedEntries(count).Except(entries).Take(count - entries.Count));
		}

		entries.Sort(CompareScores);
		return entries.Take(count);
	}

	public IEnumerable<LeaderboardEntry> GetSortedSoloEntriesIncluding(string userName, int count)
	{
		List<LeaderboardEntry> ranking = GetSortedSoloEntries(count).ToList();

		LeaderboardEntry entry = _entryDictionary[userName.ToLowerInvariant()];
		if (entry.SoloRank > count)
		{
			ranking.RemoveAt(ranking.Count - 1);
			ranking.Add(entry);
		}

		return ranking;
	}

	public int GetRank(string userName, out LeaderboardEntry entry)
	{
		if (!GetEntry(userName, out entry))
		{
			return _entryList.Count + 1;
		}

		CheckAndSort();
		return _entryList.IndexOf(entry) + 1;
	}

	public int GetRank(int rank, out LeaderboardEntry entry)
	{
		CheckAndSort();
		entry = (_entryList.Count >= rank) ? _entryList[rank - 1] : null;
		return entry?.Rank ?? 0;
	}

	public int GetSoloRank(int rank, out LeaderboardEntry entry)
	{
		CheckAndSort();
		entry = (_entryListSolo.Count >= rank) ? _entryListSolo[rank - 1] : null;
		return entry?.SoloRank ?? 0;
	}

	public int GetSoloRank(string userName, out LeaderboardEntry entry)
	{
		entry = _entryListSolo.FirstOrDefault(x => string.Equals(x.UserName, userName, StringComparison.InvariantCultureIgnoreCase));
		if (entry != null)
			return _entryListSolo.IndexOf(entry);
		else
			return 0;
	}

	public bool IsDuplicate(LeaderboardEntry person, out List<LeaderboardEntry> entries)
	{
		if (_entryDictionary.ContainsValue(person))
		{
			entries = _entryList.Where(x => x.SolveScore == person.SolveScore && x.UserName != person.UserName).ToList();
			if (entries != null && entries.Any())
				return true;
			else
			{
				entries = null;
				return false;
			}
		}
		entries = null;
		return false;
	}

	public bool IsSoloDuplicate(LeaderboardEntry person, out List<LeaderboardEntry> entries)
	{
		if (_entryListSolo.Contains(person))
		{
			entries = _entryListSolo.Where(x => x.RecordSoloTime == person.RecordSoloTime && x.UserName != person.UserName).ToList();
			if (entries != null && entries.Any())
				return true;
			else
			{
				entries = null;
				return false;
			}
		}
		entries = null;
		return false;
	}

	public void GetTotalSolveStrikeCounts(out int solveCount, out int strikeCount, out int scoreCount)
	{
		solveCount = 0;
		strikeCount = 0;
		scoreCount = 0;

		foreach (LeaderboardEntry entry in _entryList)
		{
			solveCount += entry.SolveCount;
			strikeCount += entry.StrikeCount;
			scoreCount += entry.SolveScore;
		}
	}

	public void AddEntry(LeaderboardEntry user)
	{
		LeaderboardEntry entry = GetEntry(user.UserName, user.UserColor);
		entry.SolveCount = user.SolveCount;
		entry.StrikeCount = user.StrikeCount;
		entry.SolveScore = user.SolveScore;
		entry.LastAction = user.LastAction;
		entry.RecordSoloTime = user.RecordSoloTime;
		entry.TotalSoloTime = user.TotalSoloTime;
		entry.TotalSoloClears = user.TotalSoloClears;

		if (entry.TotalSoloClears > 0)
		{
			_entryListSolo.Add(entry);
		}
	}

	public void AddEntries(List<LeaderboardEntry> entries)
	{
		foreach (LeaderboardEntry entry in entries)
		{
			AddEntry(entry);
		}
	}

	public void DeleteEntry(LeaderboardEntry user)
	{
		_entryDictionary.Remove(user.UserName.ToLowerInvariant());
		_entryList.Remove(user);
	}

	public void DeleteEntry(string userNickName) => DeleteEntry(GetEntry(userNickName));

	public void DeleteSoloEntry(LeaderboardEntry user) => _entryListSolo.Remove(user);

	public void DeleteSoloEntry(string userNickName) => DeleteSoloEntry(_entryListSolo.First(x => string.Equals(x.UserName, userNickName, StringComparison.InvariantCultureIgnoreCase)));

	public void ResetLeaderboard()
	{
		_entryDictionary.Clear();
		_entryList.Clear();
		_entryListSolo.Clear();
		CurrentSolvers.Clear();
		BombsAttempted = 0;
		BombsCleared = 0;
		BombsExploded = 0;
		OldBombsAttempted = 0;
		OldBombsCleared = 0;
		OldBombsExploded = 0;
		OldScore = 0;
		OldSolves = 0;
		OldStrikes = 0;
	}

	private void ResetSortFlag() => _sorted = false;

	private void CheckAndSort()
	{
		if (!_sorted)
		{
			_entryList.Sort(CompareScores);
			_entryListSolo.Sort(CompareSoloTimes);
			_sorted = true;

			int i = 1;
			LeaderboardEntry previous = null;
			foreach (LeaderboardEntry entry in _entryList)
			{
				if (previous == null)
				{
					entry.Rank = 1;
				}
				else
				{
					entry.Rank = (CompareScores(entry, previous) == 0) ? previous.Rank : i;
				}
				previous = entry;
				i++;
			}

			i = 1;
			foreach (LeaderboardEntry entry in _entryListSolo)
			{
				entry.SoloRank = i++;
			}
		}
	}

	private static int CompareScores(LeaderboardEntry lhs, LeaderboardEntry rhs)
	{
		if (lhs.SolveScore != rhs.SolveScore)
		{
			//Intentially reversed comparison to sort from highest to lowest
			return rhs.SolveScore.CompareTo(lhs.SolveScore);
		}

		//Intentially reversed comparison to sort from highest to lowest
		return rhs.SolveScore.CompareTo(lhs.SolveScore);
	}

	private static int CompareSoloTimes(LeaderboardEntry lhs, LeaderboardEntry rhs) => lhs.RecordSoloTime.CompareTo(rhs.RecordSoloTime);

	public void ClearSolo()
	{
		SoloSolver = null;
		CurrentSolvers.Clear();
	}

	public void LoadDataFromFile()
	{
		string path = Path.Combine(Application.persistentDataPath, usersSavePath);
		try
		{
			DebugHelper.Log($"Leaderboard: Loading leaderboard data from file: {path}");
			XmlSerializer xml = new XmlSerializer(_entryList.GetType());
			TextReader reader = new StreamReader(path);
			List<LeaderboardEntry> entries = (List<LeaderboardEntry>) xml.Deserialize(reader);
			AddEntries(entries);
			ResetSortFlag();

			path = Path.Combine(Application.persistentDataPath, statsSavePath);
			DebugHelper.Log($"Leaderboard: Loading stats data from file: {path}");
			string jsonInput = File.ReadAllText(path);
			Dictionary<string, int> stats = JsonConvert.DeserializeObject<Dictionary<string, int>>(jsonInput);

			BombsAttempted = stats["BombsAttempted"];
			BombsCleared = stats["BombsCleared"];
			BombsExploded = stats["BombsExploded"];
			OldBombsAttempted = BombsAttempted;
			OldBombsCleared = BombsCleared;
			OldBombsExploded = BombsExploded;

			GetTotalSolveStrikeCounts(out OldSolves, out OldStrikes, out OldScore);
		}
		catch (FileNotFoundException)
		{
			DebugHelper.LogWarning($"Leaderboard: File {path} was not found.");
		}
		catch (Exception ex)
		{
			DebugHelper.LogException(ex);
		}
	}

	public void SaveDataToFile()
	{
		string path = Path.Combine(Application.persistentDataPath, usersSavePath);
		try
		{
			if (!_sorted)
			{
				CheckAndSort();
			}

			DebugHelper.Log($"Leaderboard: Saving leaderboard data to file: {path}");
			XmlSerializer xml = new XmlSerializer(_entryList.GetType());
			TextWriter writer = new StreamWriter(path);
			xml.Serialize(writer, _entryList);

			path = Path.Combine(Application.persistentDataPath, statsSavePath);
			DebugHelper.Log($"Leaderboard: Saving stats data to file: {path}");
			Dictionary<string, int> stats = new Dictionary<string, int>
			{
				{ "BombsAttempted", BombsAttempted },
				{ "BombsCleared", BombsCleared },
				{ "BombsExploded", BombsExploded }
			};
			string jsonOutput = JsonConvert.SerializeObject(stats, Formatting.Indented, new JsonSerializerSettings()
			{
				ReferenceLoopHandling = ReferenceLoopHandling.Ignore
			});
			File.WriteAllText(path, jsonOutput);
		}
		catch (FileNotFoundException)
		{
			DebugHelper.LogWarning($"Leaderboard: File {path} was not found.");
		}
		catch (Exception ex)
		{
			DebugHelper.LogException(ex);
		}
	}

	public int Count => _entryList.Count;

	public int SoloCount => _entryListSolo.Count;

	private Dictionary<string, LeaderboardEntry> _entryDictionary = new Dictionary<string, LeaderboardEntry>();
	private List<LeaderboardEntry> _entryList = new List<LeaderboardEntry>();
	private List<LeaderboardEntry> _entryListSolo = new List<LeaderboardEntry>();
	private static Leaderboard _instance;
	private bool _sorted = false;
	public bool Success = false;

	public int BombsAttempted = 0;
	public int BombsCleared = 0;
	public int BombsExploded = 0;
	public int OldBombsAttempted = 0;
	public int OldBombsCleared = 0;
	public int OldBombsExploded = 0;
	public int OldSolves = 0;
	public int OldStrikes = 0;
	public int OldScore = 0;

	public LeaderboardEntry SoloSolver = null;
	public Dictionary<string, int> CurrentSolvers = new Dictionary<string, int>();

	public static int RequiredSoloSolves = 11;
	public static string usersSavePath = "TwitchPlaysUsers.xml";
	public static string statsSavePath = "TwitchPlaysStats.json";

	public static Leaderboard Instance => _instance ?? (_instance = new Leaderboard());
}
