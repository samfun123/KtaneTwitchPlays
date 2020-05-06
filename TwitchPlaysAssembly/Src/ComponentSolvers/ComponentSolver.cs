﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

public abstract class ComponentSolver
{
	#region Constructors
	protected ComponentSolver(TwitchModule module, bool hookUpEvents = true)
	{
		Module = module;

		if (!hookUpEvents) return;
		module.BombComponent.OnPass += OnPass;
		module.BombComponent.OnStrike += OnStrike;
		var gameCommands = module.BombComponent.GetComponentInChildren<KMGameCommands>();
		if (gameCommands != null)
			gameCommands.OnCauseStrike += x => OnStrike(x);
	}
	#endregion

	private int _beforeStrikeCount;
	public IEnumerator RespondToCommand(string userNickName, string command)
	{
		_disableAnarchyStrike = TwitchPlaySettings.data.AnarchyMode;

		TryCancel = false;
		_responded = false;
		_processingTwitchCommand = true;
		if (Solved && !TwitchPlaySettings.data.AnarchyMode)
		{
			_processingTwitchCommand = false;
			yield break;
		}

		Module.CameraPriority = Module.CameraPriority > CameraPriority.Interacted ? Module.CameraPriority : CameraPriority.Interacted;
		_currentUserNickName = userNickName;
		_beforeStrikeCount = StrikeCount;
		IEnumerator subcoroutine;

		try
		{
			subcoroutine = ChainableCommands ? ChainCommand(command) : RespondToCommandInternal(command);
		}
		catch (Exception e)
		{
			HandleModuleException(e);
			yield break;
		}

		bool moved = false;
		bool solved = Solved;
		if (subcoroutine != null)
		{
			try
			{
				moved = subcoroutine.MoveNext();
				if (moved && (ModInfo.DoesTheRightThing || ModInfo.builtIntoTwitchPlays))
				{
					//Handle No-focus API commands. In order to focus the module, the first thing yielded cannot be one of the things handled here, as the solver will yield break if
					//it is one of these API commands returned.
					switch (subcoroutine.Current)
					{
						case string currentString:
							if (SendToTwitchChat(currentString, userNickName) ==
								SendToTwitchChatResponse.InstantResponse)
								yield break;
							break;
					}
					_responded = true;
				}
			}
			catch (Exception e)
			{
				HandleModuleException(e);
				yield break;
			}
		}

		if (Solved != solved || _beforeStrikeCount != StrikeCount)
		{
			IRCConnection.SendMessageFormat("Please submit an issue at https://github.com/samfun123/KtaneTwitchPlays/issues regarding module !{0} ({1}) attempting to solve / strike prematurely.", Module.Code, Module.HeaderText);
			if (ModInfo != null)
			{
				ModInfo.DoesTheRightThing = false;
				ModuleData.DataHasChanged = true;
				ModuleData.WriteDataToFile();
			}

			if (!TwitchPlaySettings.data.AnarchyMode)
			{
				IEnumerator focusDefocus = Module.Bomb.Focus(Module.Selectable, FocusDistance, FrontFace);
				while (focusDefocus.MoveNext())
					yield return focusDefocus.Current;

				yield return new WaitForSeconds(0.5f);

				focusDefocus = Module.Bomb.Defocus(Module.Selectable, FrontFace);
				while (focusDefocus.MoveNext())
					yield return focusDefocus.Current;

				yield return new WaitForSeconds(0.5f);
				_currentUserNickName = null;
				_processingTwitchCommand = false;
				yield break;
			}
		}

		if (subcoroutine == null || !moved)
		{
			if (!_responded)
				Module.CommandInvalid(userNickName);

			_currentUserNickName = null;
			_processingTwitchCommand = false;
			yield break;
		}

		IEnumerator focusCoroutine = Module.Bomb.Focus(Module.Selectable, FocusDistance, FrontFace);
		while (focusCoroutine.MoveNext())
			yield return focusCoroutine.Current;

		yield return new WaitForSeconds(0.5f);

		bool parseError = false;
		bool needQuaternionReset = false;
		bool hideCamera = false;
		bool exceptionThrown = false;
		bool trycancelsequence = false;

		while ((_beforeStrikeCount == StrikeCount && !Solved || _disableOnStrike || TwitchPlaySettings.data.AnarchyMode) && !Detonated)
		{
			try
			{
				if (!subcoroutine.MoveNext())
					break;

				_responded = true;
			}
			catch (Exception e)
			{
				exceptionThrown = true;
				HandleModuleException(e);
				break;
			}

			object currentValue = subcoroutine.Current;
			if (currentValue is string currentString)
			{
				if (currentString.Equals("strike", StringComparison.InvariantCultureIgnoreCase))
					_delegatedStrikeUserNickName = userNickName;
				else if (currentString.Equals("solve", StringComparison.InvariantCultureIgnoreCase))
					_delegatedSolveUserNickName = userNickName;
				else if (currentString.Equals("unsubmittablepenalty", StringComparison.InvariantCultureIgnoreCase))
				{
					if (TwitchPlaySettings.data.UnsubmittablePenaltyPercent <= 0) continue;

					int penalty =
						Math.Max((int) (ModInfo.moduleScore * TwitchPlaySettings.data.UnsubmittablePenaltyPercent), 1);
					Leaderboard.Instance.AddScore(_currentUserNickName, -penalty);
					IRCConnection.SendMessageFormat(TwitchPlaySettings.data.UnsubmittableAnswerPenalty,
						_currentUserNickName, Code, ModInfo.moduleDisplayName, penalty, penalty > 1 ? "s" : "");
				}
				else if (currentString.Equals("parseerror", StringComparison.InvariantCultureIgnoreCase))
				{
					parseError = true;
					break;
				}
				else if (currentString.RegexMatch(out Match match, "^trycancel((?: (?:.|\\n)+)?)$"))
				{
					if (CoroutineCanceller.ShouldCancel)
					{
						CoroutineCanceller.ResetCancel();
						if (!string.IsNullOrEmpty(match.Groups[1].Value))
							IRCConnection.SendMessage(
								$"Sorry @{userNickName}, {match.Groups[1].Value.Trim()}");
						break;
					}
				}
				else if (currentString.RegexMatch(out match, "^trycancelsequence((?: (?:.|\\n)+)?)$"))
				{
					trycancelsequence = true;
					yield return currentValue;
					continue;
				}
				else if (currentString.RegexMatch(out match,
							"^trywaitcancel ([0-9]+(?:\\.[0-9]+)?)((?: (?:.|\\n)+)?)$") &&
						float.TryParse(match.Groups[1].Value, out float waitCancelTime))
				{
					yield return new WaitForSecondsWithCancel(waitCancelTime, false, this);
					if (CoroutineCanceller.ShouldCancel)
					{
						CoroutineCanceller.ResetCancel();
						if (!string.IsNullOrEmpty(match.Groups[2].Value))
							IRCConnection.SendMessage($"Sorry @{userNickName}, {match.Groups[2].Value.Trim()}");
						break;
					}
				}
				// Commands that allow messages to be sent to the chat.
				else if (SendToTwitchChat(currentString, userNickName) != SendToTwitchChatResponse.NotHandled)
				{
					if (currentString.StartsWith("antitroll") && !TwitchPlaySettings.data.EnableTrollCommands &&
						!TwitchPlaySettings.data.AnarchyMode)
						break;
					//handled
				}
				else if (currentString.StartsWith("add strike", StringComparison.InvariantCultureIgnoreCase))
					OnStrike(null);
				else if (currentString.Equals("multiple strikes", StringComparison.InvariantCultureIgnoreCase))
					_disableOnStrike = true;
				else if (currentString.Equals("end multiple strikes", StringComparison.InvariantCultureIgnoreCase))
				{
					if (_beforeStrikeCount == StrikeCount && !TwitchPlaySettings.data.AnarchyMode)
					{
						_disableOnStrike = false;
						if (Solved) OnPass(null);
					}
					else if (!TwitchPlaySettings.data.AnarchyMode)
						break;
				}
				else if (currentString.StartsWith("autosolve", StringComparison.InvariantCultureIgnoreCase))
				{
					HandleModuleException(new Exception(currentString));
					break;
				}
				else if (currentString.RegexMatch(out match, "^(?:detonate|explode)(?: ([0-9.]+))?(?: ((?:.|\\n)+))?$"))
				{
					if (!float.TryParse(match.Groups[1].Value, out float explosionTime))
					{
						if (string.IsNullOrEmpty(match.Groups[1].Value))
							explosionTime = 0.1f;
						else
						{
							DebugHelper.Log($"Badly formatted detonate command string: {currentString}");
							yield return currentValue;
							continue;
						}
					}

					_delayedExplosionPending = true;
					if (_delayedExplosionCoroutine != null)
						Module.StopCoroutine(_delayedExplosionCoroutine);
					_delayedExplosionCoroutine = Module.StartCoroutine(DelayedModuleBombExplosion(explosionTime, userNickName, match.Groups[2].Value));
				}
				else if (currentString.RegexMatch(out match, "^cancel (detonate|explode|detonation|explosion)$"))
				{
					_delayedExplosionPending = false;
					if (_delayedExplosionCoroutine != null)
						Module.StopCoroutine(_delayedExplosionCoroutine);
				}
				else if (currentString.RegexMatch(out match, "^(end |toggle )?(?:elevator|hold|waiting) music$"))
				{
					if (match.Groups.Count > 1 && _musicPlayer != null)
					{
						_musicPlayer.StopMusic();
						_musicPlayer = null;
					}
					else if (!currentString.StartsWith("end ", StringComparison.InvariantCultureIgnoreCase) &&
							_musicPlayer == null)
						_musicPlayer = MusicPlayer.StartRandomMusic();
				}
				else if (currentString.EqualsIgnoreCase("hide camera"))
				{
					if (!hideCamera)
					{
						TwitchGame.ModuleCameras?.Hide();
						TwitchGame.ModuleCameras?.HideHud();
						IEnumerator hideUI = Module.Bomb.HideMainUIWindow();
						while (hideUI.MoveNext())
							yield return hideUI.Current;
					}

					hideCamera = true;
				}
				else if (currentString.Equals("cancelled", StringComparison.InvariantCultureIgnoreCase) &&
						CoroutineCanceller.ShouldCancel)
				{
					CoroutineCanceller.ResetCancel();
					TryCancel = false;
					break;
				}
				else if (currentString.RegexMatch(out match, "^(?:skiptime|settime) ([0-9:.]+)$") &&
						match.Groups[1].Value.TryParseTime(out float skipTimeTo))
				{
					if (TwitchGame.Instance.Modules.Where(x => x.BombID == Module.BombID && x.BombComponent.IsSolvable && !x.Solved).All(x => x.Solver.SkipTimeAllowed))
					{
						if (ZenMode && Module.Bomb.Bomb.GetTimer().TimeRemaining < skipTimeTo)
							Module.Bomb.Bomb.GetTimer().TimeRemaining = skipTimeTo;

						if (!ZenMode && Module.Bomb.Bomb.GetTimer().TimeRemaining > skipTimeTo)
							Module.Bomb.Bomb.GetTimer().TimeRemaining = skipTimeTo;
					}
				}
				else if (currentString.RegexMatch(out match, @"^awardpoints(onsolve)? (-?\d+)$") && int.TryParse(match.Groups[2].Value, out int pointsAwarded))
				{
					if (OtherModes.ScoreMultiplier == 0)
						continue;

					pointsAwarded = (ComponentSolverFactory.ppaScores.TryGetValue(ModInfo.moduleID, out float ppaScore) ? ppaScore : pointsAwarded * OtherModes.ScoreMultiplier).RoundToInt();

					if (match.Groups[1].Success)
					{
						_delegatedAwardUserNickName = userNickName;
						_pointsToAward = pointsAwarded;
					}
					else
						AwardPoints(_currentUserNickName, pointsAwarded);
				}
				else if (TwitchPlaySettings.data.EnableDebuggingCommands)
					DebugHelper.Log($"Unprocessed string: {currentString}");
			}
			else if (currentValue is KMSelectable selectable1)
			{
				try
				{
					if (HeldSelectables.Contains(selectable1))
					{
						HeldSelectables.Remove(selectable1);
						DoInteractionEnd(selectable1);
					}
					else
					{
						HeldSelectables.Add(selectable1);
						DoInteractionStart(selectable1);
					}
				}
				catch (Exception exception)
				{
					exceptionThrown = true;
					HandleModuleException(exception);
					break;
				}
			}
			else if (currentValue is IEnumerable<KMSelectable> selectables)
			{
				foreach (KMSelectable selectable in selectables)
				{
					WaitForSeconds result = null;
					try
					{
						result = DoInteractionClick(selectable);
					}
					catch (Exception exception)
					{
						exceptionThrown = true;
						HandleModuleException(exception);
						break;
					}

					yield return result;

					if ((_beforeStrikeCount != StrikeCount && !_disableOnStrike || Solved) && !TwitchPlaySettings.data.AnarchyMode || trycancelsequence && CoroutineCanceller.ShouldCancel || Detonated)
						break;
				}
				if (trycancelsequence && CoroutineCanceller.ShouldCancel)
				{
					CoroutineCanceller.ResetCancel();
					break;
				}
			}
			else if (currentValue is Quaternion localQuaternion)
			{
				Module.Bomb.RotateByLocalQuaternion(localQuaternion);
				//Whitelist perspective pegs as it only returns Quaternion.Euler(x, 0, 0), which is compatible with the RotateCameraByQuaternion.
				if (Module.BombComponent.GetComponent<KMBombModule>()?.ModuleType.Equals("spwizPerspectivePegs") ?? false)
					Module.Bomb.RotateCameraByLocalQuaternion(Module.BombComponent.gameObject, localQuaternion);
				needQuaternionReset = true;
			}
			else if (currentValue is Quaternion[] localQuaternions)
			{
				if (localQuaternions.Length == 2)
				{
					Module.Bomb.RotateByLocalQuaternion(localQuaternions[0]);
					Module.Bomb.RotateCameraByLocalQuaternion(Module.BombComponent.gameObject, localQuaternions[1]);
					needQuaternionReset = true;
				}
			}
			else if (currentValue is string[] currentStrings)
			{
				if (currentStrings.Length >= 1)
				{
					if (currentStrings[0].ToLowerInvariant().EqualsAny("detonate", "explode"))
					{
						AwardStrikes(_currentUserNickName, Module.Bomb.StrikeLimit - Module.Bomb.StrikeCount);
						switch (currentStrings.Length)
						{
							case 2:
								Module.Bomb.CauseExplosionByModuleCommand(currentStrings[1], ModInfo.moduleDisplayName);
								break;
							case 3:
								Module.Bomb.CauseExplosionByModuleCommand(currentStrings[1], currentStrings[2]);
								break;
							default:
								Module.Bomb.CauseExplosionByModuleCommand(string.Empty, ModInfo.moduleDisplayName);
								break;
						}
						break;
					}
				}
			}
			yield return currentValue;

			if (CoroutineCanceller.ShouldCancel)
			{
				if (TwitchPlaySettings.data.AnarchyMode && Solved)
				{
					CoroutineCanceller.ResetCancel();
					break;
				}
				TryCancel = true;
			}

			trycancelsequence = false;
		}

		if (!_responded && !exceptionThrown)
			Module.CommandInvalid(userNickName);

		if (needQuaternionReset)
		{
			Module.Bomb.RotateByLocalQuaternion(Quaternion.identity);
			Module.Bomb.RotateCameraByLocalQuaternion(Module.BombComponent.gameObject, Quaternion.identity);
		}

		if (hideCamera)
		{
			TwitchGame.ModuleCameras?.Show();
			TwitchGame.ModuleCameras?.ShowHud();
			IEnumerator showUI = Module.Bomb.ShowMainUIWindow();
			while (showUI.MoveNext())
				yield return showUI.Current;
		}

		if (_musicPlayer != null)
		{
			_musicPlayer.StopMusic();
			_musicPlayer = null;
		}

		if (_disableOnStrike)
		{
			_disableOnStrike = false;
			TwitchGame.ModuleCameras?.UpdateStrikes(true);
			if (Solved)
				OnPass(null);
			AwardStrikes(_currentUserNickName, StrikeCount - _beforeStrikeCount);
		}
		else if (TwitchPlaySettings.data.AnarchyMode)
		{
			_disableAnarchyStrike = false;
			if (StrikeCount != _beforeStrikeCount)
				AwardStrikes(_currentUserNickName, StrikeCount - _beforeStrikeCount);
		}

		if (!parseError)
			yield return new WaitForSeconds(0.5f);

		IEnumerator defocusCoroutine = Module.Bomb.Defocus(Module.Selectable, FrontFace);
		while (defocusCoroutine.MoveNext())
			yield return defocusCoroutine.Current;

		yield return new WaitForSeconds(0.5f);

		_currentUserNickName = null;
		_processingTwitchCommand = false;
	}

