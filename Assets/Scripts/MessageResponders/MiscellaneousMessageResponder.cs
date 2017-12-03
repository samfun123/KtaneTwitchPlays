﻿using System;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public class MiscellaneousMessageResponder : MessageResponder
{
    public Leaderboard leaderboard = null;
    public int moduleCountBonus = 0;

    [HideInInspector]
    public MonoBehaviour bombComponent = null;

    protected override void OnMessageReceived(string userNickName, string userColorCode, string text)
    {
        if (text.Equals("!cancel", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            _coroutineCanceller.SetCancel();
            return;
        }
        else if (text.Equals("!stop", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            _coroutineCanceller.SetCancel();
            _coroutineQueue.CancelFutureSubcoroutines();
            return;
        }
        else if (text.Equals("!manual", StringComparison.InvariantCultureIgnoreCase) ||
                 text.Equals("!help", StringComparison.InvariantCultureIgnoreCase))
        {
            _ircConnection.SendMessage("!{0} manual [link to module {0}'s manual] | Go to {1} to get the vanilla manual for KTaNE", UnityEngine.Random.Range(1, 100), TwitchPlaysService.urlHelper.VanillaManual);
            _ircConnection.SendMessage("!{0} help [commands for module {0}] | Go to {1} to get the command reference for TP:KTaNE (multiple pages, see the menu on the right)", UnityEngine.Random.Range(1, 100), TwitchPlaysService.urlHelper.CommandReference);
            return;
        }
        else if (text.StartsWith("!bonusscore", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            string[] parts = text.Split(' ');
            if (parts.Length < 3)
            {
                return;
            }
            string playerrewarded = parts[1];
            int scorerewarded;
            if (!int.TryParse(parts[2], out scorerewarded))
            {
                return;
            }
            if (UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true))
            {
                _ircConnection.SendMessage(TwitchPlaySettings.data.GiveBonusPoints, parts[1], parts[2], userNickName);
                Color usedColor = new Color(.31f, .31f, .31f);
                leaderboard.AddScore(playerrewarded, usedColor, scorerewarded);
            }
            return;
        }
        else if (text.StartsWith("!reward", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            if (UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true))
            {
                string[] parts = text.Split(' ');
                moduleCountBonus = Int32.Parse(parts[1]);
                TwitchPlaySettings.SetRewardBonus(moduleCountBonus);
            }
        }        
        else if (text.StartsWith("!rank", StringComparison.InvariantCultureIgnoreCase))
        {
            Leaderboard.LeaderboardEntry entry = null;
            if (text.Length > 6)
            {
                string[] parts = text.Split(' ');
                int desiredRank;
                if (parts[1].Equals("solo", StringComparison.InvariantCultureIgnoreCase) && int.TryParse(parts[2], out desiredRank))
                {
                    leaderboard.GetSoloRank(desiredRank, out entry);
                }
                else if (int.TryParse(parts[1], out desiredRank))
                {
                    leaderboard.GetRank(desiredRank, out entry);
                }
                else
                {
                    return;
                }
                if (entry == null)
                {
                    _ircConnection.SendMessage(TwitchPlaySettings.data.RankTooLow);
                    return;
                }
            }
            if (entry == null)
            {
                leaderboard.GetRank(userNickName, out entry);
            }
            if (entry != null)
            {
                string txtSolver = "";
                string txtSolo = ".";
                if (entry.TotalSoloClears > 0)
                {
                    TimeSpan recordTimeSpan = TimeSpan.FromSeconds(entry.RecordSoloTime);
                    txtSolver = TwitchPlaySettings.data.SolverAndSolo;
                    txtSolo = string.Format(TwitchPlaySettings.data.SoloRankQuery, entry.SoloRank, (int)recordTimeSpan.TotalMinutes, recordTimeSpan.Seconds);
                }
                _ircConnection.SendMessage(TwitchPlaySettings.data.RankQuery, entry.UserName, entry.Rank, entry.SolveCount, entry.StrikeCount, txtSolver, txtSolo, entry.SolveScore);
            }
            else
            {
                _ircConnection.SendMessage(TwitchPlaySettings.data.DoYouEvenPlayBro, userNickName);
            }
            return;
        }
        else if (text.Equals("!log", StringComparison.InvariantCultureIgnoreCase) || text.Equals("!analysis", StringComparison.InvariantCultureIgnoreCase))
        {
            TwitchPlaysService.logUploader.PostToChat("Analysis for the previous bomb: {0}");
            return;
        }
        else if (text.Equals("!shorturl", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            _ircConnection.SendMessage((TwitchPlaysService.urlHelper.ToggleMode()) ? "Enabling shortened URLs" : "Disabling shortened URLs");
        }
        else if (text.Equals("!about", StringComparison.InvariantCultureIgnoreCase))
        {
            _ircConnection.SendMessage("Twitch Plays: KTaNE is an alternative way of playing !ktane. Unlike the original game, you play as both defuser and expert, and defuse the bomb by sending special commands to the chat. Try !help for more information!");
            return;
        }
        else if (text.Equals("!ktane", StringComparison.InvariantCultureIgnoreCase))
        {
            _ircConnection.SendMessage("Keep Talking and Nobody Explodes is developed by Steel Crate Games. It's available for Windows PC, Mac OS X, PlayStation VR, Samsung Gear VR and Google Daydream. See http://www.keeptalkinggame.com/ for more information!");
            return;
        }
        else if (text.StartsWith("!add ", StringComparison.InvariantCultureIgnoreCase) || text.StartsWith("!remove ", StringComparison.InvariantCultureIgnoreCase))
        {
            if (!IsAuthorizedDefuser(userNickName)) return;
            string[] split = text.ToLowerInvariant().Split(' ');
            if (split.Length < 3)
            {
                return;
            }

            bool stepdown = split[0].Equals("!remove",StringComparison.InvariantCultureIgnoreCase) && split[1].Equals(userNickName, StringComparison.InvariantCultureIgnoreCase);
            if (!UserAccess.HasAccess(userNickName, AccessLevel.Mod, true) && !stepdown)
            {
                return;
            }

           
            
            AccessLevel level = AccessLevel.User;
            foreach(string lvl in split.Skip(2))
            {
                switch (lvl)
                {
                    case "mod":
                    case "moderator":
                        level |= (stepdown || UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true)) ? AccessLevel.Mod : AccessLevel.User;
                        break;
                    case "admin":
                    case "administrator":
                        level |= (stepdown || UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true)) ? AccessLevel.Admin : AccessLevel.User;
                        break;
                    case "superadmin":
                    case "superuser":
                    case "super-user":
                    case "super-admin":
                    case "super-mod":
                    case "supermod":
                        level |= (stepdown || UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true)) ? AccessLevel.SuperUser : AccessLevel.User;
                        break;

                
                    case "defuser":
                        level |= AccessLevel.Defuser;
                        break;
                    case "no-points":
                    case "no-score":
                    case "noscore":
                    case "nopoints":
                        level |= UserAccess.HasAccess(userNickName, AccessLevel.Mod, true) ? AccessLevel.NoPoints : AccessLevel.User;
                        break;
                }
            }
            if (level == AccessLevel.User)
            {
                return;
            }

            if (text.StartsWith("!add ", StringComparison.InvariantCultureIgnoreCase))
            {
                UserAccess.AddUser(split[1], level);
                _ircConnection.SendMessage(TwitchPlaySettings.data.AddedUserPower, level, split[1]);
            }
            else
            {
                UserAccess.RemoveUser(split[1], level);
                _ircConnection.SendMessage(TwitchPlaySettings.data.RemoveUserPower, level, split[1]);
            }
            UserAccess.WriteAccessList();
        }


        if (UserAccess.HasAccess(userNickName, AccessLevel.SuperUser, true))
        {
            if (text.Equals("!reloaddata", StringComparison.InvariantCultureIgnoreCase))
            {
                bool streamer = UserAccess.HasAccess(userNickName, AccessLevel.Streamer);
                bool superuser = UserAccess.HasAccess(userNickName, AccessLevel.SuperUser);

                ModuleData.LoadDataFromFile();
                TwitchPlaySettings.LoadDataFromFile();
                UserAccess.LoadAccessList();

                if (streamer)
                    UserAccess.AddUser(userNickName, AccessLevel.Streamer);
                if (superuser)
                    UserAccess.AddUser(userNickName, AccessLevel.SuperUser);
                _ircConnection.SendMessage("Data reloaded");
            }
            else if (text.Equals("!enabletwitchplays", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Twitch Plays Enabled");
                TwitchPlaySettings.data.EnableTwitchPlaysMode = true;
                TwitchPlaySettings.WriteDataToFile();
                EnableDisableInput();
            }
            else if (text.Equals("!disabletwitchplays", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Twitch Plays Disabled");
                TwitchPlaySettings.data.EnableTwitchPlaysMode = false;
                TwitchPlaySettings.WriteDataToFile();
                EnableDisableInput();
            }
            else if (text.Equals("!enableinteractivemode", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Interactive Mode Enabled");
                TwitchPlaySettings.data.EnableInteractiveMode = true;
                TwitchPlaySettings.WriteDataToFile();
                EnableDisableInput();
            }
            else if (text.Equals("!disableinteractivemode", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Interactive Mode Disabled");
                TwitchPlaySettings.data.EnableInteractiveMode = false;
                TwitchPlaySettings.WriteDataToFile();
                EnableDisableInput();
            }
            else if (text.Equals("!solveunsupportedmodules", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Solving unsupported modules.");
                TwitchComponentHandle.SolveUnsupportedModules();
            }
            else if (text.Equals("!removesolvebasedmodules", StringComparison.InvariantCultureIgnoreCase))
            {
                _ircConnection.SendMessage("Removing Solve based modules");
                TwitchComponentHandle.RemoveSolveBasedModules();
            }
        }
    }

    private void EnableDisableInput()
    {
        if (!BombMessageResponder.EnableDisableInput())
        {
            return;
        }
        if (TwitchComponentHandle.SolveUnsupportedModules())
        {
            _ircConnection.SendMessage("Some modules were automatically solved to prevent problems with defusing this bomb.");
        }
    }

}
