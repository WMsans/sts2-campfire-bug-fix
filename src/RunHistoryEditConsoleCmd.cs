using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;

namespace CampfireUpgradeBugFix2;

/// <summary>
/// Console command: <c>runhistory</c>
/// Lets the player list, view, edit, and delete run history entries from inside the game.
/// Also exposes a way to rewrite entries (win/loss, ascension, seed, character, ...).
/// </summary>
public class RunHistoryEditConsoleCmd : AbstractConsoleCmd
{
	public override string CmdName => "runhistory";

	public override string Args => "<list|view|edit|delete|help> [file] [field] [value]";

	public override string Description =>
		"List, view, edit, or delete run history entries. " +
		"Editable fields: win, was_abandoned, ascension, run_time, start_time, seed, build_id, " +
		"game_mode, character, killed_by_encounter, killed_by_event.";

	public override bool IsNetworked => false;

	public override bool DebugOnly => false;

	private static readonly string[] _subcommands = { "list", "view", "edit", "delete", "help" };

	private static readonly string[] _editableFields =
	{
		"win", "was_abandoned", "ascension", "run_time", "start_time", "seed",
		"build_id", "game_mode", "character", "killed_by_encounter", "killed_by_event"
	};

	public override CmdResult Process(Player issuingPlayer, string[] args)
	{
		try
		{
			if (args.Length == 0)
			{
				return HelpResult();
			}

			switch (args[0].ToLowerInvariant())
			{
				case "list":
					return ListHistory();
				case "view":
					return ViewHistory(args);
				case "edit":
					return EditHistory(args);
				case "delete":
					return DeleteHistory(args);
				case "help":
					return HelpResult();
				default:
					return new CmdResult(false, "Unknown runhistory subcommand '" + args[0] + "'.\n" + BuildUsage());
			}
		}
		catch (Exception e)
		{
			Log.Error("[CampfireUpgradeBugFix2] runhistory command failed: " + e);
			return new CmdResult(false, "runhistory failed: " + e.Message);
		}
	}

	// ---------------------------------------------------------------------
	// Subcommands
	// ---------------------------------------------------------------------