	#region Abstract Interface
	protected internal abstract IEnumerator RespondToCommandInternal(string inputCommand);
	#endregion

	#region Protected Helper Methods
	protected enum SendToTwitchChatResponse
	{
		InstantResponse,
		Handled,
		NotHandled
	}

	protected SendToTwitchChatResponse SendToTwitchChat(string message, string userNickName)
	{
		// Within the messages, allow variables:
		// {0} = user’s nickname
		// {1} = Code (module number)
		if (message.RegexMatch(out Match match, @"^senddelayedmessage ([0-9]+(?:\.[0-9]+)?) (\S(?:\S|\s)*)$") && float.TryParse(match.Groups[1].Value, out float messageDelayTime))
		{
			Module.StartCoroutine(SendDelayedMessage(messageDelayTime, string.Format(match.Groups[2].Value, userNickName, Module.Code)));
			return SendToTwitchChatResponse.InstantResponse;
		}

		if (!message.RegexMatch(out match, @"^(sendtochat|sendtochaterror|strikemessage|antitroll) +(\S(?:\S|\s)*)$")) return SendToTwitchChatResponse.NotHandled;

		string chatMsg = string.Format(match.Groups[2].Value, userNickName, Module.Code);

		switch (match.Groups[1].Value)
		{
			case "sendtochat":
				IRCConnection.SendMessage(chatMsg);
				return SendToTwitchChatResponse.InstantResponse;
			case "antitroll":
				if (TwitchPlaySettings.data.EnableTrollCommands || TwitchPlaySettings.data.AnarchyMode) return SendToTwitchChatResponse.Handled;
				goto case "sendtochaterror";
			case "sendtochaterror":
				Module.CommandError(userNickName, chatMsg);
				return SendToTwitchChatResponse.InstantResponse;
			case "strikemessage":
				StrikeMessageConflict |= StrikeCount != _beforeStrikeCount && !string.IsNullOrEmpty(StrikeMessage) && !StrikeMessage.Equals(chatMsg);
				StrikeMessage = chatMsg;
				return SendToTwitchChatResponse.Handled;
			default:
				return SendToTwitchChatResponse.NotHandled;
		}
	}

