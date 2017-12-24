﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class MissionMessageResponder : MessageResponder
{
    private BombBinderCommander _bombBinderCommander = null;
    private FreeplayCommander _freeplayCommander = null;

    #region Unity Lifecycle
    private void OnEnable()
    {
        // InputInterceptor.DisableInput();

        StartCoroutine(CheckForBombBinderAndFreeplayDevice());
    }

    private void OnDisable()
    {
        StopAllCoroutines();

        _bombBinderCommander = null;
        _freeplayCommander = null;
    }
    #endregion

    #region Protected/Private Methods
    private IEnumerator CheckForBombBinderAndFreeplayDevice()
    {
        yield return null;

        while (true)
        {
            UnityEngine.Object[] bombBinders = FindObjectsOfType(CommonReflectedTypeInfo.BombBinderType);
            if (bombBinders != null && bombBinders.Length > 0)
            {
                _bombBinderCommander = new BombBinderCommander((MonoBehaviour)bombBinders[0]);
                break;
            }

            yield return null;
        }

        while (true)
        {
            UnityEngine.Object[] freeplayDevices = FindObjectsOfType(CommonReflectedTypeInfo.FreeplayDeviceType);
            if (freeplayDevices != null && freeplayDevices.Length > 0)
            {
                _freeplayCommander = new FreeplayCommander((MonoBehaviour)freeplayDevices[0]);
                break;
            }

            yield return null;
        }
    }

	protected override void OnMessageReceived(string userNickName, string userColorCode, string text)
	{
		if (_bombBinderCommander == null)
		{
			return;
		}
		Match binderMatch = Regex.Match(text, "^!binder (.+)", RegexOptions.IgnoreCase);
		if (binderMatch.Success)
		{
			if ((TwitchPlaySettings.data.EnableMissionBinder && TwitchPlaySettings.data.EnableTwitchPlaysMode) || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
			{
				_coroutineQueue.AddToQueue(_bombBinderCommander.RespondToCommand(userNickName, binderMatch.Groups[1].Value, null, _ircConnection));
			}
			else
			{
				_ircConnection.SendMessage(TwitchPlaySettings.data.MissionBinderDisabled, userNickName);
			}
		}

		Match freeplayMatch = Regex.Match(text, "^!freeplay (.+)", RegexOptions.IgnoreCase);
		if (freeplayMatch.Success)
		{
			if ((TwitchPlaySettings.data.EnableFreeplayBriefcase && TwitchPlaySettings.data.EnableTwitchPlaysMode) || UserAccess.HasAccess(userNickName, AccessLevel.Admin, true))
			{
				_coroutineQueue.AddToQueue(_freeplayCommander.RespondToCommand(userNickName, freeplayMatch.Groups[1].Value, null, _ircConnection));
			}
			else
			{
				_ircConnection.SendMessage(TwitchPlaySettings.data.FreePlayDisabled, userNickName);
			}
		}
		
		Match runMatch = Regex.Match(text, "^!run (.+)", RegexOptions.IgnoreCase);
		if (runMatch.Success)
		{
		    string targetID = runMatch.Groups[1].Value;
		    string allowedID = TwitchPlaySettings.data.AllowedRunCommandMissions.Where(x => x.Key.Equals(targetID, StringComparison.InvariantCultureIgnoreCase)).Select(y => y.Value).FirstOrDefault();
            if ((TwitchPlaySettings.data.EnableRunCommand && TwitchPlaySettings.data.EnableTwitchPlaysMode) || UserAccess.HasAccess(userNickName, AccessLevel.Mod, true))
		    {
		        object modManager = CommonReflectedTypeInfo.ModManagerInstanceField.GetValue(null);
		        IEnumerable<ScriptableObject> missions = ((IEnumerable) CommonReflectedTypeInfo.ModMissionsField.GetValue(modManager, null)).Cast<ScriptableObject>();
		        ScriptableObject mission = null;
		        var scriptableObjects = missions as ScriptableObject[] ?? missions.ToArray();
		        if(UserAccess.HasAccess(userNickName, AccessLevel.Mod, true)) mission = scriptableObjects.FirstOrDefault(obj => Regex.IsMatch(obj.name, "mod_.+_" + Regex.Escape(targetID)));
                if (mission == null && !string.IsNullOrEmpty(allowedID)) mission = scriptableObjects.FirstOrDefault(obj => Regex.IsMatch(obj.name, "mod_.+_" + Regex.Escape(allowedID)));
                if (mission == null)
                    _ircConnection.SendMessage("Failed to find a mission with ID \"{0}\".", targetID);
                else
		            GetComponent<KMGameCommands>().StartMission(mission.name, "-1");
		    }
		    else
            { 
		        _ircConnection.SendMessage(TwitchPlaySettings.data.RunCommandDisabled, userNickName);
		    }
		}

		Match runrawMatch = Regex.Match(text, "^!runraw (.+)", RegexOptions.IgnoreCase);
		if (runrawMatch.Success && UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true))
		{
			GetComponent<KMGameCommands>().StartMission(runrawMatch.Groups[1].Value, "-1");
		}
	}
    #endregion
}
