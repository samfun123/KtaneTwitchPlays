﻿using System;
using System.Collections;
using System.Collections.Generic;

public class SunShim : ComponentSolverShim
{
	public SunShim(TwitchModule module)
		: base(module, "sun")
	{
		ModInfo = ComponentSolverFactory.GetModuleInfo(GetModuleType());
		_component = module.BombComponent.GetComponent(ComponentType);
	}

	protected override IEnumerator RespondToCommandShimmed(string inputCommand)
	{
		IEnumerator command = RespondToCommandUnshimmed(inputCommand);
		while (command.MoveNext())
			yield return command.Current;
	}

	protected override IEnumerator ForcedSolveIEnumeratorShimmed()
	{
		yield return null;

		List<KMSelectable> btns = _component.GetValue<List<KMSelectable>>("correctButtonsOrdered");
		int stage = _component.GetValue<int>("stage");
		for (int i = stage - 1; i < btns.Count; i++)
		{
			yield return DoInteractionClick(btns[i], 0.2f);
			if (_component.GetValue<int>("stage") == 9)
				break;
		}
	}

	private static readonly Type ComponentType = ReflectionHelper.FindType("theSunScript", "sun");

	private readonly object _component;
}