	protected IEnumerator SendDelayedMessage(float delay, string message)
	{
		yield return new WaitForSeconds(delay);
		IRCConnection.SendMessage(message);
	}

	protected IEnumerator DelayedModuleBombExplosion(float delay, string userNickName, string chatMessage)
	{
		yield return new WaitForSeconds(delay);
		if (!_delayedExplosionPending) yield break;

		if (!string.IsNullOrEmpty(chatMessage)) SendToTwitchChat($"sendtochat {chatMessage}", userNickName);
		AwardStrikes(userNickName, Module.Bomb.StrikeLimit - Module.Bomb.StrikeCount);
		Module.Bomb.CauseExplosionByModuleCommand(string.Empty, ModInfo.moduleDisplayName);
	}

	protected IEnumerator ChainCommand(string command)
	{
		string[] chainedCommands = command.SplitFull(';', ',');
		if (chainedCommands.Length > 1)
		{
			var commandRoutines = chainedCommands.Select(RespondToCommandInternal).ToArray();
			var invalidCommand = Array.Find(commandRoutines, routine => !routine.MoveNext());
			if (invalidCommand != null)
			{
				yield return "sendtochaterror The command \"" + chainedCommands[Array.IndexOf(commandRoutines, invalidCommand)] + "\" is invalid.";
				yield break;
			}

			yield return null;
			foreach (IEnumerator routine in commandRoutines)
				yield return routine;
		}
		else
		{
			var enumerator = RespondToCommandInternal(command);
			while (enumerator.MoveNext())
				yield return enumerator.Current;
		}
	}

