using System;
using System.Collections;
using UnityEngine;

public class CurriculumComponentSolver : ComponentSolver
{
	public CurriculumComponentSolver(BombCommander bombCommander, BombComponent bombComponent) :
		base(bombCommander, bombComponent)
	{
		_buttons = bombComponent.GetComponent<KMSelectable>().Children;
		modInfo = ComponentSolverFactory.GetModuleInfo(GetModuleType(), "Cycle the buttons !{0} cycle. Click a button using !{0} click 2. It's possible to add a number of times to click: !{0} click 2 3. Buttons are numbered left to right. Submit your answer with !{0} submit.");
	}

	int[] buttonOffset = new int[6] { 0, 0, 0, 0, 0, 0 };
	protected override IEnumerator RespondToCommandInternal(string inputCommand)
	{
		var commands = inputCommand.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

		if (commands.Length.InRange(2, 3) && commands[0].EqualsAny("click", "press"))
		{
			if (int.TryParse(commands[1], out int buttonPosition))
			{
				if (!buttonPosition.InRange(1, 5)) yield break;

				int clicks = 1;
				if (commands.Length == 3 && !int.TryParse(commands[2], out clicks))
				{
					yield break;
				}

				clicks %= 6;

				if (clicks == 0) yield break;

				yield return null;
				
				buttonPosition -= 1;

				KMSelectable button = _buttons[buttonPosition];
				for (int i = 0; i < clicks; i++)
				{
					button.OnInteract();
					yield return new WaitForSeconds(0.1f);
				}

				buttonOffset[buttonPosition] += clicks;
				buttonOffset[buttonPosition] %= 6;
			}
		}
		else if (commands.Length == 1 && commands[0] == "submit")
		{
			yield return null;

			_buttons[5].OnInteract();
			yield return new WaitForSeconds(0.1f);
		}
		else if (commands.Length == 1 && commands[0] == "reset")
		{
			yield return null;
			for (int buttonPosition = 0; buttonPosition < 5; buttonPosition++)
			{
				KMSelectable button = _buttons[buttonPosition];
				if (buttonOffset[buttonPosition] <= 0) continue;
				for (int i = 0; i < 6 - buttonOffset[buttonPosition]; i++) button.OnInteract();
				buttonOffset[buttonPosition] = 0;
			}
		}
		else if (commands.Length == 1 && commands[0] == "cycle")
		{
			for (int buttonPosition = 0; buttonPosition < 5; buttonPosition++)
			{
				yield return null;

				KMSelectable button = _buttons[buttonPosition];
				yield return "trycancel";
				if (buttonOffset[buttonPosition] > 0)
				{
					for (int i = 0; i < 6 - buttonOffset[buttonPosition]; i++) button.OnInteract();
				}
				
				for (int i2 = 0; i2 < 2; i2++)
				{
					yield return new WaitForSecondsWithCancel(1.5f, false);
					for (int i = 0; i < 3; i++)
					{
						button.OnInteract();
						yield return new WaitForSeconds(0.1f);
					}
				}

				if (buttonOffset[buttonPosition] > 0)
				{
					for (int i = 0; i < buttonOffset[buttonPosition]; i++) button.OnInteract();
				}
			}

		}
	}

	private KMSelectable[] _buttons = null;
}