	private static CmdResult ListHistory()
	{
		List<string> names = SaveManager.Instance.GetAllRunHistoryNames();
		if (names.Count == 0)
		{
			return new CmdResult(true, "No run history entries found.");
		}

		// Sort newest first, mirroring the in-game Run History screen.
		List<KeyValuePair<string, RunHistory>> entries = new List<KeyValuePair<string, RunHistory>>();
		List<string> unreadable = new List<string>();
		foreach (string name in names)
		{
			RunHistory history = TryLoad(name);
			if (history != null)
			{
				entries.Add(new KeyValuePair<string, RunHistory>(name, history));
			}
			else
			{
				unreadable.Add(name);
			}
		}
		entries.Sort((a, b) => CompareHistories(b.Value, b.Key, a.Value, a.Key));

		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"Run history ({entries.Count} readable / {names.Count} total, newest first):");
		sb.AppendLine(FormatLine("Index", "File", "Date (local)", "Character", "Result", "Asc", "Seed"));
		for (int i = 0; i < entries.Count; i++)
		{
			KeyValuePair<string, RunHistory> entry = entries[i];
			RunHistory history = entry.Value;
			string character = GetPrimaryCharacter(history);
			string result = DescribeResult(history);
			string date = FormatDate(history.StartTime);
			sb.AppendLine(FormatLine(i.ToString(), entry.Key, date, character, result, history.Ascension.ToString(), Truncate(history.Seed, 24)));
		}
		foreach (string name in unreadable)
		{
			sb.AppendLine(FormatLine("?", name, "?", "?", "UNAVAILABLE", "?", "?"));
		}
		return new CmdResult(true, sb.ToString());
	}

	private static CmdResult ViewHistory(string[] args)
	{
		if (args.Length < 2)
		{
			return new CmdResult(false, "Usage: runhistory view <file>\n" + BuildUsage());
		}

		string fileName = args[1];
		RunHistory history = RequireHistory(fileName, out string error);
		if (history == null)
		{
			return new CmdResult(false, error);
		}

		StringBuilder sb = new StringBuilder();
		sb.AppendLine($"Run history entry '{fileName}':");
		sb.AppendLine($"  schema_version      = {history.SchemaVersion}");
		sb.AppendLine($"  platform_type       = {history.PlatformType}");
		sb.AppendLine($"  game_mode           = {history.GameMode}");
		sb.AppendLine($"  win                 = {history.Win}");
		sb.AppendLine($"  was_abandoned       = {history.WasAbandoned}");
		sb.AppendLine($"  seed                = {history.Seed}");
		sb.AppendLine($"  start_time          = {history.StartTime} ({FormatDate(history.StartTime)})");
		sb.AppendLine($"  run_time            = {history.RunTime.ToString(CultureInfo.InvariantCulture)}s ({TimeSpan.FromSeconds(history.RunTime):g})");
		sb.AppendLine($"  ascension           = {history.Ascension}");
		sb.AppendLine($"  build_id            = {history.BuildId}");
		sb.AppendLine($"  killed_by_encounter = {history.KilledByEncounter}");
		sb.AppendLine($"  killed_by_event     = {history.KilledByEvent}");
		sb.AppendLine($"  acts                = {string.Join(", ", history.Acts.Select(a => a.ToString()))}");
		sb.AppendLine($"  modifiers           = {history.Modifiers.Count}");
		sb.AppendLine($"  map_point_history   = {history.MapPointHistory.Count} act(s)");
		sb.AppendLine($"  players             = {history.Players.Count}");
		for (int i = 0; i < history.Players.Count; i++)
		{
			RunHistoryPlayer p = history.Players[i];
			sb.AppendLine($"    [{i}] id={p.Id} character={p.Character} potion_slots={p.MaxPotionSlotCount}");
			sb.AppendLine($"         deck={CountOf(p.Deck)} cards, relics={CountOf(p.Relics)}, potions={CountOf(p.Potions)}, badges={CountOf(p.Badges)}");
		}
		return new CmdResult(true, sb.ToString());
	}

	private static CmdResult EditHistory(string[] args)
	{
		if (args.Length < 4)
		{
			return new CmdResult(false, "Usage: runhistory edit <file> <field> <value>\nEditable fields: " + string.Join(", ", _editableFields));
		}

		string fileName = args[1];
		string field = args[2].ToLowerInvariant();
		string value = string.Join(" ", args.Skip(3)); // values may contain spaces (e.g. seeds)

		if (!_editableFields.Contains(field))
		{
			return new CmdResult(false, "Unknown field '" + field + "'. Editable fields: " + string.Join(", ", _editableFields));
		}

		RunHistory history = RequireHistory(fileName, out string error);
		if (history == null)
		{
			return new CmdResult(false, error);
		}

		switch (field)
		{
			case "win":
				if (!TryParseBool(value, out bool win))
				{
					return ParseError("win", "true or false");
				}
				SetProperty(history, "Win", win);
				break;
			case "was_abandoned":
				if (!TryParseBool(value, out bool abandoned))
				{
					return ParseError("was_abandoned", "true or false");
				}
				SetProperty(history, "WasAbandoned", abandoned);
				break;
			case "ascension":
				if (!int.TryParse(value, out int ascension) || ascension < 0)
				{
					return ParseError("ascension", "a non-negative integer");
				}
				SetProperty(history, "Ascension", ascension);
				break;
			case "run_time":
				if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float runTime) || runTime < 0f)
				{
					return ParseError("run_time", "a non-negative number of seconds");
				}
				SetProperty(history, "RunTime", runTime);
				break;
			case "start_time":
				if (!long.TryParse(value, out long startTime))
				{
					return ParseError("start_time", "a unix timestamp in seconds");
				}
				// Refuse to overwrite a different, existing entry when the new start_time
				// would collide with its file name.
				string prospectiveName = startTime + ".run";
				if (!prospectiveName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
					SaveManager.Instance.GetAllRunHistoryNames().Contains(prospectiveName, StringComparer.OrdinalIgnoreCase))
				{
					return new CmdResult(false, "Refusing to overwrite existing entry '" + prospectiveName + "'. Choose a different start_time.");
				}
				SetProperty(history, "StartTime", startTime);
				break;
			case "seed":
				SetProperty(history, "Seed", value);
				break;
			case "build_id":
				SetProperty(history, "BuildId", value);
				break;
			case "game_mode":
				if (!TryParseEnum(value, out GameMode gameMode))
				{
					return ParseError("game_mode", "one of: " + string.Join(", ", Enum.GetNames(typeof(GameMode))));
				}
				SetProperty(history, "GameMode", gameMode);
				break;
			case "character":
				if (!TryParseModelId(value, out ModelId characterId,
					    ModelDb.AllCharacters.Select(c => c.Id).Concat(SaveManager.Instance.Progress.CharacterStats.Keys)))
				{
					return ParseError("character", "a character id like 'Character.IRONCLAD' or a name like 'Ironclad'");
				}
				SetPlayerCharacter(history, characterId);
				break;
			case "killed_by_encounter":
				if (!TryParseModelId(value, out ModelId encounterId, ModelDb.AllEncounters.Select(e => e.Id)))
				{
					return ParseError("killed_by_encounter", "an encounter id like 'Encounter.SLIME_BOSS', an encounter name, or 'none'");
				}
				SetProperty(history, "KilledByEncounter", encounterId);
				break;
			case "killed_by_event":
				if (!TryParseModelId(value, out ModelId eventId, ModelDb.AllEvents.Select(e => e.Id)))
				{
					return ParseError("killed_by_event", "an event id, an event name, or 'none'");
				}
				SetProperty(history, "KilledByEvent", eventId);
				break;
			default:
				return new CmdResult(false, "Unknown field '" + field + "'.");
		}

		SaveManager.Instance.SaveRunHistory(history);

		string savedFileName = history.StartTime + ".run";
		bool renamed = !savedFileName.Equals(fileName, StringComparison.OrdinalIgnoreCase);

		StringBuilder result = new StringBuilder();
		result.AppendLine($"Updated {field} for '{fileName}' (start_time={history.StartTime}, win={history.Win}, ascension={history.Ascension}, character={GetPrimaryCharacter(history)}).");
		if (renamed)
		{
			DeleteHistoryFile(fileName);
			result.AppendLine($"Changed start_time, so the entry was moved from '{fileName}' to '{savedFileName}'.");
		}
		result.Append("NOTE: This edits the history entry only. It does NOT change win/loss totals or streaks stored in progress.save. Use 'streak recompute' to resync streaks from history.");
		return new CmdResult(true, result.ToString());
	}

	private static CmdResult DeleteHistory(string[] args)
	{
		if (args.Length < 2)
		{
			return new CmdResult(false, "Usage: runhistory delete <file>\n" + BuildUsage());
		}

		string fileName = args[1];
		if (!SaveManager.Instance.GetAllRunHistoryNames().Contains(fileName, StringComparer.OrdinalIgnoreCase))
		{
			return new CmdResult(false, "No run history entry named '" + fileName + "' exists.");
		}

		DeleteHistoryFile(fileName);
		return new CmdResult(true, "Deleted run history entry '" + fileName + "'.");
	}

	private static CmdResult HelpResult()
	{
		return new CmdResult(true, BuildUsage());
	}

	// ---------------------------------------------------------------------
	// Helpers
	// ---------------------------------------------------------------------

	private static string BuildUsage()
	{
		return "Usage:\n" +
			"  runhistory list                    - list all run history entries (newest first)\n" +
			"  runhistory view <file>             - show every field of one entry\n" +
			"  runhistory edit <file> <field> <value> - change one field and re-save the entry\n" +
			"  runhistory delete <file>           - permanently delete an entry (local + cloud)\n" +
			"  runhistory help                    - show this help\n" +
			"\n" +
			"Editable fields:\n" +
			"  win (true/false), was_abandoned (true/false), ascension (int), run_time (seconds),\n" +
			"  start_time (unix seconds), seed (string), build_id (string), game_mode (Standard/Daily/Dread/Blitz),\n" +
			"  character (e.g. Character.IRONCLAD or Ironclad), killed_by_encounter, killed_by_event (id or 'none')\n" +
			"\n" +
			"Tip: 'streak recompute' recalculates current/best win streaks from the edited history.";
	}

	private static string FormatLine(params string[] columns)
	{
		int[] widths = { 6, 22, 20, 16, 12, 6, 24 };
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < columns.Length && i < widths.Length; i++)
		{
			string value = columns[i];
			sb.Append(value.Length >= widths[i] ? value : value + new string(' ', widths[i] - value.Length));
		}
		return sb.ToString();
	}

	private static string Truncate(string value, int maxLength)
	{
		return value.Length <= maxLength ? value : value.Substring(0, maxLength - 1) + "…";
	}

	private static string FormatDate(long unixSeconds)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
		}
		catch
		{
			return unixSeconds.ToString(CultureInfo.InvariantCulture);
		}
	}

	private static string DescribeResult(RunHistory history)
	{
		if (history.Win)
		{
			return "WIN";
		}
		return history.WasAbandoned ? "ABANDONED" : "LOSS";
	}

	private static string GetPrimaryCharacter(RunHistory history)
	{
		RunHistoryPlayer first = history.Players.FirstOrDefault();
		if (first == null || first.Character == null)
		{
			return "?";
		}
		return first.Character.ToString();
	}

	private static int CountOf(IEnumerable<object> items)
	{
		return items?.Count() ?? 0;
	}

	private static bool TryParseBool(string value, out bool result)
	{
		if (bool.TryParse(value, out result))
		{
			return true;
		}
		if (value == "1")
		{
			result = true;
			return true;
		}
		if (value == "0")
		{
			result = false;
			return true;
		}
		return false;
	}

	private static bool TryParseEnum(string value, out GameMode result)
	{
		foreach (string name in Enum.GetNames(typeof(GameMode)))
		{
			if (name.Equals(value, StringComparison.OrdinalIgnoreCase))
			{
				result = (GameMode)Enum.Parse(typeof(GameMode), name);
				return true;
			}
		}
		result = default;
		return false;
	}

	/// <summary>
	/// Accepts "none", "Category.Entry", or a bare entry name matched against the given candidates.
	/// </summary>
	private static bool TryParseModelId(string value, out ModelId id, IEnumerable<ModelId> knownCandidates = null)
	{
		id = ModelId.none;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
		{
			id = ModelId.none;
			return true;
		}
		if (value.Contains('.'))
		{
			try
			{
				ModelId parsed = ModelId.Deserialize(value);
				// When a candidate list is provided, only accept ids the game knows about.
				if (knownCandidates == null || knownCandidates.Any(c => c != null && c.Equals(parsed)))
				{
					id = parsed;
					return true;
				}
			}
			catch
			{
				// Fall through to candidate matching below.
			}
		}
		if (knownCandidates != null)
		{
			foreach (ModelId candidate in knownCandidates)
			{
				if (candidate != null &&
					(candidate.ToString().Equals(value, StringComparison.OrdinalIgnoreCase) ||
					 candidate.Entry.Equals(value, StringComparison.OrdinalIgnoreCase)))
				{
					id = candidate;
					return true;
				}
			}
		}
		return false;
	}

	/// <summary>
	/// RunHistory / RunHistoryPlayer properties are init-only; the compiler forbids direct
	/// assignment, but reflection can still set them at runtime.
	/// </summary>
	private static void SetProperty(object instance, string propertyName, object value)
	{
		PropertyInfo prop = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
			?? throw new InvalidOperationException("Could not find property '" + propertyName + "' on " + instance.GetType().Name + ".");
		prop.SetValue(instance, value);
	}

	private static void SetPlayerCharacter(RunHistory history, ModelId characterId)
	{
		if (history.Players.Count == 0)
		{
			history.Players.Add(new RunHistoryPlayer { Character = characterId });
		}
		else
		{
			SetProperty(history.Players[0], "Character", characterId);
		}
	}

	private static RunHistory TryLoad(string fileName)
	{
		try
		{
			ReadSaveResult<RunHistory> result = SaveManager.Instance.LoadRunHistory(fileName);
			return result.Success ? result.SaveData : null;
		}
		catch (Exception e)
		{
			Log.Warn("[CampfireUpgradeBugFix2] Failed to load run history '" + fileName + "': " + e.Message);
			return null;
		}
	}

	private static RunHistory RequireHistory(string fileName, out string error)
	{
		error = null;
		if (!SaveManager.Instance.GetAllRunHistoryNames().Contains(fileName, StringComparer.OrdinalIgnoreCase))
		{
			error = "No run history entry named '" + fileName + "' exists. Use 'runhistory list' to see available files.";
			return null;
		}
		ReadSaveResult<RunHistory> result = SaveManager.Instance.LoadRunHistory(fileName);
		if (!result.Success || result.SaveData == null)
		{
			error = "Could not load run history '" + fileName + "' (status: " + result.Status + ").";
			return null;
		}
		return result.SaveData;
	}

	/// <summary>
	/// Deletes a run history file through the game's save store so that both the local
	/// copy and any cloud (Steam) copy are removed.
	/// </summary>
	private static void DeleteHistoryFile(string fileName)
	{
		ISaveStore saveStore = GetRunHistorySaveStore();
		string path = Path.Combine(RunHistorySaveManager.GetHistoryPath(SaveManager.Instance.CurrentProfileId), fileName);
		saveStore.DeleteFile(path);
		Log.Info("[CampfireUpgradeBugFix2] Deleted run history file: " + path);
	}

	private static ISaveStore GetRunHistorySaveStore()
	{
		object runHistorySaveManager = ReflectUtil.GetFieldValue(SaveManager.Instance, "_runHistorySaveManager");
		if (runHistorySaveManager == null)
		{
			throw new InvalidOperationException("Could not find RunHistorySaveManager on SaveManager.");
		}
		object saveStore = ReflectUtil.GetFieldValue(runHistorySaveManager, "_saveStore");
		if (saveStore is ISaveStore store)
		{
			return store;
		}
		throw new InvalidOperationException("Could not find the save store used by RunHistorySaveManager.");
	}

	private static int CompareHistories(RunHistory a, string aName, RunHistory b, string bName)
	{
		if (a != null && b != null)
		{
			int byTime = b.StartTime.CompareTo(a.StartTime);
			return byTime != 0 ? byTime : string.Compare(bName, aName, StringComparison.Ordinal);
		}
		if (a != null)
		{
			return -1;
		}
		if (b != null)
		{
			return 1;
		}
		return string.Compare(bName, aName, StringComparison.Ordinal);
	}

	private static CmdResult ParseError(string field, string expected)
	{
		return new CmdResult(false, "Could not parse value for '" + field + "'. Expected " + expected + ".");
	}

	// ---------------------------------------------------------------------
	// Tab completion
	// ---------------------------------------------------------------------

	public override CompletionResult GetArgumentCompletions(Player player, string[] args)
	{
		if (args.Length <= 1)
		{
			return CompleteArgument(_subcommands, Array.Empty<string>(), args.FirstOrDefault() ?? "", CompletionType.Subcommand);
		}

		string sub = args[0].ToLowerInvariant();
		if (sub == "view" || sub == "delete" || sub == "edit")
		{
			if (args.Length == 2)
			{
				return CompleteArgument(SaveManager.Instance.GetAllRunHistoryNames(), new[] { args[0] }, args[1]);
			}
			if (sub == "edit" && args.Length == 3)
			{
				return CompleteArgument(_editableFields, new[] { args[0], args[1] }, args[2]);
			}
		}
		return base.GetArgumentCompletions(player, args);
	}
}