	protected void DoInteractionStart(MonoBehaviour interactable) => interactable.GetComponent<Selectable>().HandleInteract();

	protected void DoInteractionEnd(MonoBehaviour interactable)
	{
		Selectable selectable = interactable.GetComponent<Selectable>();
		selectable.OnInteractEnded();
		selectable.SetHighlight(false);
	}

	protected void DoInteractionHighlight(MonoBehaviour interactable) => interactable.GetComponent<Selectable>().SetHighlight(true);

	protected string GetModuleType() => Module.BombComponent.GetComponent<KMBombModule>()?.ModuleType ?? Module.BombComponent.GetComponent<KMNeedyModule>()?.ModuleType;

	// ReSharper disable once UnusedMember.Global
	protected WaitForSeconds DoInteractionClick(MonoBehaviour interactable, float delay) => DoInteractionClick(interactable, null, delay);

	protected WaitForSeconds DoInteractionClick(MonoBehaviour interactable, string strikeMessage = null, float delay = 0.1f)
	{
		if (strikeMessage != null)
		{
			StrikeMessageConflict |= StrikeCount != _beforeStrikeCount && !string.IsNullOrEmpty(StrikeMessage) && !StrikeMessage.Equals(strikeMessage);
			StrikeMessage = strikeMessage;
		}

		if (interactable == null) return new WaitForSeconds(delay);
		DoInteractionStart(interactable);
		DoInteractionEnd(interactable);
		return new WaitForSeconds(delay);
	}

	protected IEnumerator SelectIndex(int current, int target, int length, MonoBehaviour increase, MonoBehaviour decrease)
	{
		var difference = target - current;
		if (Math.Abs(difference) > length / 2)
		{
			difference = Math.Abs(difference) - length;

			if (target < current)
				difference = -difference;
		}

		for (int i = 0; i < Math.Abs(difference); i++)
			yield return DoInteractionClick(difference > 0 ? increase : decrease);
	}

	protected void HandleModuleException(Exception e)
	{
		if (Module.BombComponent.GetModuleDisplayName() == "Manometers")
		{
			var j = 0;
			try
			{
				var component = Module.BombComponent.GetComponent(ReflectionHelper.FindType("manometers"));
				var _pressureList = component.GetValue<int[,]>("pressureList");
				DebugHelper.Log(_pressureList.GetLength(0) + " " + _pressureList.GetLength(1));
				for (int i = 0; i < _pressureList.GetLength(0); i++)
				{
					DebugHelper.Log($"Checking value {i} of the list...");
					j++;
					var value1 = _pressureList[i, 0];
					j++;
					var value2 = _pressureList[i, 1];
					j++;
					var value3 = _pressureList[i, 2];
					j = 0;
					DebugHelper.Log($"Values are: {value1}, {value2}, and {value3}");
				}
				DebugHelper.Log("List checks out, problem is elsewhere.");
			}
			catch (Exception ex)
			{
				if (j != 0)
					DebugHelper.Log($"Index at { j - 1 } was out of range.");
				DebugHelper.LogException(ex, "While attempting to process an issue with Manometers, an exception has occurred. Here's the error:");
			}
			//Stop the audio from playing, but separate it out from the previous try/catch to identify errors better
			try
			{
				var component = Module.BombComponent.GetComponent(ReflectionHelper.FindType("manometers"));
				var audio = component.GetValue<KMAudio.KMAudioRef>("timerSound");
				if (audio != null)
					audio.StopSound();
			}
			catch
			{
				DebugHelper.Log("Audio for manometers could not be stopped.");
			}
		}
		DebugHelper.LogException(e, $"While solving a module ({Module.BombComponent.GetModuleDisplayName()}) an exception has occurred! Here's the error:");
		SolveModule($"Looks like {Module.BombComponent.GetModuleDisplayName()} ran into a problem while running a command, automatically solving module.");
	}

	public void SolveModule(string reason)
	{
		IRCConnection.SendMessage(reason);
		SolveSilently();
	}
	#endregion

	#region Private Methods
	private bool _silentlySolve;
	private bool OnPass(object ignore)
	{
		if (_disableOnStrike) return false;
		if (ModInfo != null)
		{
			int moduleScore = (int) ModInfo.moduleScore;
			if (ModInfo.moduleScoreIsDynamic)
			{
				if (!ComponentSolverFactory.dynamicScores.TryGetValue(ModInfo.moduleID, out float multiplier))
					multiplier = 2;

				switch (ModInfo.moduleID)
				{
					case "cookieJars": // Cookie Jars
						moduleScore += (int) Mathf.Clamp(Module.Bomb.bombSolvableModules * multiplier * TwitchPlaySettings.data.DynamicScorePercentage, 1f, float.PositiveInfinity);
						break;

					case "HexiEvilFMN": // Forget Everything
					case "forgetEnigma": // Forget Enigma
						moduleScore += (int) (Mathf.Clamp(Module.Bomb.bombSolvableModules, 1, 100) * multiplier * TwitchPlaySettings.data.DynamicScorePercentage);
						break;

					default:
						moduleScore += (int) (Module.Bomb.bombSolvableModules * multiplier * TwitchPlaySettings.data.DynamicScorePercentage);
						break;
				}
			}

			if (Module.BombComponent is NeedyComponent)
				return false;

			if (UnsupportedModule)
				Module?.IDTextUnsupported?.gameObject.SetActive(false);

			string solverNickname = null;
			if (!_silentlySolve)
			{
				if (_delegatedSolveUserNickName != null)
				{
					solverNickname = _delegatedSolveUserNickName;
					_delegatedSolveUserNickName = null;
				}
				else if (_currentUserNickName != null)
					solverNickname = _currentUserNickName;
				else if (Module?.PlayerName != null)
					solverNickname = Module.PlayerName;
				else
					solverNickname = IRCConnection.Instance.ChannelName;

				if (_delegatedAwardUserNickName != null)
					AwardPoints(_delegatedAwardUserNickName, _pointsToAward);
				AwardSolve(solverNickname, moduleScore);
			}
			Module?.OnPass(solverNickname);
		}

		TwitchGame.ModuleCameras?.UpdateSolves();

		if (TurnQueued)
		{
			DebugHelper.Log($"[ComponentSolver] Activating queued turn for completed module {Code}.");
			_readyToTurn = true;
			TurnQueued = false;
		}

		TwitchGame.ModuleCameras?.UnviewModule(Module);
		CommonReflectedTypeInfo.UpdateTimerDisplayMethod.Invoke(Module.Bomb.Bomb.GetTimer(), null);
		return false;
	}

