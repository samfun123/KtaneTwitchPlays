﻿using System;
using System.Reflection;
using System.Collections;
using UnityEngine;

public class MicrocontrollerComponentSolver : ComponentSolver
{
	public MicrocontrollerComponentSolver(BombCommander bombCommander, BombComponent bombComponent) :
		base(bombCommander, bombComponent)
	{
		object _component = bombComponent.GetComponent(_componentType);
		_buttonOK = (KMSelectable) _buttonOKField.GetValue(_component);
		_buttonUp = (KMSelectable) _buttonUpField.GetValue(_component);
		modInfo = ComponentSolverFactory.GetModuleInfo(GetModuleType(), "Set the current pin color with !{0} set red. Cycle the current pin !{0} cycle. Valid colors: white, red, yellow, magenta, blue, green.");
	}

	protected internal override IEnumerator RespondToCommandInternal(string inputCommand)
	{
		var commands = inputCommand.ToLowerInvariant().Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

		if (commands.Length == 2 && commands[0].Equals("set"))
		{
			int colorIndex = Array.IndexOf(_colors, commands[1]);
			if (colorIndex > -1)
			{
				yield return null;

				while (currentIndex != colorIndex) {
					DoInteractionClick(_buttonUp);
					currentIndex = (currentIndex + 1) % 6;

					yield return new WaitForSeconds(0.1f);
				}

				int lastStrikeCount = StrikeCount;

				DoInteractionClick(_buttonOK);
				yield return new WaitForSeconds(0.1f);

				if (lastStrikeCount == StrikeCount)
				{
					currentIndex = 0;
				}
			}
		}
		else if (commands.Length == 1 && commands[0].Equals("cycle"))
		{
			yield return null;

			for (int i = 0; i < 6; i++)
			{
				DoInteractionClick(_buttonUp);
				yield return new WaitForSeconds(0.2f);
			}
		}
	}

	static MicrocontrollerComponentSolver()
	{
		_componentType = ReflectionHelper.FindType("Micro");
		_buttonOKField = _componentType.GetField("buttonOK", BindingFlags.Public | BindingFlags.Instance);
		_buttonUpField = _componentType.GetField("buttonUp", BindingFlags.Public | BindingFlags.Instance);
	}

	private static Type _componentType = null;
	private static FieldInfo _buttonOKField = null;
	private static FieldInfo _buttonUpField = null;

	private static readonly string[] _colors = { "white", "red", "yellow", "magenta", "blue", "green" };
	private int currentIndex = 0;

	private readonly KMSelectable _buttonOK = null;
	private readonly KMSelectable _buttonUp = null;
}
