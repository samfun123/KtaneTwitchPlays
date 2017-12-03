﻿using UnityEngine;

public abstract class MessageResponder : MonoBehaviour
{
    protected IRCConnection _ircConnection = null;
    protected CoroutineQueue _coroutineQueue = null;
    protected CoroutineCanceller _coroutineCanceller = null;

    private void OnDestroy()
    {
        if (_ircConnection != null)
        {
            _ircConnection.OnMessageReceived.RemoveListener(OnInternalMessageReceived);
        }
    }

    public void SetupResponder(IRCConnection ircConnection, CoroutineQueue coroutineQueue, CoroutineCanceller coroutineCanceller)
    {
        _ircConnection = ircConnection;
        _coroutineQueue = coroutineQueue;
        _coroutineCanceller = coroutineCanceller;

        _ircConnection.OnMessageReceived.AddListener(OnInternalMessageReceived);
    }

    public bool IsAuthorizedDefuser(string userNickName)
    {
        bool result = (TwitchPlaySettings.data.EnableTwitchPlaysMode || UserAccess.HasAccess(userNickName, AccessLevel.Defuser, true));
        if (!result)
            _ircConnection.SendMessage(TwitchPlaySettings.data.TwitchPlaysDisabled, userNickName);

        return result;
    }

    protected abstract void OnMessageReceived(string userNickName, string userColorCode, string text);

    private void OnInternalMessageReceived(string userNickName, string userColorCode, string text)
    {
        if (gameObject.activeInHierarchy && isActiveAndEnabled)
        {
            OnMessageReceived(userNickName, userColorCode, text);
        }
    }
}