	public IEnumerator TurnBombOnSolve()
	{
		while (TurnQueued)
			yield return new WaitForSeconds(0.1f);

		if (!_readyToTurn)
			yield break;

		while (_processingTwitchCommand)
			yield return new WaitForSeconds(0.1f);

		_readyToTurn = false;
		IEnumerator turnCoroutine = Module.Bomb.TurnBomb();
		while (turnCoroutine.MoveNext())
			yield return turnCoroutine.Current;

		yield return new WaitForSeconds(0.5f);
	}

	public void OnFakeStrike()
	{
		if (_delegatedStrikeUserNickName != null)
		{
			AwardStrikes(_delegatedStrikeUserNickName, 0);
			_delegatedStrikeUserNickName = null;
		}
		else if (_currentUserNickName != null)
			AwardStrikes(_currentUserNickName, 0);
		else if (Module.PlayerName != null)
			AwardStrikes(Module.PlayerName, 0);
		else
			AwardStrikes(IRCConnection.Instance.ChannelName, 0);
	}

	public void EnableAnarchyStrike() => _disableAnarchyStrike = false;

	private bool _disableOnStrike;
	private bool _disableAnarchyStrike;
	private bool OnStrike(object ignore)
	{
		//string headerText = (string)CommonReflectedTypeInfo.ModuleDisplayNameField.Invoke(BombComponent, null);
		StrikeCount++;

		if (_disableOnStrike || _disableAnarchyStrike)
		{
			TwitchGame.ModuleCameras?.UpdateStrikes(true);
			return false;
		}

		if (_delegatedStrikeUserNickName != null)
		{
			AwardStrikes(_delegatedStrikeUserNickName, 1);
			_delegatedStrikeUserNickName = null;
		}
		else if (_currentUserNickName != null)
			AwardStrikes(_currentUserNickName, 1);
		else if (Module.PlayerName != null)
			AwardStrikes(Module.PlayerName, 1);
		else
			//AwardStrikes(IRCConnection.Instance.ChannelName, 1); - Instead of striking the streamer, decrease the reward
			AwardStrikes(1);

		TwitchGame.ModuleCameras?.UpdateStrikes(true);

		return false;
	}

	protected void PrepareSilentSolve()
	{
		_delegatedSolveUserNickName = null;
		_currentUserNickName = null;
		_silentlySolve = true;
	}

	public void SolveSilently()
	{
		_delegatedSolveUserNickName = null;
		_currentUserNickName = null;
		_silentlySolve = true;
		HandleForcedSolve(Module);
	}

	protected virtual IEnumerator ForcedSolveIEnumerator()
	{
		yield break;
	}

	protected bool HandleForcedSolve()
	{
		_delegatedSolveUserNickName = null;
		_currentUserNickName = null;
		_silentlySolve = true;
		_responded = true;
		IEnumerator forcedSolve = ForcedSolveIEnumerator();
		if (!forcedSolve.MoveNext()) return false;

		CoroutineQueue.AddForcedSolve(EnsureSolve(ForcedSolveIEnumerator(), Module.BombComponent));
		return true;
	}

	public static void HandleForcedSolve(TwitchModule handle)
	{
		try
		{
			BombComponent bombComponent = handle == null ? null : handle.BombComponent;
			ComponentSolver solver = handle == null ? null : handle.Solver;

			KMBombModule module = bombComponent == null ? null : bombComponent.GetComponent<KMBombModule>();
			if (module != null)
			{
				foreach (TwitchModule h in TwitchGame.Instance.Modules.Where(x => x.Bomb == handle.Bomb))
				{
					h.Solver.AddAbandonedModule(module);
				}
			}

			if (solver.AttemptedForcedSolve)
			{
				IRCConnection.SendMessage("Forcing the module into a solved state.");
				CommonReflectedTypeInfo.HandlePassMethod.Invoke(bombComponent, null);
				return;
			}

			solver.AttemptedForcedSolve = true;

			if (solver?.HandleForcedSolve() ?? false)
			{
				// The force solve is being handled by a TP solver.
			}
			else if (solver?.ForcedSolveMethod != null)
			{
				// The force solve is being handled by the module's solver.
				solver.AttemptedForcedSolve = true;
				solver._delegatedSolveUserNickName = null;
				solver._currentUserNickName = null;
				solver._silentlySolve = true;
				try
				{
					object result = solver.ForcedSolveMethod.Invoke(solver.CommandComponent, null);
					if (result is IEnumerator enumerator)
						CoroutineQueue.AddForcedSolve(EnsureSolve(enumerator, bombComponent));
				}
				catch (Exception ex)
				{
					DebugHelper.LogException(ex, $"An exception occurred while using the Forced Solve handler ({bombComponent.GetModuleDisplayName()}):");
					CommonReflectedTypeInfo.HandlePassMethod.Invoke(bombComponent, null);
					foreach (MonoBehaviour behavior in bombComponent.GetComponentsInChildren<MonoBehaviour>(true))
					{
						behavior?.StopAllCoroutines();
					}
				}
			}
			else if (handle != null)
			{
				// There is no force solver, just force a pass.
				if (solver != null)
				{
					solver._delegatedSolveUserNickName = null;
					solver._currentUserNickName = null;
					solver._silentlySolve = true;
				}

				CommonReflectedTypeInfo.HandlePassMethod.Invoke(bombComponent, null);
				foreach (MonoBehaviour behavior in bombComponent.GetComponentsInChildren<MonoBehaviour>(true))
				{
					behavior?.StopAllCoroutines();
				}
			}
		}
		catch (Exception ex)
		{
			DebugHelper.LogException(ex, $"An exception occurred while silently solving a module ({handle.BombComponent.GetModuleDisplayName()}):");
		}
	}

	static IEnumerator EnsureSolve(IEnumerator enumerator, BombComponent bombComponent)
	{
		yield return enumerator;
		CommonReflectedTypeInfo.HandlePassMethod.Invoke(bombComponent, null);
	}

