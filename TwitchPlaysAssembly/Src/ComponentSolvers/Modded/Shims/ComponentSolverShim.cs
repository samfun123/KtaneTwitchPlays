﻿using System.Collections;

namespace TwitchPlaysAssembly.ComponentSolvers.Modded.Shims
{
	public abstract class ComponentSolverShim : ComponentSolver
	{
		protected ComponentSolver _unshimmed;

		protected ComponentSolverShim(BombCommander bombCommander, BombComponent bombComponent, string moduleType) : base(bombCommander, bombComponent)
		{
			// Passing null to the BombCommander argument here because _unshimmed is only used to run RespondToCommandInternal(); we don’t want it to award strikes/solves etc. because this object already does that
			_unshimmed = ComponentSolverFactory.CreateDefaultModComponentSolver(null, bombComponent, moduleType, bombComponent.GetModuleDisplayName());
			modInfo = _unshimmed.modInfo;
		}

		protected sealed override IEnumerator ForcedSolveIEnumerator()
		{
			return TwitchPlaySettings.data.EnableTwitchPlayShims ? ForcedSolveIEnumratorShimmed() : ForcedSolveIEnumeratorUnshimmed();
		}

		protected virtual IEnumerator ForcedSolveIEnumratorShimmed()
		{
			return ForcedSolveIEnumeratorUnshimmed();
		}

		protected IEnumerator ForcedSolveIEnumeratorUnshimmed()
		{
			if (_unshimmed.ForcedSolveMethod == null) yield break;

			object result = _unshimmed.ForcedSolveMethod.Invoke(_unshimmed.CommandComponent, null);
			if (result is IEnumerator e)
			{
				while (e.MoveNext())
					yield return e.Current;
			}
			else
			{
				yield return null;
			}
		}
		

		protected internal sealed override IEnumerator RespondToCommandInternal(string inputCommand)
		{
			return TwitchPlaySettings.data.EnableTwitchPlayShims ? RespondToCommandShimmed(inputCommand) : RespondToCommandUnshimmed(inputCommand);
		}

		protected abstract IEnumerator RespondToCommandShimmed(string inputCommand);

		protected IEnumerator RespondToCommandUnshimmed(string inputCommand)
		{
			var e = _unshimmed.RespondToCommandInternal(inputCommand);
			while (e.MoveNext())
				yield return e.Current;
		}
	}
}
