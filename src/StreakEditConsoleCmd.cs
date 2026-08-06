using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace CampfireUpgradeBugFix2;

/// <summary>
/// Console command: <c>streak</c>
/// Lets the player view and edit win streak numbers (current + best) per character,
/// and recompute them from the run history.
/// </summary>
public class StreakEditConsoleCmd : AbstractConsoleCmd
{
	public override string CmdName => "streak";

	public override string Args => "<list|set|best|add|recompute|help> [character] [value]";

	public override string Description =>
		"View or edit win streaks. " +
		"'set' changes the current win streak, 'best' changes the best win streak, " +
		"'add' adjusts the current streak by a delta, 'recompute' recalculates streaks from run history. " +
		"Characters: e.g. Ironclad, Silent, Regent, Necrobinder, Defect.";

	public override bool IsNetworked => false;

	public override bool DebugOnly => false;

	private static readonly string[] _subcommands = { "list", "set", "best", "add", "recompute", "help" };

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
					return ListStreaks();
				case "set":
					return SetStreak(args, isBest: false);
				case "best":
					return SetStreak(args, isBest: true);
				case "add":
					return AddStreak(args);
				case "recompute":
					return RecomputeStreaks();
				case "help":
					return HelpResult();
				default:
					return new CmdResult(false, "Unknown streak subcommand '" + args[0] + "'.\n" + BuildUsage());
			}
		}
		catch (Exception e)
		{
			Log.Error("[CampfireUpgradeBugFix2] streak command failed: " + e);
			return new CmdResult(false, "streak failed: " + e.Message);
		}
	}

	// ---------------------------------------------------------------------
	// Subcommands
	// ---------------------------------------------------------------------

	private static CmdResult ListStreaks()
	{
		ProgressState progress = SaveManager.Instance.Progress;
		if (progress.CharacterStats.Count == 0)
		{
			return new CmdResult(true, "No character stats recorded yet.");
		}

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Character             Current  Best   Wins  Losses");
		foreach (KeyValuePair<ModelId, CharacterStats> kv in progress.CharacterStats
			.OrderBy(kv => kv.Key == null ? "" : kv.Key.ToString()))
		{
			CharacterStats stats = kv.Value;
			string name = kv.Key == null ? "?" : kv.Key.ToString();
			sb.AppendLine(
				Pad(name, 22) +
				Pad(stats.CurrentWinStreak.ToString(CultureInfo.InvariantCulture), 9) +
				Pad(stats.BestWinStreak.ToString(CultureInfo.InvariantCulture), 7) +
				Pad(stats.TotalWins.ToString(CultureInfo.InvariantCulture), 6) +
				stats.TotalLosses.ToString(CultureInfo.InvariantCulture));
		}
		sb.AppendLine();
		sb.AppendLine("Aggregate: " + progress.Wins + " wins, " + progress.Losses + " losses, best streak " + progress.BestWinStreak + ".");
		sb.AppendLine("Changes are saved to progress.save immediately.");
		return new CmdResult(true, sb.ToString());
	}

	private static CmdResult SetStreak(string[] args, bool isBest)
	{
		if (args.Length < 3)
		{
			return new CmdResult(false, "Usage: streak " + (isBest ? "best" : "set") + " <character> <value>\n" + BuildUsage());
		}

		if (!TryResolveCharacter(args[1], out ModelId characterId))
		{
			return CharacterError(args[1]);
		}
		if (!long.TryParse(args[2], out long value) || value < 0)
		{
			return new CmdResult(false, "Streak value must be a non-negative integer.");
		}

		CharacterStats stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(characterId);
		if (isBest)
		{
			stats.BestWinStreak = value;
		}
		else
		{
			stats.CurrentWinStreak = value;
		}
		SaveManager.Instance.SaveProgressFile();

		return new CmdResult(true,
			"Set " + (isBest ? "best" : "current") + " win streak for " + characterId + " to " + value +
			" (current=" + stats.CurrentWinStreak + ", best=" + stats.BestWinStreak + ").");
	}

	private static CmdResult AddStreak(string[] args)
	{
		if (args.Length < 3)
		{
			return new CmdResult(false, "Usage: streak add <character> <delta>\n" + BuildUsage());
		}

		if (!TryResolveCharacter(args[1], out ModelId characterId))
		{
			return CharacterError(args[1]);
		}
		if (!long.TryParse(args[2], out long delta))
		{
			return new CmdResult(false, "Delta must be an integer (positive or negative).");
		}

		CharacterStats stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(characterId);
		long newValue = Math.Max(0L, stats.CurrentWinStreak + delta);
		stats.CurrentWinStreak = newValue;
		if (newValue > stats.BestWinStreak)
		{
			stats.BestWinStreak = newValue;
		}
		SaveManager.Instance.SaveProgressFile();

		return new CmdResult(true,
			"Adjusted current win streak for " + characterId + " by " + delta + " -> " + newValue +
			" (best=" + stats.BestWinStreak + ").");
	}

	/// <summary>
	/// Recalculates current and best win streaks for every character based on the run
	/// history files, mirroring the rules in ProgressSaveManager.UpdateWithRunData:
	///  - Only single-player runs (Players.Count == 1) count.
	///  - Daily and Custom game modes are ignored entirely (they never touch streaks).
	///  - A win increments the current streak; best = max(best, current).
	///  - A loss or abandoned run resets the current streak to 0.
	/// Multiplayer runs are skipped (the game counts those against the local player's
	/// character, which this tool cannot identify reliably).
	/// </summary>
	private static CmdResult RecomputeStreaks()
	{
		List<string> names = SaveManager.Instance.GetAllRunHistoryNames();
		if (names.Count == 0)
		{
			return new CmdResult(false, "No run history entries found; nothing to recompute.");
		}

		List<KeyValuePair<string, RunHistory>> runs = new List<KeyValuePair<string, RunHistory>>();
		foreach (string name in names)
		{
			ReadSaveResult<RunHistory> result = SaveManager.Instance.LoadRunHistory(name);
			if (result.Success && result.SaveData != null)
			{
				runs.Add(new KeyValuePair<string, RunHistory>(name, result.SaveData));
			}
		}
		runs.Sort((a, b) => a.Value.StartTime.CompareTo(b.Value.StartTime));

		Dictionary<ModelId, KeyValuePair<long, long>> streaks = new Dictionary<ModelId, KeyValuePair<long, long>>();
		int counted = 0;
		int skippedMultiplayer = 0;
		int skippedGameMode = 0;

		foreach (KeyValuePair<string, RunHistory> pair in runs)
		{
			RunHistory history = pair.Value;
			if (history.GameMode == GameMode.Daily || history.GameMode == GameMode.Custom)
			{
				skippedGameMode++;
				continue;
			}
			if (history.Players.Count != 1)
			{
				skippedMultiplayer++;
				continue;
			}
			ModelId characterId = history.Players[0].Character;
			if (characterId == null || characterId == ModelId.none)
			{
				continue;
			}

			KeyValuePair<long, long> current = streaks.TryGetValue(characterId, out KeyValuePair<long, long> existing)
				? existing
				: new KeyValuePair<long, long>(0L, 0L);
			long cur = current.Key;
			long best = current.Value;
			if (history.Win)
			{
				cur++;
				best = Math.Max(best, cur);
			}
			else
			{
				cur = 0;
			}
			streaks[characterId] = new KeyValuePair<long, long>(cur, best);
			counted++;
		}

		if (streaks.Count == 0)
		{
			return new CmdResult(false,
				"No countable single-player runs found (" + skippedGameMode + " daily/custom skipped, " + skippedMultiplayer + " multiplayer skipped).");
		}

		StringBuilder sb = new StringBuilder();
		sb.AppendLine("Recomputed streaks from " + counted + " single-player runs:");
		sb.AppendLine("Character             Current  Best");
		foreach (KeyValuePair<ModelId, KeyValuePair<long, long>> kv in streaks
			.OrderBy(kv => kv.Key == null ? "" : kv.Key.ToString()))
		{
			CharacterStats stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(kv.Key);
			stats.CurrentWinStreak = kv.Value.Key;
			stats.BestWinStreak = kv.Value.Value;
			sb.AppendLine(
				Pad(kv.Key.ToString(), 22) +
				Pad(kv.Value.Key.ToString(CultureInfo.InvariantCulture), 9) +
				kv.Value.Value.ToString(CultureInfo.InvariantCulture));
		}
		SaveManager.Instance.SaveProgressFile();

		if (skippedGameMode > 0 || skippedMultiplayer > 0)
		{
			sb.AppendLine("(Skipped " + skippedGameMode + " daily/custom and " + skippedMultiplayer + " multiplayer runs.)");
		}
		sb.AppendLine("Saved to progress.save. Characters with no single-player runs were left untouched.");
		return new CmdResult(true, sb.ToString());
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
			"  streak list              - show current/best win streaks and totals per character\n" +
			"  streak set <char> <n>    - set the current win streak for a character\n" +
			"  streak best <char> <n>   - set the best win streak for a character\n" +
			"  streak add <char> <d>    - add a delta (can be negative) to the current win streak\n" +
			"  streak recompute         - recalculate current/best streaks from run history\n" +
			"  streak help              - show this help\n" +
			"\n" +
			"Characters: Ironclad, Silent, Regent, Necrobinder, Defect (case-insensitive).\n" +
			"recompute rules: only single-player runs count; daily/custom runs are ignored;\n" +
			"wins extend the streak, losses and abandoned runs reset it.";
	}

	private static bool TryResolveCharacter(string input, out ModelId characterId)
	{
		characterId = ModelId.none;
		if (string.IsNullOrWhiteSpace(input))
		{
			return false;
		}

		ProgressState progress = SaveManager.Instance.Progress;
		IEnumerable<ModelId> known = ModelDb.AllCharacters.Select(c => c.Id).Concat(progress.CharacterStats.Keys).Distinct();

		foreach (ModelId id in known)
		{
			if (id != null &&
				(id.ToString().Equals(input, StringComparison.OrdinalIgnoreCase) ||
				 id.Entry.Equals(input, StringComparison.OrdinalIgnoreCase)))
			{
				characterId = id;
				return true;
			}
		}

		if (input.Contains('.'))
		{
			try
			{
				ModelId parsed = ModelId.Deserialize(input);
				// Only accept model ids the game actually knows about, so we never
				// create phantom character stats.
				if (ModelDb.GetByIdOrNull<CharacterModel>(parsed) != null)
				{
					characterId = parsed;
					return true;
				}
			}
			catch
			{
				// not a valid model id
			}
		}
		return false;
	}

	private static CmdResult CharacterError(string input)
	{
		string known = string.Join(", ", ModelDb.AllCharacters.Select(c => c.Id.Entry));
		return new CmdResult(false, "Unknown character '" + input + "'. Known characters: " + known + ".");
	}

	private static string Pad(string value, int width)
	{
		return value.Length >= width ? value : value + new string(' ', width - value.Length);
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
		if ((sub == "set" || sub == "best" || sub == "add") && args.Length == 2)
		{
			List<string> candidates = ModelDb.AllCharacters.Select(c => c.Id.Entry).ToList();
			candidates.AddRange(SaveManager.Instance.Progress.CharacterStats.Keys.Select(k => k.Entry));
			return CompleteArgument(candidates.Distinct().OrderBy(s => s), new[] { args[0] }, args[1]);
		}
		return base.GetArgumentCompletions(player, args);
	}
}