	private void AwardSolve(string userNickName, int componentValue)
	{
		List<string> messageParts = new List<string>();

		componentValue = (componentValue * OtherModes.ScoreMultiplier).RoundToInt();
		if (userNickName == null)
			TwitchPlaySettings.AddRewardBonus(componentValue);
		else
		{
			int HPDamage = 0;
			bool solve = false;
			OtherModes.Team? teamDamaged = null;
			if (OtherModes.VSModeOn)
			{
				HPDamage = componentValue * 5;
				Leaderboard.Instance.GetRank(userNickName, out Leaderboard.LeaderboardEntry entry);
				teamDamaged = entry.Team == OtherModes.Team.Good ? OtherModes.Team.Evil : OtherModes.Team.Good; //if entry is null here something went very wrong
				if (UnsupportedModule)
					HPDamage = 0;

				switch (teamDamaged)
				{
					case OtherModes.Team.Evil:
						if (OtherModes.GetEvilHealth() <= 1)
							solve = true;

						if (OtherModes.GetEvilHealth() <= HPDamage && !solve)
							HPDamage = OtherModes.GetEvilHealth() - 1;
						break;
					case OtherModes.Team.Good:
						if (OtherModes.GetGoodHealth() <= 1)
							solve = true;

						if (OtherModes.GetGoodHealth() <= HPDamage && !solve)
							HPDamage = OtherModes.GetGoodHealth() - 1;
						break;
				}
			}
			string headerText = UnsupportedModule ? ModInfo.moduleDisplayName : Module.BombComponent.GetModuleDisplayName();
			if (OtherModes.VSModeOn && !UnsupportedModule)
				messageParts.Add(string.Format(TwitchPlaySettings.data.AwardVsSolve, Code, userNickName,
					componentValue, headerText, HPDamage,
					teamDamaged == OtherModes.Team.Evil ? "the evil team" : "the good team"));
			else
				messageParts.Add(string.Format(TwitchPlaySettings.data.AwardSolve, Code, userNickName, componentValue, headerText));
			string recordMessageTone = $"Module ID: {Code} | Player: {userNickName} | Module Name: {headerText} | Value: {componentValue}";
			if (!OtherModes.TrainingModeOn) Leaderboard.Instance?.AddSolve(userNickName);
			if (!UserAccess.HasAccess(userNickName, AccessLevel.NoPoints))
				Leaderboard.Instance?.AddScore(userNickName, componentValue);
			else
				TwitchPlaySettings.AddRewardBonus(componentValue);

			if (OtherModes.VSModeOn)
			{
				if (!solve)
				{
					switch (teamDamaged)
					{
						case OtherModes.Team.Good:
							OtherModes.SubtractGoodHealth(HPDamage);
							break;
						case OtherModes.Team.Evil:
							OtherModes.SubtractEvilHealth(HPDamage);
							break;
					}
				}
				else
				{
					switch (teamDamaged)
					{
						case OtherModes.Team.Good:
							OtherModes.goodHealth = 0;

							// If Good loses, the bomb blows up.
							TwitchPlaysService.Instance.CoroutineQueue.CancelFutureSubcoroutines();
							TwitchGame.Instance.Bombs[0].CauseVersusExplosion();

							// This was here originally to detonate the bomb, but was nonfunctional.
							//TwitchPlaysService.Instance.CoroutineQueue.AddToQueue(BombCommands.Explode(TwitchGame.Instance.Bombs[0]));
							break;
						case OtherModes.Team.Evil:
							OtherModes.evilHealth = 0;

							// If Evil loses, the bomb is solved.
							GameCommands.SolveBomb();
							break;
					}
				}
				if (!solve)
					TwitchGame.ModuleCameras.UpdateConfidence();
			}

			TwitchPlaySettings.AppendToSolveStrikeLog(recordMessageTone);
			TwitchPlaySettings.AppendToPlayerLog(userNickName);
		}

		if (OtherModes.TimeModeOn && Module.Bomb.bombSolvedModules < Module.Bomb.bombSolvableModules)
		{
			float time = OtherModes.GetAdjustedMultiplier() * componentValue;
			if (time < TwitchPlaySettings.data.TimeModeMinimumTimeGained)
			{
				Module.Bomb.Bomb.GetTimer().TimeRemaining = Module.Bomb.CurrentTimer + TwitchPlaySettings.data.TimeModeMinimumTimeGained;
				messageParts.Add($"Bomb time increased by the minimum {TwitchPlaySettings.data.TimeModeMinimumTimeGained} seconds!");
			}
			else
			{
				Module.Bomb.Bomb.GetTimer().TimeRemaining = Module.Bomb.CurrentTimer + time;
				messageParts.Add($"Bomb time increased by {Math.Round(time, 1)} seconds!");
			}
			OtherModes.SetMultiplier(OtherModes.GetMultiplier() + TwitchPlaySettings.data.TimeModeSolveBonus);
		}

		IRCConnection.SendMessage(messageParts.Join());

		if (ModInfo.moduleID == "organizationModule")
		{
			TwitchPlaySettings.AddRewardBonus((Module.Bomb.bombSolvableModules - 1) * 3);
			IRCConnection.SendMessage($"Reward increased by {(Module.Bomb.bombSolvableModules - 1) * 3} for defusing module !{Code} ({ModInfo.moduleDisplayName}).");
		}
	}

	private void AwardStrikes(int strikeCount) => AwardStrikes(null, strikeCount);

