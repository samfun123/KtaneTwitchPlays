﻿using System.Collections;
using TwitchPlaysAssembly.ComponentSolvers.Modded.Shims;

public class GameOfLifeShim : ComponentSolverShim
{
	public GameOfLifeShim(BombCommander bombCommander, BombComponent bombComponent) : base(bombCommander, bombComponent)
	{

	}

	protected override IEnumerator RespondToCommandInternal(string inputCommand)
	{
		var send = base.RespondToCommandInternal(inputCommand);
		if (!inputCommand.ToLowerInvariant().EqualsAny("submit", "reset"))
		{
			var split = inputCommand.Split(' ');
			foreach (string set in split)
			{
				if (set.Length != 2) yield break;
			}
		}
		while (send.MoveNext()) yield return send.Current;
	}
}