	private void AwardStrikes(string userNickName, int strikeCount)
	{
		List<string> messageParts = new List<string>();

		string headerText = UnsupportedModule ? ModInfo.moduleDisplayName : Module.BombComponent.GetModuleDisplayName();
		int strikePenalty = -TwitchPlaySettings.data.StrikePenalty * (TwitchPlaySettings.data.EnableRewardMultipleStrikes ? strikeCount : 1);
		int hpPenalty = 0;
		OtherModes.Team? team = null;
		strikePenalty = (strikePenalty * OtherModes.ScoreMultiplier).RoundToInt();
		if (OtherModes.VSModeOn)
		{
			if (!string.IsNullOrEmpty(userNickName))
			{
				Leaderboard.Instance.GetRank(userNickName, out Leaderboard.LeaderboardEntry entry);
				team = entry?.Team ?? OtherModes.Team.Good;
				hpPenalty = team == OtherModes.Team.Good
					? OtherModes.GetGoodHealth() > 30 ? 30 : OtherModes.GetGoodHealth() < 2 ? 0 : OtherModes.GetGoodHealth() - 1
					: OtherModes.GetEvilHealth() > 30 ? 30 : OtherModes.GetEvilHealth() < 2 ? 0 : OtherModes.GetEvilHealth() - 1;
				messageParts.Add(string.Format(TwitchPlaySettings.data.AwardVsStrike, Code,
					strikeCount == 1 ? "a" : strikeCount.ToString(), strikeCount == 1 ? "" : "s", "0", team == OtherModes.Team.Good ? "the good team" : "the evil team", string.IsNullOrEmpty(StrikeMessage) || StrikeMessageConflict ? "" : " caused by " + StrikeMessage, headerText,
					hpPenalty == 0 ? team == OtherModes.Team.Good ? OtherModes.GetGoodHealth() : OtherModes.GetEvilHealth() : hpPenalty, strikePenalty, userNickName));
			}
		}
		else
		{
			if (!string.IsNullOrEmpty(userNickName))
				messageParts.Add(string.Format(TwitchPlaySettings.data.AwardStrike, Code,
					strikeCount == 1 ? "a" : strikeCount.ToString(), strikeCount == 1 ? "" : "s", "0", userNickName,
					string.IsNullOrEmpty(StrikeMessage) || StrikeMessageConflict ? "" : " caused by " + StrikeMessage,
					headerText, strikePenalty));
			else
				messageParts.Add(string.Format(TwitchPlaySettings.data.AwardRewardStrike, Code,
					strikeCount == 1 ? "a" : strikeCount.ToString(), strikeCount == 1 ? "" : "s", headerText,
					string.IsNullOrEmpty(StrikeMessage) || StrikeMessageConflict ? "" : " caused by " + StrikeMessage));
		}

		if (strikeCount <= 0) return;

		string recordMessageTone = !string.IsNullOrEmpty(userNickName) ? $"Module ID: {Code} | Player: {userNickName} | Module Name: {headerText} | Strike" : $"Module ID: {Code} | Module Name: {headerText} | Strike";

		TwitchPlaySettings.AppendToSolveStrikeLog(recordMessageTone, TwitchPlaySettings.data.EnableRewardMultipleStrikes ? strikeCount : 1);
		int originalReward = TwitchPlaySettings.GetRewardBonus();
		int currentReward = Convert.ToInt32(originalReward * TwitchPlaySettings.data.AwardDropMultiplierOnStrike);
		TwitchPlaySettings.AddRewardBonus(currentReward - originalReward);
		if (currentReward != originalReward)
			messageParts.Add($"Reward {(currentReward > 0 ? "reduced" : "increased")} to {currentReward} points.");

		if (OtherModes.VSModeOn)
		{
			if (userNickName != null)
			{
				if (hpPenalty != 0)
				{
					if (team == OtherModes.Team.Good)
						OtherModes.SubtractGoodHealth(hpPenalty);
					else
						OtherModes.SubtractEvilHealth(hpPenalty);
				}
				else
				{
					if (team == OtherModes.Team.Evil)
					{
						OtherModes.evilHealth = 0;

						// If Evil loses, the bomb is solved.
						GameCommands.SolveBomb();
					}
					else
					{
						// If Good loses, the bomb blows up.
						TwitchPlaysService.Instance.CoroutineQueue.CancelFutureSubcoroutines();
						TwitchGame.Instance.Bombs[0].CauseVersusExplosion();

						// This was here originally to detonate the bomb, but was nonfunctional.
						//TwitchPlaysService.Instance.CoroutineQueue.AddToQueue(BombCommands.Explode(TwitchGame.Instance.Bombs[0]));
					}
				}

				if (hpPenalty != 0)
					TwitchGame.ModuleCameras.UpdateConfidence();
			}

			// Ensure strikes are always set to 0 in VS mode, even for strikes not assigned to a team. (Needies, etc.)
			Module.Bomb.StrikeCount = 0;
		}

		if (OtherModes.TimeModeOn)
		{
			float originalMultiplier = OtherModes.GetAdjustedMultiplier();
			bool multiDropped = OtherModes.DropMultiplier();
			float multiplier = OtherModes.GetAdjustedMultiplier();
			string tempMessage;
			if (multiDropped)
			{
				if (Mathf.Abs(originalMultiplier - multiplier) >= 0.1)
					tempMessage = "Multiplier reduced to " + Math.Round(multiplier, 1) + " and time";
				else
					tempMessage = "Time";
			}
			else
				tempMessage =
					$"Multiplier set at {TwitchPlaySettings.data.TimeModeMinMultiplier}, cannot be further reduced.  Time";

			if (Module.Bomb.CurrentTimer < (TwitchPlaySettings.data.TimeModeMinimumTimeLost / TwitchPlaySettings.data.TimeModeTimerStrikePenalty))
			{
				Module.Bomb.Bomb.GetTimer().TimeRemaining = Module.Bomb.CurrentTimer - TwitchPlaySettings.data.TimeModeMinimumTimeLost;
				tempMessage += $" reduced by {TwitchPlaySettings.data.TimeModeMinimumTimeLost} seconds.";
			}
			else
			{
				float timeReducer = Module.Bomb.CurrentTimer * TwitchPlaySettings.data.TimeModeTimerStrikePenalty;
				double easyText = Math.Round(timeReducer, 1);
				Module.Bomb.Bomb.GetTimer().TimeRemaining = Module.Bomb.CurrentTimer - timeReducer;
				tempMessage += $" reduced by {Math.Round(TwitchPlaySettings.data.TimeModeTimerStrikePenalty * 100, 1)}%. ({easyText} seconds)";
			}
			messageParts.Add(tempMessage);
			Module.Bomb.StrikeCount = 0;
			TwitchGame.ModuleCameras.UpdateStrikes();
		}

		if (OtherModes.Unexplodable)
			Module.Bomb.StrikeLimit += strikeCount;

		if (!string.IsNullOrEmpty(userNickName) && !OtherModes.TrainingModeOn)
		{
			Leaderboard.Instance.AddScore(userNickName, strikePenalty);
			Leaderboard.Instance.AddStrike(userNickName, strikeCount);
		}
		StrikeMessage = string.Empty;
		StrikeMessageConflict = false;

		IRCConnection.SendMessage(messageParts.Join());
	}
	
	private void AwardPoints(string userNickName, int pointsAwarded)
	{
		List<string> messageParts = new List<string>();
		if (!UserAccess.HasAccess(userNickName, AccessLevel.NoPoints))
		{
			Leaderboard.Instance?.AddScore(userNickName, pointsAwarded);
			messageParts.Add(string.Format(TwitchPlaySettings.data.PointsAwardedByModule,
			userNickName, pointsAwarded, pointsAwarded > 1 ? "s" : "", Code, ModInfo.moduleDisplayName));
		}

		if (TwitchPlaySettings.data.TimeModeTimeForActions && OtherModes.TimeModeOn)
		{
			float time = OtherModes.GetAdjustedMultiplier() * pointsAwarded;
			Module.Bomb.Bomb.GetTimer().TimeRemaining = Module.Bomb.CurrentTimer + time;
			messageParts.Add($"Bomb time increased by {Math.Round(time, 1)} seconds!");
		}

		IRCConnection.SendMessage(messageParts.Join());
	}

	protected void ReleaseHeldButtons()
	{
		var copy = HeldSelectables.ToArray();
		HeldSelectables.Clear();

		foreach (var selectable in copy)
		{
			DoInteractionEnd(selectable);
		}
	}

	protected void AddAbandonedModule(KMBombModule module)
	{
		if (!(AbandonModule?.Contains(module) ?? true))
			AbandonModule?.Add(module);
	}
	#endregion

	public string Code
	{
		get;
		set;
	}

	public bool UnsupportedModule { get; set; } = false;

	#region Protected Properties

	protected string StrikeMessage
	{
		get;
		set;
	}

	protected bool StrikeMessageConflict { get; set; }

	public bool Solved => Module.Solved;

	protected bool Detonated => Module.Bomb.Bomb.HasDetonated;

	public int StrikeCount { get; private set; }

	protected float FocusDistance => Module.FocusDistance;

	protected bool FrontFace => Module.FrontFace;

	protected FieldInfo HelpMessageField { get; set; }
	private string _helpMessage = null;
	public string HelpMessage
	{
		get
		{
			if (!(HelpMessageField?.GetValue(HelpMessageField.IsStatic ? null : CommandComponent) is string))
				return _helpMessage ?? ModInfo.helpText;
			return ModInfo.helpTextOverride
				? ModInfo.helpText
				: (string) HelpMessageField.GetValue(HelpMessageField.IsStatic ? null : CommandComponent);
		}
		protected set
		{
			if (HelpMessageField?.GetValue(HelpMessageField.IsStatic ? null : CommandComponent) is string)
				HelpMessageField.SetValue(HelpMessageField.IsStatic ? null : CommandComponent, value);
			else _helpMessage = value;
		}
	}

	protected FieldInfo ManualCodeField { get; set; }
	private string _manualCode = null;
	public string ManualCode
	{
		get
		{
			if (!(ManualCodeField?.GetValue(ManualCodeField.IsStatic ? null : CommandComponent) is string))
				return _manualCode ?? ModInfo.manualCode;
			return ModInfo.manualCodeOverride
				? ModInfo.manualCode
				: (string) ManualCodeField.GetValue(ManualCodeField.IsStatic ? null : CommandComponent);
		}
		protected set
		{
			if (ManualCodeField?.GetValue(ManualCodeField.IsStatic ? null : CommandComponent) is string)
				ManualCodeField.SetValue(ManualCodeField.IsStatic ? null : CommandComponent, value);
			else _manualCode = value;
		}
	}

	protected FieldInfo SkipTimeField { get; set; }
	private bool _skipTimeAllowed;
	public bool SkipTimeAllowed
	{
		get
		{
			if (!(SkipTimeField?.GetValue(SkipTimeField.IsStatic ? null : CommandComponent) is bool))
				return _skipTimeAllowed;
			return (bool) SkipTimeField.GetValue(SkipTimeField.IsStatic ? null : CommandComponent);
		}
		protected set
		{
			if (SkipTimeField?.GetValue(SkipTimeField.IsStatic ? null : CommandComponent) is bool)
				SkipTimeField.SetValue(SkipTimeField.IsStatic ? null : CommandComponent, value);
			else _skipTimeAllowed = value;
		}
	}

	protected FieldInfo AbandonModuleField { get; set; }
	protected List<KMBombModule> AbandonModule
	{
		get
		{
			if (!(AbandonModuleField?.GetValue(AbandonModuleField.IsStatic ? null : CommandComponent) is List<KMBombModule>))
				return null;
			return (List<KMBombModule>) AbandonModuleField.GetValue(AbandonModuleField.IsStatic ? null : CommandComponent);
		}
		set
		{
			if (AbandonModuleField?.GetValue(AbandonModuleField.IsStatic ? null : CommandComponent) is List<KMBombModule>)
				AbandonModuleField.SetValue(AbandonModuleField.IsStatic ? null : CommandComponent, value);
		}
	}

	protected FieldInfo TryCancelField { get; set; }
	protected bool TryCancel
	{
		get
		{
			if (!(TryCancelField?.GetValue(TryCancelField.IsStatic ? null : CommandComponent) is bool))
				return false;
			return (bool) TryCancelField.GetValue(TryCancelField.IsStatic ? null : CommandComponent);
		}
		set
		{
			if (TryCancelField?.GetValue(TryCancelField.IsStatic ? null : CommandComponent) is bool)
				TryCancelField.SetValue(TryCancelField.IsStatic ? null : CommandComponent, value);
		}
	}
	protected FieldInfo ZenModeField { get; set; }
	protected bool ZenMode
	{
		get
		{
			if (!(ZenModeField?.GetValue(ZenModeField.IsStatic ? null : CommandComponent) is bool))
				return OtherModes.Unexplodable;
			return (bool) ZenModeField.GetValue(ZenModeField.IsStatic ? null : CommandComponent);
		}
		set
		{
			if (ZenModeField?.GetValue(ZenModeField.IsStatic ? null : CommandComponent) is bool)
				ZenModeField.SetValue(ZenModeField.IsStatic ? null : CommandComponent, value);
		}
	}
	protected FieldInfo TimeModeField { get; set; }
	protected bool TimeMode
	{
		get
		{
			if (!(TimeModeField?.GetValue(TimeModeField.IsStatic ? null : CommandComponent) is bool))
				return OtherModes.TimeModeOn;
			return (bool) TimeModeField.GetValue(TimeModeField.IsStatic ? null : CommandComponent);
		}
		set
		{
			if (TimeModeField?.GetValue(TimeModeField.IsStatic ? null : CommandComponent) is bool)
				TimeModeField.SetValue(TimeModeField.IsStatic ? null : CommandComponent, value);
		}
	}
	protected FieldInfo TwitchPlaysField { get; set; }
	protected bool TwitchPlays
	{
		get
		{
			if (!(TwitchPlaysField?.GetValue(TwitchPlaysField.IsStatic ? null : CommandComponent) is bool))
				return false;
			return (bool) TwitchPlaysField.GetValue(TwitchPlaysField.IsStatic ? null : CommandComponent);
		}
		set
		{
			if (TwitchPlaysField?.GetValue(TwitchPlaysField.IsStatic ? null : CommandComponent) is bool)
				TwitchPlaysField.SetValue(TwitchPlaysField.IsStatic ? null : CommandComponent, value);
		}
	}
	#endregion

	#region Fields
	protected readonly TwitchModule Module = null;
	protected readonly HashSet<KMSelectable> HeldSelectables = new HashSet<KMSelectable>();

	private string _delegatedStrikeUserNickName;
	private string _delegatedSolveUserNickName;
	private string _delegatedAwardUserNickName;
	private string _currentUserNickName;
	private int _pointsToAward;

	private MusicPlayer _musicPlayer;
	public ModuleInformation ModInfo = null;
	public bool ChainableCommands = false;

	public bool TurnQueued;
	private bool _readyToTurn;
	private bool _processingTwitchCommand;
	private bool _responded;
	public bool AttemptedForcedSolve;
	private bool _delayedExplosionPending;
	private Coroutine _delayedExplosionCoroutine;

	protected MethodInfo ProcessMethod = null;
	public MethodInfo ForcedSolveMethod = null;
	public Component CommandComponent = null;
	#endregion
}
 
